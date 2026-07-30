using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Export-time placeholders, and substitutions that touch the output and never
/// the prose.
/// </summary>
public class ExportTokensTests
{
    private static TokenContext Context() => new()
    {
        Title = "The Salt Road",
        Author = "Ada Cole",
        Isbn = "9781234567897",
        Publisher = "Gull Press",
        Series = "The Reach",
        SeriesIndex = "2",
        WordCount = 91234,
        PageCount = 312,
        ChapterNumber = 4,
        ChapterTitle = "The Crossing",
        SceneTitle = "Low water",
        Act = "Act Two",
        ExportedAt = new DateTime(2026, 7, 30)
    };

    [Fact]
    public void ATitlePageResolvesItselfFromTheBook()
    {
        var resolved = ExportTokens.Resolve(
            "<$title>, book <$seriesindex> of <$series>, by <$author>", Context());

        Assert.Equal("The Salt Road, book 2 of The Reach, by Ada Cole", resolved);
    }

    [Fact]
    public void ChapterTokensResolveWhereAChapterIsBeingWritten()
    {
        var resolved = ExportTokens.Resolve(
            "Chapter <$chapternumber> (<$chapterroman>): <$chaptertitle> - <$scenetitle>, <$act>",
            Context());

        Assert.Equal("Chapter 4 (IV): The Crossing - Low water, Act Two", resolved);
    }

    [Fact]
    public void TokensAreCaseInsensitive()
        => Assert.Equal("Ada Cole", ExportTokens.Resolve("<$AUTHOR>", Context()));

    [Fact]
    public void TheRestOfThePublishingAndCountTokensResolveToo()
    {
        // A copyright page is the reason most of these exist, and it wants all
        // of them on one line.
        Assert.Equal(
            "9781234567897 / Gull Press / 2026 / 91,234 words / 312 pages",
            ExportTokens.Resolve(
                "<$isbn> / <$publisher> / <$year> / <$wordcount> words / <$pagecount> pages",
                Context() with { WordCount = 91234 }));

        // The date is culture-formatted, so this asserts that something dated
        // came out rather than pinning a format the machine decides.
        Assert.Contains("2026", ExportTokens.Resolve("<$date>", Context()));
    }

    [Fact]
    public void AnUnknownTokenIsLeftExactlyAsWritten()
    {
        // Silently deleting something a writer typed is worse than printing it,
        // and it makes a typo visible instead of invisible.
        Assert.Equal("<$rhubarb>", ExportTokens.Resolve("<$rhubarb>", Context()));
        Assert.Equal("<$titel>", ExportTokens.Resolve("<$titel>", Context()));
    }

    [Fact]
    public void TextWithNoTokensComesBackUntouched()
    {
        Assert.Equal("Nothing here.", ExportTokens.Resolve("Nothing here.", Context()));
        Assert.Equal(string.Empty, ExportTokens.Resolve(null, Context()));
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(-3, "")]
    [InlineData(1, "I")]
    [InlineData(4, "IV")]
    [InlineData(9, "IX")]
    [InlineData(14, "XIV")]
    [InlineData(40, "XL")]
    [InlineData(1994, "MCMXCIV")]
    public void RomanNumeralsAreTheOnesBooksActuallyUse(int value, string expected)
        => Assert.Equal(expected, ExportTokens.Roman(value));

    // ── Compile-time replacements ──

    private static ExportReplacement Rule(
        string find, string replace, bool regex = false, bool matchCase = false, int order = 0)
        => new() { Find = find, Replace = replace, IsRegex = regex, MatchCase = matchCase, Order = order };

    [Fact]
    public void APlainReplacementRunsOnTheOutput()
        => Assert.Equal(
            "the Salt House",
            ExportReplacements.Apply("the salt house", [Rule("salt house", "Salt House")]));

    [Fact]
    public void MatchCaseIsHonoured()
    {
        Assert.Equal(
            "Nightbrand and nightbrand",
            ExportReplacements.Apply(
                "Nightbrand and nightbrand", [Rule("NIGHTBRAND", "Gullwing", matchCase: true)]));
    }

    [Fact]
    public void ARegexCanUseItsCapturedGroups()
        => Assert.Equal(
            "Cole, Ada",
            ExportReplacements.Apply(
                "Ada Cole", [Rule(@"(\w+) (\w+)", "$2, $1", regex: true)]));

    [Fact]
    public void APlainRuleTreatsItsTextAsText()
    {
        // Typing "$1" in the replacement means a dollar and a one, not the
        // first captured group of a pattern the writer did not write.
        Assert.Equal("a$1b", ExportReplacements.Apply("aXb", [Rule("X", "$1")]));

        // And "1.5" means a dot, not "any character", so it does not match
        // "1x5" the way a regular expression would.
        Assert.Equal("1x5", ExportReplacements.Apply("1x5", [Rule("1.5", "changed")]));
        Assert.Equal("changed", ExportReplacements.Apply("1.5", [Rule("1.5", "changed")]));
    }

    [Fact]
    public void RulesRunInOrderSoOneFeedsTheNext()
        => Assert.Equal(
            "three",
            ExportReplacements.Apply("one", [Rule("one", "two", order: 0), Rule("two", "three", order: 1)]));

    [Fact]
    public void ADisabledRuleIsKeptAndNotRun()
    {
        var off = Rule("one", "two");
        off.Enabled = false;

        Assert.Equal("one", ExportReplacements.Apply("one", [off]));
    }

    [Fact]
    public void ABrokenPatternIsSkippedRatherThanFailingTheExport()
    {
        // A half-typed regular expression should not cost somebody their file.
        Assert.Equal(
            "untouched",
            ExportReplacements.Apply("untouched", [Rule("(unclosed", "x", regex: true)]));
    }

    [Fact]
    public void NothingToDoIsNotAnError()
    {
        Assert.Equal("text", ExportReplacements.Apply("text", null));
        Assert.Equal(string.Empty, ExportReplacements.Apply(string.Empty, [Rule("a", "b")]));
        // A rule with nothing to find is not a rule.
        Assert.Equal("text", ExportReplacements.Apply("text", [Rule("", "b")]));
    }

    [Fact]
    public void AStoreBuildResolvesItsOwnNameAndLink()
    {
        var resolved = ExportTokens.Resolve(
            "Enjoyed it? Leave a review at <$storename>: <$storelink>",
            new TokenContext { StoreName = "Kobo", StoreLink = "https://kobo.example/book" });

        // A book in five shops carried one link before this, which sends four
        // of those readers to a competitor.
        Assert.Equal(
            "Enjoyed it? Leave a review at Kobo: https://kobo.example/book",
            resolved);
    }

    [Fact]
    public void ANeutralBuildLeavesTheStoreLineShortRatherThanShowingTheToken()
    {
        var resolved = ExportTokens.Resolve("Also at <$storename>", new TokenContext());

        // An unknown token prints itself, which is right for a typo and wrong
        // here: this token is known and simply has nothing to say.
        Assert.Equal("Also at ", resolved);
    }

    [Fact]
    public void TheStoreTokensAreListedForTheUi()
    {
        Assert.Contains("storename", ExportTokens.Known);
        Assert.Contains("storelink", ExportTokens.Known);
    }
}
