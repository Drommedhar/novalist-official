using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Hand-curated scene sets.
///
/// Saved lists are queries and recompute on open; the nearest thing to curation
/// was a favourite flag no RPC exposed. So "the eight scenes to fix before
/// Tuesday" - which no filter describes, because being on that list is the only
/// thing they have in common - had nowhere to live.
/// </summary>
public sealed class CollectionsRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly CollectionsRpc _rpc;
    private readonly string _chapterGuid;
    private readonly string[] _sceneIds;

    public CollectionsRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-coll-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "CollNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();

        var chapter = _workspace.Projects.CreateChapterAsync("One").GetAwaiter().GetResult();
        _chapterGuid = chapter.Guid;
        _sceneIds =
        [
            _workspace.Projects.CreateSceneAsync(chapter.Guid, "A").GetAwaiter().GetResult().Id,
            _workspace.Projects.CreateSceneAsync(chapter.Guid, "B").GetAwaiter().GetResult().Id,
            _workspace.Projects.CreateSceneAsync(chapter.Guid, "C").GetAwaiter().GetResult().Id
        ];
        _rpc = new CollectionsRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void ABookWithNoneListsNone() => Assert.Empty(_rpc.List());

    [Fact]
    public void WithNoProjectOpenThereIsNothingToList()
        => Assert.Empty(new CollectionsRpc(new Workspace(Path.Combine(_root, "none"))).List());

    [Fact]
    public async Task CreatingWithASelectionGathersItStraightAway()
    {
        // Making a set and then adding the scenes already picked is two steps
        // for one intent.
        var all = await _rpc.CreateAsync("Before Tuesday", [_sceneIds[0], _sceneIds[2]]);

        var collection = Assert.Single(all);
        Assert.Equal("Before Tuesday", collection.Name);
        Assert.Equal([_sceneIds[0], _sceneIds[2]], collection.Scenes.Select(s => s.SceneId));
    }

    [Fact]
    public async Task ACollectionCanStartEmpty()
        => Assert.Empty(Assert.Single(await _rpc.CreateAsync("Later")).Scenes);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ANamelessCollectionIsNotCreated(string name)
    {
        // A row nobody can tell from the next one.
        Assert.Empty(await _rpc.CreateAsync(name));
    }

    [Fact]
    public async Task TheNameIsTrimmed()
        => Assert.Equal("Run", Assert.Single(await _rpc.CreateAsync("  Run  ")).Name);

    [Fact]
    public async Task CreatingNeedsABook()
    {
        var bare = new CollectionsRpc(new Workspace(Path.Combine(_root, "no-project")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bare.CreateAsync("X"));
    }

    [Fact]
    public async Task ScenesCarryTheirChapterSoARowCanOpen()
    {
        var collection = Assert.Single(await _rpc.CreateAsync("Run", [_sceneIds[1]]));

        var scene = Assert.Single(collection.Scenes);
        Assert.Equal(_chapterGuid, scene.ChapterGuid);
        Assert.Equal("B", scene.Title);
    }

    [Fact]
    public async Task AddingSkipsWhatIsAlreadyThere()
    {
        var id = (await _rpc.CreateAsync("Run", [_sceneIds[0]])).Single().Id;

        var all = await _rpc.AddAsync(id, [_sceneIds[0], _sceneIds[1]]);

        Assert.Equal([_sceneIds[0], _sceneIds[1]], all.Single().Scenes.Select(s => s.SceneId));
    }

    [Fact]
    public async Task AddingToSomethingThatIsGoneIsQuiet()
    {
        await _rpc.CreateAsync("Run");
        Assert.Single(await _rpc.AddAsync("no-such-collection", _sceneIds));
    }

    [Fact]
    public async Task ABlankSceneIdIsNotAdded()
    {
        var id = (await _rpc.CreateAsync("Run")).Single().Id;

        Assert.Empty((await _rpc.AddAsync(id, ["", "   "])).Single().Scenes);
    }

    [Fact]
    public async Task RemovingTakesTheSceneOutOfTheSetAndNowhereElse()
    {
        var id = (await _rpc.CreateAsync("Run", _sceneIds)).Single().Id;

        var all = await _rpc.RemoveAsync(id, _sceneIds[1]);

        Assert.Equal([_sceneIds[0], _sceneIds[2]], all.Single().Scenes.Select(s => s.SceneId));
        // Deleting from a collection has never been a way to delete a scene.
        Assert.Equal(3, _workspace.Projects.GetScenesForChapter(_chapterGuid).Count);
    }

    [Fact]
    public async Task RemovingSomethingThatIsNotThereIsQuiet()
    {
        var id = (await _rpc.CreateAsync("Run", [_sceneIds[0]])).Single().Id;

        Assert.Single((await _rpc.RemoveAsync(id, "no-such-scene")).Single().Scenes);
        Assert.Single(await _rpc.RemoveAsync("no-such-collection", _sceneIds[0]));
    }

    [Fact]
    public async Task RenamingSticks()
    {
        var id = (await _rpc.CreateAsync("Old")).Single().Id;

        Assert.Equal("New", (await _rpc.RenameAsync(id, " New ")).Single().Name);
    }

    [Fact]
    public async Task RenamingToNothingIsRefused()
    {
        var id = (await _rpc.CreateAsync("Kept")).Single().Id;

        Assert.Equal("Kept", (await _rpc.RenameAsync(id, "  ")).Single().Name);
        Assert.Single(await _rpc.RenameAsync("no-such-collection", "X"));
    }

    [Fact]
    public async Task DeletingTakesTheSetAndLeavesTheScenes()
    {
        var id = (await _rpc.CreateAsync("Run", _sceneIds)).Single().Id;

        Assert.Empty(await _rpc.DeleteAsync(id));
        Assert.Equal(3, _workspace.Projects.GetScenesForChapter(_chapterGuid).Count);
    }

    [Fact]
    public async Task DeletingSomethingThatIsGoneIsQuiet()
    {
        await _rpc.CreateAsync("Run");
        Assert.Single(await _rpc.DeleteAsync("no-such-collection"));
    }

    [Fact]
    public async Task TheOrderInsideACollectionIsTheWritersAndNotReadingOrder()
    {
        var id = (await _rpc.CreateAsync("Run", [_sceneIds[0], _sceneIds[1], _sceneIds[2]])).Single().Id;

        var all = await _rpc.MoveAsync(id, _sceneIds[2], 0);

        // A revision run is often deliberately out of sequence; re-sorting it
        // would throw away the only thing the writer said about the set.
        Assert.Equal(
            [_sceneIds[2], _sceneIds[0], _sceneIds[1]],
            all.Single().Scenes.Select(s => s.SceneId));
    }

    [Fact]
    public async Task MovingPastTheEndClampsRatherThanThrowing()
    {
        var id = (await _rpc.CreateAsync("Run", [_sceneIds[0], _sceneIds[1]])).Single().Id;

        var all = await _rpc.MoveAsync(id, _sceneIds[0], 99);

        Assert.Equal([_sceneIds[1], _sceneIds[0]], all.Single().Scenes.Select(s => s.SceneId));
    }

    [Fact]
    public async Task MovingSomethingThatIsNotInTheSetIsQuiet()
    {
        var id = (await _rpc.CreateAsync("Run", [_sceneIds[0]])).Single().Id;

        Assert.Single((await _rpc.MoveAsync(id, "no-such-scene", 0)).Single().Scenes);
        Assert.Single(await _rpc.MoveAsync("no-such-collection", _sceneIds[0], 0));
    }

    [Fact]
    public async Task ASceneThatHasBeenDeletedDropsOutOfTheList()
    {
        var id = (await _rpc.CreateAsync("Run", _sceneIds)).Single().Id;

        await _workspace.Projects.DeleteSceneAsync(_chapterGuid, _sceneIds[1]);

        // A collection outlives the scenes in it, and a row that opens nothing
        // is worse than a shorter list.
        Assert.Equal(
            [_sceneIds[0], _sceneIds[2]],
            _rpc.List().Single(c => c.Id == id).Scenes.Select(s => s.SceneId));
    }

    [Fact]
    public async Task CollectionsComeBackInTheOrderTheyWereMade()
    {
        await _rpc.CreateAsync("First");
        await _rpc.CreateAsync("Second");
        var all = await _rpc.CreateAsync("Third");

        Assert.Equal(["First", "Second", "Third"], all.Select(c => c.Name));
    }

    [Fact]
    public async Task ACollectionSurvivesReopeningTheProject()
    {
        await _rpc.CreateAsync("Before Tuesday", [_sceneIds[0]]);

        await _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!);

        var collection = Assert.Single(_rpc.List());
        Assert.Equal("Before Tuesday", collection.Name);
        Assert.Single(collection.Scenes);
    }
}
