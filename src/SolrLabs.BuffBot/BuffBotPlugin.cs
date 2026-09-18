using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Casting;
using SolrLabs.BuffBot.Chat;
using SolrLabs.BuffBot.Components;
using SolrLabs.BuffBot.Donations;
using SolrLabs.BuffBot.Guard;
using SolrLabs.BuffBot.Policy;
using SolrLabs.BuffBot.Requests;
using SolrLabs.BuffBot.Settings;
using SolrLabs.BuffBot.Spells;
using SolrLabs.BuffBot.Stats;
using SolrLabs.BuffBot.Timing;
using SolrLabs.BuffBot.Ui;
using SolrLabs.BuffBot.Vocabulary;
using SolrLabs.BuffBot.Web;

namespace SolrLabs.BuffBot;

public sealed class BuffBotPlugin : IAcDreamPlugin
{
    private const string Version = "0.1.0-beta.1";

    /// <summary>How many requests may wait behind the one being processed.</summary>
    private const int QueueCapacity = 5;

    /// <summary>Fixed loopback port the web console mesh binds to — how a spoke finds the hub.</summary>
    private const int MeshPort = 8347;

    /// <summary>Default n for <c>/buffbot chatdump</c> when none is given.</summary>
    private const int DefaultChatDumpCount = 20;

    private readonly TellListener _tells = new();
    private readonly RequestQueue _queue = new(QueueCapacity);
    private readonly LoopGuard _guard;
    private readonly Responder _responder;
    private readonly OperatorConsole _operator;

    /// <summary>Lives for the plugin's own lifetime, not rebuilt per <see cref="Enable"/>.</summary>
    private readonly BotStats _stats = new();

    private IPluginHost? _host;
    private CharacterEnablement? _enablement;
    private BuffCoordinator? _coordinator;
    private BuffBotPanelViewModel? _panel;
    private Action<double>? _tick;
    private IDisposable? _commandRegistration;
    private IDisposable? _panelRegistration;
    private int _tellsAnswered;

    /// <summary>Runs whenever bound, independent of the per-character enable switch; never throws.</summary>
    private MeshSupervisor? _mesh;
    private byte[]? _consolePageBytes;

    /// <summary>Stays not-ready until inventory is proven stable, so nothing acts on an empty
    /// capture.</summary>
    private InventoryReadiness? _inventoryReadiness;

    /// <summary>Only <see cref="PumpPeaSplitter"/>'s top-up waits on <see
    /// cref="_inventoryReadiness"/> — a mid-chain retry only fires inside an already-casting run.</summary>
    private PeaSplitter? _peaSplitter;

    /// <summary>Host-free core wrapped by <see cref="PumpPeaSplitter"/>.</summary>
    private PeaTopUpLoop? _peaTopUpLoop;

    private TradeDonationSession? _tradeSession;

    /// <summary>Every accepted donation, appended here on completion.</summary>
    private ContributorLedger? _contributorLedger;

    private DirectGiveListener? _directGiveListener;

    /// <summary>Read fresh each tick rather than off <see cref="_lastComponents"/>'s slower
    /// cadence, since a trade's acceptance decision must never act on a stale answer.</summary>
    private bool _botHoldsSplittingToolThisTick;

    /// <summary>Kept so a web console mute (<see cref="ApplyMeshCommand"/>) can resolve an object
    /// id back to a name.</summary>
    private BuffBotStatus? _lastStatus;

    private SessionCounters _lastCounters;

    /// <summary>So a relog re-reads storage instead of reusing a stale answer.</summary>
    private uint? _cachedCharacterObjectId;
    private bool _enabledForCachedCharacter;
    private bool _announcedEnablementState;

    private BuffBotSettingsStore? _settingsStore;

    /// <summary>Paces the components tile off <see cref="PumpCoordinator"/>'s hot path; forced
    /// due the moment a chain closes.</summary>
    private readonly ComponentSampleCadence _componentCadence = new();

    private ComponentReport _lastComponents = ComponentReport.Unavailable;

    /// <summary>Bumped only when <see cref="SampleComponents"/> actually resamples, so <see
    /// cref="_peaTopUpLoop"/> can tell a fresher report without comparing timestamps.</summary>
    private int _componentSampleGeneration;

    private ContributionSummary _lastContribution = ContributionSummary.Unavailable;

    /// <summary><see langword="null"/> until the first sample, or whenever inventory could not
    /// be read.</summary>
    private Func<uint, bool>? _lastUsesScarabOnlyFormula;

    /// <summary>Re-synced into <see cref="_guard"/>/<see cref="_coordinator"/> every tick, so a
    /// console write lands the tick it arrives.</summary>
    private BuffBotSettings _currentSettings = BuffBotSettings.Default;

    public BuffBotPlugin()
    {
        _guard = new LoopGuard(SystemClock.Instance, LogInfo, LogWarn);
        _responder = new Responder(
            DefaultVocabulary.Table, new AccessPolicy(AccessMode.Free), _queue, Version, _guard,
            AreSelfCastsDue, WillTopUpBeforeNextRequest, _stats, () => _currentSettings.IntakePaused,
            WouldFindNothingLearned, StopActiveRun, () => _lastContribution);
        _operator = new OperatorConsole(_queue, _guard);
    }

    public void Initialize(IPluginHost host)
    {
        _host = host;
        IPluginStorage storage = SelectStorage(
            host.Storage, new PluginFileStorage(PluginFileStorage.DefaultRoot()), host.Log.Info);
        _enablement = new CharacterEnablement(storage);
        _settingsStore = new BuffBotSettingsStore(storage);
        _contributorLedger = new ContributorLedger(storage, LogWarn);
        host.Log.Info($"BuffBot {Version} initialised (api {PluginApi.Current})");
    }

    /// <summary>Host-free so the choice is testable without a fake <see
    /// cref="IPluginHost"/>.</summary>
    internal static IPluginStorage SelectStorage(
        IPluginStorage hostStorage, IPluginStorage fallback, Action<string> logInfo)
    {
        if (hostStorage.IsAvailable)
            return hostStorage;

        logInfo("host provides no plugin storage; using the plugin's own file-backed store instead.");
        return fallback;
    }

