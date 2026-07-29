using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// The query language behind global search.
///
/// One substring pass could not express "in the title", "not this word", or
/// "these words in this order" - the three things anyone hunting a
/// half-remembered line actually needs.
/// </summary>
public class SearchQueryTests
{
    [Fact]
    public void APlainWordIsOneTermThatLooksAnywhere()
    {
        var query = SearchQuery.Parse("bell");

        var term = Assert.Single(query.Terms);
        Assert.Equal("bell", term.Value);
        Assert.Equal(SearchField.Any, term.Field);
        Assert.False(term.Negated);
    }

    [Theory]
    [InlineData("title:bell", SearchField.Title)]
    [InlineData("text:bell", SearchField.Text)]
    [InlineData("body:bell", SearchField.Text)]
    [InlineData("notes:bell", SearchField.Notes)]
    [InlineData("synopsis:bell", SearchField.Notes)]
    [InlineData("tag:bell", SearchField.Tag)]
    [InlineData("kind:scene", SearchField.Kind)]
    public void AFieldPrefixScopesTheTerm(string text, SearchField expected)
        => Assert.Equal(expected, SearchQuery.Parse(text).Terms[0].Field);

    [Fact]
    public void AWordThatIsNotAFieldNameKeepsItsColon()
    {
        // A search box that rejects what you typed is worse than one that
        // looks for it.
        var term = Assert.Single(SearchQuery.Parse("chapter:one").Terms);
        Assert.Equal("chapter:one", term.Value);
        Assert.Equal(SearchField.Any, term.Field);
    }

    [Fact]
    public void AMinusNegatesTheTerm()
    {
        var term = Assert.Single(SearchQuery.Parse("-draft").Terms);
        Assert.True(term.Negated);
        Assert.Equal("draft", term.Value);
    }

    [Fact]
    public void QuotesKeepAPhraseTogether()
    {
        var term = Assert.Single(SearchQuery.Parse("\"the bell tolled\"").Terms);
        Assert.Equal("the bell tolled", term.Value);
        Assert.True(term.Exact);
    }

    [Fact]
    public void AnUnclosedQuoteRunsToTheEndRatherThanFailing()
        => Assert.Equal("the bell", Assert.Single(SearchQuery.Parse("\"the bell").Terms).Value);

    [Fact]
    public void AFieldAndAPhraseCombine()
    {
        var term = Assert.Single(SearchQuery.Parse("notes:\"come back to this\"").Terms);
        Assert.Equal(SearchField.Notes, term.Field);
        Assert.Equal("come back to this", term.Value);
    }

    [Fact]
    public void ATermsOfSeveralWordsParseSeparately()
        => Assert.Equal(3, SearchQuery.Parse("bell  -draft title:tower").Terms.Count);

    [Fact]
    public void NothingToSearchForIsAnEmptyQuery()
    {
        Assert.True(SearchQuery.Parse("").IsEmpty);
        Assert.True(SearchQuery.Parse("   ").IsEmpty);
        Assert.True(SearchQuery.Parse(null).IsEmpty);
    }

    // ── Matching ──

    [Fact]
    public void EveryTermHasToHold()
    {
        var query = SearchQuery.Parse("bell tower");

        Assert.True(query.Matches("The bell", "a tower stood", null, null, "scene"));
        Assert.False(query.Matches("The bell", "nothing else", null, null, "scene"));
    }

    [Fact]
    public void AScopedTermOnlyLooksInThatField()
    {
        var query = SearchQuery.Parse("title:bell");

        Assert.True(query.Matches("The bell", "nothing", null, null, "scene"));
        Assert.False(query.Matches("Arrival", "the bell tolled", null, null, "scene"));
    }

    [Fact]
    public void ANegatedTermExcludesAMatchAnywhere()
    {
        var query = SearchQuery.Parse("-draft");

        Assert.True(query.Matches("Arrival", "finished prose", null, null, "scene"));
        Assert.False(query.Matches("Arrival", "still a draft", null, null, "scene"));
    }

    [Fact]
    public void ANegatedScopedTermOnlyExcludesThatField()
    {
        var query = SearchQuery.Parse("-title:draft");

        Assert.True(query.Matches("Arrival", "still a draft", null, null, "scene"));
        Assert.False(query.Matches("Draft one", "finished", null, null, "scene"));
    }

    [Fact]
    public void ATextTermOnlyLooksInTheProse()
    {
        var query = SearchQuery.Parse("text:bell");

        Assert.True(query.Matches("Arrival", "the bell tolled", null, null, "scene"));
        Assert.False(query.Matches("The bell", "nothing", null, null, "scene"));
    }

    [Fact]
    public void ANotesTermOnlyLooksAtWhatWasWrittenAboutIt()
    {
        var query = SearchQuery.Parse("notes:bell");

        Assert.True(query.Matches("Arrival", "nothing", "check the bell", null, "scene"));
        Assert.False(query.Matches("The bell", "the bell", null, null, "scene"));
    }

    [Fact]
    public void ATagTermReadsTheTags()
    {
        var query = SearchQuery.Parse("tag:night");

        Assert.True(query.Matches("Arrival", null, null, ["night"], "scene"));
        Assert.False(query.Matches("Arrival", null, null, ["rain"], "scene"));
        Assert.False(query.Matches("Arrival", null, null, null, "scene"));
    }

    [Fact]
    public void AKindTermRestrictsToThatKind()
    {
        var query = SearchQuery.Parse("kind:scene bell");

        Assert.True(query.Matches("The bell", null, null, null, "scene"));
        Assert.False(query.Matches("The bell", null, null, null, "entity"));
        Assert.Equal(["scene"], query.Kinds);
    }

    [Fact]
    public void ANegatedKindIsNotARestriction()
        // "everything except research" restricts nothing up front; it excludes
        // per result, which is what the match does.
        => Assert.Empty(SearchQuery.Parse("-kind:research").Kinds);

    // ── Ranking ──

    [Fact]
    public void ATitleMatchOutranksABodyMatch()
    {
        var query = SearchQuery.Parse("bell");

        var inTitle = query.Score("The bell", "nothing here", null);
        var inBody = query.Score("Arrival", "the bell tolled", null);

        Assert.True(inTitle > inBody);
    }

    [Fact]
    public void AnExactTitleOutranksATitleThatMerelyContainsIt()
    {
        var query = SearchQuery.Parse("bell");

        Assert.True(query.Score("bell", null, null) > query.Score("The bell tower", null, null));
    }

    [Fact]
    public void MatchingEveryTermOutranksMatchingOne()
    {
        var query = SearchQuery.Parse("bell tower");

        var both = query.Score("The bell tower", null, null);
        var one = query.Score("The bell", null, null);

        Assert.True(both > one);
    }

    [Fact]
    public void AnEarlierMatchOutranksALaterOne()
    {
        var query = SearchQuery.Parse("bell");

        var early = query.Score("bell at the start", null, null);
        var late = query.Score("a very long title that only ends with bell", null, null);

        Assert.True(early > late);
    }

    [Fact]
    public void ANegatedTermScoresNothing()
        => Assert.Equal(0, SearchQuery.Parse("-draft").Score("Arrival", "prose", null));
}
