using SolrLabs.BuffBot.Portals;

namespace SolrLabs.BuffBot.Settings;

/// <summary>Every per-character setting the web console's Controls section writes. A plain value, clamped on construction; the mesh's settings patch and <see cref="BuffBotSettingsStore"/> are the only things that touch it from outside this ring. Every field is read live, once a tick, taking effect on the next run rather than the one already in flight.</summary>
internal sealed record BuffBotSettings(
    bool SelfBuffUpkeep,
    double RefusalRangeMeters,
    int RepliesPerSenderPerMinute,
    bool IntakePaused,
    int? TargetTier,
    bool TierFallback,
    int FizzlesBeforeSkip,
    PortalTie PrimaryPortal,
    PortalTie SecondaryPortal,
    int ComponentLowStock = BuffBotSettings.DefaultComponentLowStock,
    double ManaBounceLowWaterFraction = BuffBotSettings.DefaultManaBounceLowWaterFraction,
    double ManaBounceHighWaterFraction = BuffBotSettings.DefaultManaBounceHighWaterFraction,
    bool SplitPeas = BuffBotSettings.DefaultSplitPeas,
    double QueuePauseSeconds = BuffBotSettings.DefaultQueuePauseSeconds)
{
    internal const bool DefaultSelfBuffUpkeep = true;

    /// <summary>90% of <see cref="Policy.RangePolicy.MaxCastRangeMeters"/>, restated as a plain number to avoid importing <c>Policy</c>.</summary>
    internal const double DefaultRefusalRangeMeters = 67.5;
    internal const double MinRefusalRangeMeters = 40d;
    internal const double MaxRefusalRangeMeters = 75d;

    /// <summary>Matches <see cref="Guard.LoopGuard.MaxRequestsPerWindow"/>'s own default.</summary>
    internal const int DefaultRepliesPerSenderPerMinute = 12;
    internal const int MinRepliesPerSenderPerMinute = 2;
    internal const int MaxRepliesPerSenderPerMinute = 20;

    internal const bool DefaultIntakePaused = false;

    /// <summary><see langword="null"/> means "the top learned tier" — never a stored 0.</summary>
    internal const int MinTargetTier = 1;
    internal const int MaxTargetTier = 8;

    internal const bool DefaultTierFallback = true;

    internal const int DefaultFizzlesBeforeSkip = 6;
    internal const int MinFizzlesBeforeSkip = 1;
    internal const int MaxFizzlesBeforeSkip = 20;

    /// <summary>Below this stock, the console's components tile shows a reagent in the warning colour.</summary>
    internal const int DefaultComponentLowStock = 25;
    internal const int MinComponentLowStock = 0;
    internal const int MaxComponentLowStock = 10000;

    /// <summary>Fractions of max mana for the mana bar's draggable low/high handles.</summary>
    internal const double DefaultManaBounceLowWaterFraction = 0.20;
    internal const double DefaultManaBounceHighWaterFraction = 0.80;
    internal const double MinManaBounceLowWaterFraction = 0.05;
    internal const double MaxManaBounceLowWaterFraction = 0.60;
    internal const double MinManaBounceHighWaterFraction = 0.40;
    internal const double MaxManaBounceHighWaterFraction = 1.00;

    /// <summary>The smallest gap <see cref="Clamped"/> allows between the two handles.</summary>
    internal const double MinManaBounceGapFraction = 0.10;

    internal const bool DefaultSplitPeas = true;

    /// <summary>Held quietly between two queued requesters, giving room to open a trade.</summary>
    internal const double DefaultQueuePauseSeconds = 7d;
    internal const double MinQueuePauseSeconds = 5d;
    internal const double MaxQueuePauseSeconds = 10d;

    internal static readonly BuffBotSettings Default = new(
        DefaultSelfBuffUpkeep,
        DefaultRefusalRangeMeters,
        DefaultRepliesPerSenderPerMinute,
        DefaultIntakePaused,
        TargetTier: null,
        DefaultTierFallback,
        DefaultFizzlesBeforeSkip,
        PortalTie.Empty,
        PortalTie.Empty,
        DefaultComponentLowStock,
        DefaultManaBounceLowWaterFraction,
        DefaultManaBounceHighWaterFraction,
        DefaultSplitPeas,
        DefaultQueuePauseSeconds);

    /// <summary>Every field clamped to its own bound; never throws. The mana-bounce pair clamps each handle first, then widens the high handle to keep <see cref="MinManaBounceGapFraction"/> between them.</summary>
    internal BuffBotSettings Clamped()
    {
        double clampedLow = Math.Clamp(
            ManaBounceLowWaterFraction, MinManaBounceLowWaterFraction, MaxManaBounceLowWaterFraction);
        double clampedHigh = Math.Clamp(
            ManaBounceHighWaterFraction, MinManaBounceHighWaterFraction, MaxManaBounceHighWaterFraction);
        if (clampedHigh < clampedLow + MinManaBounceGapFraction)
            clampedHigh = clampedLow + MinManaBounceGapFraction;

        return new(
            SelfBuffUpkeep,
            Math.Clamp(RefusalRangeMeters, MinRefusalRangeMeters, MaxRefusalRangeMeters),
            Math.Clamp(RepliesPerSenderPerMinute, MinRepliesPerSenderPerMinute, MaxRepliesPerSenderPerMinute),
            IntakePaused,
            TargetTier is { } tier ? Math.Clamp(tier, MinTargetTier, MaxTargetTier) : null,
            TierFallback,
            Math.Clamp(FizzlesBeforeSkip, MinFizzlesBeforeSkip, MaxFizzlesBeforeSkip),
            PrimaryPortal.Clamped(),
            SecondaryPortal.Clamped(),
            Math.Clamp(ComponentLowStock, MinComponentLowStock, MaxComponentLowStock),
            clampedLow,
            clampedHigh,
            SplitPeas,
            Math.Clamp(QueuePauseSeconds, MinQueuePauseSeconds, MaxQueuePauseSeconds));
    }

    /// <summary>Applies a partial change, each field left alone when its patch value is <see langword="null"/> — except <see cref="TargetTier"/>, whose <paramref name="hasTargetTier"/> tells an explicit "set it back to top learned" apart from "untouched". A tie patches as a whole: <paramref name="primaryPortal"/> or <paramref name="secondaryPortal"/> null leaves that tie alone, non-null replaces it outright. Clamped on the way out.</summary>
    internal BuffBotSettings WithPatch(
        bool? selfBuffUpkeep,
        double? refusalRangeMeters,
        int? repliesPerSenderPerMinute,
        bool? intakePaused,
        bool hasTargetTier,
        int? targetTier,
        bool? tierFallback,
        int? fizzlesBeforeSkip,
        int? componentLowStock = null,
        double? manaBounceLowWaterFraction = null,
        double? manaBounceHighWaterFraction = null,
        bool? splitPeas = null,
        PortalTie? primaryPortal = null,
        PortalTie? secondaryPortal = null,
        double? queuePauseSeconds = null) => new BuffBotSettings(
        selfBuffUpkeep ?? SelfBuffUpkeep,
        refusalRangeMeters ?? RefusalRangeMeters,
        repliesPerSenderPerMinute ?? RepliesPerSenderPerMinute,
        intakePaused ?? IntakePaused,
        hasTargetTier ? targetTier : TargetTier,
        tierFallback ?? TierFallback,
        fizzlesBeforeSkip ?? FizzlesBeforeSkip,
        primaryPortal ?? PrimaryPortal,
        secondaryPortal ?? SecondaryPortal,
        componentLowStock ?? ComponentLowStock,
        manaBounceLowWaterFraction ?? ManaBounceLowWaterFraction,
        manaBounceHighWaterFraction ?? ManaBounceHighWaterFraction,
        splitPeas ?? SplitPeas,
        queuePauseSeconds ?? QueuePauseSeconds).Clamped();
}