    public void Enable()
    {
        if (_host is null)
            return;

        _inventoryReadiness = new InventoryReadiness();

        _peaSplitter = new PeaSplitter(_host.Automation.Items, _host.Automation.Combat, LogInfo, LogWarn);
        _peaTopUpLoop = new PeaTopUpLoop(_peaSplitter, LogWarn, requestResample: _componentCadence.ForceDue);

        _tradeSession = new TradeDonationSession(
            new TradeAutomationView(_host.Automation.Trade),
            ResolveTradeItem,
            () => _botHoldsSplittingToolThisTick,
            LogInfo);
        _directGiveListener = new DirectGiveListener();

        _coordinator = new BuffCoordinator(
            _queue,
            _host.Automation.Magic,
            _host.Automation.Enchantments,
            _host.Automation.Items,
            _host.Automation.Equipment,
            _host.Automation.Combat,
            trace: _host.Log.Info,
            warn: _host.Log.Warn,
            character: _host.Automation.Character,
            stats: _stats,
            peaSplitCoordinator: _peaSplitter,
            catalog: _host.Automation.Spells);

        _tick = OnTick;
        _host.Events.Tick += _tick;

        // /buffbot status and /buffbot on must work while inert.
        _commandRegistration = _host.Commands.Register("buffbot", OnCommand);

        IBrowserLauncher launcher = _host.HasUi ? new ProcessBrowserLauncher() : NullBrowserLauncher.Instance;
        _panel = new BuffBotPanelViewModel(
            setEnabled: ToggleEnabledFromPanel,
            logWarn: LogWarn, launcher: launcher, stats: _stats);

        // Plugins load from their own directory, not the host's working directory.
        string directory = Path.GetDirectoryName(typeof(BuffBotPlugin).Assembly.Location) ?? ".";
        _panelRegistration = _host.Ui.RegisterPanel(
            new PluginPanelDescriptor("main", "OpenAC Buffbot")
            {
                // Incantation of Strength Other's icon; IconText is the fallback if it is absent.
                IconSurfaceId = 0x0600138Cu,
                IconText = "BB",
                StartVisible = false, // opened from its sidepanel button, not shown on load
            },
            Path.Combine(directory, "buffbot.xml"),
            _panel);

        _mesh = new MeshSupervisor(SystemClock.Instance, LogInfo, LogWarn, StartMeshNode);

        AnnounceEnablementIfPossible(_host);
    }

    public void Disable()
    {
        if (_host is not null && _tick is not null)
            _host.Events.Tick -= _tick;
        _tick = null;
        _coordinator = null;

        _commandRegistration?.Dispose();
        _commandRegistration = null;

        _panelRegistration?.Dispose();
        _panelRegistration = null;
        _panel = null;

        _mesh?.Stop();
        _mesh = null;

        _inventoryReadiness = null;
        _peaSplitter = null;
        _peaTopUpLoop = null;
        _tradeSession = null;
        _directGiveListener = null;
    }

    private void OnTick(double deltaSeconds)
    {
        if (_host is not { Automation.IsAvailable: true } host)
            return;

        try
        {
            AnnounceEnablementIfPossible(host);

            // Commands are drained ahead of the enable check so a console enable/disable lands
            // this tick.
            _mesh?.Tick(enabled: true);
            DrainMeshCommands(host);
            SyncSettingsIntoRuntime();
            SampleComponents(host, deltaSeconds);

            _panel?.UpdateConsole(
                _mesh?.DescribeConsole() ?? new MeshConsoleLink(MeshConsoleState.Unavailable, null));

            // Pumping _coordinator here would risk casting while meant to be off, so the panel
            // and mesh get a minimal snapshot instead.
            if (!IsEnabledForCurrentCharacter(host))
            {
                // Disabling already asked the caster to stop after its current cast; still ticking
                // it here lets that cast's confirmation land in the ledger before the run closes.
                if (_coordinator is { HasActiveRequester: true })
                {
                    IReadOnlyList<PluginChatMessage> draining =
                        host.Automation.Chat.CaptureMessages(_tells.LastSequence);
                    PumpCoordinator(host, deltaSeconds, draining, host.Automation.Objects.CaptureObjects());
                    return;
                }

                BuffBotStatus disabled =
                    BuildDisabledStatus(
                        host.Automation.Character, _guard.MutedEntries(), _lastCounters, _tellsAnswered,
                        _stats.Snapshot())
                        with { Settings = _currentSettings, Components = _lastComponents };
                _panel?.UpdateStatus(disabled);
                PublishMeshStatus(host, disabled);
                return;
            }

            // Shared: the caster needs the same batch to confirm a targeted cast landed.
            IReadOnlyList<PluginChatMessage> captured =
                host.Automation.Chat.CaptureMessages(_tells.LastSequence);
            RespondToTells(host, captured);

            // Shared with PumpDirectGiveLines below rather than a second host call.
            IReadOnlyList<PluginWorldObject> capturedObjects = host.Automation.Objects.CaptureObjects();

            bool tradeOpen = host.Automation.Trade.IsOpen;
            _botHoldsSplittingToolThisTick = ComputeBotHoldsSplittingTool(capturedObjects);

            PumpCoordinator(host, deltaSeconds, captured, capturedObjects, tradeOpen);
            PumpInventoryReadiness(host, deltaSeconds);
            PumpTradeDonations(host, deltaSeconds, captured);
            PumpDirectGiveLines(host, captured, capturedObjects);
            if (!tradeOpen)
                PumpPeaSplitter(host, deltaSeconds); // no pea top-up while a trade is open either
        }
        catch (Exception error)
        {
            host.Log.Error("BuffBot tick failed.", error);
        }
    }

    private void RespondToTells(IPluginHost host, IReadOnlyList<PluginChatMessage> captured)
    {
        IReadOnlyList<PluginChatMessage> tells =
            _tells.ExtractTells(captured, host.Automation.Character.ObjectId);

        foreach (PluginChatMessage tell in tells)
        {
            _tellsAnswered++;
            string? reply = _responder.Reply(tell, _tellsAnswered);
            if (reply is null)
                continue;

            if (host.Automation.Chat.Submit(reply))
                host.Log.Info($"reply to {tell.Sender}: {reply}");
            else
                host.Log.Warn($"reply to {tell.Sender} failed: {reply}");
        }
    }

    private void PumpCoordinator(
        IPluginHost host, double deltaSeconds, IReadOnlyList<PluginChatMessage> captured,
        IReadOnlyList<PluginWorldObject> capturedObjects, bool tradeOpen = false)
    {
        if (_coordinator is null)
            return;

        BotStatsSnapshot statsSnapshot = _stats.Snapshot();

        BuffBotStatus status = _coordinator.Pump(
            deltaSeconds,
            host.Automation.Spells.KnownSelfBuffs,
            host.Automation.Character.ActiveEnchantments,
            captured,
            selfBuffingEnabled: _currentSettings.SelfBuffUpkeep,
            requesterObjectId => DistanceTo(host, requesterObjectId),
            (requesterObjectId, name, text) =>
            {
                // Refresh right away rather than waiting out the rest of the interval.
                _componentCadence.ForceDue();
                SendClosingReply(host, requesterObjectId, name, text);
            },
            tellsAnswered: _tellsAnswered,
            muted: _guard.MutedEntries(),
            capturedObjects: capturedObjects,
            components: _lastComponents,
            usesScarabOnlyFormula: _lastUsesScarabOnlyFormula,
            tradeOpen: tradeOpen)
            with
            {
                Settings = _currentSettings,
                Components = _lastComponents,
                TradeOpen = tradeOpen,
                DonationsCompleted = statsSnapshot.DonationsCompleted,
                DonationItemsReceived = statsSnapshot.DonationItemsReceived,
            };

        _lastCounters = status.Counters;
        _lastStatus = status;
        _panel?.UpdateStatus(status);
        PublishMeshStatus(host, status);
    }

