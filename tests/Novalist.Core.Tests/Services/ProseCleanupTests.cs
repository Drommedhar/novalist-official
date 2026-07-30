using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// A cleanup pass over prose the writer already has.
///
/// Auto-replacements fire while typing and skip pasted text on purpose, so a
/// chapter written elsewhere and pasted in kept its straight quotes, its hyphen
/// pairs and its double spaces permanently.
/// </summary>
public class ProseCleanupTests
{
    private static CleanupOptions With(string language = "en", params CleanupRule[] rules)
        => new() { Rules = [.. rules], Language = language };

    private static string Clean(string html, string language = "en", params CleanupRule[] rules)
        => ProseCleanup.Apply(html, With(language, rules));

    // ─── Nothing asked for, nothing done ─────────────────────────────

    [Theory]
    [InlineData("<p>Text</p>")]
    [InlineData("")]
    public void NoRulesLeavesTheProseExactlyAsItWas(string html)
        => Assert.Equal(html, ProseCleanup.Apply(html, new CleanupOptions()));

    [Fact]
    public void AnEmptySceneIsNotTouched()
        => Assert.Equal(string.Empty, Clean(string.Empty, "en", CleanupRule.SmartenQuotes));

    // ─── Quotes ──────────────────────────────────────────────────────

    [Fact]
    public void StraightQuotesAlternateOpenAndClosed()
        => Assert.Equal("<p>“Come in,” she said, “it is cold.”</p>",
            Clean("<p>\"Come in,\" she said, \"it is cold.\"</p>", "en", CleanupRule.SmartenQuotes));

    [Fact]
    public void GermanGetsTheQuotesGermanUses()
    {
        // Smartening a German manuscript to the English pair is worse than
        // leaving the straight quotes alone.
        var cleaned = Clean("<p>\"Komm rein\", sagte sie.</p>", "de-low", CleanupRule.SmartenQuotes);

        Assert.Equal("<p>„Komm rein“, sagte sie.</p>", cleaned);
    }

    [Fact]
    public void AQuotationSpanningEmphasisStillCloses()
    {
        // Three text runs, one quotation. Restarting the open/closed state at
        // each run would close a quotation that never opened.
        var cleaned = Clean("<p>\"It was <em>his</em> coat,\" she said.</p>",
            "en", CleanupRule.SmartenQuotes);

        Assert.Equal("<p>“It was <em>his</em> coat,” she said.</p>", cleaned);
    }

    [Theory]
    [InlineData("<p>don't</p>", "<p>don’t</p>")]
    [InlineData("<p>the boys' coats</p>", "<p>the boys’ coats</p>")]
    public void AnApostropheIsNotAQuotationMark(string html, string expected)
        => Assert.Equal(expected, Clean(html, "en", CleanupRule.SmartenQuotes));

    [Fact]
    public void AQuoteThatIsNeitherIsLeftAlone()
    {
        // '73 could be an elision or an opening single quote. Guessing wrong
        // is worse than leaving it as the writer typed it.
        Assert.Equal("<p>'73 was cold</p>", Clean("<p>'73 was cold</p>", "en", CleanupRule.SmartenQuotes));
    }

    [Fact]
    public void MarkupIsNotProse()
    {
        // A straight quote inside class="..." is not a quotation mark, and
        // curling it would break the attribute it belongs to.
        var html = "<p class=\"scene\" style=\"margin:  0\">She   said \"no\".</p>";

        var cleaned = Clean(html, "en", CleanupRule.SmartenQuotes, CleanupRule.CollapseSpaces);

        Assert.Contains("class=\"scene\"", cleaned);
        Assert.Contains("style=\"margin:  0\"", cleaned);
        Assert.Contains("She said “no”.", cleaned);
    }

    [Fact]
    public void AnUnknownLanguageStillGetsAPair()
    {
        // A language with no preset falls back rather than leaving the quotes
        // straight and reporting nothing happened.
        Assert.Equal("<p>“Hi”</p>", Clean("<p>\"Hi\"</p>", "kl", CleanupRule.SmartenQuotes));
    }

    // ─── Typography ──────────────────────────────────────────────────

    [Fact]
    public void HyphenPairsAndThreeDotsBecomeTheRealGlyphs()
        => Assert.Equal("<p>She waited—then—…</p>",
            Clean("<p>She waited--then--...</p>", "en", CleanupRule.Typography));

    [Fact]
    public void TypographyLeavesQuotesAlone()
    {
        // The two rules are offered separately, so asking for one must not
        // quietly bring the other.
        Assert.Equal("<p>\"Yes\"—no</p>", Clean("<p>\"Yes\"--no</p>", "en", CleanupRule.Typography));
    }

