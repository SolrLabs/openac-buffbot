namespace SolrLabs.BuffBot.Stats;

/// <summary>Mirrors the three <see cref="Vocabulary.DefaultReplies"/> refusal replies one for
/// one, so the two never drift.</summary>
internal enum RefusalReason
{
    OutOfRange,
    UnknownLine,
    NothingLearned,
}
