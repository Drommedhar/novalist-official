using Novalist.Core.Utilities;
using Xunit;

namespace Novalist.Core.Tests.Utilities;

public class DialogueScannerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<p>Plain narration with no quoted speech at all.</p>")]
    public void Scan_NoDialogue_ReturnsEmpty(string? html)
        => Assert.Empty(DialogueScanner.Scan(html));

    [Fact]
    public void Scan_TagsOnly_ReturnsEmpty()
        => Assert.Empty(DialogueScanner.Scan("<p></p><br/>"));

    [Theory]
    [InlineData("<p>\"I won't go.\"</p>", "I won't go.")]
    [InlineData("<p>“I won't go.”</p>", "I won't go.")]
    [InlineData("<p>„Ich gehe nicht.“</p>", "Ich gehe nicht.")]
    [InlineData("<p>«Je n'irai pas.»</p>", "Je n'irai pas.")]
    [InlineData("<p>»Ich gehe nicht.«</p>", "Ich gehe nicht.")]
    [InlineData("<p>‹kurz›</p>", "kurz")]
    [InlineData("<p>‚knapp‘</p>", "knapp")]
    public void Scan_RecognizesEveryQuotePair(string html, string expected)
        => Assert.Equal(expected, Assert.Single(DialogueScanner.Scan(html)).Text);

    [Fact]
    public void Scan_SkipsEmptyAndWhitespaceOnlyQuotes()
    {
        // "" is empty, " " is whitespace; only the real line should survive.
        var spans = DialogueScanner.Scan("<p>\"\" and \"   \" and \"Real.\"</p>");

        Assert.Equal("Real.", Assert.Single(spans).Text);
    }

    [Fact]
    public void Scan_ReturnsSpansInDocumentOrder()
    {
        var spans = DialogueScanner.Scan("<p>\"First.\"</p><p>\"Second.\"</p><p>\"Third.\"</p>");

        Assert.Equal(["First.", "Second.", "Third."], spans.Select(s => s.Text));
    }

    [Fact]
    public void Scan_HtmlRangePointsAtSpokenTextOnly()
    {
        const string html = "<p>\"I won't go,\" she said.</p>";
        var span = Assert.Single(DialogueScanner.Scan(html));

        Assert.Equal("I won't go,", html[span.HtmlStart..span.HtmlEnd]);
    }

    [Fact]
    public void Scan_ContextStopsAtParagraphBoundary()
    {
        var spans = DialogueScanner.Scan("<p>Before it.</p><p>\"Line.\" After it.</p>");
        var span = Assert.Single(spans);

        // The previous paragraph must not leak into this line's attribution context.
        Assert.DoesNotContain("Before", span.ContextBefore);
        Assert.Contains("After it.", span.ContextAfter);
    }

    [Fact]
    public void Scan_ContextStopsAtNeighbouringQuote()
    {
        var spans = DialogueScanner.Scan(
            "<p>\"One,\" said Mira. \"Two,\" said Aldric.</p>");

        // Each line sees its own tag, never the next speaker's.
        Assert.Contains("Mira", spans[0].ContextAfter);
        Assert.DoesNotContain("Aldric", spans[0].ContextAfter);
        Assert.Contains("Aldric", spans[1].ContextAfter);
    }

    [Fact]
    public void Scan_ContextIsCappedInLongProse()
    {
        var filler = new string('x', 400);
        var span = Assert.Single(DialogueScanner.Scan($"<p>{filler} \"Line.\" {filler}</p>"));

        Assert.True(span.ContextBefore.Length <= 120);
        Assert.True(span.ContextAfter.Length <= 120);
    }

    [Fact]
    public void Scan_HtmlBeforeAndAfterCarryTheSurroundingMarkup()
    {
        const string html =
            "<p><span class=\"nv-entity-mention\" data-entity-id=\"hero\">Aldric</span> said, "
            + "\"Now.\" <em>quietly</em></p>";
        var span = Assert.Single(DialogueScanner.Scan(html));

        Assert.Contains("data-entity-id=\"hero\"", span.HtmlBefore);
        Assert.Contains("<em>", span.HtmlAfter);
    }

    [Fact]
    public void Scan_HtmlContextIsEmpty_WhenQuoteStandsAlone()
    {
        var span = Assert.Single(DialogueScanner.Scan("<p>\"Alone.\"</p>"));

        Assert.Equal(string.Empty, span.HtmlBefore);
        Assert.Equal(string.Empty, span.HtmlAfter);
    }

    [Fact]
    public void Scan_DecodesEntitiesInSpokenText()
    {
        var span = Assert.Single(DialogueScanner.Scan("<p>\"Salt &amp; iron.\"</p>"));

        Assert.Equal("Salt & iron.", span.Text);
    }

    [Fact]
    public void Scan_LeavesBareAmpersandAsText()
    {
        var span = Assert.Single(DialogueScanner.Scan("<p>\"Salt & iron.\"</p>"));

        Assert.Equal("Salt & iron.", span.Text);
    }

    [Fact]
    public void Scan_LeavesRunawayEntityAsText()
    {
        // No ';' within reach, so this is punctuation, not an entity.
        var span = Assert.Single(
            DialogueScanner.Scan("<p>\"A & a very long stretch; done.\"</p>"));

        Assert.Contains("&", span.Text);
    }

    [Fact]
    public void Scan_LeavesNearbySemicolonThatIsNotAnEntityAsText()
    {
        // A ';' close enough to look like an entity terminator, but "& b;" decodes
        // to itself — so it is punctuation, and the '&' stays literal.
        var span = Assert.Single(DialogueScanner.Scan("<p>\"A & b; done.\"</p>"));

        Assert.Equal("A & b; done.", span.Text);
    }

    [Fact]
    public void Scan_StopsAtTruncatedMarkup()
    {
        // The unterminated tag swallows the rest, so nothing past it is addressable.
        Assert.Empty(DialogueScanner.Scan("<p>text <span class=\"broken\" \"Line.\""));
    }

    [Fact]
    public void Scan_MarksMarkupBearingLinesNotEditable()
    {
        var spans = DialogueScanner.Scan(
            "<p>\"Plain line.\"</p><p>\"With <em>stress</em> inside.\"</p>");

        Assert.True(spans[0].Editable);
        Assert.False(spans[1].Editable);
    }

    [Fact]
    public void Scan_MarksEntityBearingLinesNotEditable()
    {
        // Rewriting the range would turn &amp; into a bare ampersand.
        var span = Assert.Single(DialogueScanner.Scan("<p>\"Salt &amp; iron.\"</p>"));

        Assert.False(span.Editable);
    }

    [Fact]
    public void Scan_IdenticalLinesGetDistinctKeys()
    {
        var spans = DialogueScanner.Scan("<p>\"Yes.\"</p><p>\"Yes.\"</p>");

        Assert.NotEqual(spans[0].LineKey, spans[1].LineKey);
    }

    [Fact]
    public void Scan_KeyIgnoresCasingAndSpacing()
    {
        var a = Assert.Single(DialogueScanner.Scan("<p>\"Come   here.\"</p>"));
        var b = Assert.Single(DialogueScanner.Scan("<p>\"come here.\"</p>"));

        Assert.Equal(a.LineKey, b.LineKey);
    }

    [Fact]
    public void Scan_KeyIsStableWhenNeighbouringLinesChange()
    {
        var before = DialogueScanner.Scan("<p>\"One.\"</p><p>\"Two.\"</p>");
        var after = DialogueScanner.Scan("<p>\"Nought.\"</p><p>\"One.\"</p><p>\"Two.\"</p>");

        // A line inserted ahead of them must not renumber the lines that follow.
        Assert.Equal(before[0].LineKey, after[1].LineKey);
        Assert.Equal(before[1].LineKey, after[2].LineKey);
    }

    [Fact]
    public void Scan_TreatsBlockTagsAsParagraphBreaks()
    {
        var spans = DialogueScanner.Scan("<div>\"One.\"<br/>Mira waited.</div>");

        Assert.DoesNotContain("Mira", spans[0].ContextAfter);
    }

    [Fact]
    public void Scan_InlineTagsDoNotBreakContext()
    {
        var span = Assert.Single(DialogueScanner.Scan("<p>\"Now,\" said <em>Mira</em>.</p>"));

        Assert.Contains("Mira", span.ContextAfter);
    }

    [Fact]
    public void ReplaceLine_SwapsSpokenTextAndLeavesTagIntact()
    {
        const string html = "<p>\"I won't go,\" she said.</p>";
        var span = Assert.Single(DialogueScanner.Scan(html));

        var updated = DialogueScanner.ReplaceLine(html, span, "I am staying,");

        Assert.Equal("<p>\"I am staying,\" she said.</p>", updated);
    }

    [Fact]
    public void ReplaceLine_KeepsSpacingInsideTheQuoteMarks()
    {
        const string html = "<p>\" padded \" she said.</p>";
        var span = Assert.Single(DialogueScanner.Scan(html));

        var updated = DialogueScanner.ReplaceLine(html, span, "new");

        Assert.Equal("<p>\" new \" she said.</p>", updated);
    }

    [Fact]
    public void ReplaceLine_EncodesMarkupCharactersInNewText()
    {
        const string html = "<p>\"Plain.\"</p>";
        var span = Assert.Single(DialogueScanner.Scan(html));

        var updated = DialogueScanner.ReplaceLine(html, span, "Salt & <iron>");

        Assert.Equal("<p>\"Salt &amp; &lt;iron&gt;\"</p>", updated);
    }

    [Fact]
    public void ReplaceLine_RefusesWhenLineCarriesMarkup()
    {
        const string html = "<p>\"With <em>stress</em>.\"</p>";
        var span = Assert.Single(DialogueScanner.Scan(html));

        Assert.Null(DialogueScanner.ReplaceLine(html, span, "plain now"));
    }

    [Fact]
    public void ReplaceLine_RefusesWhenSceneNoLongerReadsThatWay()
    {
        const string html = "<p>\"Original.\"</p>";
        var span = Assert.Single(DialogueScanner.Scan(html));

        // The scene was edited elsewhere; the span's offsets now point at other words.
        Assert.Null(DialogueScanner.ReplaceLine("<p>\"Something else.\"</p>", span, "new"));
    }

    [Fact]
    public void ReplaceLine_RefusesWhenSceneIsShorterThanTheSpan()
    {
        const string html = "<p>\"A reasonably long original line.\"</p>";
        var span = Assert.Single(DialogueScanner.Scan(html));

        Assert.Null(DialogueScanner.ReplaceLine("<p>\"x\"</p>", span, "new"));
    }

    [Fact]
    public void ScanScene_ReturnsThePlainTextAlongsideTheSpans()
    {
        var (text, spans) = DialogueScanner.ScanScene("<p>\"Now,\" said Mira.</p>");

        Assert.Contains("said Mira.", text);
        Assert.Equal("Now,", Assert.Single(spans).Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<p></p>")]
    public void ScanScene_NothingToRead_ReturnsEmptyText(string? html)
    {
        var (text, spans) = DialogueScanner.ScanScene(html);

        Assert.Equal(string.Empty, text);
        Assert.Empty(spans);
    }

    [Fact]
    public void Scan_NumbersParagraphsSoContinuationsCanBeSpotted()
    {
        var spans = DialogueScanner.Scan(
            "<p>\"One.\" \"Still one.\"</p><p>Narration.</p><p>\"Three.\"</p>");

        Assert.Equal(0, spans[0].ParagraphIndex);
        Assert.Equal(0, spans[1].ParagraphIndex);
        Assert.Equal(2, spans[2].ParagraphIndex);
    }

    [Fact]
    public void Scan_TextOffsetsAddressTheLineInThePlainText()
    {
        var (text, spans) = DialogueScanner.ScanScene("<p>Before. \"Now,\" said Mira.</p>");
        var span = Assert.Single(spans);

        Assert.Equal("\"Now,\"", text[span.TextStart..span.TextEnd]);
    }

    [Fact]
    public void Normalize_CollapsesWhitespaceAndLowercases()
        => Assert.Equal("come here now", DialogueScanner.Normalize("  Come\n  HERE   now "));

    [Fact]
    public void BuildLineKey_IsDeterministicAndOrdinalSensitive()
    {
        Assert.Equal(DialogueScanner.BuildLineKey("yes", 0), DialogueScanner.BuildLineKey("yes", 0));
        Assert.NotEqual(DialogueScanner.BuildLineKey("yes", 0), DialogueScanner.BuildLineKey("yes", 1));
        Assert.NotEqual(DialogueScanner.BuildLineKey("yes", 0), DialogueScanner.BuildLineKey("no", 0));
    }
}
