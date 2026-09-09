using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class CalendarRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly CalendarRpc _rpc;

    public CalendarRpcTests()
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
    public async Task Get_ResolvesScenesOntoDays_WithMultiDayAndTimes()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var single = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Single", "1043-03-05");
        var multi = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Multi");
        await _workspace.Projects.SetSceneDateRangeAsync(chapter.Guid, multi.Id, new StoryDateRange
        {
            Start = "1043-03-06",
            End = "1043-03-08",
            StartTime = "09:30",
            EndTime = "17:00",
            Note = "Council of elders"
        });
        await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Undated");

        var events = _rpc.Get("1043-03-01", "1043-03-31");

        var singleEvent = Assert.Single(events, e => e.SceneId == single.Id && e.AllDay && e.Date == "1043-03-05");
        Assert.Null(singleEvent.Note);
        var multiDays = events.Where(e => e.SceneId == multi.Id).Select(e => e.Date).ToArray();
        Assert.Equal(new[] { "1043-03-06", "1043-03-07", "1043-03-08" }, multiDays);
        var timed = events.First(e => e.SceneId == multi.Id);
        Assert.False(timed.AllDay);
        Assert.Equal(9, timed.StartHour);
        Assert.Equal(30, timed.StartMinute);
        Assert.Equal(17, timed.EndHour);
        Assert.Equal("Council of elders", timed.Note);
        Assert.DoesNotContain(events, e => e.Title == "Undated");

        Assert.Empty(_rpc.Get("1044-01-01", "1044-12-31"));
    }

    [Fact]
    public void Get_ThrowsWhenNoProjectOpen()
    {
        var noProjectRoot = Path.Combine(Path.GetTempPath(), "nl-cal-np-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(noProjectRoot);
        var workspace = new Workspace(Path.Combine(noProjectRoot, "settings"));
        var rpc = new CalendarRpc(workspace);

        Assert.Throws<InvalidOperationException>(() => rpc.Get("1043-03-01", "1043-03-31"));

        try { Directory.Delete(noProjectRoot, true); } catch (IOException) { }
    }

    [Fact]
    public async Task Reschedule_WritesIsoDate()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S", "1043-03-05");

        await _rpc.RescheduleAsync(chapter.Guid, scene.Id, "1043-04-01");

        var events = _rpc.Get("1043-04-01", "1043-04-01");
        Assert.Single(events, e => e.SceneId == scene.Id);
    }

    [Fact]
    public async Task Anchor_RoundTrips()
    {
        Assert.Null(_rpc.GetAnchor());
        await _rpc.SetAnchorAsync("1043-03-05");
        Assert.Equal("1043-03-05", _rpc.GetAnchor());
    }

    [Fact]
    public async Task CustomCalendar_ResolvesAndClipsRangesAcrossYears_AndReschedules()
    {
        await _rpc.SetConfigAsync("Custom", "Cycle", ["Rimefall", "Thaw", "Highsun"],
            [30, 30, 40], ["Firstday", "Starday"]);
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Crossing");
        await _workspace.Projects.SetSceneDateRangeAsync(chapter.Guid, scene.Id, new StoryDateRange
        {
            Start = "-1.3.39", End = "0.1.2", StartTime = "09:30", EndTime = "10:00", Note = "Crossing"
        });
        await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Invalid", "0.4.1");
        await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Undated");
        var events = _rpc.Get("-1.3.40", "0.1.1");
        Assert.Equal(new[] { "-1.3.40", "0.1.1" }, events.Select(e => e.Date));
        Assert.All(events, e => { Assert.Equal(scene.Id, e.SceneId); Assert.Equal(9, e.StartHour); });
        Assert.Empty(_rpc.Get("1.1.1", "1.1.2"));
        await _rpc.RescheduleAsync(chapter.Guid, scene.Id, "812.3.40");
        var moved = Assert.Single(_rpc.Get("812.3.40", "812.3.40"), e => e.SceneId == scene.Id);
        Assert.Equal(9, moved.StartHour);
        Assert.Equal(10, moved.EndHour);
        Assert.Equal("Crossing", moved.Note);
        Assert.Equal("813.1.3", scene.DateRange!.End);
        Assert.Throws<FormatException>(() => _rpc.Get("0.4.1", "0.1.1"));
        Assert.Throws<FormatException>(() => _rpc.Get("0.1.1", "0.1.31"));
    }

    [Fact]
    public async Task Reschedule_PreservesGregorianRangesBeforeYearOneHundred()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        await _workspace.Projects.SetSceneDateRangeAsync(chapter.Guid, scene.Id,
            new StoryDateRange { Start = "0012-02-28", End = "0012-03-01" });
        await _rpc.RescheduleAsync(chapter.Guid, scene.Id, "0012-02-29");
        Assert.Equal(new[] { "0012-02-29", "0012-03-01", "0012-03-02" },
            _rpc.Get("0012-02-01", "0012-03-31").Select(e => e.Date));
    }

    [Fact]
    public async Task CustomCalendar_AnchorUsesSavedDateOrFirstDatedScene()
    {
        await _rpc.SetConfigAsync("Custom", "", ["Rimefall"], [40], ["Starday"]);
        Assert.Equal("0.1.1", _rpc.GetAnchor());
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Undated");
        await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Invalid", "bad");
        await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Dated", "-12.1.40");
        await _rpc.SetAnchorAsync("2026-09-09");
        Assert.Equal("-12.1.40", _rpc.GetAnchor());
        await _rpc.SetAnchorAsync("812.1.40");
        Assert.Equal("812.1.40", _rpc.GetAnchor());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("09:30", 9 * 60 + 30)]
    [InlineData("9:05", 9 * 60 + 5)]
    [InlineData("garbage", null)]
    public void ParseTime_HandlesFormats(string? input, int? expectedMinutes)
    {
        var result = CalendarRpc.ParseTime(input);
        Assert.Equal(expectedMinutes, result == null ? null : (int?)result.Value.TotalMinutes);
    }

    [Fact]
    public void TryParseDate_RejectsEmptyAndGarbage()
    {
        Assert.False(CalendarRpc.TryParseDate(null, out _));
        Assert.False(CalendarRpc.TryParseDate("nope", out _));
        Assert.True(CalendarRpc.TryParseDate("1043-03-05", out var date));
        Assert.Equal(5, date.Day);
    }
}
