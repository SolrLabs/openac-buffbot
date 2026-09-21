using SolrLabs.BuffBot.Casting;
using SolrLabs.BuffBot.Requests;
using SolrLabs.BuffBot.Stats;
using SolrLabs.BuffBot.Tests.Timing;
using SolrLabs.BuffBot.Ui;
using SolrLabs.BuffBot.Web;

namespace SolrLabs.BuffBot.Tests.Ui;

public sealed class BuffBotPanelViewModelTests
{
    private const uint WaitingId = 11;
    private const string WaitingName = "Probe";
    private const uint ServedId = 33;
    private static readonly BotStatsSnapshot EmptyStats = new BotStats().Snapshot();

    [Fact]
    public void ProjectsEnabledAndIdleActivityBeforeAnyStatusArrives()
    {
        BuffBotPanelViewModel panel = NewPanel();

        Assert.Equal("Disabled", panel.EnabledLabel);
        Assert.Equal("Idle", panel.ActivityLabel);
        Assert.Equal(string.Empty, panel.SpellLineLabel);
        Assert.Equal(string.Empty, panel.StepLabel);
        Assert.Empty(panel.WaitingNames);
    }

    [Fact]
    public void ProjectsAServingSnapshot()
    {
        BuffBotPanelViewModel panel = NewPanel();
        panel.UpdateStatus(new BuffBotStatus(
            Enabled: true,
            Activity: BotActivity.Serving,
            CurrentRequesterName: "Archer",
            CurrentRequesterObjectId: ServedId,
            CurrentSpellLine: "Strength Other",
            CurrentSpellId: 0x10E4u,
            StepIndex: 3,
            StepCount: 7,
            CurrentMana: 750u,
            MaxMana: 1000u,
            Waiting: [new QueueEntry(WaitingId, WaitingName, "heavy", 42d)],
            Muted: [],
            Counters: new SessionCounters(5, 4, 1, 0, 0),
            Stats: EmptyStats.Session,
            Recent: EmptyStats.Recent));

        Assert.Equal("Enabled", panel.EnabledLabel);
        Assert.Equal("Serving Archer", panel.ActivityLabel);
        Assert.Equal(0x10E4u, panel.CurrentSpellId);
        Assert.Equal("Strength Other", panel.SpellLineLabel);
        Assert.Equal("step 3 of 7", panel.StepLabel);
        Assert.Equal([WaitingName], panel.WaitingNames);
        Assert.Equal(["heavy"], panel.WaitingArchetypes);
        Assert.Equal(["0:42"], panel.WaitingTimes);
        Assert.Equal(
            "tells 5 · casts 4 · fizzles 1 · bounces 0 · step-downs 0",
            panel.CountersLabel);
    }

    [Fact]
    public void ProjectsASummoningPortalActivity()
    {
        BuffBotPanelViewModel panel = NewPanel();
        panel.UpdateStatus(StatusWith(currentMana: 0, maxMana: 0) with { Activity = BotActivity.SummoningPortal });

        Assert.Equal("Summoning a portal", panel.ActivityLabel);
    }

    [Fact]
    public void EnableToggleLabelIsEnableBeforeAnyStatusArrives()
    {
        BuffBotPanelViewModel panel = NewPanel();

        Assert.Equal("Enable", panel.EnableToggleLabel);
    }

    [Fact]
    public void EnableToggleLabelIsDisableOnceTheStatusReportsEnabled()
    {
        BuffBotPanelViewModel panel = NewPanel();
        panel.UpdateStatus(StatusWith(currentMana: 0, maxMana: 0)); // Enabled: true

        Assert.Equal("Disable", panel.EnableToggleLabel);
    }

    [Fact]
    public void ToggleEnabledAsksToEnableWhileInert()
    {
        var calls = new List<bool>();
        var panel = new BuffBotPanelViewModel(setEnabled: calls.Add);

        panel.ToggleEnabled();

        Assert.Equal([true], calls);
    }

    [Fact]
    public void ToggleEnabledAsksToDisableOnceEnabled()
    {
        var calls = new List<bool>();
        var panel = new BuffBotPanelViewModel(setEnabled: calls.Add);
        panel.UpdateStatus(StatusWith(currentMana: 0, maxMana: 0)); // Enabled: true

        panel.ToggleEnabled();

        Assert.Equal([false], calls);
    }

    [Fact]
    public void SelectWaitingRowUpdatesSelectedWaitingIndex()
    {
        BuffBotPanelViewModel panel = NewPanel();
        panel.UpdateStatus(StatusWithWaiting(new QueueEntry(WaitingId, WaitingName, "heavy", 0d)));

        panel.SelectWaitingRow(0);

        Assert.Equal(0, panel.SelectedWaitingIndex);
    }

    [Fact]
    public void ConsoleStartsUnavailableWithNoLinkBeforeAnyStateArrives()
    {
        BuffBotPanelViewModel panel = NewPanel();

        Assert.Equal("Web console: unavailable", panel.ConsoleStateLabel);
        Assert.Equal(string.Empty, panel.ConsoleLink);
        Assert.False(panel.HasConsoleLink);
    }

