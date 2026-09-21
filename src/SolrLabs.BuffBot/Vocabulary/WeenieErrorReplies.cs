namespace SolrLabs.BuffBot.Vocabulary;

/// <summary>Plain-language text for the cast-failure weenie errors a requester can cause or observe.</summary>
internal static class WeenieErrorReplies
{
    internal const string NoMana = "I'm out of mana"; // YouDontHaveEnoughManaToCast (0x401)

    // YouDontHaveAllTheComponents (0x400); untestable on the mock, which runs with @requirecomps off
    internal const string NoComponents = "I'm out of the reagents for that spell";

    // The only code CastStateMachine reads beyond plain text: a reagent-shortage retry signal.
    internal const uint NoComponentsCode = 0x0400;

    internal const string TargetGone = "I lost track of you mid-cast"; // TargetNotAcquired (0x42C)

    internal const string OutOfRange = // MissileOutOfRange (0x550), sent for targeted spells too
        "you moved out of range while I was casting";

    internal const string SpellNotKnown = // MagicInvalidSpellType (0x3FC): not in the caster's spellbook
        "I don't actually know that spell anymore";

    internal const string PortalNotTied = "I'm not tied to that portal"; // 0x04A5

    internal const string PortalCannotSummon = "that portal can't be summoned"; // 0x04AE

    internal const string PortalSummonFailed = "the portal failed to appear"; // 0x04A4

    // The server's own chat line for each code, never our phrasing above: a portal refusal never
    // reaches the cast completion, only a Magic-class chat message PortalRun scans for directly.
    internal const string PortalNotTiedServerText =
        "You must have linked with a portal in order to summon it!"; // 0x04A5

    internal const string PortalCannotSummonServerText = "You cannot summon that portal!"; // 0x04AE

    internal const string PortalSummonFailedServerText = "You fail to summon the portal!"; // 0x04A4

    private static readonly IReadOnlyDictionary<uint, string> ByCode = new Dictionary<uint, string>
    {
        [0x0401] = NoMana,
        [NoComponentsCode] = NoComponents,
        [0x042C] = TargetGone,
        [0x0550] = OutOfRange,
        [0x03FC] = SpellNotKnown,
        [0x04A5] = PortalNotTied,
        [0x04AE] = PortalCannotSummon,
        [0x04A4] = PortalSummonFailed,
    };

    internal static string Describe(uint weenieError) =>
        ByCode.TryGetValue(weenieError, out string? phrase) ? phrase : $"weenie error {weenieError}";
}