    /// <summary>Ticked unconditionally so <see cref="PumpPeaSplitter"/> reads a settled gate.</summary>
    private void PumpInventoryReadiness(IPluginHost host, double deltaSeconds) =>
        _inventoryReadiness?.Tick(
            deltaSeconds, host.Automation.Character.IsInWorld, host.Automation.Character.ObjectId,
            host.Automation.Items.CaptureOwnedItems);

    /// <summary>Replies go out with <c>isDonationReply: true</c>: exempt from the repeat breaker,
    /// still blocked by an existing mute.</summary>
    private void PumpTradeDonations(IPluginHost host, double deltaSeconds, IReadOnlyList<PluginChatMessage> captured)
    {
        if (_tradeSession is null)
            return;

        _tradeSession.Tick(deltaSeconds, captured);

        uint partnerObjectId = host.Automation.Trade.PartnerObjectId;
        string partnerName = host.Automation.Trade.PartnerName;
        foreach (string reply in _tradeSession.DrainReplies())
            SendClosingReply(host, partnerObjectId, partnerName, reply, isDonationReply: true);

        foreach (CompletedDonation donation in _tradeSession.DrainCompletedDonations())
        {
            _contributorLedger?.Append(host.Automation.Character.ObjectId, donation);

            int itemCount = 0;
            foreach (DonatedItem item in donation.Items)
                itemCount += item.Count;
            _stats.RecordDonation(itemCount);

            _componentCadence.ForceDue();
        }
    }

    /// <summary>Refuses a direct give at the source, and warns the operator once, in the log
    /// only, if <c>AllowGive</c> is still on and one landed anyway.</summary>
    private void PumpDirectGiveLines(
        IPluginHost host, IReadOnlyList<PluginChatMessage> captured, IReadOnlyList<PluginWorldObject> capturedObjects)
    {
        if (_directGiveListener is null)
            return;

        DirectGiveOutcome outcome = _directGiveListener.Process(captured, capturedObjects);

        if (outcome.Refusals.Count > 0)
        {
            string wantedItemsDescription = DonationPolicy.WantedItemsDescription(_botHoldsSplittingToolThisTick);
            foreach (DirectGiveRefusal refusal in outcome.Refusals)
                SendClosingReply(
                    host, refusal.GiverObjectId, refusal.GiverName,
                    DefaultReplies.DirectGiveRefused(wantedItemsDescription));
        }

        if (outcome.WarnOperatorAboutLandedGive)
            host.Log.Warn(
                "BuffBot kept a landed direct gift: this character still allows direct gives. Turn off "
                + "\"Let other players give you items\" in the GUI options, or set "
                + "\"characterOptions\": {\"AllowGive\": false} in a headless session config.");
    }

    /// <summary>A staged item's object reaches the client before <c>ItemAdded</c>, so <see
    /// cref="IWorldObjectAutomation.TryGet"/> already answers this without an appraisal.</summary>
    private (uint wcid, string name, int stack)? ResolveTradeItem(uint objectId) =>
        _host!.Automation.Objects.TryGet(objectId, out PluginWorldObject value)
            ? (value.WeenieClassId, value.Name, value.StackSize)
            : null;

    private static bool ComputeBotHoldsSplittingTool(IReadOnlyList<PluginWorldObject> capturedObjects)
    {
        foreach (PluginWorldObject item in capturedObjects)
            if (item.IsOwned && item.WeenieClassId == ContributionAdvisor.SplittingToolWeenieClassId)
                return true;
        return false;
    }

    /// <summary>Only runs while idle, which also keeps it from racing a mid-chain split — that
    /// only exists while a run is active.</summary>
    private void PumpPeaSplitter(IPluginHost host, double deltaSeconds)
    {
        if (_peaTopUpLoop is null || !_currentSettings.SplitPeas)
            return;

        if (_inventoryReadiness is not { IsReady: true })
            return;

        bool botBusy = (_lastStatus?.Activity ?? BotActivity.Idle) != BotActivity.Idle
            || host.Automation.Items.IsBusy
            || host.Automation.Equipment.IsBusy;
        if (botBusy)
            return;

        _peaTopUpLoop.Pump(
            deltaSeconds, _lastComponents, _componentSampleGeneration, _currentSettings.ComponentLowStock);
    }

    /// <summary>Can throw (a locked-down key directory, a port already taken); <see
    /// cref="MeshSupervisor"/> turns that into a logged retry instead of a dead tick.</summary>
    private MeshNode StartMeshNode()
    {
        IPluginHost host = _host!;
        _consolePageBytes ??= ConsolePage.Load();
        var node = new MeshNode(new MeshNodeOptions(
            Port: MeshPort,
            KeyPath: MeshKeyStore.DefaultPath(),
            LinkFilePath: MeshOpenerPage.DefaultPath(),
            Clock: SystemClock.Instance,
            PageBytes: _consolePageBytes,
            LogInfo: host.Log.Info,
            ReadContributors: characterObjectId =>
                _contributorLedger?.Load(characterObjectId) ?? Array.Empty<Donations.Contributor>()));
        node.Start();
        return node;
    }

    /// <summary>Never allowed to take the tick down — a broken item surface degrades to <see
    /// cref="ComponentReport.Unavailable"/>, logged once.</summary>
    private void SampleComponents(IPluginHost host, double deltaSeconds)
    {
        if (!_componentCadence.Advance(deltaSeconds))
            return;

        // Bumped whether or not the sample below succeeds: PumpPeaSplitter's stale-report gate
        // only needs to know a newer attempt happened.
        _componentSampleGeneration++;

        try
        {
            bool inventoryReadable = host.Automation.IsAvailable;

            _lastUsesScarabOnlyFormula = inventoryReadable
                ? BuildUsesScarabOnlyFormulaPredicate(host)
                : null;

            _lastComponents = ComponentReportBuilder.Build(
                host.Automation.Spells, host.Automation.Items, inventoryReadable,
                usesScarabOnlyFormula: _lastUsesScarabOnlyFormula);

            // The `contribute` keyword needs every learned tier's reagent, not just the top one
            // the console tile shows.
            ComponentReport allTiers = ComponentReportBuilder.Build(
                host.Automation.Spells, host.Automation.Items, inventoryReadable, allLearnedTiers: true,
                usesScarabOnlyFormula: _lastUsesScarabOnlyFormula);
            _lastContribution = ContributionAdvisor.Build(
                allTiers, host.Automation.Items.CaptureOwnedItems(), inventoryReadable,
                _currentSettings.ComponentLowStock);
        }
        catch (Exception error)
        {
            host.Log.Warn($"BuffBot component sample failed: {error.Message}");
            _lastComponents = ComponentReport.Unavailable;
            _lastContribution = ContributionSummary.Unavailable;
            _lastUsesScarabOnlyFormula = null;
        }
    }

