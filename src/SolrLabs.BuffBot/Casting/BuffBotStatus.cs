using SolrLabs.BuffBot.Guard;
using SolrLabs.BuffBot.Requests;
using SolrLabs.BuffBot.Stats;

namespace SolrLabs.BuffBot.Casting;

internal enum BotActivity
{
    Idle,
    Serving,
    SelfBuffing,

    // Bounces mana toward the high-water mark between two requesters' chains.
    ToppingUp,

    // A held chain (if any) still counts as this, not Idle — see the portal lane.
    SummoningPortal,
}

internal readonly record struct SessionCounters(
    int TellsAnswered, int CastsLanded, int Fizzles, int ManaBounces, int TierStepDowns);

internal sealed record BuffBotStatus(
    bool Enabled,
    BotActivity Activity,
    string? CurrentRequesterName,
    uint? CurrentRequesterObjectId,
    string? CurrentSpellLine,
    uint CurrentSpellId,
    int StepIndex,
    int StepCount,
    uint CurrentMana,
    uint MaxMana,
    IReadOnlyList<QueueEntry> Waiting,
    IReadOnlyList<MutedEntry> Muted,
    SessionCounters Counters,
    SessionStatsSnapshot Stats,
    IReadOnlyList<RecentEvent> Recent,
    Settings.BuffBotSettings? Settings = null,
    Components.ComponentReport? Components = null,
    uint? CurrentHealth = null,
    uint? MaxHealth = null,
    uint? CurrentStamina = null,
    uint? MaxStamina = null,
    bool TradeOpen = false,
    int DonationsCompleted = 0,
    int DonationItemsReceived = 0);
