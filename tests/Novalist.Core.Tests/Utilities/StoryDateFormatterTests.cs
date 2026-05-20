using Novalist.Core.Models;
using Novalist.Core.Utilities;
using Xunit;

namespace Novalist.Core.Tests.Utilities;

public class StoryDateFormatterTests
{
    [Fact]
    public void HasAnyDate_TrueForDate()
        => Assert.True(StoryDateFormatter.HasAnyDate("2024-01-01", null));

    [Fact]
    public void HasAnyDate_TrueForRangeWithValue()
        => Assert.True(StoryDateFormatter.HasAnyDate(null, new StoryDateRange { Start = "2024-01-01" }));

    [Fact]
    public void HasAnyDate_FalseForNothing()
        => Assert.False(StoryDateFormatter.HasAnyDate(null, new StoryDateRange()));

    [Fact]
    public void Format_PrefersRange()
        => Assert.Equal("2024-01-01", StoryDateFormatter.Format("ignored", new StoryDateRange { Start = "2024-01-01" }));

    [Fact]
    public void Format_FallsBackToTrimmedDate()
        => Assert.Equal("2024-01-01", StoryDateFormatter.Format("  2024-01-01  ", null));

    [Fact]
    public void Format_EmptyWhenNothing()
        => Assert.Equal(string.Empty, StoryDateFormatter.Format("   ", null));

    [Fact]
    public void FormatRange_SingleDate_NoTime()
        => Assert.Equal("2024-10-22", StoryDateFormatter.FormatRange(new StoryDateRange { Start = "2024-10-22" }));

    [Fact]
    public void FormatRange_SingleDate_StartTimeOnly()
        => Assert.Equal("2024-10-22 18:00",
            StoryDateFormatter.FormatRange(new StoryDateRange { Start = "2024-10-22", StartTime = "18:00" }));

    [Fact]
    public void FormatRange_SingleDate_BothTimes()
        => Assert.Equal("2024-10-22 18:00-18:30",
            StoryDateFormatter.FormatRange(new StoryDateRange { Start = "2024-10-22", StartTime = "18:00", EndTime = "18:30" }));

    [Fact]
    public void FormatRange_SingleDate_SameStartEndTime_Collapses()
        => Assert.Equal("2024-10-22 18:00",
            StoryDateFormatter.FormatRange(new StoryDateRange { Start = "2024-10-22", StartTime = "18:00", EndTime = "18:00" }));

    [Fact]
    public void FormatRange_SameStartAndEndDate_TreatedAsSingle()
        => Assert.Equal("2024-10-22",
            StoryDateFormatter.FormatRange(new StoryDateRange { Start = "2024-10-22", End = "2024-10-22" }));

    [Fact]
    public void FormatRange_MultiDay_NoTimes()
        => Assert.Equal("2024-10-22 - 2024-10-23",
            StoryDateFormatter.FormatRange(new StoryDateRange { Start = "2024-10-22", End = "2024-10-23" }));

    [Fact]
    public void FormatRange_MultiDay_WithTimes()
        => Assert.Equal("2024-10-22 23:30 - 2024-10-23 00:30",
            StoryDateFormatter.FormatRange(new StoryDateRange
            {
                Start = "2024-10-22", End = "2024-10-23", StartTime = "23:30", EndTime = "00:30"
            }));

    [Fact]
    public void FormatRange_NoDates_TimesOnly()
        => Assert.Equal("10:00-11:00",
            StoryDateFormatter.FormatRange(new StoryDateRange { StartTime = "10:00", EndTime = "11:00" }));

    [Fact]
    public void FormatRange_Empty_ReturnsEmpty()
        => Assert.Equal(string.Empty, StoryDateFormatter.FormatRange(new StoryDateRange()));

    [Fact]
    public void FormatRange_NullStringFields_CoalesceToEmpty()
    {
        // Defensive null-coalescing branches: force nulls via null-forgiving.
        var range = new StoryDateRange { Start = null!, End = null!, StartTime = null!, EndTime = null! };
        Assert.Equal(string.Empty, StoryDateFormatter.FormatRange(range));
    }

    [Fact]
    public void FormatRange_EndTimeOnly_NoStartTime()
        => Assert.Equal("2024-10-22 11:00",
            StoryDateFormatter.FormatRange(new StoryDateRange { Start = "2024-10-22", EndTime = "11:00" }));

    [Fact]
    public void FormatRange_MultiDay_StartTimeOnly()
        => Assert.Equal("2024-10-22 09:00 - 2024-10-23",
            StoryDateFormatter.FormatRange(new StoryDateRange { Start = "2024-10-22", End = "2024-10-23", StartTime = "09:00" }));

    [Theory]
    [InlineData(null, null)]
    [InlineData("short", null)]
    [InlineData("2024-10-22 extra", "2024-10-22")]
    [InlineData("2024x10-22", null)]
    [InlineData("2024-1x-22", null)]
    [InlineData("20a4-10-22", null)]
    public void ExtractLeadingDate(string? input, string? expected)
        => Assert.Equal(expected, StoryDateFormatter.ExtractLeadingDate(input));
}
