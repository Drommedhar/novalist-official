using Novalist.Core.Utilities;
using Xunit;

namespace Novalist.Core.Tests.Utilities;

public class NormseitenRendererTests
{
    [Fact]
    public void RenderLines_TitleIsUpperCased_NoBlankBefore()
    {
        var lines = NormseitenRenderer.RenderLines([NormseitenBlock.Title("Frostschwur")]);
        Assert.Equal(["FROSTSCHWUR"], lines);
    }

    [Fact]
    public void RenderLines_HeadingIsUpperCased_BlankBeforeAndAfter()
    {
        var lines = NormseitenRenderer.RenderLines(
        [
            NormseitenBlock.Body("Erste Zeile"),
            NormseitenBlock.Heading("Handlung"),
            NormseitenBlock.Body("Zweite Zeile")
        ]);
        Assert.Equal(["Erste Zeile", "", "HANDLUNG", "", "Zweite Zeile"], lines);
    }

    [Fact]
    public void RenderLines_LeadingHeadingDoesNotOpenWithBlank()
    {
        var lines = NormseitenRenderer.RenderLines(
            [NormseitenBlock.Heading("Handlung"), NormseitenBlock.Body("Text")]);
        Assert.Equal(["HANDLUNG", "", "Text"], lines);
    }

    [Fact]
    public void RenderLines_CollapsesConsecutiveBlanks_AndTrimsTrailing()
    {
        var lines = NormseitenRenderer.RenderLines(
        [
            NormseitenBlock.Blank(),
            NormseitenBlock.Body("Eins"),
            NormseitenBlock.Blank(),
            NormseitenBlock.Blank(),
            NormseitenBlock.Body("Zwei"),
            NormseitenBlock.Blank(),
            NormseitenBlock.Blank()
        ]);
        Assert.Equal(["Eins", "", "Zwei"], lines);
    }

    [Fact]
    public void RenderLines_WhitespaceOnlyTextCountsAsBlank()
    {
        var lines = NormseitenRenderer.RenderLines(
            [NormseitenBlock.Body("Eins"), NormseitenBlock.Body("   "), NormseitenBlock.Body("Zwei")]);
        Assert.Equal(["Eins", "", "Zwei"], lines);
    }

    [Fact]
    public void RenderLines_AllBlank_ReturnsEmpty()
        => Assert.Empty(NormseitenRenderer.RenderLines([NormseitenBlock.Blank()]));

    [Fact]
    public void RenderLines_WrapsAtColumnWidth_WithoutSplittingWords()
    {
        var lines = NormseitenRenderer.RenderLines(
            [NormseitenBlock.Body("aaa bbb ccc ddd")], columns: 7);
        Assert.Equal(["aaa bbb", "ccc ddd"], lines);
        Assert.All(lines, l => Assert.True(l.Length <= 7));
    }

    [Fact]
    public void RenderLines_OverlongWordGetsItsOwnLine()
    {
        var lines = NormseitenRenderer.RenderLines(
            [NormseitenBlock.Body("kurz Donaudampfschifffahrtsgesellschaft kurz")], columns: 10);
        Assert.Equal(["kurz", "Donaudampfschifffahrtsgesellschaft", "kurz"], lines);
    }

    [Fact]
    public void RenderLines_CollapsesWhitespaceRunsAndNewlines()
    {
        var lines = NormseitenRenderer.RenderLines(
            [NormseitenBlock.Body("  eins   zwei\n\tdrei  ")], columns: 60);
        Assert.Equal(["eins zwei drei"], lines);
    }

    [Fact]
    public void RenderLines_NonPositiveColumnsFallsBackToDefault()
    {
        var text = string.Join(' ', Enumerable.Repeat("wort", 40));
        var lines = NormseitenRenderer.RenderLines([NormseitenBlock.Body(text)], columns: 0);
        Assert.All(lines, l => Assert.True(l.Length <= NormseitenRenderer.DefaultColumns));
        Assert.True(lines.Count > 1);
    }

    [Fact]
    public void Measure_CountsLinesPagesAndCharacters()
    {
        var lines = Enumerable.Repeat("0123456789", 31).ToList();
        var metrics = NormseitenRenderer.Measure(lines);
        Assert.Equal(31, metrics.Lines);
        Assert.Equal(2, metrics.Pages);
        Assert.Equal(310, metrics.Characters);
        Assert.Equal(310d / 1500, metrics.CharacterPages);
    }

    [Fact]
    public void Measure_NonPositivePageHeightFallsBackToDefault()
    {
        var metrics = NormseitenRenderer.Measure(Enumerable.Repeat("x", 30).ToList(), linesPerPage: 0);
        Assert.Equal(1, metrics.Pages);
    }

    [Fact]
    public void Measure_EmptyDocumentHasNoPages()
        => Assert.Equal(0, NormseitenRenderer.Measure(Array.Empty<string>()).Pages);

    [Fact]
    public void Measure_FromBlocks_RendersThenCounts()
    {
        var metrics = NormseitenRenderer.MeasureBlocks(
            [NormseitenBlock.Body("aaa bbb")], columns: 3, linesPerPage: 1);
        Assert.Equal(2, metrics.Lines);
        Assert.Equal(2, metrics.Pages);
        Assert.Equal(6, metrics.Characters);
    }

    [Fact]
    public void BlockFactories_CarryTheirKind()
    {
        Assert.Equal(NormseitenBlockKind.Title, NormseitenBlock.Title("t").Kind);
        Assert.Equal(NormseitenBlockKind.Heading, NormseitenBlock.Heading("h").Kind);
        Assert.Equal(NormseitenBlockKind.Text, NormseitenBlock.Body("b").Kind);
        Assert.Equal(NormseitenBlockKind.Blank, NormseitenBlock.Blank().Kind);
        Assert.Equal(string.Empty, NormseitenBlock.Blank().Text);
    }
}
