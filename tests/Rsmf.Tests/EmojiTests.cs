using Rsmf.Core;
using Xunit;

namespace Rsmf.Tests;

public class EmojiTests
{
    private static string[] Runs(string text) => EmojiText.FindRuns(text).Select(r => text.Substring(r.Start, r.Length)).ToArray();

    [Fact]
    public void Plain_text_has_no_emoji()
    {
        Assert.Empty(Runs("Hello, world! 123 #1 * café 你好 مرحبا"));
        Assert.Empty(Runs(""));
        Assert.Empty(EmojiText.FindRuns(null));
        Assert.False(EmojiText.HasEmoji("no emoji here ❤"));          // a bare heart symbol without the emoji selector stays text
    }

    [Fact]
    public void Single_emoji_are_found_with_their_position()
    {
        var text = "ok 😀 then 👍.";
        Assert.Equal(new[] { (3, 2), (11, 2) }, EmojiText.FindRuns(text));
        Assert.Equal(new[] { "😀", "👍" }, Runs(text));
    }

    [Fact]
    public void Emoji_in_a_row_form_one_stretch()
    {
        Assert.Equal(new[] { "😀😀🎉" }, Runs("a😀😀🎉b"));
    }

    [Theory]
    [InlineData("👍🏽")]                                          // thumbs up + skin tone
    [InlineData("👨‍👩‍👧")]                  // family joined with ZWJ
    [InlineData("🇺🇸")]                                          // flag: two regional indicators
    [InlineData("❤️")]                                                      // heart + emoji selector
    [InlineData("1️⃣")]                                                     // keycap 1
    [InlineData("🏳️‍🌈")]                              // rainbow flag
    [InlineData("🏴󠁧󠁢󠁳󠁣󠁴󠁿")]   // flag with tag characters
    [InlineData("✅")]                                                            // check mark that is emoji by default
    public void A_sequence_stays_in_one_stretch(string emoji)
    {
        var text = "x " + emoji + " y";
        Assert.Equal(new[] { emoji }, Runs(text));
        Assert.Equal((2, emoji.Length), Assert.Single(EmojiText.FindRuns(text)));
    }

    [Fact]
    public void Text_selector_keeps_a_symbol_as_text_and_digits_alone_are_not_emoji()
    {
        Assert.Empty(Runs("❤︎"));                // heart + text selector
        Assert.Empty(Runs("call 1 2 3 # *"));
        Assert.Empty(Runs("a️"));                     // a letter followed by a stray selector
    }

    [Fact]
    public void Stretches_never_split_a_surrogate_pair_and_cover_only_emoji()
    {
        var text = "A😀B👍🏽C ❤️!";
        foreach (var (start, length) in EmojiText.FindRuns(text))
        {
            Assert.False(char.IsLowSurrogate(text[start]));
            Assert.False(char.IsHighSurrogate(text[start + length - 1]));
        }
        Assert.Equal(new[] { "😀", "👍🏽", "❤️" }, Runs(text));
    }

    // ------------------------------------------------------------ short names

    [Theory]
    [InlineData("thumbsup", "\uD83D\uDC4D")]
    [InlineData(":thumbsup:", "\uD83D\uDC4D")]
    [InlineData("+1", "\uD83D\uDC4D")]
    [InlineData(":-1:", "\uD83D\uDC4E")]
    [InlineData("THUMBSUP", "\uD83D\uDC4D")]
    [InlineData("smile", "\uD83D\uDE04")]
    [InlineData("joy", "\uD83D\uDE02")]
    [InlineData("100", "\uD83D\uDCAF")]
    [InlineData("tada", "\uD83C\uDF89")]
    [InlineData("fire", "\uD83D\uDD25")]
    [InlineData("face_palm", "\uD83E\uDD26")]
    [InlineData("face_with_cowboy_hat", "\uD83E\uDD20")]
    public void Known_short_names_resolve(string name, string emoji)
    {
        Assert.True(EmojiShortNames.TryGet(name, out var found));
        Assert.Equal(emoji, found);
        Assert.Equal(emoji, EmojiShortNames.ExpandReaction(name));
    }

    [Fact]
    public void Heart_short_name_is_a_heart_emoji()
    {
        Assert.True(EmojiShortNames.TryGet("heart", out var heart));
        Assert.StartsWith("\u2764", heart);
        Assert.True(EmojiText.HasEmoji(heart));
    }

    [Fact]
    public void Unknown_names_and_ordinary_text_are_left_exactly_as_written()
    {
        Assert.False(EmojiShortNames.TryGet("test_emoji", out _));
        Assert.Equal("custom-reaction!", EmojiShortNames.ExpandReaction("custom-reaction!"));
        Assert.Equal(":test_emoji: stays", EmojiShortNames.Expand(":test_emoji: stays"));
        Assert.Equal("Meet at 10:30:00 in room 4", EmojiShortNames.Expand("Meet at 10:30:00 in room 4"));
        Assert.Equal("ratio 1:2:3 and a::b", EmojiShortNames.Expand("ratio 1:2:3 and a::b"));
        Assert.Equal("", EmojiShortNames.Expand(null));
        Assert.Equal("", EmojiShortNames.ExpandReaction(null));
        Assert.Equal("no colons here", EmojiShortNames.Expand("no colons here"));
    }

    [Fact]
    public void Short_names_inside_message_text_become_emoji_and_the_rest_is_untouched()
    {
        Assert.Equal("Here is an emoji short name \uD83D\uDE04", EmojiShortNames.Expand("Here is an emoji short name :smile:"));
        Assert.Equal("\uD83D\uDC4D\uD83D\uDC4D ok :nope: \uD83D\uDD25!", EmojiShortNames.Expand(":+1::thumbsup: ok :nope: :fire:!"));
        Assert.True(EmojiShortNames.Count > 3000);
    }

    [Fact]
    public void Reaction_display_uses_the_emoji_and_keeps_the_count()
    {
        Assert.Equal("\uD83D\uDC4D", new ReactionView("thumbsup", 1).Display);
        Assert.Equal("\uD83D\uDC4D \u00D73", new ReactionView("thumbsup", 3).Display);
        Assert.Equal("my-custom \u00D72", new ReactionView("my-custom", 2).Display);
        Assert.Equal("thumbsup", new ReactionView("thumbsup", 1).Value);        // the file's value is unchanged
    }
}
