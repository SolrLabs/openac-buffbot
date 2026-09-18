namespace SolrLabs.BuffBot.Vocabulary;

/// <summary>The one place a player phrase may appear as a literal.</summary>
internal static class DefaultVocabulary
{
    internal static VocabularyTable Table { get; } = new(
    [
        ("help", Intent.Help),
        ("?", Intent.Help),
        ("status", Intent.Status),
        ("contribute", Intent.Contribute),

        ("buffs", Intent.Buffs),
        ("buff me", Intent.Buffs),
        ("buff", Intent.Buffs),

        ("prots", Intent.Prots),
        ("heavy", Intent.Heavy),
        ("light", Intent.Light),
        ("finesse", Intent.Finesse),
        ("missile", Intent.Missile),
        ("void", Intent.Void),
        ("mage", Intent.Mage),
        ("2h", Intent.TwoHanded),
        ("dual", Intent.Dual),

        // A player names what they shoot or swing, not the archetype category.
        ("bow", Intent.Missile),
        ("xbow", Intent.Missile),
        ("crossbow", Intent.Missile),
        ("thrown", Intent.Missile),
        ("war", Intent.Mage),
        ("fw", Intent.Finesse),
        ("finesseweapons", Intent.Finesse),
        ("twohand", Intent.TwoHanded),
        ("twohanded", Intent.TwoHanded),
        ("twohandedweapons", Intent.TwoHanded),
        ("dualwield", Intent.Dual),

        // Not combat archetypes: no weapon masteries, no protections.
        ("tink", Intent.Tink),
        ("trades", Intent.Trades),

        // Kept for players who remember an older bot's own words.
        ("position", Intent.Position),
        ("line", Intent.Position),
        ("cancel", Intent.Cancel),
        ("remove", Intent.Cancel),
    ]);
}
