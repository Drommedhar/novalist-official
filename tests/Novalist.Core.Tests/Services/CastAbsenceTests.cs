using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Who drops out of the book. The counts have been drawn as a grid for a while;
/// nothing ever read the grid for the answer.
/// </summary>
public class CastAbsenceTests
{
    private static PresenceRow Row(string label, params int[] perChapter)
        => new(label.ToLowerInvariant(), label, perChapter.Sum(), perChapter);

    [Fact]
    public void FindsTheLongestGapBetweenAppearances()
    {
        var rows = CastAbsence.From([Row("Mira", 1, 0, 0, 0, 2, 1)], chapterCount: 6);

        var mira = Assert.Single(rows);
        Assert.Equal(3, mira.LongestGap);
        Assert.Equal(1, mira.GapStartChapter);
        Assert.Equal(3, mira.GapEndChapter);
        Assert.Equal(0, mira.FirstChapter);
        Assert.Equal(5, mira.LastChapter);
        Assert.Equal(0, mira.ChaptersSinceLastSeen);
        Assert.Equal(4, mira.TotalScenes);
    }

    // Arriving late is an entrance, not a disappearance, so the empty chapters
    // in front of a character are not a gap.
    [Fact]
    public void ChaptersBeforeTheFirstAppearanceAreNotAGap()
    {
        var rows = CastAbsence.From([Row("Tomas", 0, 0, 0, 1, 1, 1)], chapterCount: 6);

        Assert.Empty(rows);
    }

    // The other half of the question: somebody who simply stops has no second
    // appearance to measure a gap to.
    [Fact]
    public void CountsChaptersSinceTheLastAppearance()
    {
        var rows = CastAbsence.From([Row("Solo", 1, 1, 0, 0, 0, 0)], chapterCount: 6);

        var solo = Assert.Single(rows);
        Assert.Equal(0, solo.LongestGap);
        Assert.Equal(-1, solo.GapStartChapter);
        Assert.Equal(1, solo.LastChapter);
        Assert.Equal(4, solo.ChaptersSinceLastSeen);
    }

    [Fact]
    public void AShortGapIsASceneRatherThanAProblem()
    {
        // One chapter missing, and present in the last chapter, so neither
        // measure reaches the threshold.
        Assert.Empty(CastAbsence.From([Row("Nora", 1, 0, 1)], chapterCount: 3));

        // The threshold is the caller's to set.
        Assert.Single(CastAbsence.From([Row("Nora", 1, 0, 1)], chapterCount: 3, minimumGap: 1));
    }

    [Fact]
    public void AnEntryThatNeverAppearsHasNothingToBeAbsentFrom()
        => Assert.Empty(CastAbsence.From([Row("Ghost", 0, 0, 0)], chapterCount: 3));

    [Fact]
    public void WorstFirst()
    {
        var rows = CastAbsence.From(
            [
                Row("Small", 1, 0, 0, 1, 1, 1),      // gap 2
                Row("Biggest", 1, 0, 0, 0, 0, 1),    // gap 4
                Row("Stopped", 1, 1, 0, 0, 0, 0)     // gap 0, gone for 4
            ],
            chapterCount: 6);

        Assert.Equal(["Biggest", "Small", "Stopped"], rows.Select(r => r.Label));
    }

    [Fact]
    public void TwoWithTheSameGapReadInNameOrder()
    {
        var rows = CastAbsence.From(
            [Row("Bea", 1, 0, 0, 1), Row("Ada", 1, 0, 0, 1)], chapterCount: 4);

        Assert.Equal(["Ada", "Bea"], rows.Select(r => r.Label));
    }

    [Fact]
    public void NoChaptersMeansNothingToReport()
        => Assert.Empty(CastAbsence.From([Row("Mira")], chapterCount: 0));
}
