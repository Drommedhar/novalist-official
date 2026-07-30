using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Turning "the next morning" into a date.
///
/// Novalist stored absolute dates and nothing else, so a writer who knows a
/// scene is two hours after the last one had to invent a date or leave it blank
/// - and blank meant the scene fell out of the Calendar and the Timeline.
/// </summary>
public class StoryClockTests
{
    private static (ChapterData, SceneData) At(string id, string date)
        => (new ChapterData(), new SceneData { Id = id, Date = date });

    private static (ChapterData, SceneData) After(string id, int amount, StoryTimeUnit unit)
        => (new ChapterData(),
            new SceneData
            {
                Id = id,
                RelativeTime = new RelativeStoryTime { Amount = amount, Unit = unit }
            });

    [Fact]
    public void ARelativeSceneIsPlacedAfterTheOneBeforeIt()
    {
        var resolved = StoryClock.Resolve([
            At("s1", "1043-03-01"),
            After("s2", 2, StoryTimeUnit.Hours),
            After("s3", 1, StoryTimeUnit.Days)
        ]);

        Assert.Equal("1043-03-01", resolved[0].Iso);
        Assert.Equal("1043-03-01 02:00", resolved[1].Iso);
        // Offsets accumulate: the third is a day after the second, not after
        // the anchor - which is how a writer means it.
        Assert.Equal("1043-03-02 02:00", resolved[2].Iso);
    }

    [Fact]
    public void ADateTheWriterTypedReAnchorsTheClock()
    {
        var resolved = StoryClock.Resolve([
            At("s1", "1043-03-01"),
            After("s2", 5, StoryTimeUnit.Days),
            At("s3", "1043-06-01"),
            After("s4", 1, StoryTimeUnit.Days)
        ]);

        // Relative time is for the gaps, never an override.
        Assert.Equal("1043-06-01", resolved[2].Iso);
        Assert.Equal("1043-06-02", resolved[3].Iso);
        Assert.False(resolved[2].Derived);
        Assert.True(resolved[3].Derived);
    }

    [Fact]
    public void ScenesBeforeTheFirstRealDateStayUnanchored()
    {
        var resolved = StoryClock.Resolve([
            After("s1", 2, StoryTimeUnit.Hours),
            At("s2", "1043-03-01")
        ]);

        // A book whose first date arrives in chapter nine has eight chapters
        // with no answer; hanging them off an invented epoch would put every
        // one of them on the wrong day.
        Assert.Null(resolved[0].Iso);
        Assert.False(resolved[0].Derived);
        Assert.Equal("1043-03-01", resolved[1].Iso);
    }

    [Fact]
    public void ANegativeOffsetIsACutBack()
    {
        var resolved = StoryClock.Resolve([
            At("s1", "1043-03-01 12:00"),
            After("s2", -3, StoryTimeUnit.Hours)
        ]);

        // A scene can be an hour before the one printed ahead of it. That is
        // what a cut-back is, and forbidding it would forbid the technique.
        Assert.Equal("1043-03-01 09:00", resolved[1].Iso);
    }

    [Fact]
    public void AChapterDateAnchorsTheScenesInsideIt()
    {
        var chapter = new ChapterData { Date = "1043-03-01" };
        var resolved = StoryClock.Resolve([
            (chapter, new SceneData { Id = "s1" }),
            (chapter, new SceneData
            {
                Id = "s2",
                RelativeTime = new RelativeStoryTime { Amount = 1, Unit = StoryTimeUnit.Days }
            })
        ]);

        Assert.Equal("1043-03-01", resolved[0].Iso);
        Assert.Equal("1043-03-02", resolved[1].Iso);
    }

    [Fact]
    public void ASceneWithNothingSaidAboutItStaysThatWay()
    {
        var resolved = StoryClock.Resolve([(new ChapterData(), new SceneData { Id = "s1" })]);

        Assert.Null(Assert.Single(resolved).Iso);
        Assert.Equal(string.Empty, resolved[0].Display);
    }

    [Fact]
    public void AWholeDayOffsetDoesNotClaimMidnight()
    {
        var resolved = StoryClock.Resolve([
            At("s1", "1043-03-01"),
            After("s2", 1, StoryTimeUnit.Weeks)
        ]);

        // A scene a week after another has a date, not a time.
        Assert.Equal("1043-03-08", resolved[1].Iso);
    }

    [Theory]
    [InlineData(2, StoryTimeUnit.Hours, "2 hours later")]
    [InlineData(1, StoryTimeUnit.Days, "1 day later")]
    [InlineData(-3, StoryTimeUnit.Minutes, "3 minutes earlier")]
    [InlineData(1, StoryTimeUnit.Weeks, "1 week later")]
    public void AnOffsetReadsAsAPhrase(int amount, StoryTimeUnit unit, string expected)
        => Assert.Equal(
            expected,
            StoryClock.Describe(new RelativeStoryTime { Amount = amount, Unit = unit }));

    [Fact]
    public void NoOffsetHasNoPhrase()
    {
        Assert.Equal(string.Empty, StoryClock.Describe(null));
        Assert.Equal(string.Empty, StoryClock.Describe(new RelativeStoryTime { Amount = 0 }));
    }

    [Theory]
    [InlineData(90, StoryTimeUnit.Minutes, 90)]
    [InlineData(2, StoryTimeUnit.Hours, 120)]
    [InlineData(1, StoryTimeUnit.Days, 1440)]
    [InlineData(1, StoryTimeUnit.Weeks, 10080)]
    public void EveryUnitConvertsToMinutes(int amount, StoryTimeUnit unit, int expected)
        => Assert.Equal(
            expected, new RelativeStoryTime { Amount = amount, Unit = unit }.TotalMinutes);

    [Fact]
    public void ADateNoGregorianParserCanReadLeavesTheSceneUnanchored()
    {
        // A book on its own calendar writes something no Gregorian parser
        // reads. Better unanchored than placed on a date nobody meant.
        var resolved = StoryClock.Resolve([
            (new ChapterData { Date = "Third moon, Year of the Gull" },
             new SceneData { Id = "s1" }),
            (new ChapterData(), new SceneData
            {
                Id = "s2",
                RelativeTime = new RelativeStoryTime { Amount = 1, Unit = StoryTimeUnit.Days }
            })
        ]);

        Assert.Null(resolved[0].Iso);
        // And the display still shows what the writer wrote, because that is
        // the only thing anybody can read it as.
        Assert.Equal("Third moon, Year of the Gull", resolved[0].Display);
        // The offset has nothing to be relative to, so it stays unanchored too.
        Assert.Null(resolved[1].Iso);
    }

}
