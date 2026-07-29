using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// The in-world calendar configuration surface. The parsing and arithmetic
/// already understood a custom calendar; these cover the config that reaches it.
/// </summary>
public sealed class CalendarConfigRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly CalendarRpc _rpc;

    public CalendarConfigRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-cal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "CalNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new CalendarRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void GetConfig_UnconfiguredBook_ReturnsGregorianDefaults()
    {
        var config = _rpc.GetConfig();

        Assert.Equal("Gregorian", config.Type);
        Assert.Empty(config.MonthNames);
        Assert.Equal(0, config.YearLength);
    }

    [Fact]
    public async Task SetConfig_StoresACustomCalendar()
    {
        var config = await _rpc.SetConfigAsync(
            "Custom", "AC",
            ["Frost", "Thaw", "High Sun"], [30, 28, 32],
            ["Moonday", "Sunday", "Starday"]);

        Assert.Equal("Custom", config.Type);
        Assert.Equal("AC", config.YearLabel);
        Assert.Equal(["Frost", "Thaw", "High Sun"], config.MonthNames);
        Assert.Equal([30, 28, 32], config.DaysPerMonth);
        Assert.Equal(90, config.YearLength);
        Assert.Equal(3, config.WeekdayNames.Length);
    }

    [Fact]
    public async Task SetConfig_SurvivesAReload()
    {
        await _rpc.SetConfigAsync("Custom", "AC", ["Frost"], [40], ["Moonday"]);

        await _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!);

        Assert.Equal(40, new CalendarRpc(_workspace).GetConfig().YearLength);
    }

    [Fact]
    public async Task SetConfig_MismatchedMonthsAndLengths_ZipToTheShorter()
    {
        // A half-finished edit must never produce months and lengths that
        // disagree, because year-length arithmetic depends on them pairing up.
        var config = await _rpc.SetConfigAsync(
            "Custom", "", ["One", "Two", "Three"], [30, 30], []);

        Assert.Equal(2, config.MonthNames.Length);
        Assert.Equal(2, config.DaysPerMonth.Length);
        Assert.Equal(60, config.YearLength);
    }

    [Fact]
    public async Task SetConfig_BlankMonthNamesAreDropped()
    {
        var config = await _rpc.SetConfigAsync(
            "Custom", "", ["Frost", "   ", "Thaw"], [30, 30, 30], []);

        Assert.Equal(["Frost", "Thaw"], config.MonthNames);
        Assert.Equal(60, config.YearLength);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task SetConfig_NonPositiveMonthLengthBecomesOne(int days)
    {
        // A zero-day month makes year length meaningless.
        var config = await _rpc.SetConfigAsync("Custom", "", ["Frost"], [days], []);

        Assert.Equal(1, config.DaysPerMonth[0]);
    }

    [Fact]
    public async Task SetConfig_BlankWeekdaysAreDropped()
    {
        var config = await _rpc.SetConfigAsync("Custom", "", ["Frost"], [30], ["Moonday", "  ", "Starday"]);

        Assert.Equal(["Moonday", "Starday"], config.WeekdayNames);
    }

    [Fact]
    public async Task SetConfig_UnknownType_FallsBackToGregorian()
    {
        var config = await _rpc.SetConfigAsync("Martian", "", [], [], []);
        Assert.Equal("Gregorian", config.Type);
    }

    [Fact]
    public async Task SetConfig_TrimsTheYearLabel()
    {
        var config = await _rpc.SetConfigAsync("Custom", "  AC  ", ["Frost"], [30], []);
        Assert.Equal("AC", config.YearLabel);
    }

    [Fact]
    public async Task SetConfig_FeedsTheDateArithmetic()
    {
        // The point of the feature: a configured calendar changes what a
        // duration means. Three 30-day months make a 90-day year.
        await _rpc.SetConfigAsync("Custom", "AC", ["A", "B", "C"], [30, 30, 30], []);

        var calendar = _workspace.Projects.ActiveBook!.Calendar;
        var service = new InWorldCalendarService();

        Assert.Equal(90, calendar!.CustomYearLength);
        // One year apart in a 90-day year is 90 days, not 365.
        Assert.Equal(90, service.DiffDays("1.1.1", "2.1.1", calendar));
    }

    [Fact]
    public async Task SetConfig_WithoutAProject_Throws()
    {
        using var bare = new Workspace(Path.Combine(_root, "settings2"));
        var rpc = new CalendarRpc(bare);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => rpc.SetConfigAsync("Custom", "", ["A"], [30], []));
    }

    [Fact]
    public void GetConfig_WithoutAProject_StillReturnsDefaults()
    {
        using var bare = new Workspace(Path.Combine(_root, "settings3"));

        Assert.Equal("Gregorian", new CalendarRpc(bare).GetConfig().Type);
    }
}
