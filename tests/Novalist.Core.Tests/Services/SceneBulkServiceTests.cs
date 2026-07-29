using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Bulk operations behind the multi-select bar. The recurring theme: a selection
/// outlives the list that built it, so a stale id must never take the whole
/// operation down with it.
/// </summary>
public class SceneBulkServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProjectService _projects = new(new FileService());
    private readonly SceneBulkService _sut;

    public SceneBulkServiceTests()
    {
        _sut = new SceneBulkService(_projects, new InWorldCalendarService());
    }

    public void Dispose() => _dir.Dispose();

    private async Task<(string ChapterGuid, SceneData A, SceneData B)> TwoScenesAsync()
    {
        await _projects.CreateProjectAsync(_dir.Path, "P", "Book");
        var chapter = await _projects.CreateChapterAsync("Chapter One");
        var a = await _projects.CreateSceneAsync(chapter.Guid, "Scene A");
        var b = await _projects.CreateSceneAsync(chapter.Guid, "Scene B");
        return (chapter.Guid, a, b);
    }

    // ── Resolve ──

    [Fact]
    public async Task Resolve_ReturnsScenesInBookOrder()
    {
        await _projects.CreateProjectAsync(_dir.Path, "P", "Book");
        var one = await _projects.CreateChapterAsync("One");
        var two = await _projects.CreateChapterAsync("Two");
        var first = await _projects.CreateSceneAsync(one.Guid, "First");
        var second = await _projects.CreateSceneAsync(one.Guid, "Second");
        var third = await _projects.CreateSceneAsync(two.Guid, "Third");

        // Asked for out of order; comes back in the order the binder shows.
        var resolved = _sut.Resolve([third.Id, first.Id, second.Id]);

        Assert.Equal([first.Id, second.Id, third.Id], resolved.Select(r => r.Scene.Id));
        Assert.Equal(one.Guid, resolved[0].ChapterGuid);
        Assert.Equal(two.Guid, resolved[2].ChapterGuid);
    }

    [Fact]
    public async Task Resolve_DropsIdsThatNameNothing()
    {
        var (_, a, _) = await TwoScenesAsync();

        var resolved = _sut.Resolve([a.Id, "deleted-since-the-selection-was-made"]);

        Assert.Equal([a.Id], resolved.Select(r => r.Scene.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Resolve_IgnoresBlankIds(string id)
    {
        await TwoScenesAsync();

        Assert.Empty(_sut.Resolve([id]));
    }

    [Fact]
    public async Task Resolve_EmptySelectionResolvesToNothing()
    {
        await TwoScenesAsync();

        Assert.Empty(_sut.Resolve([]));
    }

    // ── Delete ──

    [Fact]
    public async Task Delete_RemovesEveryNamedScene()
    {
        var (chapter, a, b) = await TwoScenesAsync();
        var c = await _projects.CreateSceneAsync(chapter, "Scene C");

        var deleted = await _sut.DeleteAsync([a.Id, b.Id]);

        Assert.Equal(2, deleted);
        Assert.Equal([c.Id], _projects.GetScenesForChapter(chapter).Select(s => s.Id));
    }

    [Fact]
    public async Task Delete_ReindexesWhatIsLeft()
    {
        var (chapter, a, _) = await TwoScenesAsync();

        await _sut.DeleteAsync([a.Id]);

        Assert.Equal([1], _projects.GetScenesForChapter(chapter).Select(s => s.Order));
    }

    [Fact]
    public async Task Delete_SpansChapters()
    {
        await _projects.CreateProjectAsync(_dir.Path, "P", "Book");
        var one = await _projects.CreateChapterAsync("One");
        var two = await _projects.CreateChapterAsync("Two");
        var here = await _projects.CreateSceneAsync(one.Guid, "Here");
        var there = await _projects.CreateSceneAsync(two.Guid, "There");

        Assert.Equal(2, await _sut.DeleteAsync([here.Id, there.Id]));
        Assert.Empty(_projects.GetScenesForChapter(one.Guid));
        Assert.Empty(_projects.GetScenesForChapter(two.Guid));
    }

    // ── Archive ──

    [Fact]
    public async Task Archive_MovesEveryNamedSceneToTheArchive()
    {
        var (chapter, a, b) = await TwoScenesAsync();

        var archived = await _sut.ArchiveAsync([a.Id, b.Id]);

        Assert.Equal(2, archived);
        Assert.Empty(_projects.GetScenesForChapter(chapter));
        Assert.Equal(2, _projects.ScenesManifest!.Archived.Count);
    }

    [Fact]
    public async Task Archive_RemembersWhereEachSceneCameFrom()
    {
        var (chapter, a, _) = await TwoScenesAsync();

        await _sut.ArchiveAsync([a.Id]);

        Assert.Equal(chapter, _projects.ScenesManifest!.Archived.Single().OriginChapterGuid);
    }

    // ── Tags ──

    [Fact]
    public async Task SetTags_AddsToEveryNamedScene()
    {
        var (chapter, a, b) = await TwoScenesAsync();

        var changed = await _sut.SetTagsAsync([a.Id, b.Id], ["flashback", "night"], replace: false);

        Assert.Equal(2, changed);
        foreach (var scene in _projects.GetScenesForChapter(chapter))
            Assert.Equal(["flashback", "night"], scene.AnalysisOverrides!.Tags);
    }

    [Fact]
    public async Task SetTags_KeepsTagsTheSceneAlreadyHad()
    {
        var (chapter, a, _) = await TwoScenesAsync();
        await _projects.SetSceneAnalysisOverridesAsync(
            chapter, a.Id, new SceneAnalysisOverrides { Tags = ["night"] });

        await _sut.SetTagsAsync([a.Id], ["flashback"], replace: false);

        var tags = _projects.GetScenesForChapter(chapter).First(s => s.Id == a.Id)
            .AnalysisOverrides!.Tags!;
        Assert.Equal(["night", "flashback"], tags);
    }

    [Fact]
    public async Task SetTags_ReplaceDropsWhatWasThere()
    {
        var (chapter, a, _) = await TwoScenesAsync();
        await _projects.SetSceneAnalysisOverridesAsync(
            chapter, a.Id, new SceneAnalysisOverrides { Tags = ["night"] });

        await _sut.SetTagsAsync([a.Id], ["flashback"], replace: true);

        Assert.Equal(
            ["flashback"],
            _projects.GetScenesForChapter(chapter).First(s => s.Id == a.Id).AnalysisOverrides!.Tags);
    }

    [Fact]
    public async Task SetTags_ASceneThatAlreadyHasThemIsNotRewritten()
    {
        var (chapter, a, _) = await TwoScenesAsync();
        await _projects.SetSceneAnalysisOverridesAsync(
            chapter, a.Id, new SceneAnalysisOverrides { Tags = ["night"] });

        Assert.Equal(0, await _sut.SetTagsAsync([a.Id], ["Night"], replace: false));
    }

    [Fact]
    public async Task SetTags_BlanksAndDuplicatesAreDropped()
    {
        var (chapter, a, _) = await TwoScenesAsync();

        await _sut.SetTagsAsync([a.Id], ["  ", "night", "NIGHT", ""], replace: false);

        Assert.Equal(
            ["night"],
            _projects.GetScenesForChapter(chapter).First(s => s.Id == a.Id).AnalysisOverrides!.Tags);
    }

    [Fact]
    public async Task SetTags_ReplaceWithNothingClearsThem()
    {
        var (chapter, a, _) = await TwoScenesAsync();
        await _projects.SetSceneAnalysisOverridesAsync(
            chapter, a.Id, new SceneAnalysisOverrides { Tags = ["night"] });

        Assert.Equal(1, await _sut.SetTagsAsync([a.Id], [], replace: true));
        Assert.Empty(
            _projects.GetScenesForChapter(chapter).First(s => s.Id == a.Id).AnalysisOverrides!.Tags!);
    }

    // ── Date shift ──

    [Fact]
    public async Task PreviewDateShift_ShowsBeforeAndAfterForEachScene()
    {
        var (chapter, a, b) = await TwoScenesAsync();
        await _projects.SetSceneDateAsync(chapter, a.Id, "2026-03-01");
        await _projects.SetSceneDateAsync(chapter, b.Id, "2026-03-05");

        var rows = _sut.PreviewDateShift([a.Id, b.Id], 3);

        Assert.Equal(["2026-03-01", "2026-03-05"], rows.Select(r => r.Before));
        Assert.Equal(["2026-03-04", "2026-03-08"], rows.Select(r => r.After));
        Assert.Equal(["Scene A", "Scene B"], rows.Select(r => r.Title));
    }

    [Fact]
    public async Task PreviewDateShift_ADatelessSceneIsListedUnchanged()
    {
        var (chapter, a, b) = await TwoScenesAsync();
        await _projects.SetSceneDateAsync(chapter, a.Id, "2026-03-01");

        var rows = _sut.PreviewDateShift([a.Id, b.Id], 3);

        // Both rows appear, so a selection of two never previews as one.
        Assert.Equal(2, rows.Count);
        var dateless = rows.Single(r => r.SceneId == b.Id);
        Assert.Equal(string.Empty, dateless.Before);
        Assert.Equal(string.Empty, dateless.After);
    }

    [Fact]
    public async Task PreviewDateShift_ReadsTheRangeStartWhenASceneHasOne()
    {
        var (chapter, a, _) = await TwoScenesAsync();
        await _projects.SetSceneDateRangeAsync(
            chapter, a.Id, new StoryDateRange { Start = "2026-03-01", End = "2026-03-04" });

        var row = _sut.PreviewDateShift([a.Id], -1).Single();

        Assert.Equal("2026-03-01", row.Before);
        Assert.Equal("2026-02-28", row.After);
    }

    [Fact]
    public async Task ShiftDates_MovesPlainDates()
    {
        var (chapter, a, _) = await TwoScenesAsync();
        await _projects.SetSceneDateAsync(chapter, a.Id, "2026-03-01");

        Assert.Equal(1, await _sut.ShiftDatesAsync([a.Id], 10));
        Assert.Equal(
            "2026-03-11",
            _projects.GetScenesForChapter(chapter).First(s => s.Id == a.Id).Date);
    }

    [Fact]
    public async Task ShiftDates_MovesBothEndsOfARange()
    {
        var (chapter, a, _) = await TwoScenesAsync();
        await _projects.SetSceneDateRangeAsync(
            chapter, a.Id, new StoryDateRange { Start = "2026-03-01", End = "2026-03-04" });

        Assert.Equal(1, await _sut.ShiftDatesAsync([a.Id], 2));

        var range = _projects.GetScenesForChapter(chapter).First(s => s.Id == a.Id).DateRange!;
        Assert.Equal("2026-03-03", range.Start);
        Assert.Equal("2026-03-06", range.End);
    }

    [Fact]
    public async Task ShiftDates_ARangeWithOnlyANoteIsLeftAlone()
    {
        var (chapter, a, _) = await TwoScenesAsync();
        await _projects.SetSceneDateRangeAsync(
            chapter, a.Id, new StoryDateRange { Note = "some time later" });

        Assert.Equal(0, await _sut.ShiftDatesAsync([a.Id], 5));
        Assert.Equal(
            "some time later",
            _projects.GetScenesForChapter(chapter).First(s => s.Id == a.Id).DateRange!.Note);
    }

    [Fact]
    public async Task ShiftDates_ASceneWithNoDateDoesNotCount()
    {
        var (_, a, _) = await TwoScenesAsync();

        Assert.Equal(0, await _sut.ShiftDatesAsync([a.Id], 7));
    }

    [Fact]
    public async Task ShiftDates_AnUnparseableDateIsPreservedRatherThanBlanked()
    {
        var (chapter, a, _) = await TwoScenesAsync();
        await _projects.SetSceneDateAsync(chapter, a.Id, "the morning after");

        Assert.Equal(0, await _sut.ShiftDatesAsync([a.Id], 3));
        Assert.Equal(
            "the morning after",
            _projects.GetScenesForChapter(chapter).First(s => s.Id == a.Id).Date);
    }

    [Fact]
    public async Task ShiftDates_UsesTheBooksOwnCalendar()
    {
        var (chapter, a, _) = await TwoScenesAsync();
        _projects.ActiveBook!.Calendar = new InWorldCalendar
        {
            Type = InWorldCalendarType.Custom,
            MonthNames = ["Frost", "Thaw"],
            DaysPerMonth = [10, 10]
        };
        await _projects.SetSceneDateAsync(chapter, a.Id, "1.1.9");

        await _sut.ShiftDatesAsync([a.Id], 2);

        // Ten-day months, so the ninth plus two lands in the next month.
        Assert.Equal(
            "1.2.1",
            _projects.GetScenesForChapter(chapter).First(s => s.Id == a.Id).Date);
    }
}