    [Fact]
    public void ConsoleStateLabelNamesEachOfTheFourStates()
    {
        AssertConsoleStateLabel(MeshConsoleState.Hub, "Web console: hub");
        AssertConsoleStateLabel(MeshConsoleState.Spoke, "Web console: spoke");
        AssertConsoleStateLabel(MeshConsoleState.Starting, "Web console: deciding a key…");
        AssertConsoleStateLabel(MeshConsoleState.Unavailable, "Web console: unavailable");
    }

    private static void AssertConsoleStateLabel(MeshConsoleState state, string expected)
    {
        BuffBotPanelViewModel panel = NewPanel();
        panel.UpdateConsole(new MeshConsoleLink(state, Link: null));

        Assert.Equal(expected, panel.ConsoleStateLabel);
    }

    [Fact]
    public void ConsoleLinkAndHasConsoleLinkReflectAHubsSettledLink()
    {
        BuffBotPanelViewModel panel = NewPanel();
        panel.UpdateConsole(new MeshConsoleLink(MeshConsoleState.Hub, "http://127.0.0.1:8347/?token=abc"));

        Assert.True(panel.HasConsoleLink);
        Assert.Equal("http://127.0.0.1:8347/?token=abc", panel.ConsoleLink);
        Assert.Equal("http://127.0.0.1:8347/", panel.ConsoleAddress);
    }

    [Fact]
    public void ConsoleLinkIsEmptyWheneverThereIsNoLinkToOffer()
    {
        AssertNoConsoleLink(MeshConsoleState.Starting);
        AssertNoConsoleLink(MeshConsoleState.Unavailable);
    }

    private static void AssertNoConsoleLink(MeshConsoleState state)
    {
        BuffBotPanelViewModel panel = NewPanel();
        panel.UpdateConsole(new MeshConsoleLink(state, Link: null));

        Assert.False(panel.HasConsoleLink);
        Assert.Equal(string.Empty, panel.ConsoleLink);
    }

    [Fact]
    public void OpenConsoleLaunchesTheExactCurrentLink()
    {
        var launcher = new FakeBrowserLauncher();
        var panel = new BuffBotPanelViewModel(launcher: launcher);
        panel.UpdateConsole(new MeshConsoleLink(MeshConsoleState.Hub, "http://127.0.0.1:8347/?token=abc"));

        panel.OpenConsole();

        Assert.Equal(["http://127.0.0.1:8347/?token=abc"], launcher.Opened);
    }

    [Fact]
    public void OpenConsoleNeverCallsTheLauncherWithoutALink()
    {
        AssertOpenConsoleDoesNotLaunch(MeshConsoleState.Starting);
        AssertOpenConsoleDoesNotLaunch(MeshConsoleState.Unavailable);
    }

    private static void AssertOpenConsoleDoesNotLaunch(MeshConsoleState state)
    {
        var launcher = new FakeBrowserLauncher();
        var panel = new BuffBotPanelViewModel(launcher: launcher);
        panel.UpdateConsole(new MeshConsoleLink(state, Link: null));

        panel.OpenConsole();

        Assert.Empty(launcher.Opened);
    }

    [Fact]
    public void OpenConsoleSwallowsALauncherFailureAndLogsItAsAWarning()
    {
        var launcher = new FakeBrowserLauncher { ThrowOnOpen = new InvalidOperationException("no default browser") };
        var warnings = new List<string>();
        var panel = new BuffBotPanelViewModel(logWarn: warnings.Add, launcher: launcher);
        panel.UpdateConsole(new MeshConsoleLink(MeshConsoleState.Hub, "http://127.0.0.1:8347/?token=abc"));

        Exception? exception = Record.Exception(() => panel.OpenConsole());

        Assert.Null(exception);
        Assert.Single(warnings);
        Assert.Contains("no default browser", warnings[0]);
    }

    private sealed class FakeBrowserLauncher : IBrowserLauncher
    {
        internal List<string> Opened { get; } = [];
        internal Exception? ThrowOnOpen { get; set; }

        public void Open(string url)
        {
            if (ThrowOnOpen is { } error)
                throw error;
            Opened.Add(url);
        }
    }

    private static BuffBotPanelViewModel NewPanel(FakeClock? clock = null) =>
        new(clock: clock ?? new FakeClock());

    private static BuffBotStatus StatusWith(uint currentMana, uint maxMana) => new(
        Enabled: true,
        Activity: BotActivity.Idle,
        CurrentRequesterName: null,
        CurrentRequesterObjectId: null,
        CurrentSpellLine: null,
        CurrentSpellId: 0u,
        StepIndex: 0,
        StepCount: 0,
        CurrentMana: currentMana,
        MaxMana: maxMana,
        Waiting: [],
        Muted: [],
        Counters: new SessionCounters(0, 0, 0, 0, 0),
        Stats: EmptyStats.Session,
        Recent: EmptyStats.Recent);

    private static BuffBotStatus StatusWithWaiting(params QueueEntry[] waiting) => new(
        Enabled: true,
        Activity: BotActivity.Idle,
        CurrentRequesterName: null,
        CurrentRequesterObjectId: null,
        CurrentSpellLine: null,
        CurrentSpellId: 0u,
        StepIndex: 0,
        StepCount: 0,
        CurrentMana: 0u,
        MaxMana: 0u,
        Waiting: waiting,
        Muted: [],
        Counters: new SessionCounters(0, 0, 0, 0, 0),
        Stats: EmptyStats.Session,
        Recent: EmptyStats.Recent);
}
