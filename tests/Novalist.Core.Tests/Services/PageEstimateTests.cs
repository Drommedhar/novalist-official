using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// How thick the paperback will be. Novalist could answer this exactly and only
/// through the Normseiten export preset, which is the right answer to a
/// different question.
/// </summary>
public class PageEstimateTests
{
    [Theory]
    [InlineData(250, 250, 1)]
    [InlineData(500, 250, 2)]
    // Half a page of prose still costs a leaf of paper.
    [InlineData(251, 250, 2)]
    [InlineData(1, 250, 1)]
    public void PagesRoundUp(int words, int perPage, int expected)
        => Assert.Equal(expected, PageEstimate.Pages(words, perPage));

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void NoWordsIsNoPages(int words)
        => Assert.Equal(0, PageEstimate.Pages(words, 250));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void AnUnusableFigureFallsBackRatherThanDividingByNothing(int perPage)
    {
        // A settings file edited by hand should not be able to break the count.
        Assert.Equal(4, PageEstimate.Pages(1000, perPage));
    }

    [Fact]
    public void TheDefaultIsTheTradePaperbackConvention()
        => Assert.Equal(250, PageEstimate.DefaultWordsPerPage);

    [Fact]
    public void TheFigureChangesTheAnswer()
    {
        // A mass-market edition of the same book is meaningfully shorter, which
        // is why the figure is the writer's to set.
        Assert.Equal(400, PageEstimate.Pages(100_000, 250));
        Assert.Equal(334, PageEstimate.Pages(100_000, 300));
        Assert.Equal(667, PageEstimate.Pages(100_000, 150));
    }
}