    /// <summary>Closes over one snapshot — keeps <see cref="ComponentReportBuilder"/> and <see
    /// cref="Casting.CastStateMachine"/> host-free.</summary>
    private static Func<uint, bool> BuildUsesScarabOnlyFormulaPredicate(IPluginHost host)
    {
        // Main pack only: ACE's HasFoci reads the player's own Inventory, so a focus in a side
        // pack does not count.
        uint self = host.Automation.Character.ObjectId;
        IReadOnlyList<PluginInventoryItem> ownedItems = host.Automation.Items.CaptureOwnedItems()
            .Where(item => item.ContainerObjectId == self)
            .ToList();
        PluginItemProperties? selfProperties = host.Automation.Objects.TryCaptureProperties(
            self, out PluginItemProperties properties)
            ? properties
            : null;
        return school => CasterFormulaMode.UsesScarabOnlyFormula(school, ownedItems, selfProperties);
    }

    private void PublishMeshStatus(IPluginHost host, BuffBotStatus status)
    {
        if (_mesh is null)
            return;

        string world = host.Automation.Character.WorldName;
        string botId = MeshIdentity.BotId(world, host.Automation.Character.ObjectId);
        _mesh.Publish(botId, host.Automation.Character.Name, MeshIdentity.World(world), MeshStatusMapper.From(status, SystemClock.Instance));
    }

    /// <summary>Runs before <see cref="RespondToTells"/>, so a mesh command cannot race a tell
    /// answered the same tick.</summary>
    private void DrainMeshCommands(IPluginHost host)
    {
        if (_mesh is null)
            return;

        while (_mesh.TryDequeueCommand(out MeshCommand command))
            ApplyMeshCommand(host, command);
    }

    private void ApplyMeshCommand(IPluginHost host, MeshCommand command)
    {
        switch (command.Kind)
        {
            case MeshCommandKind.Mute:
                if (command.ObjectId is { } muteId && TryResolveWaitingName(muteId, out string muteName))
                {
                    _guard.Mute(muteId, muteName);
                    _stats.RecordMute(muteName, "web console");
                    host.Log.Info($"BuffBot muted {muteName} via web console.");
                }
                break;

            case MeshCommandKind.Release:
                if (command.ObjectId is { } releaseId)
                {
                    string releaseName = TryResolveMutedName(releaseId, out string resolved)
                        ? resolved
                        : $"object {releaseId}";
                    _guard.Unmute(releaseId);
                    _stats.RecordRelease(releaseName, "web console");
                    host.Log.Info($"BuffBot released object {releaseId} via web console.");
                }
                break;

            case MeshCommandKind.Enable:
                SetEnabledForCurrentCharacter(host, enabled: true, source: "the web console");
                break;

            case MeshCommandKind.Disable:
                SetEnabledForCurrentCharacter(host, enabled: false, source: "the web console");
                break;

            case MeshCommandKind.Settings:
                if (command.Settings is { } patch)
                    ApplySettingsPatch(host, patch, source: "the web console");
                break;

            case MeshCommandKind.Drain:
                PerformDrain(host, source: "the web console");
                break;
        }
    }

    /// <summary>Folds settings straight back into the runtime, saving a tick of staleness ahead
    /// of the next <see cref="SyncSettingsIntoRuntime"/>.</summary>
    private void ApplySettingsPatch(IPluginHost host, MeshSettingsPatch patch, string source)
    {
        _currentSettings = _currentSettings.WithPatch(
            patch.SelfBuffUpkeep, patch.RefusalRangeMeters, patch.RepliesPerSenderPerMinute,
            patch.IntakePaused, patch.HasTargetTier, patch.TargetTier, patch.TierFallback,
            patch.FizzlesBeforeSkip, patch.ComponentLowStock,
            patch.ManaBounceLowWaterFraction, patch.ManaBounceHighWaterFraction, patch.SplitPeas);
        _settingsStore?.Save(host.Automation.Character.ObjectId, _currentSettings);
        SyncSettingsIntoRuntime();
        host.Log.Info($"BuffBot settings updated via {source}.");
    }

    /// <summary>Removes every still-waiting requester and tells each one once, without touching
    /// the run in flight or any mute.</summary>
    private void PerformDrain(IPluginHost host, string source)
    {
        IReadOnlyList<BuffRequest> drained = _queue.DrainWaiting();
        foreach (BuffRequest request in drained)
            SendClosingReply(host, request.RequesterObjectId, request.RequesterName, DefaultReplies.QueueDrained);

        host.Log.Info($"BuffBot queue drained via {source}: {drained.Count} removed.");
    }

    private bool TryResolveWaitingName(uint objectId, out string name)
    {
        if (_lastStatus is { } status)
        {
            foreach (QueueEntry entry in status.Waiting)
            {
                if (entry.ObjectId == objectId)
                {
                    name = entry.Name;
                    return true;
                }
            }
        }
        name = "";
        return false;
    }

    private bool TryResolveMutedName(uint objectId, out string name)
    {
        if (_lastStatus is { } status)
        {
            foreach (MutedEntry entry in status.Muted)
            {
                if (entry.ObjectId == objectId)
                {
                    name = entry.Name;
                    return true;
                }
            }
        }
        name = "";
        return false;
    }

    /// <summary>Health/stamina go <see langword="null"/> when <see cref="ICharacterInfo.IsInWorld"/>
    /// is false; mana stays a plain <see langword="uint"/> and reads 0 then.</summary>
    internal static BuffBotStatus BuildDisabledStatus(
        ICharacterInfo character, IReadOnlyList<MutedEntry> muted, SessionCounters lastCounters,
        int tellsAnswered, BotStatsSnapshot statsSnapshot)
    {
        bool available = character.IsInWorld;
        return new(
            Enabled: false,
            Activity: BotActivity.Idle,
            CurrentRequesterName: null,
            CurrentRequesterObjectId: null,
            CurrentSpellLine: null,
            CurrentSpellId: 0u,
            StepIndex: 0,
            StepCount: 0,
            CurrentMana: available ? character.CurrentMana : 0u,
            MaxMana: available ? character.MaxMana : 0u,
            Waiting: Array.Empty<QueueEntry>(),
            Muted: muted,
            Counters: lastCounters with { TellsAnswered = tellsAnswered },
            Stats: statsSnapshot.Session,
            Recent: statsSnapshot.Recent,
            CurrentHealth: available ? character.CurrentHealth : null,
            MaxHealth: available ? character.MaxHealth : null,
            CurrentStamina: available ? character.CurrentStamina : null,
            MaxStamina: available ? character.MaxStamina : null,
            // TradeOpen is left at its default false: this path never reads the trade surface.
            DonationsCompleted: statsSnapshot.DonationsCompleted,
            DonationItemsReceived: statsSnapshot.DonationItemsReceived);
    }

