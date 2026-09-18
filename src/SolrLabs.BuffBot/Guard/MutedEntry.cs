namespace SolrLabs.BuffBot.Guard;

/// <summary>One muted sender for the operator panel: who, until when, and which kind. <see cref="ObjectId"/> lets the view-model release the right person from a selected list index.</summary>
internal readonly record struct MutedEntry(uint ObjectId, string Name, DateTimeOffset? Until, bool IsManual);

/// <summary>How many mutes <see cref="LoopGuard.ClearMutes"/> lifted, split by kind, so <c>Unwedge</c> can report both.</summary>
internal readonly record struct MuteClearResult(int Automatic, int Manual)
{
    internal int Total => Automatic + Manual;
}
