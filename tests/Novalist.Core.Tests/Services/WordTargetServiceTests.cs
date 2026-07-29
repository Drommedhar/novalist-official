using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Word targets on scenes, chapters and acts.
///
/// The rule worth guarding: a level with no target of its own aggregates what
/// is beneath it, so putting targets on a handful of scenes already tells the
/// writer where the chapter stands without them restating the number.
/// </summary>
public class WordTargetServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProjectService _projects = new(new FileService());
    private readonly WordTargetService _sut;

    public WordTargetServiceTests()
    {
        _sut = new WordTargetService(_projects);
    }

    public void Dispose() => _dir.Dispose();

    private async Task<(ChapterData Chapter, SceneData A, SceneData B)> BookAsync()
    {
        await _projects.CreateProjectAsync(_dir.Path, "P", "Book");
        var chapter = await _projects.CreateChapterAsync("One");
        var a = await _projects.CreateSceneAsync(chapter.Guid, "A");
        var b = await _projects.CreateSceneAsync(chapter.Guid, "B");
        return (chapter, a, b);
    }

    // ── Scenes ──

    [Fact]
    public async Task ASceneWithNoTargetHasNothingToShow()
    {
        var (chapter, a, _) = await BookAsync();

        var progress = _sut.Scene(chapter.Guid, a.Id)!;

        Assert.False(progress.HasTarget);
        Assert.False(progress.Explicit);
    }

    [Fact]
    public async Task ASceneTargetRoundTripsAndReportsProgress()
    {
        var (chapter, a, _) = await BookAsync();
        a.WordCount = 400;

        await _sut.SetSceneTargetAsync(chapter.Guid, a.Id, 1000);

        var progress = _sut.Scene(chapter.Guid, a.Id)!;
        Assert.Equal(1000, progress.Target);
        Assert.Equal(400, progress.Words);
        Assert.Equal(600, progress.Remaining);
        Assert.Equal(0, progress.Overrun);
        Assert.True(progress.Explicit);
    }

    [Fact]
    public async Task PastTheTargetTheOverrunIsReportedRatherThanANegativeRemainder()
    {
        var (chapter, a, _) = await BookAsync();
        a.WordCount = 1200;
        await _sut.SetSceneTargetAsync(chapter.Guid, a.Id, 1000);

        var progress = _sut.Scene(chapter.Guid, a.Id)!;

        Assert.Equal(0, progress.Remaining);
        Assert.Equal(200, progress.Overrun);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(null)]
    public async Task ATargetOfNothingClearsItRatherThanBeingStored(int? target)
    {
        // A stored zero would render as a permanently-complete progress bar.
        var (chapter, a, _) = await BookAsync();
        await _sut.SetSceneTargetAsync(chapter.Guid, a.Id, 1000);

        await _sut.SetSceneTargetAsync(chapter.Guid, a.Id, target);

        Assert.False(_sut.Scene(chapter.Guid, a.Id)!.HasTarget);
    }

    [Fact]
    public async Task SettingTheTargetOfASceneThatIsGoneDoesNothing()
    {
        var (chapter, _, _) = await BookAsync();

        await _sut.SetSceneTargetAsync(chapter.Guid, "no-such-scene", 500);

        Assert.Null(_sut.Scene(chapter.Guid, "no-such-scene"));
    }

    // ── Chapters ──

    [Fact]
    public async Task AChapterWithNoTargetAddsUpItsScenes()
    {
        var (chapter, a, b) = await BookAsync();
        await _sut.SetSceneTargetAsync(chapter.Guid, a.Id, 1000);
        await _sut.SetSceneTargetAsync(chapter.Guid, b.Id, 1500);

        var progress = _sut.Chapter(chapter.Guid)!;

        Assert.Equal(2500, progress.Target);
        // Aggregated, not stated, and the UI can say so.
        Assert.False(progress.Explicit);
    }

    [Fact]
    public async Task AChapterTargetWinsOverWhatItsScenesAddUpTo()
    {
        var (chapter, a, _) = await BookAsync();
        await _sut.SetSceneTargetAsync(chapter.Guid, a.Id, 1000);

        await _sut.SetChapterTargetAsync(chapter.Guid, 4000);

        var progress = _sut.Chapter(chapter.Guid)!;
        Assert.Equal(4000, progress.Target);
        Assert.True(progress.Explicit);
    }

    [Fact]
    public async Task AChapterCountsTheWordsOfEveryScene()
    {
        var (chapter, a, b) = await BookAsync();
        a.WordCount = 300;
        b.WordCount = 700;

        Assert.Equal(1000, _sut.Chapter(chapter.Guid)!.Words);
    }

    [Fact]
    public async Task AChapterWithNothingSetAnywhereHasNoTarget()
    {
        var (chapter, _, _) = await BookAsync();

        Assert.False(_sut.Chapter(chapter.Guid)!.HasTarget);
    }

    [Fact]
    public async Task ClearingAChapterTargetFallsBackToItsScenes()
    {
        var (chapter, a, _) = await BookAsync();
        await _sut.SetSceneTargetAsync(chapter.Guid, a.Id, 1000);
        await _sut.SetChapterTargetAsync(chapter.Guid, 4000);

        await _sut.SetChapterTargetAsync(chapter.Guid, null);

        Assert.Equal(1000, _sut.Chapter(chapter.Guid)!.Target);
    }

    [Fact]
    public async Task SettingTheTargetOfAChapterThatIsGoneDoesNothing()
    {
        await BookAsync();

        await _sut.SetChapterTargetAsync("no-such-chapter", 500);

        Assert.Null(_sut.Chapter("no-such-chapter"));
    }

    // ── Acts ──

    [Fact]
    public async Task AnActWithNoTargetAddsUpItsChapters()
    {
        var (chapter, a, _) = await BookAsync();
        chapter.Act = "Act One";
        await _sut.SetSceneTargetAsync(chapter.Guid, a.Id, 1200);

        Assert.Equal(1200, _sut.Act("Act One")!.Target);
    }

    [Fact]
    public async Task AnActTargetWinsOverWhatItsChaptersAddUpTo()
    {
        var (chapter, a, _) = await BookAsync();
        chapter.Act = "Act One";
        await _sut.SetSceneTargetAsync(chapter.Guid, a.Id, 1200);

        await _sut.SetActTargetAsync("Act One", 30000);

        var progress = _sut.Act("Act One")!;
        Assert.Equal(30000, progress.Target);
        Assert.True(progress.Explicit);
    }

    [Fact]
    public async Task SettingAnActTargetCreatesTheActEntryWhenItWasOnlyAName()
    {
        // Acts exist as a string on chapters until something needs metadata.
        var (chapter, _, _) = await BookAsync();
        chapter.Act = "Act One";

        await _sut.SetActTargetAsync("Act One", 30000);

        Assert.Contains(_projects.ActiveBook!.Acts, a => a.Name == "Act One");
    }

    [Fact]
    public async Task AnActNoChapterBelongsToDoesNotExist()
    {
        await BookAsync();

        Assert.Null(_sut.Act("Act Nine"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SettingATargetOnANamelessActDoesNothing(string name)
    {
        await BookAsync();

        await _sut.SetActTargetAsync(name, 1000);

        Assert.Empty(_projects.ActiveBook!.Acts);
    }

    [Fact]
    public async Task SettingAnActTargetWithNoBookOpenDoesNothing()
    {
        await _sut.SetActTargetAsync("Act One", 1000);

        Assert.Null(_projects.ActiveBook);
    }

    // ── The whole list ──

    [Fact]
    public async Task AllReportsEveryLevelThatHasATargetInReadingOrder()
    {
        var (chapter, a, _) = await BookAsync();
        chapter.Act = "Act One";
        await _sut.SetSceneTargetAsync(chapter.Guid, a.Id, 1000);

        var rows = _sut.All();

        Assert.Equal(["act", "chapter", "scene"], rows.Select(r => r.Kind));
    }

    [Fact]
    public async Task AllLeavesOutWhatHasNoTarget()
    {
        var (chapter, a, _) = await BookAsync();
        await _sut.SetSceneTargetAsync(chapter.Guid, a.Id, 1000);

        // Scene B has none, so it is not a row.
        Assert.Single(_sut.All(), r => r.Kind == "scene");
    }

    [Fact]
    public async Task AnActIsReportedOnceHoweverManyChaptersItHas()
    {
        await _projects.CreateProjectAsync(_dir.Path, "P", "Book");
        foreach (var title in new[] { "One", "Two" })
        {
            var chapter = await _projects.CreateChapterAsync(title);
            chapter.Act = "Act One";
            var scene = await _projects.CreateSceneAsync(chapter.Guid, "S");
            await _sut.SetSceneTargetAsync(chapter.Guid, scene.Id, 500);
        }

        Assert.Single(_sut.All(), r => r.Kind == "act");
    }
}
