using SolrLabs.BuffBot.Guard;
using SolrLabs.BuffBot.Vocabulary;

namespace SolrLabs.BuffBot.Tests;

public sealed class BotShapeMatcherTests
{
    [Theory]
    [InlineData("I didn't understand that. Send help to see what I know.")]
    [InlineData("On it.")]
    [InlineData("You're already in line, hang tight.")]
    [InlineData("Too many people are waiting right now. Try again shortly.")]
    [InlineData("You're too far away. Come closer and ask again.")]
    [InlineData("I'm buffing you right now.")]
    [InlineData("You're not in line.")]
    [InlineData("You're out of the line.")]
    [InlineData("Too late, I'm already casting on you. Say cancel if you'd like me to stop.")]
    [InlineData("Okay, I'll stop after this cast.")]
    [InlineData("There was nothing for me to cast.")]
    [InlineData("You're already fully buffed, nothing new to cast.")]
    [InlineData("The queue was just cleared. Ask again if you still want a buff.")]
    [InlineData("I'm not taking new requests right now. Try again shortly.")]
    [InlineData("I'm out of spell components, so I had to stop.")]
    [InlineData("Sorry folks, need a short break.")]
    [InlineData("Stopped, like you asked.")]
    public void FixedRepliesAreRecognisedVerbatim(string text) =>
        Assert.True(BotShapeMatcher.IsBotShaped(text));

    [Theory]
    [InlineData("Happy to help! Ask me for a buff: buffs, prots, heavy. While you're waiting: position, cancel. Send status any time to see how busy I am. Send contribute to hear what I'm low on.")]
    [InlineData("Still here: 5 tell(s) answered so far, running BuffBot 0.1.0.")]
    [InlineData("Queued, 1 person ahead of you.")]
    [InlineData("Queued, 3 people ahead of you.")]
    [InlineData("Something's misconfigured on my end for \"Strength Other\". Sorry about that.")]
    [InlineData("I haven't learned Strength Other.")]
    [InlineData("All set: cast 1 buff.")]
    [InlineData("All set: cast 27 buffs, 1 already up.")]
    [InlineData("I cast what I could: buffed 27, 1 already up, but missed 2: Endurance Other (fizzled twice), Health Other (didn't land).")]
    [InlineData("I had to stop: couldn't cast Strength Other (out of range).")]
    [InlineData("I had to stop: Blade Protection Other failed (I'm out of mana).")]
    [InlineData("In line: you're next.")]
    [InlineData("In line: 1 ahead of you.")]
    [InlineData("In line: 4 ahead of you.")]
    [InlineData("Sure, I'll buff heavy instead of bow.")]
    [InlineData("I cast what I could: buffed 3, 1 already up. I'm out of spell components, so I had to stop.")]
    public void TemplatedRepliesAreRecognisedWithTheirVariablePartWildcarded(string text) =>
        Assert.True(BotShapeMatcher.IsBotShaped(text));

    [Theory]
    [InlineData("buff")]
    [InlineData("buff me")]
    [InlineData("help")]
    [InlineData("status")]
    [InlineData("position")]
    [InlineData("line")]
    [InlineData("cancel")]
    [InlineData("remove")]
    [InlineData("what's up")]
    public void OrdinaryPlayerTextIsNotBotShaped(string text) =>
        Assert.False(BotShapeMatcher.IsBotShaped(text));

    [Fact]
    public void MatchingIsWhitespaceTolerantAtTheEdges() =>
        Assert.True(BotShapeMatcher.IsBotShaped("  On it.  "));

    /// <summary>The trailing "Portals: ..." sentence Help appends once a tie is offered must not
    /// defeat the loop guard.</summary>
    [Fact]
    public void HelpReplyIsRecognisedWithAndWithoutThePortalsSentence()
    {
        Assert.True(BotShapeMatcher.IsBotShaped(DefaultReplies.Help(DefaultVocabulary.Table, portalsOffered: false)));
        Assert.True(BotShapeMatcher.IsBotShaped(DefaultReplies.Help(DefaultVocabulary.Table, portalsOffered: true)));
    }

    /// <summary>Pausing is no longer a const the pattern can share, so every rung of the mute
    /// ladder is pinned here instead -- a reworded reply must not slip past the loop guard.</summary>
    [Fact]
    public void EveryPausingReplyOnTheMuteLadderIsRecognised()
    {
        Assert.True(BotShapeMatcher.IsBotShaped(DefaultReplies.Pausing(LoopGuard.FirstMuteDuration)));
        Assert.True(BotShapeMatcher.IsBotShaped(DefaultReplies.Pausing(LoopGuard.SecondMuteDuration)));
        Assert.True(BotShapeMatcher.IsBotShaped(DefaultReplies.Pausing(LoopGuard.MaxMuteDuration)));
    }
}
