namespace SolrLabs.BuffBot.Vocabulary;

internal enum Intent
{
    Help,
    Status,

    Buffs, // the bare, ungrouped 14-spell set

    Prots,
    Heavy,
    Light,
    Finesse,
    Missile,
    Void,
    Mage,
    TwoHanded,
    Dual,

    Tink, // both non-combat profiles nest xpchain rather than the full _generic base
    Trades,

    Position,
    Cancel,

    Contribute, // answered immediately, like Status, never queued

    Where, // answered immediately, like Status, never queued
    PortalPrimary,
    PortalSecondary,
}
