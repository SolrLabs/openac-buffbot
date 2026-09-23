namespace SolrLabs.BuffBot.Stats;

/// <summary>Mirrors the four <see cref="Vocabulary.DefaultReplies"/> refusal replies one for one.</summary>
internal enum RefusalReason
{
    OutOfRange,
    UnknownLine,
    NothingLearned,
    Unresolvable,
}