    /// <summary>Lets <see cref="Responder"/>'s ack become "On it — buffing up first."</summary>
    private bool AreSelfCastsDue()
    {
        if (_host is not { Automation.IsAvailable: true } host)
            return false;

        IReadOnlyList<string> selfLines = DefaultSpellSets.Table.TryGetValue(DefaultSpellSets.Self, out var lines)
            ? lines
            : Array.Empty<string>();

        return SelfBuffPlanner.PlanDue(
            host.Automation.Spells.KnownSelfBuffs,
            selfLines,
            host.Automation.Character.ActiveEnchantments).Count > 0;
    }

    /// <summary>Lets <see cref="Responder"/>'s ack become "On it — topping up mana first."</summary>
    private bool WillTopUpBeforeNextRequest() => _coordinator?.WillTopUpBeforeNextRequest ?? false;

    /// <summary>Lets <see cref="Responder"/> refuse a doomed request before an "On it." ack.</summary>
    private bool WouldFindNothingLearned(string setName) =>
        _host is { Automation.IsAvailable: true } host
        && (_coordinator?.WouldFindNothingLearned(setName, host.Automation.Spells.KnownSelfBuffs) ?? false);

    private bool StopActiveRun(uint requesterObjectId) =>
        _coordinator?.TryStopActiveRun(requesterObjectId, RunStopReason.RequesterCancelled) ?? false;

    /// <summary>The last distance traced per requester, so <see cref="DistanceTo"/> logs again
    /// only when something worth reading has changed.</summary>
    private readonly Dictionary<uint, (double Metres, bool InRange)> _lastTracedDistance = [];

    internal static bool DistanceTraceIsWorthLogging(
        double metres, bool inRange, (double Metres, bool InRange)? lastTraced) =>
        lastTraced is not { } last
        || last.InRange != inRange
        || Math.Abs(metres - last.Metres) >= 1d;

    /// <summary>Horizontal distance, in meters, from the local player to a requester's object,
    /// or <see langword="null"/> if the surface cannot currently place them.</summary>
    private double? DistanceTo(IPluginHost host, uint requesterObjectId)
    {
        // Null means "cannot tell", never "far" — see BuffCoordinator.IsRequesterInRange.
        if (!host.Automation.Objects.TryGet(requesterObjectId, out PluginWorldObject requester))
        {
            host.Log.Info($"distance unknown: object {requesterObjectId} is not in our table");
            return null;
        }

        if (!requester.HasPosition)
        {
            host.Log.Info($"distance unknown: object {requesterObjectId} carries no position");
            return null;
        }

        PluginNavigationSnapshot local = host.Automation.Navigation.Snapshot;
        if (!local.IsAvailable)
        {
            host.Log.Info("distance unknown: our own navigation snapshot is unavailable");
            return null;
        }

        double metres = local.Position.HorizontalDistanceMeters(requester.Position);

        // A dungeon's cells are a different coordinate frame from the surface landblock, so a
        // horizontal distance across that boundary may be meaningless rather than merely large.
        bool inRange = RangePolicy.IsInRange(metres);
        (double Metres, bool InRange)? lastTraced =
            _lastTracedDistance.TryGetValue(requesterObjectId, out var last) ? last : null;
        if (DistanceTraceIsWorthLogging(metres, inRange, lastTraced))
        {
            _lastTracedDistance[requesterObjectId] = (metres, inRange);
            host.Log.Info(
                $"distance to {requesterObjectId}: {metres:F1}m | "
                + $"self cell {local.Position.CellId:X8} outdoor={local.Position.IsOutdoor} | "
                + $"requester cell {requester.Position.CellId:X8} outdoor={requester.Position.IsOutdoor}");
        }
        return metres;
    }

    /// <summary>Exempt from the rate limit; <paramref name="isDonationReply"/> also exempts it
    /// from the repeat breaker, so several donations in a row can never mute the donor.</summary>
    private void SendClosingReply(
        IPluginHost host, uint requesterObjectId, string requesterName, string text, bool isDonationReply = false)
    {
        string? admitted = _guard.Admit(
            requesterObjectId, requesterName, text, isUnresolvedReply: false, countsAsRequest: false,
            countsTowardMuteTrigger: !isDonationReply);
        if (admitted is null)
            return;

        string reply = $"/tell {requesterName}, {admitted}";
        if (host.Automation.Chat.Submit(reply))
            host.Log.Info($"reply to {requesterName}: {reply}");
        else
            host.Log.Warn($"reply to {requesterName} failed: {reply}");
    }

    private void OnCommand(PluginCommand command)
    {
        if (_host is not { } host)
            return;

        string argument = command.Arguments.Trim();

        if (string.Equals(argument, "on", StringComparison.OrdinalIgnoreCase))
        {
            SetEnabledForCurrentCharacter(host, enabled: true, source: "/buffbot on");
            return;
        }

        if (string.Equals(argument, "off", StringComparison.OrdinalIgnoreCase))
        {
            SetEnabledForCurrentCharacter(host, enabled: false, source: "/buffbot off");
            return;
        }

        if (string.Equals(argument, "status", StringComparison.OrdinalIgnoreCase))
        {
            if (host.Automation.IsAvailable)
            {
                host.Automation.Chat.PostSystemMessage(
                    DefaultReplies.Status(Version, _tellsAnswered, IsEnabledForCurrentCharacter(host)));
            }
            return;
        }

        // Console-only: ChatCommandRouter routes local input here, and an incoming tell never
        // reaches it.
        if (string.Equals(argument, "inspect", StringComparison.OrdinalIgnoreCase))
        {
            if (host.Automation.IsAvailable)
            {
                host.Automation.Chat.PostSystemMessage(
                    _operator.Inspect(IsEnabledForCurrentCharacter(host), _tellsAnswered));
            }
            return;
        }

        if (argument.StartsWith("items ", StringComparison.OrdinalIgnoreCase) || string.Equals(argument, "items", StringComparison.OrdinalIgnoreCase))
        {
            HandleItemsCommand(host, argument.Length > 5 ? argument[5..].Trim() : string.Empty);
            return;
        }

        if (argument.StartsWith("inv ", StringComparison.OrdinalIgnoreCase) || string.Equals(argument, "inv", StringComparison.OrdinalIgnoreCase))
        {
            HandleInvCommand(host, argument.Length > 3 ? argument[3..].Trim() : string.Empty);
            return;
        }

        if (string.Equals(argument, "logout", StringComparison.OrdinalIgnoreCase))
        {
            HandleLogoutCommand(host);
            return;
        }

        if (argument.StartsWith("chatdump", StringComparison.OrdinalIgnoreCase))
        {
            HandleChatDumpCommand(host, argument.Length > 8 ? argument[8..].Trim() : string.Empty);
        }
    }

