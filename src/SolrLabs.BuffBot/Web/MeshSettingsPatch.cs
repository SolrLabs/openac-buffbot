namespace SolrLabs.BuffBot.Web;

/// <summary>The wire's partial settings write: only a field the console actually changed is present, everything else is left alone. Every field but <see cref="TargetTier"/> uses a nullable scalar for that. <see cref="TargetTier"/> is legitimately nullable on the model (<see langword="null"/> means "top learned"), so <see cref="HasTargetTier"/> distinguishes an explicit JSON <c>null</c> from the key being missing entirely.</summary>
internal sealed record MeshSettingsPatch(
    bool? SelfBuffUpkeep,
    double? RefusalRangeMeters,
    int? RepliesPerSenderPerMinute,
    bool? IntakePaused,
    bool HasTargetTier,
    int? TargetTier,
    bool? TierFallback,
    int? FizzlesBeforeSkip,
    int? ComponentLowStock = null,
    double? ManaBounceLowWaterFraction = null,
    double? ManaBounceHighWaterFraction = null,
    bool? SplitPeas = null);
