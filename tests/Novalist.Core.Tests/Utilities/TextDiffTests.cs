using Novalist.Core.Utilities;
using Xunit;

namespace Novalist.Core.Tests.Utilities;

public class TextDiffTests
{
    [Fact]
    public void Compute_IdenticalText_AllEqual()
    {
        var diff = TextDiff.Compute("a\nb\nc", "a\nb\nc");
        Assert.All(diff, d => Assert.Equal(DiffOp.Equal, d.Op));
        Assert.Equal(3, diff.Count);
    }

    [Fact]
    public void Compute_AddedLine()
    {
        var diff = TextDiff.Compute("a\nb", "a\nb\nc");
        Assert.Contains(diff, d => d.Op == DiffOp.Added && d.Text == "c");
        var added = diff.Single(d => d.Op == DiffOp.Added);
        Assert.NotNull(added.RightIndex);
        Assert.Null(added.LeftIndex);
    }

    [Fact]
    public void Compute_RemovedLine()
    {
        var diff = TextDiff.Compute("a\nb\nc", "a\nc");
        var removed = diff.Single(d => d.Op == DiffOp.Removed);
        Assert.Equal("b", removed.Text);
        Assert.NotNull(removed.LeftIndex);
        Assert.Null(removed.RightIndex);
    }

    [Fact]
    public void Compute_EmptyStrings_NoLines()
        => Assert.Empty(TextDiff.Compute("", ""));

    [Fact]
    public void ComputePaired_EqualRows()
    {
        var rows = TextDiff.ComputePaired("a\nb", "a\nb");
        Assert.All(rows, r => Assert.True(r.IsEqual));
        Assert.All(rows, r => Assert.Equal(r.LeftText, r.RightText));
    }

    [Fact]
    public void ComputePaired_ChangedRow_PairsRemovedAndAdded()
    {
        var rows = TextDiff.ComputePaired("hello world", "hello there");
        var changed = Assert.Single(rows, r => r.IsChanged);
        Assert.Equal("hello world", changed.LeftText);
        Assert.Equal("hello there", changed.RightText);
    }

    [Fact]
    public void ComputePaired_LeftOnly_WhenPureDeletion()
    {
        var rows = TextDiff.ComputePaired("a\nb\nc", "a\nc");
        Assert.Contains(rows, r => r.IsLeftOnly && r.LeftText == "b");
    }

    [Fact]
    public void ComputePaired_RightOnly_WhenPureInsertion()
    {
        var rows = TextDiff.ComputePaired("a\nc", "a\nb\nc");
        Assert.Contains(rows, r => r.IsRightOnly && r.RightText == "b");
    }

    [Fact]
    public void WordDiff_EqualAddedRemoved_AndMergesAdjacent()
    {
        var spans = TextDiff.WordDiff("the cat", "the dog");
        Assert.Contains(spans, s => s.Op == WordDiffOp.Equal);
        Assert.Contains(spans, s => s.Op == WordDiffOp.Removed);
        Assert.Contains(spans, s => s.Op == WordDiffOp.Added);
    }

    [Fact]
    public void WordDiff_EmptyInputs()
        => Assert.Empty(TextDiff.WordDiff("", ""));

    [Fact]
    public void StripHtml_Empty_ReturnsEmpty()
        => Assert.Equal(string.Empty, TextDiff.StripHtml(""));

    [Fact]
    public void StripHtml_ConvertsParagraphAndBreakToNewlines()
    {
        var result = TextDiff.StripHtml("<p>one</p><p>two</p>");
        Assert.Contains("one", result);
        Assert.Contains("two", result);
        Assert.Contains("\n", result);
    }

    [Fact]
    public void StripHtml_RemovesTagsAndDecodesEntities()
    {
        var result = TextDiff.StripHtml("<b>tom &amp; jerry</b>");
        Assert.Equal("tom & jerry", result);
    }

    [Fact]
    public void StripHtml_HandlesSelfClosingBreak()
    {
        var result = TextDiff.StripHtml("line1<br/>line2");
        Assert.Contains("\n", result);
    }
}
