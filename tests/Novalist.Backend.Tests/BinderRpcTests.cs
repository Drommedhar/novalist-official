using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Pinning, and the threads the binder filters by.
///
/// <c>IsFavorite</c> was on the model, saved to disk and settable through the
/// service, and no RPC anywhere called it - so no writer could pin anything.
/// These tests exist because a flag only the tests can reach is not a feature.
/// </summary>
public sealed class BinderRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly BinderRpc _rpc;

    public BinderRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-binder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "BinderNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        var chapter = _workspace.Projects.CreateChapterAsync("One").GetAwaiter().GetResult();
        _workspace.Projects.CreateSceneAsync(chapter.Guid, "The Arrival").GetAwaiter().GetResult();
        _rpc = new BinderRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private ChapterData FirstChapter() => _workspace.Projects.GetChaptersOrdered()[0];

    private SceneData FirstScene()
        => _workspace.Projects.GetScenesForChapter(FirstChapter().Guid)[0];

    [Fact]
    public async Task PinningASceneSticksAndComesBackInTheState()
    {
        var chapter = FirstChapter();
        var scene = FirstScene();

        await _rpc.PinSceneAsync(chapter.Guid, scene.Id, true);

        Assert.True(FirstScene().IsFavorite);
        var dto = _workspace.BuildState().Chapters
            .Single(c => c.Guid == chapter.Guid).Scenes
            .Single(sc => sc.Id == scene.Id);
        Assert.True(dto.IsFavorite);
    }

    [Fact]
    public async Task UnpinningPutsItBack()
    {
        var chapter = FirstChapter();
        var scene = FirstScene();

        await _rpc.PinSceneAsync(chapter.Guid, scene.Id, true);
        await _rpc.PinSceneAsync(chapter.Guid, scene.Id, false);

        Assert.False(FirstScene().IsFavorite);
    }

    [Fact]
    public async Task APinSurvivesReopeningTheProject()
    {
        var chapter = FirstChapter();
        await _rpc.PinSceneAsync(chapter.Guid, FirstScene().Id, true);

        // The point of a pin is that it is still there tomorrow.
        await _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!);

        Assert.True(FirstScene().IsFavorite);
    }

    [Fact]
    public async Task PinningAChapterSticks()
    {
        var chapter = FirstChapter();

        await _rpc.PinChapterAsync(chapter.Guid, true);

        Assert.True(FirstChapter().IsFavorite);
    }

    [Fact]
    public async Task PinningSomethingThatIsNotThereIsQuiet()
    {
        // A stale row in a binder that has already been redrawn. Nothing to
        // pin is nothing to do, not a reason to break the writer's session.
        await _rpc.PinChapterAsync("no-such-chapter", true);
        await _rpc.PinSceneAsync(FirstChapter().Guid, "no-such-scene", true);

        Assert.False(FirstScene().IsFavorite);
    }

    [Fact]
    public void ABookWithNoThreadsListsNone() => Assert.Empty(_rpc.Plotlines());

    [Fact]
    public async Task ThreadsComeBackNamedAndInTheBooksOrder()
    {
        var book = _workspace.Projects.ActiveBook!;
        book.Plotlines =
        [
            new PlotlineData { Id = "b", Name = "The Betrayal", Color = "#c04040", Order = 2 },
            new PlotlineData { Id = "a", Name = "The Hunt", Color = "#4080c0", Order = 1 }
        ];
        await _workspace.Projects.SaveProjectAsync();

        var threads = _rpc.Plotlines();

        // Named, because a filter over opaque guids is not a filter anyone can
        // use - and in the book's order, not the order they were typed in.
        Assert.Equal(["The Hunt", "The Betrayal"], threads.Select(p => p.Name));
        Assert.Equal("#4080c0", threads[0].Color);
    }

    [Fact]
    public async Task ASceneCarriesItsThreadIdsToTheBinder()
    {
        var book = _workspace.Projects.ActiveBook!;
        book.Plotlines =
        [
            new PlotlineData { Id = "a", Name = "The Hunt", Color = "#4080c0", Order = 1 },
            new PlotlineData { Id = "b", Name = "The Betrayal", Color = "#c04040", Order = 2 }
        ];
        var chapter = FirstChapter();
        var scene = FirstScene();
        scene.PlotlineIds = ["b"];
        await _workspace.Projects.SaveScenesAsync();
        await _workspace.Projects.SaveProjectAsync();

        var dto = _workspace.BuildState().Chapters
            .Single(c => c.Guid == chapter.Guid).Scenes
            .Single(sc => sc.Id == scene.Id);

        Assert.Equal(["b"], dto.PlotlineIds);
        Assert.Equal(["#c04040"], dto.PlotlineColors);
    }

    [Fact]
    public async Task AThreadTheBookNoLongerHasIsNotReported()
    {
        var scene = FirstScene();
        scene.PlotlineIds = ["deleted-thread"];
        await _workspace.Projects.SaveScenesAsync();

        var dto = _workspace.BuildState().Chapters
            .Single(c => c.Guid == FirstChapter().Guid).Scenes
            .Single(sc => sc.Id == scene.Id);

        // Filtering by a thread that is gone would return scenes nobody can
        // reach from the dropdown, because the dropdown does not list it.
        Assert.Empty(dto.PlotlineIds);
    }
}
