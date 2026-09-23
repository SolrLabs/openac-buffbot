using SolrLabs.BuffBot.Timing;

namespace SolrLabs.BuffBot.Stats;

/// <summary><see cref="DonationsCompleted"/>/<see cref="DonationItemsReceived"/> fold in <see
/// cref="Donations.TradeDonationSession"/>'s own completions, rather than a second snapshot type.</summary>
internal sealed record BotStatsSnapshot(
    SessionStatsSnapshot Session,
    IReadOnlyList<RecentEvent> Recent,
    int DonationsCompleted = 0,
    int DonationItemsReceived = 0);

/// <summary>Constructed once per plugin instance and shared for its whole lifetime, unlike <see
/// cref="Casting.CastStateMachine"/>'s own totals, rebuilt on every (re-)enable.</summary>
internal sealed class BotStats
{
    private readonly IClock _clock;
    private readonly SessionStats _session;
    private readonly ActivityLog _log;
    private int _donationsCompleted;
    private int _donationItemsReceived;

    internal BotStats(IClock? clock = null)
    {
        _clock = clock ?? SystemClock.Instance;
        _session = new SessionStats(_clock);
        _log = new ActivityLog();
    }

    internal void RecordCastLanded(string line, string targetName, bool isPlayerRequest)
    {
        _session.RecordCastLanded(line, isPlayerRequest);
        _log.Add(RecentEvent.Cast(_clock.UtcNow, line, targetName));
    }

    internal void RecordFizzle(string line)
    {
        _session.RecordFizzle(line);
        _log.Add(RecentEvent.Fizzle(_clock.UtcNow, line));
    }

    internal void RecordSpellSkipped(string line, string reason) =>
        _log.Add(RecentEvent.Skipped(_clock.UtcNow, line, reason));

    internal void RecordOtherAttemptFailure(string line) => _session.RecordOtherAttemptFailure(line);

    internal void RecordManaBounce(uint before, uint after) =>
        _log.Add(RecentEvent.ManaBounce(_clock.UtcNow, before, after));

    internal void RecordRequestAccepted(string requesterName, string archetype) =>
        _log.Add(RecentEvent.RequestAccepted(_clock.UtcNow, requesterName, archetype));

    internal void RecordRefusal(RefusalReason reason, string requesterName)
    {
        _session.RecordRefusal(reason);
        _log.Add(RecentEvent.Refused(_clock.UtcNow, requesterName, ReasonText(reason)));
    }

    internal void RecordMute(string who, string source) =>
        _log.Add(RecentEvent.Mute(_clock.UtcNow, who, source));

    internal void RecordRelease(string who, string source) =>
        _log.Add(RecentEvent.Release(_clock.UtcNow, who, source));

    internal void RecordUnwedge(string source) =>
        _log.Add(RecentEvent.Unwedge(_clock.UtcNow, source));

    internal void RecordWaitSeconds(double seconds) => _session.RecordWaitSeconds(seconds);

    internal void RecordPlayerServed(uint requesterObjectId) => _session.RecordPlayerServed(requesterObjectId);

    /// <summary>Never derived from <see cref="Donations.ContributorLedger"/> itself, so this
    /// stays available to the console's Live tab before the ledger is ever read back.</summary>
    internal void RecordDonation(int itemCount)
    {
        _donationsCompleted++;
        _donationItemsReceived += itemCount;
    }

    internal BotStatsSnapshot Snapshot() =>
        new(_session.Snapshot(), _log.Snapshot(), _donationsCompleted, _donationItemsReceived);

    private static string ReasonText(RefusalReason reason) => reason switch
    {
        RefusalReason.OutOfRange => "out of range",
        RefusalReason.UnknownLine => "unknown line",
        RefusalReason.NothingLearned => "nothing learned in profile",
        RefusalReason.Unresolvable => "requester not found",
        _ => "unknown",
    };
}