    /// <summary>Split on the literal " cast " rather than a second token, since a player name can
    /// carry a space.</summary>
    private void HandleItemsCommand(IPluginHost host, string rest)
    {
        if (!host.Automation.IsAvailable)
            return;

        int castAt = rest.IndexOf(" cast ", StringComparison.OrdinalIgnoreCase);
        string playerName = (castAt >= 0 ? rest[..castAt] : rest).Trim();
        string? castLine = castAt >= 0 ? rest[(castAt + " cast ".Length)..].Trim() : null;

        if (playerName.Length == 0)
        {
            host.Automation.Chat.PostSystemMessage("Usage: /buffbot items <player name> [cast <line>]");
            return;
        }

        IReadOnlyList<PluginWorldObject> objects = host.Automation.Objects.CaptureObjects();
        if (!PlayerItemsLookup.TryFindByName(objects, playerName, out PluginWorldObject player))
        {
            host.Automation.Chat.PostSystemMessage($"BuffBot items: no object named \"{playerName}\" is captured right now.");
            return;
        }

        IReadOnlyList<PluginWorldObject> wielded = PlayerItemsLookup.WieldedBy(objects, player.ObjectId);
        host.Log.Info($"BuffBot items: {player.Name} ({player.ObjectId}) wields {wielded.Count} object(s).");
        foreach (PluginWorldObject item in wielded)
            host.Log.Info($"BuffBot items: wielded {item.ObjectId} \"{item.Name}\" ({item.ObjectClass}).");

        IReadOnlyList<ResolvedSpell> learnedItemLines = SpellSelector.ResolveLenient(
            host.Automation.Spells.KnownSelfBuffs, DefaultSpellSets.AllItemTargetedLines(), SpellTargetKind.Other);

        if (castLine is null)
        {
            // The player itself too: an "Aura of ... Other" line targets the creature, not the
            // weapon, so it gates TargetIncompatible at every wielded item.
            foreach (ResolvedSpell resolved in learnedItemLines)
            {
                PluginCastGate gate = host.Automation.Magic.EvaluateGate(resolved.Spell.SpellId, player.ObjectId);
                host.Log.Info(
                    $"BuffBot items: gate for {resolved.Line} ({resolved.Spell.SpellId}, target mask "
                    + $"0x{resolved.Spell.TargetMask:X}) at the player {player.Name} = {gate}");
            }

            foreach (PluginWorldObject item in wielded)
                foreach (ResolvedSpell resolved in learnedItemLines)
                {
                    PluginCastGate gate = host.Automation.Magic.EvaluateGate(resolved.Spell.SpellId, item.ObjectId);
                    host.Log.Info(
                        $"BuffBot items: gate for {resolved.Line} ({resolved.Spell.SpellId}) at "
                        + $"{item.Name} ({item.ObjectId}) = {gate}");
                }

            host.Automation.Chat.PostSystemMessage(
                $"BuffBot items: {player.Name} wields {wielded.Count}, {learnedItemLines.Count} learned "
                + "item-targeted line(s) gated — see the log.");
            return;
        }

        ResolvedSpell? chosen = null;
        foreach (ResolvedSpell resolved in learnedItemLines)
        {
            if (string.Equals(resolved.Line, castLine, StringComparison.OrdinalIgnoreCase))
            {
                chosen = resolved;
                break;
            }
        }

        if (chosen is not { } line)
        {
            host.Automation.Chat.PostSystemMessage(
                $"BuffBot items: \"{castLine}\" isn't a learned item-targeted line.");
            return;
        }

        // A raw RequestCast outside magic mode is dropped by the server without a word, which
        // would read as "the target was refused" when it was never tried.
        if (host.Automation.Combat.Snapshot.Mode != PluginCombatMode.Magic)
        {
            host.Automation.Combat.EnterMode(PluginCombatMode.Magic);
            host.Automation.Chat.PostSystemMessage("BuffBot items: entering magic mode first; run the cast again in a moment.");
            return;
        }

        PluginWorldObject? castTarget = null;
        PluginCastGate lastGate = PluginCastGate.Unavailable;
        foreach (PluginWorldObject item in wielded.Append(player))
        {
            PluginCastGate gate = host.Automation.Magic.EvaluateGate(line.Spell.SpellId, item.ObjectId);
            host.Log.Info(
                $"BuffBot items: gate for {line.Line} ({line.Spell.SpellId}) at {item.Name} ({item.ObjectId}) = {gate}");
            if (gate == PluginCastGate.Ready)
            {
                castTarget = item;
                break;
            }
            lastGate = gate;
        }

        if (castTarget is not { } target)
        {
            host.Automation.Chat.PostSystemMessage(
                $"BuffBot items: no wielded object let the gate through for {line.Line} ({lastGate}).");
            return;
        }

        PluginCastRequestResult result = host.Automation.Magic.RequestCast(line.Spell.SpellId, target.ObjectId);
        host.Log.Info(
            $"BuffBot items: RequestCast {line.Line} ({line.Spell.SpellId}) at {target.Name} ({target.ObjectId}) = {result}.");
        host.Automation.Chat.PostSystemMessage(
            $"BuffBot items: cast {line.Line} at {target.Name} -> {result}.");
    }

    /// <summary>Hex, so an operator reading the log can paste one id straight into the next
    /// command.</summary>
    private static string FormatId(uint objectId) => $"0x{objectId:X8}";

    private static string FormatItemResult(PluginItemCommandResult result) =>
        result.Notice is { Length: > 0 } notice ? $"{result.Status} ({notice})" : result.Status.ToString();

    /// <summary>Every host call below logs the result verbatim, prefixed <c>[owner]</c> so a log
    /// grep finds it.</summary>
    private void HandleInvCommand(IPluginHost host, string rest)
    {
        if (!host.Automation.IsAvailable)
            return;

        string[] tokens = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string sub = tokens.Length > 0 ? tokens[0] : string.Empty;

        if (string.Equals(sub, "list", StringComparison.OrdinalIgnoreCase))
        {
            HandleInvList(host, tokens.Length > 1 ? string.Join(' ', tokens[1..]) : string.Empty);
            return;
        }

        if (string.Equals(sub, "use", StringComparison.OrdinalIgnoreCase))
        {
            HandleInvUse(host, tokens);
            return;
        }

        if (string.Equals(sub, "apply", StringComparison.OrdinalIgnoreCase))
        {
            HandleInvApply(host, tokens);
            return;
        }

        if (string.Equals(sub, "split", StringComparison.OrdinalIgnoreCase))
        {
            HandleInvSplit(host, tokens);
            return;
        }

        if (string.Equals(sub, "merge", StringComparison.OrdinalIgnoreCase))
        {
            HandleInvMerge(host, tokens);
            return;
        }

        if (string.Equals(sub, "drop", StringComparison.OrdinalIgnoreCase))
        {
            HandleInvDrop(host, tokens);
            return;
        }

        if (string.Equals(sub, "give", StringComparison.OrdinalIgnoreCase))
        {
            HandleInvGive(host, tokens);
            return;
        }

        host.Automation.Chat.PostSystemMessage(
            "Usage: /buffbot inv list|use|apply|split|merge|drop|give ...");
    }

