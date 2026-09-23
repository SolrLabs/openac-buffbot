using SolrLabs.BuffBot.Casting;
using SolrLabs.BuffBot.Guard;
using SolrLabs.BuffBot.Requests;
using SolrLabs.BuffBot.Stats;
using SolrLabs.BuffBot.Timing;

namespace SolrLabs.BuffBot.Web;

/// <summary>Turns the coordinator's own <see cref="BuffBotStatus"/> into the wire's <see cref="MeshStatus"/>, kept as a separate type so neither side has to change shape for the other's reason.</summary>
internal static class MeshStatusMapper
{
    internal static MeshStatus From(BuffBotStatus status, IClock clock)
    {
        var waiting = new List<MeshWaitingEntry>(status.Waiting.Count);
        foreach (QueueEntry entry in status.Waiting)
            waiting.Add(new MeshWaitingEntry(entry.ObjectId, entry.Name, entry.Archetype, entry.WaitingSeconds));

        SessionCounters counters = status.Counters;
        Settings.BuffBotSettings settings = status.Settings ?? Settings.BuffBotSettings.Default;
        Components.ComponentReport report = status.Components ?? Components.ComponentReport.Unavailable;
        var componentItems = new List<MeshComponentItem>(report.Items.Count);
        foreach (Components.ComponentUsage usage in report.Items)
            componentItems.Add(new MeshComponentItem(usage.WeenieClassId, usage.Name, usage.Stock, usage.UsedBy));

        var lines = new List<MeshSpellLineStats>(status.Stats.Lines.Count);
        foreach (SpellLineStats line in status.Stats.Lines)
            lines.Add(new MeshSpellLineStats(line.Line, line.Attempts, line.Landed, line.Fizzles));

        var hours = new List<MeshHourlyBucket>(status.Stats.Hours.Count);
        foreach (HourlyBucket hour in status.Stats.Hours)
            hours.Add(new MeshHourlyBucket(hour.HourStartUtc, hour.Requested, hour.Upkeep, hour.Fizzles));

        RefusalCounts refusals = status.Stats.Refusals;
        var meshStats = new MeshStats(
            status.Stats.StartedUtc,
            new MeshRefusalCounts(
                refusals.Total, refusals.OutOfRange, refusals.UnknownLine, refusals.NothingLearned,
                refusals.Unresolvable),
            lines,
            hours,
            new MeshWaitStats(status.Stats.Wait.MedianSeconds, status.Stats.Wait.LongestSeconds),
            status.Stats.PlayersServed,
            status.Stats.PlayersReturning);

        var recent = new List<MeshRecentEvent>(status.Recent.Count);
        foreach (RecentEvent evt in status.Recent)
            recent.Add(new MeshRecentEvent(
                evt.Timestamp, RecentEventName(evt.Kind), evt.Line, evt.TargetName, evt.Reason,
                evt.ManaBefore, evt.ManaAfter, evt.Who, evt.Archetype, evt.Source));

        return new MeshStatus(
            status.Enabled,
            ActivityName(status.Activity),
            status.CurrentRequesterName,
            status.CurrentRequesterObjectId,
            status.CurrentSpellLine,
            status.CurrentSpellId,
            status.StepIndex,
            status.StepCount,
            status.CurrentMana,
            status.MaxMana,
            waiting,
            new MeshCounters(
                counters.TellsAnswered, counters.CastsLanded, counters.Fizzles,
                counters.ManaBounces, counters.TierStepDowns),
            meshStats,
            recent,
            new MeshSettings(
                settings.SelfBuffUpkeep, settings.RefusalRangeMeters, settings.RepliesPerSenderPerMinute,
                settings.IntakePaused, settings.TargetTier, settings.TierFallback, settings.FizzlesBeforeSkip,
                settings.ComponentLowStock, settings.ManaBounceLowWaterFraction, settings.ManaBounceHighWaterFraction,
                settings.SplitPeas, PortalTieObject(settings.PrimaryPortal), PortalTieObject(settings.SecondaryPortal),
                settings.QueuePauseSeconds),
            new MeshComponents(report.Available, componentItems, report.CatalogAvailable),
            status.CurrentHealth, status.MaxHealth, status.CurrentStamina, status.MaxStamina,
            status.TradeOpen, status.DonationsCompleted, status.DonationItemsReceived);
    }

    private static string RecentEventName(RecentEventKind kind) => kind switch
    {
        RecentEventKind.Cast => "cast",
        RecentEventKind.Fizzle => "fizzle",
        RecentEventKind.Skipped => "skipped",
        RecentEventKind.ManaBounce => "manaBounce",
        RecentEventKind.RequestAccepted => "requestAccepted",
        RecentEventKind.Refused => "refused",
        RecentEventKind.Mute => "mute",
        RecentEventKind.Release => "release",
        _ => "unwedge",
    };

    private static string ActivityName(BotActivity activity) => activity switch
    {
        BotActivity.Serving => "serving",
        BotActivity.SelfBuffing => "selfBuffing",
        BotActivity.ToppingUp => "toppingUp",
        BotActivity.SummoningPortal => "summoningPortal",
        _ => "idle",
    };

    private static MeshPortalTie PortalTieObject(Portals.PortalTie tie) =>
        new(tie.Description, Portals.PortalDirectionText.ToText(tie.Direction));
}
