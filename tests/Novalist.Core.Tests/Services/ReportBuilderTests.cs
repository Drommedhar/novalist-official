using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Documents compiled out of what the writer already recorded.
///
/// Every scene carried a synopsis and a POV, and neither could be read as a
/// whole: the synopsis of a book existed only as forty separate boxes nobody
/// could put side by side, and "how much of this book is in Mira's head" could
/// not be asked at all.
/// </summary>
public class ReportBuilderTests
{
    private static ReportScene Scene(
        string chapter, string title, string synopsis = "", string pov = "", int words = 0)
        => new()
        {
            Chapter = chapter,
            Title = title,
            Synopsis = synopsis,
            Pov = pov,
            Words = words
        };

    // ─── Synopsis ────────────────────────────────────────────────────

    [Fact]
    public void EverySynopsisUnderItsChapter()
    {
        var report = ReportBuilder.Synopsis(
        [
            Scene("One", "Arrival", "She finds the deed.", words: 1200),
            Scene("One", "Departure", "He leaves."),
            Scene("Two", "The Rookery", "The house is damp.")
        ], "Salt Road");

        Assert.Contains("# Salt Road", report);
        Assert.Contains("## One", report);
        Assert.Contains("## Two", report);
        Assert.Contains("She finds the deed.", report);
        Assert.Contains("1,200 words", report);
    }

    [Fact]
    public void AChapterHeadingIsWrittenOncePerChapter()
    {
        var report = ReportBuilder.Synopsis(
            [Scene("One", "Arrival"), Scene("One", "Departure")], "Salt Road");

        Assert.Equal(1, report.Split("## One").Length - 1);
    }

    [Fact]
    public void ASceneWithNoSynopsisIsNamedAndLeftBlank()
    {
        var report = ReportBuilder.Synopsis([Scene("One", "Arrival")], "Salt Road");

        // The gaps are the reason to read this. A document that quietly omits
        // them reads as a finished outline.
        Assert.Contains("Arrival", report);
        Assert.Contains("No synopsis yet", report);
    }

    [Fact]
    public void ASceneWithNoWordsDoesNotClaimZero()
    {
        var report = ReportBuilder.Synopsis([Scene("One", "Arrival", "Something.")], "Salt Road");

        Assert.DoesNotContain("0 words", report);
    }

    [Fact]
    public void AnEmptyBookSaysSo()
    {
        var report = ReportBuilder.Synopsis([], "Salt Road");

        // Not an empty file, which reads as an export that failed.
        Assert.Contains("Nothing to report yet", report);
    }

    // ─── Point of view ───────────────────────────────────────────────

    [Fact]
    public void TheBookDividesBetweenPointsOfView()
    {
        var report = ReportBuilder.PovBreakdown(
        [
            Scene("One", "Arrival", pov: "Mira", words: 3000),
            Scene("One", "Departure", pov: "Tomas", words: 1000),
            Scene("Two", "The Rookery", pov: "Mira", words: 1000)
        ], "Salt Road");

        Assert.Contains("| Mira | 2 | 4,000 | 80% |", report);
        Assert.Contains("| Tomas | 1 | 1,000 | 20% |", report);
        Assert.Contains("**5,000**", report);
    }

    [Fact]
    public void TheBiggestShareComesFirst()
    {
        var report = ReportBuilder.PovBreakdown(
        [
            Scene("One", "A", pov: "Tomas", words: 100),
            Scene("One", "B", pov: "Mira", words: 900)
        ], "Salt Road");

        // The question this answers is whose book it is.
        Assert.True(report.IndexOf("Mira", StringComparison.Ordinal)
            < report.IndexOf("Tomas", StringComparison.Ordinal));
    }

    [Fact]
    public void ScenesWithNoPointOfViewAreTheirOwnRow()
    {
        var report = ReportBuilder.PovBreakdown(
        [
            Scene("One", "A", pov: "Mira", words: 800),
            Scene("One", "B", words: 200)
        ], "Salt Road");

        // A breakdown that silently excludes a fifth of the book is worse than
        // one saying a fifth of the book has no POV recorded.
        Assert.Contains($"| {ReportBuilder.UntitledPov} | 1 | 200 | 20% |", report);
    }

    [Fact]
    public void OneCharacterSpelledTwoWaysIsOneCharacter()
    {
        var report = ReportBuilder.PovBreakdown(
        [
            Scene("One", "A", pov: "Mira", words: 500),
            Scene("One", "B", pov: "mira", words: 500)
        ], "Salt Road");

        Assert.Contains("| 2 | 1,000 | 100% |", report);
    }

    [Fact]
    public void ABookOfEmptyScenesDoesNotDivideByZero()
    {
        var report = ReportBuilder.PovBreakdown([Scene("One", "A", pov: "Mira")], "Salt Road");

        // An outline is exactly where every scene has no words yet.
        Assert.Contains("| Mira | 1 | 0 | — |", report);
    }

    [Fact]
    public void AnEmptyBookSaysSoHereToo()
        => Assert.Contains("Nothing to report yet", ReportBuilder.PovBreakdown([], "Salt Road"));
}