    // ─── Spaces ──────────────────────────────────────────────────────

    [Fact]
    public void RepeatedSpacesCollapseToOne()
        => Assert.Equal("<p>He left. She stayed.</p>",
            Clean("<p>He   left.  She stayed.</p>", "en", CleanupRule.CollapseSpaces));

    [Fact]
    public void ANoBreakSpaceIsDeliberate()
    {
        // French punctuation and a name held together are both authored.
        var html = "<p>Mira  Vane</p>";

        Assert.Equal(html, Clean(html, "en", CleanupRule.CollapseSpaces));
    }

    [Fact]
    public void SpacesLeftHangingInAParagraphGo()
        => Assert.Equal("<p>She left.</p>",
            Clean("<p>  She left.  </p>", "en", CleanupRule.TrimParagraphs,
                CleanupRule.DropEmptyParagraphs));

    [Fact]
    public void TrimmingLeavesAParagraphWithNoTagsAlone()
    {
        // A fragment with no closing tag has no inside to trim, and inventing
        // one would rewrite the markup.
        var html = "<p>Only an open tag";

        Assert.Equal(html, Clean(html, "en", CleanupRule.TrimParagraphs,
            CleanupRule.DropEmptyParagraphs));
    }

    // ─── Whole paragraphs ────────────────────────────────────────────

    [Fact]
    public void ParagraphsHoldingNothingAreDropped()
        => Assert.Equal("<p>One</p><p>Two</p>",
            Clean("<p>One</p><p></p><p>   </p><p>Two</p>", "en", CleanupRule.DropEmptyParagraphs));

    [Fact]
    public void AParagraphHoldingOnlyAPictureStays()
    {
        // It carries no text and is the whole point of the paragraph it sits in.
        var html = "<p><img src=\"map.png\"></p>";

        Assert.Equal(html, Clean(html, "en", CleanupRule.DropEmptyParagraphs));
    }

    [Fact]
    public void ARuleParagraphStays()
        => Assert.Equal("<p><hr></p>", Clean("<p><hr></p>", "en", CleanupRule.DropEmptyParagraphs));

    [Theory]
    [InlineData("<p>***</p>")]
    [InlineData("<p>* * *</p>")]
    [InlineData("<p>---</p>")]
    [InlineData("<p>#</p>")]
    [InlineData("<p>• • •</p>")]
    public void EverySceneBreakBecomesTheSameOne(string html)
        => Assert.Equal("<p>* * *</p>", Clean(html, "en", CleanupRule.NormaliseSceneBreaks));

    [Fact]
    public void ProseIsNotASceneBreak()
    {
        var html = "<p>She left - and did not come back.</p>";

        Assert.Equal(html, Clean(html, "en", CleanupRule.NormaliseSceneBreaks));
    }

    [Fact]
    public void AnEmptyParagraphIsNotASceneBreak()
    {
        // Both rules on: an empty paragraph has to be dropped rather than
        // turned into a break nobody asked for.
        Assert.Equal("<p>One</p>", Clean("<p>One</p><p>  </p>", "en",
            CleanupRule.NormaliseSceneBreaks, CleanupRule.DropEmptyParagraphs));
    }

    [Fact]
    public void HeadingsAndQuotesAreParagraphsToo()
        => Assert.Equal("<h2>One</h2>",
            Clean("<h2>One</h2><blockquote>  </blockquote>", "en", CleanupRule.DropEmptyParagraphs));

    // ─── Asking without doing ────────────────────────────────────────

    [Fact]
    public void ChangesSaysWhetherAPassWouldDoAnything()
    {
        var options = With("en", CleanupRule.SmartenQuotes);

        Assert.True(ProseCleanup.Changes("<p>\"Hi\"</p>", options));
        Assert.False(ProseCleanup.Changes("<p>“Hi”</p>", options));
    }

    [Fact]
    public void EverythingAtOnce()
    {
        var cleaned = Clean(
            "<p>  \"He left--again...\"  </p><p></p><p>***</p><p>Then  she did.</p>",
            "en", CleanupRule.SmartenQuotes, CleanupRule.Typography, CleanupRule.CollapseSpaces,
            CleanupRule.TrimParagraphs, CleanupRule.DropEmptyParagraphs,
            CleanupRule.NormaliseSceneBreaks);

        Assert.Equal("<p>“He left—again…”</p><p>* * *</p><p>Then she did.</p>", cleaned);
    }
}
