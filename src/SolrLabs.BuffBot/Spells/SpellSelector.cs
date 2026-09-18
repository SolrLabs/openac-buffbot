using System.Text.RegularExpressions;
using AcDream.Plugin.Abstractions;

namespace SolrLabs.BuffBot.Spells;

/// <summary>One line of a spell set, resolved to the exact learned spell that casts it.</summary>
internal readonly record struct ResolvedSpell(string Line, PluginSpellInfo Spell)
{
    /// <summary>Ranked highest first, so <see cref="Casting.CastStateMachine"/> can step down
    /// without rescanning. Index 0 is <see cref="Spell"/> before any step-down.</summary>
    internal IReadOnlyList<PluginSpellInfo> LearnedTiersDescending { get; init; } =
        Array.Empty<PluginSpellInfo>();

    /// <summary><see langword="null"/> ordinarily; set to the requester's wielded shield for a
    /// bane line (<see cref="Guard.ShieldFinder"/>). Never set for a self-targeted step.</summary>
    internal (uint ObjectId, string Name)? TargetOverride { get; init; }
}

internal enum SpellPlanFailureKind
{
    /// <summary>A malformed entry in <see cref="DefaultSpellSets"/>, not a training gap.</summary>
    UnknownLine,

    NothingLearned,
}

internal readonly record struct SpellPlanFailure(string Line, SpellPlanFailureKind Kind);

/// <summary>Which half of a catalog spell's self/other pair a line resolves against.</summary>
internal enum SpellTargetKind
{
    Other,
    Self,
}

/// <summary>A resolved plan, or the first line that could not be resolved.</summary>
internal sealed record SpellSelectionResult(
    bool IsSuccess,
    IReadOnlyList<ResolvedSpell> Plan,
    SpellPlanFailure? Failure)
{
    /// <summary>Lines a player request named that no tier of is learned. Only
    /// <see cref="ResolveForPlayerRequest"/> populates this.</summary>
    internal IReadOnlyList<string> UnlearnedLines { get; init; } = Array.Empty<string>();

    internal static SpellSelectionResult Success(IReadOnlyList<ResolvedSpell> plan) =>
        new(true, plan, null);

    internal static SpellSelectionResult SuccessWithUnlearned(
        IReadOnlyList<ResolvedSpell> plan, IReadOnlyList<string> unlearnedLines) =>
        new(true, plan, null) { UnlearnedLines = unlearnedLines };

    internal static SpellSelectionResult Failed(SpellPlanFailure failure) =>
        new(false, Array.Empty<ResolvedSpell>(), failure);
}

/// <summary>Ranks by each catalog spell's Name tier suffix rather than <see
/// cref="PluginSpellInfo.Tier"/>, a rough heuristic that can rank a numeral equal to an Incantation.</summary>
internal static partial class SpellSelector
{
    [GeneratedRegex(@"^(?<line>.+?)\s+(?<suffix>[IVXLCDM]+|Incantation)$", RegexOptions.IgnoreCase)]
    private static partial Regex TierSuffixPattern();

    [GeneratedRegex(@"^Incantation\s+of\s+(?<line>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex IncantationPrefixPattern();

    /// <summary><paramref name="tierOffset"/> walks that many rungs down the learned ladder,
    /// clamped to the lowest tier rather than failing when it runs out.</summary>
    internal static SpellSelectionResult Resolve(
        IReadOnlyList<PluginSpellInfo> catalog,
        IReadOnlyList<string> lines,
        SpellTargetKind kind = SpellTargetKind.Other,
        int tierOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(lines);

        var plan = new List<ResolvedSpell>(lines.Count);
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
                return SpellSelectionResult.Failed(
                    new SpellPlanFailure(line, SpellPlanFailureKind.UnknownLine));

            if (TryResolveLine(catalog, trimmed, kind, tierOffset) is not { } resolved)
                return SpellSelectionResult.Failed(
                    new SpellPlanFailure(trimmed, SpellPlanFailureKind.NothingLearned));

            plan.Add(resolved);
        }

        return SpellSelectionResult.Success(plan);
    }

    /// <summary>An untrained line is dropped silently instead of failing every line behind it,
    /// unlike <see cref="Resolve"/>.</summary>
    internal static IReadOnlyList<ResolvedSpell> ResolveLenient(
        IReadOnlyList<PluginSpellInfo> catalog,
        IReadOnlyList<string> lines,
        SpellTargetKind kind,
        int tierOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(lines);

        var plan = new List<ResolvedSpell>(lines.Count);
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;
            if (TryResolveLine(catalog, trimmed, kind, tierOffset) is { } resolved)
                plan.Add(resolved);
        }

        return plan;
    }

