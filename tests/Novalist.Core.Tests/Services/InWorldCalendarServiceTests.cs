using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class InWorldCalendarServiceTests
{
    private readonly InWorldCalendarService _sut = new();

    private static InWorldCalendar Custom() => new()
    {
        Type = InWorldCalendarType.Custom,
        MonthNames = { "M1", "M2", "M3" },
        DaysPerMonth = { 10, 10, 10 }
    };

    [Fact]
    public void Parse_Blank_ReturnsNull()
        => Assert.Null(_sut.Parse("  ", null));

    [Fact]
    public void Parse_Gregorian_ValidDate()
        => Assert.NotNull(_sut.Parse("2024-10-22", null));

    [Fact]
    public void Parse_Gregorian_Invalid_ReturnsNull()
        => Assert.Null(_sut.Parse("not-a-date", new InWorldCalendar { Type = InWorldCalendarType.Gregorian }));

    [Fact]
    public void Parse_Custom_ValidOrdinal()
    {
        // Year 1, month 2, day 1 => 1*30 + 10 + 0 = 40
        Assert.Equal(40, _sut.Parse("1.2.1", Custom()));
    }

    [Fact]
    public void Parse_Custom_NoMonths_ReturnsNull()
    {
        var cal = new InWorldCalendar { Type = InWorldCalendarType.Custom };
        Assert.Null(_sut.Parse("1.1.1", cal));
    }

    [Theory]
    [InlineData("1.0.1")]   // month < 1
    [InlineData("1.4.1")]   // month > count
    [InlineData("1.1.0")]   // day < 1
    [InlineData("1.1.99")]  // day > days in month
    [InlineData("garbage")] // regex miss
    public void Parse_Custom_OutOfRange_ReturnsNull(string raw)
        => Assert.Null(_sut.Parse(raw, Custom()));

    [Fact]
    public void DiffDays_ComputesDifference()
        => Assert.Equal(10, _sut.DiffDays("1.1.1", "1.2.1", Custom()));

    [Fact]
    public void DiffDays_InvalidInput_ReturnsNull()
        => Assert.Null(_sut.DiffDays("bad", "1.1.1", Custom()));

    [Fact]
    public void AddDays_Gregorian_AddsAndFormats()
        => Assert.Equal("2024-10-25", _sut.AddDays("2024-10-22", 3, null));

    [Fact]
    public void AddDays_Gregorian_Invalid_ReturnsRaw()
        => Assert.Equal("bad", _sut.AddDays("bad", 3, new InWorldCalendar { Type = InWorldCalendarType.Gregorian }));

    [Fact]
    public void AddDays_Custom_RollsOverMonths()
    {
        // 1.1.1 (ordinal 30) + 12 days = ordinal 42 => year 1, month 2, day 3
        Assert.Equal("1.2.3", _sut.AddDays("1.1.1", 12, Custom()));
    }

    [Fact]
    public void AddDays_Custom_NegativeWrapsYear()
    {
        // 1.1.1 (ordinal 30) - 1 = ordinal 29 => year 0, last month, last day
        Assert.Equal("0.3.10", _sut.AddDays("1.1.1", -1, Custom()));
    }

    [Fact]
    public void AddDays_Custom_Unparseable_ReturnsRaw()
        => Assert.Equal("bad", _sut.AddDays("bad", 1, Custom()));

    [Theory]
    [InlineData("1.1.1", "1.1.1", "same day")]
    [InlineData("1.1.1", "1.1.2", "1 day")]
    [InlineData("1.1.1", "1.1.6", "5 days")]
    [InlineData("1.1.1", "1.2.6", "2 weeks")]   // 15 days
    public void DurationLabel_CustomRanges(string start, string end, string expected)
    {
        var range = new StoryDateRange { Start = start, End = end };
        Assert.Equal(expected, _sut.DurationLabel(range, Custom()));
    }

    [Fact]
    public void DurationLabel_Months_And_Years_Gregorian()
    {
        Assert.Equal("3 months", _sut.DurationLabel(new StoryDateRange { Start = "2024-01-01", End = "2024-04-10" }, null));
        Assert.Equal("2 years", _sut.DurationLabel(new StoryDateRange { Start = "2020-01-01", End = "2022-06-01" }, null));
    }

    [Fact]
    public void DurationLabel_NullOrIncomplete_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, _sut.DurationLabel(null, null));
        Assert.Equal(string.Empty, _sut.DurationLabel(new StoryDateRange(), null));
        Assert.Equal(string.Empty, _sut.DurationLabel(new StoryDateRange { Start = "2024-01-01" }, null));
    }

    [Fact]
    public void DurationLabel_Unparseable_ReturnsEmpty()
        => Assert.Equal(string.Empty, _sut.DurationLabel(new StoryDateRange { Start = "x", End = "y" }, Custom()));
}