    private void HandleInvList(IPluginHost host, string filter)
    {
        IReadOnlyList<PluginInventoryItem> items = host.Automation.Items.CaptureOwnedItems();
        IReadOnlyList<PluginInventoryItem> filtered = OwnerItemCommands.FilterOwnedItems(items, filter);

        host.Log.Info(
            $"[owner] inv list{(filter.Length > 0 ? $" \"{filter}\"" : string.Empty)}: "
            + $"{filtered.Count} of {items.Count} owned item(s).");
        foreach (PluginInventoryItem item in filtered)
            host.Log.Info(
                $"[owner] inv {FormatId(item.ObjectId)} \"{item.Name}\" x{item.StackSize} "
                + $"container {FormatId(item.ContainerObjectId)}");

        host.Automation.Chat.PostSystemMessage($"inv list: {filtered.Count} item(s) — see the log.");
    }

    private void HandleInvUse(IPluginHost host, string[] tokens)
    {
        if (tokens.Length < 2 || !OwnerItemCommands.TryParseObjectId(tokens[1], out uint itemId))
        {
            host.Automation.Chat.PostSystemMessage("Usage: /buffbot inv use <itemId>");
            return;
        }

        PluginItemCommandResult result = host.Automation.Items.Use(itemId);
        host.Log.Info($"[owner] inv use {FormatId(itemId)} -> {FormatItemResult(result)}");
        host.Automation.Chat.PostSystemMessage($"inv use {FormatId(itemId)} -> {result.Status}");
    }

    private void HandleInvApply(IPluginHost host, string[] tokens)
    {
        if (tokens.Length < 3
            || !OwnerItemCommands.TryParseObjectId(tokens[1], out uint toolId)
            || !OwnerItemCommands.TryParseObjectId(tokens[2], out uint targetId))
        {
            host.Automation.Chat.PostSystemMessage("Usage: /buffbot inv apply <toolId> <targetId>");
            return;
        }

        PluginItemCommandResult result = host.Automation.Items.Apply(toolId, targetId);
        host.Log.Info(
            $"[owner] inv apply {FormatId(toolId)} -> {FormatId(targetId)} = {FormatItemResult(result)}");
        host.Automation.Chat.PostSystemMessage(
            $"inv apply {FormatId(toolId)} -> {FormatId(targetId)} = {result.Status}");
    }

    private void HandleInvSplit(IPluginHost host, string[] tokens)
    {
        if (tokens.Length < 4
            || !OwnerItemCommands.TryParseObjectId(tokens[1], out uint itemId)
            || !OwnerItemCommands.TryParseObjectId(tokens[2], out uint containerId)
            || !uint.TryParse(tokens[3], out uint amount))
        {
            host.Automation.Chat.PostSystemMessage("Usage: /buffbot inv split <itemId> <containerId> <amount>");
            return;
        }

        PluginItemCommandResult result = host.Automation.Items.MoveToContainer(itemId, containerId, amount);
        host.Log.Info(
            $"[owner] inv split {FormatId(itemId)} -> {FormatId(containerId)} x{amount} = "
            + $"{FormatItemResult(result)}");
        host.Automation.Chat.PostSystemMessage(
            $"inv split {FormatId(itemId)} -> {FormatId(containerId)} x{amount} = {result.Status}");
    }

    private void HandleInvMerge(IPluginHost host, string[] tokens)
    {
        if (tokens.Length < 3
            || !OwnerItemCommands.TryParseObjectId(tokens[1], out uint fromId)
            || !OwnerItemCommands.TryParseObjectId(tokens[2], out uint toId))
        {
            host.Automation.Chat.PostSystemMessage("Usage: /buffbot inv merge <fromId> <toId> [amount]");
            return;
        }

        uint amount = 0u;
        if (tokens.Length > 3 && !uint.TryParse(tokens[3], out amount))
        {
            host.Automation.Chat.PostSystemMessage("Usage: /buffbot inv merge <fromId> <toId> [amount]");
            return;
        }

        PluginItemCommandResult result = host.Automation.Items.Merge(fromId, toId, amount);
        host.Log.Info(
            $"[owner] inv merge {FormatId(fromId)} -> {FormatId(toId)} x{amount} = {FormatItemResult(result)}");
        host.Automation.Chat.PostSystemMessage(
            $"inv merge {FormatId(fromId)} -> {FormatId(toId)} = {result.Status}");
    }

    private void HandleInvDrop(IPluginHost host, string[] tokens)
    {
        if (tokens.Length < 2 || !OwnerItemCommands.TryParseObjectId(tokens[1], out uint itemId))
        {
            host.Automation.Chat.PostSystemMessage("Usage: /buffbot inv drop <itemId> [amount]");
            return;
        }

        uint amount = 0u;
        if (tokens.Length > 2 && !uint.TryParse(tokens[2], out amount))
        {
            host.Automation.Chat.PostSystemMessage("Usage: /buffbot inv drop <itemId> [amount]");
            return;
        }

        PluginItemCommandResult result = host.Automation.Items.Drop(itemId, amount);
        host.Log.Info($"[owner] inv drop {FormatId(itemId)} x{amount} = {FormatItemResult(result)}");
        host.Automation.Chat.PostSystemMessage($"inv drop {FormatId(itemId)} = {result.Status}");
    }

    /// <summary>The last token is the amount only when it parses as one and more than one token
    /// remains, so a target name of two or more words survives.</summary>
    private void HandleInvGive(IPluginHost host, string[] tokens)
    {
        if (tokens.Length < 3 || !OwnerItemCommands.TryParseObjectId(tokens[1], out uint itemId))
        {
            host.Automation.Chat.PostSystemMessage("Usage: /buffbot inv give <itemId> <targetName|targetId> [amount]");
            return;
        }

        string[] rest = tokens[2..];
        int targetTokenCount = rest.Length;
        uint amount = 0u;
        if (rest.Length > 1 && uint.TryParse(rest[^1], out uint parsedAmount))
        {
            amount = parsedAmount;
            targetTokenCount = rest.Length - 1;
        }

        string targetToken = string.Join(' ', rest[..targetTokenCount]);
        if (targetToken.Length == 0)
        {
            host.Automation.Chat.PostSystemMessage("Usage: /buffbot inv give <itemId> <targetName|targetId> [amount]");
            return;
        }

        IReadOnlyList<PluginWorldObject> objects = host.Automation.Objects.CaptureObjects();
        OwnerItemCommands.TargetResolution resolved = OwnerItemCommands.ResolveTarget(objects, targetToken);

        if (resolved.Result == OwnerItemCommands.TargetLookup.NotFound)
        {
            host.Log.Info($"[owner] inv give {FormatId(itemId)} -> \"{targetToken}\": not found.");
            host.Automation.Chat.PostSystemMessage(
                $"inv give: no object named \"{targetToken}\" is captured right now.");
            return;
        }

        if (resolved.Result == OwnerItemCommands.TargetLookup.Ambiguous)
        {
            host.Log.Info(
                $"[owner] inv give {FormatId(itemId)} -> \"{targetToken}\": ambiguous, "
                + $"{resolved.MatchCount} objects share that name.");
            host.Automation.Chat.PostSystemMessage(
                $"inv give: \"{targetToken}\" matches {resolved.MatchCount} objects; use its id instead.");
            return;
        }

        PluginItemCommandResult result = host.Automation.Items.Give(itemId, resolved.ObjectId, amount);
        host.Log.Info(
            $"[owner] inv give {FormatId(itemId)} -> {FormatId(resolved.ObjectId)} x{amount} = "
            + $"{FormatItemResult(result)}");
        host.Automation.Chat.PostSystemMessage(
            $"inv give {FormatId(itemId)} -> {FormatId(resolved.ObjectId)} = {result.Status}");
    }

    private void HandleLogoutCommand(IPluginHost host)
    {
        if (!host.Automation.IsAvailable)
            return;

        bool canRequest = host.Automation.Login.CanRequestLogout;
        host.Log.Info($"[owner] logout: CanRequestLogout = {canRequest}");
        if (!canRequest)
        {
            host.Automation.Chat.PostSystemMessage("logout: not available right now.");
            return;
        }

        bool requested = host.Automation.Login.RequestLogout();
        host.Log.Info($"[owner] logout: RequestLogout() = {requested}");
        host.Automation.Chat.PostSystemMessage($"logout: requested -> {requested}");
    }

    private void HandleChatDumpCommand(IPluginHost host, string rest)
    {
        if (!host.Automation.IsAvailable)
            return;

        int count = DefaultChatDumpCount;
        if (rest.Length > 0 && (!int.TryParse(rest, out count) || count < 0))
        {
            host.Automation.Chat.PostSystemMessage("Usage: /buffbot chatdump [n]");
            return;
        }

        IReadOnlyList<PluginChatMessage> messages = host.Automation.Chat.CaptureMessages(0);
        IReadOnlyList<PluginChatMessage> tail = OwnerItemCommands.TakeLastMessages(messages, count);

        host.Log.Info($"[owner] chatdump: {tail.Count} of {messages.Count} captured message(s).");
        foreach (PluginChatMessage message in tail)
            host.Log.Info(
                $"[owner] chat #{message.Sequence} {message.ChannelName}/{message.Kind} "
                + $"{message.Sender} ({FormatId(message.SenderObjectId)}): {message.Text}");

        host.Automation.Chat.PostSystemMessage($"chatdump: {tail.Count} message(s) — see the log.");
    }

    /// <summary>Cached per character. Default off.</summary>
    private bool IsEnabledForCurrentCharacter(IPluginHost host)
    {
        if (_enablement is null || !host.Automation.IsAvailable)
            return false;

        RefreshCacheIfCharacterChanged(host);
        return _enabledForCachedCharacter;
    }

    private void RefreshCacheIfCharacterChanged(IPluginHost host)
    {
        uint characterObjectId = host.Automation.Character.ObjectId;
        if (_cachedCharacterObjectId == characterObjectId)
            return;

        _cachedCharacterObjectId = characterObjectId;
        _enabledForCachedCharacter = _enablement!.IsEnabled(characterObjectId);
        _announcedEnablementState = false;

        _currentSettings = _settingsStore?.Load(characterObjectId) ?? BuffBotSettings.Default;
    }

    /// <summary>Runs before the enable check, so a console write lands this same tick. <see
    /// cref="_coordinator"/> may not exist yet, so its half is skipped.</summary>
    private void SyncSettingsIntoRuntime()
    {
        _guard.SetMaxRequestsPerWindow(_currentSettings.RepliesPerSenderPerMinute);
        if (_coordinator is not null)
        {
            _coordinator.RefusalRangeMeters = _currentSettings.RefusalRangeMeters;
            // Read at Begin/TryStart, never mid-run, so a change lands on the next run, not this
            // one.
            _coordinator.TargetTier = _currentSettings.TargetTier;
            _coordinator.TierFallbackEnabled = _currentSettings.TierFallback;
            _coordinator.FizzlesBeforeSkip = _currentSettings.FizzlesBeforeSkip;
            _coordinator.ManaBounceLowWaterFraction = _currentSettings.ManaBounceLowWaterFraction;
            _coordinator.ManaBounceHighWaterFraction = _currentSettings.ManaBounceHighWaterFraction;
        }
    }

    /// <summary>Logs once per character, at <see cref="Enable"/> if the automation surface is
    /// already bound, otherwise on the first tick after.</summary>
    private void AnnounceEnablementIfPossible(IPluginHost host)
    {
        if (_enablement is null || !host.Automation.IsAvailable || _announcedEnablementState)
            return;

        RefreshCacheIfCharacterChanged(host);
        _announcedEnablementState = true;

        host.Log.Info(_enabledForCachedCharacter
            ? "BuffBot is enabled for this character."
            : "BuffBot is inert for this character until enabled — send /buffbot on to switch it on.");
    }

    private void ToggleEnabledFromPanel(bool enabled)
    {
        if (_host is { } host)
            SetEnabledForCurrentCharacter(host, enabled, source: "the operator panel");
    }

    private void SetEnabledForCurrentCharacter(IPluginHost host, bool enabled, string source)
    {
        if (_enablement is null || !host.Automation.IsAvailable)
            return;

        uint characterObjectId = host.Automation.Character.ObjectId;
        // A `/buffbot on` sent as a login command reaches here before any tick has refreshed the
        // cache; loading first avoids leaving the bot on default settings for the session.
        RefreshCacheIfCharacterChanged(host);
        _enablement.SetEnabled(characterObjectId, enabled);
        _cachedCharacterObjectId = characterObjectId;
        _enabledForCachedCharacter = enabled;
        _announcedEnablementState = true;

        // Clears the queue and tells each waiting player once; OnTick keeps ticking the
        // coordinator afterward so the run in flight still resolves.
        if (!enabled && _coordinator is not null)
        {
            IReadOnlyList<BuffRequest> drained = _coordinator.RequestStopForDisable();
            foreach (BuffRequest request in drained)
                SendClosingReply(host, request.RequesterObjectId, request.RequesterName, DefaultReplies.TakingABreak);
        }
        else if (enabled)
        {
            _coordinator?.ClearComponentBackoffForReenable();
        }

        host.Automation.Chat.PostSystemMessage(enabled ? DefaultReplies.Enabled : DefaultReplies.Disabled);
        host.Log.Info($"BuffBot {(enabled ? "enabled" : "disabled")} for this character via {source}.");
    }

    private void LogInfo(string message) => _host?.Log.Info(message);

    private void LogWarn(string message) => _host?.Log.Warn(message);
}
