using SolrLabs.BuffBot.Spells;
using SolrLabs.BuffBot.Vocabulary;

namespace SolrLabs.BuffBot.Tests;

public sealed class VocabularyTests
{
    [Theory]
    [InlineData("help", nameof(Intent.Help))]
    [InlineData("HELP", nameof(Intent.Help))]
    [InlineData("  help  ", nameof(Intent.Help))]
    [InlineData("?", nameof(Intent.Help))]
    [InlineData("status", nameof(Intent.Status))]
    [InlineData("Status", nameof(Intent.Status))]
    [InlineData("buffs", nameof(Intent.Buffs))]
    [InlineData("buff me", nameof(Intent.Buffs))]
    [InlineData("Buff Me", nameof(Intent.Buffs))]
    [InlineData("buff", nameof(Intent.Buffs))]
    [InlineData("prots", nameof(Intent.Prots))]
    [InlineData("heavy", nameof(Intent.Heavy))]
    [InlineData("HEAVY", nameof(Intent.Heavy))]
    [InlineData("light", nameof(Intent.Light))]
    [InlineData("finesse", nameof(Intent.Finesse))]
    [InlineData("missile", nameof(Intent.Missile))]
    [InlineData("void", nameof(Intent.Void))]
    [InlineData("mage", nameof(Intent.Mage))]
    [InlineData("2h", nameof(Intent.TwoHanded))]
    [InlineData("dual", nameof(Intent.Dual))]
    [InlineData("tink", nameof(Intent.Tink))]
    [InlineData("trades", nameof(Intent.Trades))]
    // Aliases DoThingsBot carried and we had not: a player says what they shoot.
    [InlineData("bow", nameof(Intent.Missile))]
    [InlineData("xbow", nameof(Intent.Missile))]
    [InlineData("crossbow", nameof(Intent.Missile))]
    [InlineData("thrown", nameof(Intent.Missile))]
    [InlineData("war", nameof(Intent.Mage))]
    [InlineData("fw", nameof(Intent.Finesse))]
    [InlineData("twohanded", nameof(Intent.TwoHanded))]
    [InlineData("dualwield", nameof(Intent.Dual))]
    [InlineData("position", nameof(Intent.Position))]
    [InlineData("line", nameof(Intent.Position))]
    [InlineData("cancel", nameof(Intent.Cancel))]
    [InlineData("remove", nameof(Intent.Cancel))]
    public void ResolvesCaseInsensitivelyAndTrimmed(string text, string expectedName)
    {
        bool resolved = DefaultVocabulary.Table.TryResolve(text, out Intent intent);

        Assert.True(resolved);
        Assert.Equal(expectedName, intent.ToString());
    }

    [Fact]
    public void MultiplePhrasesMapToOneIntent()
    {
        var table = new VocabularyTable(
        [
            ("one", Intent.Buffs),
            ("two", Intent.Buffs),
        ]);

        Assert.True(table.TryResolve("one", out Intent first));
        Assert.True(table.TryResolve("two", out Intent second));
        Assert.Equal(Intent.Buffs, first);
        Assert.Equal(Intent.Buffs, second);
    }

    [Fact]
    public void RejectsUnknownText()
    {
        bool resolved = DefaultVocabulary.Table.TryResolve("do a backflip", out _);

        Assert.False(resolved);
    }

    /// <summary>Owner-only operator commands (<see cref="Guard.OperatorConsole"/>) are console-only:
    /// this table is what <c>help</c> is generated from, so a phrase here is player-discoverable.</summary>
    [Theory]
    [InlineData("inspect")]
    [InlineData("unwedge")]
    [InlineData("admin")]
    [InlineData("operator")]
    [InlineData("inv")]
    [InlineData("logout")]
    [InlineData("chatdump")]
    public void OperatorVerbsAreNeverInTheTellVocabulary(string operatorVerb)
    {
        Assert.False(DefaultVocabulary.Table.TryResolve(operatorVerb, out _));
        Assert.DoesNotContain(
            DefaultVocabulary.Table.Phrases, phrase => string.Equals(phrase, operatorVerb, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HelpTextNeverMentionsAnOperatorVerb()
    {
        string help = DefaultReplies.Help(DefaultVocabulary.Table);

        Assert.DoesNotContain("inspect", help);
        Assert.DoesNotContain("unwedge", help);
        Assert.DoesNotContain("inv", help);
        Assert.DoesNotContain("logout", help);
        Assert.DoesNotContain("chatdump", help);
    }
}

/// <summary>ACE replaces characters outside ASCII with '?' before relaying,
/// so replies must be ASCII or players read mojibake.</summary>
public sealed class ReplyEncodingTests
{
    public static TheoryData<string, string> EveryStaticReply()
    {
        var data = new TheoryData<string, string>();
        foreach (Type type in new[] { typeof(DefaultReplies), typeof(WeenieErrorReplies) })
        {
            foreach (var field in type.GetFields(
                         System.Reflection.BindingFlags.NonPublic |
                         System.Reflection.BindingFlags.Public |
                         System.Reflection.BindingFlags.Static))
            {
                if (field.GetValue(null) is string value)
                    data.Add($"{type.Name}.{field.Name}", value);
            }
        }

        for (int i = 0; i < DefaultReplies.BotShapePatterns.Count; i++)
            data.Add($"BotShapePatterns[{i}]", DefaultReplies.BotShapePatterns[i]);

        return data;
    }

    /// <summary>Adding an Intent value and a phrase for it without adding the arm in
    /// Responder's switch is a silent way to ship a keyword that answers nothing.</summary>
    [Fact]
    public void EveryArchetypePhraseResolvesToACastableProfile()
    {
        (string Phrase, string SetName)[] archetypes =
        [
            ("buff", DefaultSpellSets.Buff),
            ("prots", DefaultSpellSets.Prots),
            ("heavy", DefaultSpellSets.Heavy),
            ("light", DefaultSpellSets.Light),
            ("finesse", DefaultSpellSets.Finesse),
            ("missile", DefaultSpellSets.Missile),
            ("void", DefaultSpellSets.Void),
            ("mage", DefaultSpellSets.Mage),
            ("2h", DefaultSpellSets.TwoHanded),
            ("dual", DefaultSpellSets.Dual),
            ("tink", DefaultSpellSets.Tink),
            ("trades", DefaultSpellSets.Trades),
        ];

        foreach ((string phrase, string setName) in archetypes)
        {
            Assert.True(
                DefaultVocabulary.Table.TryResolve(phrase, out _),
                $"'{phrase}' is not in the vocabulary, so no player can ask for it");
            Assert.True(
                DefaultSpellSets.Table.TryGetValue(setName, out var lines),
                $"'{phrase}' names set '{setName}', which is not in the spell-set table");
            Assert.NotEmpty(lines);
        }

        Assert.Contains("tink", DefaultVocabulary.Table.Phrases);
        Assert.Contains("trades", DefaultVocabulary.Table.Phrases);
    }

    [Theory]
    [MemberData(nameof(EveryStaticReply))]
    public void EveryReplyIsAscii(string name, string text)
    {
        foreach (char c in text)
        {
            Assert.True(
                c <= 0x7F,
                $"{name} carries U+{(int)c:X4} ('{c}'), which ACE replaces with '?': {text}");
        }
    }
}