    /// <summary>An unlearned line is skipped into <see cref="SpellSelectionResult.UnlearnedLines"/>
    /// rather than failing the request; a blank line still fails. <paramref name="targetTier"/> runs 1-8, Incantation is 8.</summary>
    internal static SpellSelectionResult ResolveForPlayerRequest(
        IReadOnlyList<PluginSpellInfo> catalog,
        IReadOnlyList<string> lines,
        SpellTargetKind kind = SpellTargetKind.Other,
        int? targetTier = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(lines);

        var plan = new List<ResolvedSpell>(lines.Count);
        var unlearned = new List<string>();
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
                return SpellSelectionResult.Failed(
                    new SpellPlanFailure(line, SpellPlanFailureKind.UnknownLine));

            if (TryResolveLine(catalog, trimmed, kind, tierOffset: 0, targetTier) is { } resolved)
                plan.Add(resolved);
            else
                unlearned.Add(trimmed);
        }

        return SpellSelectionResult.SuccessWithUnlearned(plan, unlearned);
    }

    private static ResolvedSpell? TryResolveLine(
        IReadOnlyList<PluginSpellInfo> catalog, string trimmedLine, SpellTargetKind kind, int tierOffset,
        int? targetTier = null)
    {
        var byRank = new Dictionary<int, (int CatalogIndex, PluginSpellInfo Spell)>();
        int catalogIndex = 0;
        foreach (PluginSpellInfo spell in catalog)
        {
            bool matchesKind = kind == SpellTargetKind.Self ? spell.IsSelfTargeted : !spell.IsSelfTargeted;
            if (matchesKind && TryMatchLine(spell.Name, trimmedLine, out int rank)
                && !byRank.ContainsKey(rank))
                byRank[rank] = (catalogIndex, spell);
            catalogIndex++;
        }

        if (byRank.Count == 0)
            return null;

        var matches = new List<(int Rank, int CatalogIndex, PluginSpellInfo Spell)>(byRank.Count);
        foreach (var (rank, entry) in byRank)
            matches.Add((rank, entry.CatalogIndex, entry.Spell));

        // Highest rank first; ties break toward whichever appeared first in the catalog.
        matches.Sort((a, b) =>
            a.Rank != b.Rank ? b.Rank.CompareTo(a.Rank) : a.CatalogIndex.CompareTo(b.CatalogIndex));

        var ladder = new PluginSpellInfo[matches.Count];
        for (int i = 0; i < matches.Count; i++)
            ladder[i] = matches[i].Spell;

        int index = targetTier is { } target
            ? IndexForTargetTier(matches, target)
            : Math.Clamp(tierOffset, 0, ladder.Length - 1);
        return new ResolvedSpell(trimmedLine, ladder[index]) { LearnedTiersDescending = ladder };
    }

    private static int IndexForTargetTier(
        List<(int Rank, int CatalogIndex, PluginSpellInfo Spell)> matches, int targetTier)
    {
        int clampedTarget = Math.Clamp(targetTier, 1, 8);
        for (int i = 0; i < matches.Count; i++)
        {
            if (EffectiveTierValue(matches[i].Rank) <= clampedTarget)
                return i;
        }

        // Every learned rung outranks the target: fall back to the weakest one.
        return matches.Count - 1;
    }

    private static int EffectiveTierValue(int rank) => rank == int.MaxValue ? 8 : rank;

    private static bool TryMatchLine(string spellName, string line, out int rank)
    {
        rank = 0;
        string trimmedName = spellName.Trim();

        Match suffixMatch = TierSuffixPattern().Match(trimmedName);
        if (suffixMatch.Success)
        {
            if (!string.Equals(suffixMatch.Groups["line"].Value, line, StringComparison.OrdinalIgnoreCase))
                return false;

            string suffix = suffixMatch.Groups["suffix"].Value;
            if (string.Equals(suffix, "Incantation", StringComparison.OrdinalIgnoreCase))
            {
                rank = int.MaxValue;
                return true;
            }

            rank = RomanNumeral.ToValue(suffix);
            return true;
        }

        Match prefixMatch = IncantationPrefixPattern().Match(trimmedName);
        if (prefixMatch.Success
            && string.Equals(prefixMatch.Groups["line"].Value, line, StringComparison.OrdinalIgnoreCase))
        {
            rank = int.MaxValue;
            return true;
        }

        return false;
    }

    private static class RomanNumeral
    {
        private static readonly Dictionary<char, int> Values = new()
        {
            ['I'] = 1, ['V'] = 5, ['X'] = 10, ['L'] = 50, ['C'] = 100, ['D'] = 500, ['M'] = 1000,
        };

        internal static int ToValue(string roman)
        {
            int total = 0;
            int previous = 0;
            for (int i = roman.Length - 1; i >= 0; i--)
            {
                int value = Values[char.ToUpperInvariant(roman[i])];
                total += value < previous ? -value : value;
                previous = value;
            }

            return total;
        }
    }
}
