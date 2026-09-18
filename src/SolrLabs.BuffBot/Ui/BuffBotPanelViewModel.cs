using SolrLabs.BuffBot.Casting;
using SolrLabs.BuffBot.Guard;
using SolrLabs.BuffBot.Requests;
using SolrLabs.BuffBot.Stats;
using SolrLabs.BuffBot.Timing;
using SolrLabs.BuffBot.Web;

namespace SolrLabs.BuffBot.Ui;

/// <summary>Bindings are pull-based reads of whatever <see cref="UpdateStatus"/> last stored — the
/// plugin's tick thread assigns <see cref="_status"/> by reference and the render thread reads it.</summary>
internal sealed class BuffBotPanelViewModel
{
    /// <summary>What a fresh <see cref="Stats.BotStats"/> would report before the plugin ever ticks.</summary>
    private static readonly BotStatsSnapshot EmptyStats = new BotStats().Snapshot();

    private static readonly BuffBotStatus InitialStatus = new(
        Enabled: false,
        Activity: BotActivity.Idle,
        CurrentRequesterName: null,
        CurrentRequesterObjectId: null,
        CurrentSpellLine: null,
        CurrentSpellId: 0u,
        StepIndex: 0,
        StepCount: 0,
        CurrentMana: 0u,
        MaxMana: 0u,
        Waiting: Array.Empty<QueueEntry>(),
        Muted: Array.Empty<MutedEntry>(),
        Counters: new SessionCounters(0, 0, 0, 0, 0),
        Stats: EmptyStats.Session,
        Recent: EmptyStats.Recent);

    private readonly Action<bool> _setEnabled;
    private readonly IClock _clock;
    private readonly Action<string> _logWarn;
    private readonly IBrowserLauncher _launcher;
    private readonly BotStats? _stats;

    private BuffBotStatus _status = InitialStatus;
    private MeshConsoleLink _console = new(MeshConsoleState.Unavailable, null);
    private int _selectedWaitingIndex = -1;

    internal BuffBotPanelViewModel(
        Action<bool>? setEnabled = null,
        IClock? clock = null,
        Action<string>? logWarn = null,
        IBrowserLauncher? launcher = null,
        BotStats? stats = null)
    {
        _setEnabled = setEnabled ?? (static _ => { });
        _clock = clock ?? SystemClock.Instance;
        _logWarn = logWarn ?? (static _ => { });
        _launcher = launcher ?? NullBrowserLauncher.Instance;
        _stats = stats;
    }

    /// <summary>Called once a tick with the latest snapshot, either the coordinator's real one or the minimal disabled snapshot <see cref="BuffBotPlugin"/> builds on the early-return path, so the panel stays live rather than freezing on stale state.</summary>
    internal void UpdateStatus(BuffBotStatus status) => _status = status;

    /// <summary>Called once a tick with this node's place on the web console mesh, independent of <see cref="UpdateStatus"/> since the mesh node runs regardless of this character's enable switch.</summary>
    internal void UpdateConsole(MeshConsoleLink console) => _console = console;

    public string EnabledLabel => _status.Enabled ? "Enabled" : "Disabled";

    /// <summary>The toggle button's own text: always the action it takes, the opposite of <see cref="EnabledLabel"/>'s current state.</summary>
    public string EnableToggleLabel => _status.Enabled ? "Disable" : "Enable";

    /// <summary>Same effect as <c>/buffbot on</c>/<c>off</c>, including the drain-and-tell on disable — reads <see cref="_status"/> rather than carrying its own idea of the state, so it flips correctly whether the bot is enabled or inert right now.</summary>
    public Action ToggleEnabled => () => _setEnabled(!_status.Enabled);

    public string ActivityLabel => _status.Activity switch
    {
        BotActivity.Serving => $"Serving {_status.CurrentRequesterName}",
        BotActivity.SelfBuffing => "Self-buffing",
        BotActivity.ToppingUp => "Topping up mana",
        _ => "Idle",
    };

    /// <summary>Feeds <c>&lt;icon spell="{CurrentSpellId}"/&gt;</c>.</summary>
    public uint CurrentSpellId => _status.CurrentSpellId;

    public string SpellLineLabel => _status.CurrentSpellLine ?? string.Empty;

    public string StepLabel => _status.StepCount > 0
        ? $"step {_status.StepIndex} of {_status.StepCount}"
        : string.Empty;

    public IReadOnlyList<string> WaitingNames =>
        _status.Waiting.Select(static entry => entry.Name).ToArray();

    public IReadOnlyList<string> WaitingArchetypes =>
        _status.Waiting.Select(static entry => entry.Archetype).ToArray();

    public IReadOnlyList<string> WaitingTimes =>
        _status.Waiting.Select(static entry => FormatMinSec(entry.WaitingSeconds)).ToArray();

    public int SelectedWaitingIndex => _selectedWaitingIndex;

    public Action<int> SelectWaitingRow => index => _selectedWaitingIndex = index;

    public string CountersLabel
    {
        get
        {
            SessionCounters counters = _status.Counters;
            return $"tells {counters.TellsAnswered} · casts {counters.CastsLanded} · "
                + $"fizzles {counters.Fizzles} · bounces {counters.ManaBounces} · "
                + $"step-downs {counters.TierStepDowns}";
        }
    }

    public string ConsoleStateLabel => _console.State switch
    {
        MeshConsoleState.Hub => "Web console: hub",
        MeshConsoleState.Spoke => "Web console: spoke",
        MeshConsoleState.Starting => "Web console: deciding a key…",
        _ => "Web console: unavailable",
    };

    /// <summary>Empty whenever <see cref="_console"/> carries no link, never a stale or invalid one.</summary>
    public string ConsoleLink => _console.Link ?? string.Empty;

    /// <summary>The link without its query string, since a panel can end up in a screenshot or stream and the token is a mesh key; the Open button still uses <see cref="ConsoleLink"/>.</summary>
    public string ConsoleAddress
    {
        get
        {
            string link = ConsoleLink;
            int query = link.IndexOf('?');
            return query < 0 ? link : link[..query];
        }
    }

    /// <summary>Gates the Open web console button so a click with nothing to open is refused by the widget itself.</summary>
    public bool HasConsoleLink => !string.IsNullOrEmpty(_console.Link);

    /// <summary>A launcher failure is caught and logged as a warning rather than thrown.</summary>
    public Action OpenConsole => () =>
    {
        string? link = _console.Link;
        if (string.IsNullOrEmpty(link))
            return;

        try
        {
            _launcher.Open(link);
        }
        catch (Exception error)
        {
            _logWarn($"BuffBot: could not open the web console ({error.Message}).");
        }
    };

    private static string FormatMinSec(double seconds)
    {
        int total = (int)Math.Max(0, Math.Round(seconds));
        return $"{total / 60}:{total % 60:D2}";
    }
}
