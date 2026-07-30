using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// The book's keyword vocabulary.
///
/// Scene tags were a free-text list with nothing behind them, so "flashback",
/// "Flashback" and "flash-back" were three tags and correcting that meant
/// opening every scene that used the wrong one. These tests are mostly about
/// the operations that reach into the scenes, because a registry that only
/// changes itself leaves the scenes saying the old thing.
/// </summary>
public sealed class KeywordsRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly KeywordsRpc _rpc;
    private readonly string _chapterGuid;
    private readonly string[] _sceneIds;

    public KeywordsRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-kw-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "KwNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();

        var chapter = _workspace.Projects.CreateChapterAsync("One").GetAwaiter().GetResult();
        _chapterGuid = chapter.Guid;
        _sceneIds =
        [
            _workspace.Projects.CreateSceneAsync(chapter.Guid, "A").GetAwaiter().GetResult().Id,
            _workspace.Projects.CreateSceneAsync(chapter.Guid, "B").GetAwaiter().GetResult().Id
        ];
        _rpc = new KeywordsRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private void Tag(string sceneId, params string[] tags)
    {
        var scene = _workspace.Projects.GetScenesForChapter(_chapterGuid).Single(s => s.Id == sceneId);
        scene.AnalysisOverrides ??= new SceneAnalysisOverrides();
        scene.AnalysisOverrides.Tags = [.. tags];
        _workspace.Projects.SaveScenesAsync().GetAwaiter().GetResult();
    }

    private IReadOnlyList<string> TagsOf(string sceneId)
        => _workspace.Projects.GetScenesForChapter(_chapterGuid)
            .Single(s => s.Id == sceneId).AnalysisOverrides?.Tags ?? [];

    private static KeywordDto New(string name, string colour = "#8b8b8b", string parent = "")
        => new(string.Empty, name, colour, parent, 0);

    // ── The registry ──

    [Fact]
    public void ABookWithNoVocabularyListsNone() => Assert.Empty(_rpc.List());

    [Fact]
    public void WithNoProjectOpenThereIsNothingToList()
        => Assert.Empty(new KeywordsRpc(new Workspace(Path.Combine(_root, "none"))).List());

    [Fact]
    public async Task SavingKeepsNameColourAndOrder()
    {
        var all = await _rpc.SaveAsync([New("grief", "#c04040"), New("loss")]);

        Assert.Equal(["grief", "loss"], all.Select(k => k.Name));
        Assert.Equal("#c04040", all[0].Color);
        Assert.All(all, k => Assert.NotEmpty(k.Id!));
    }

    [Fact]
    public async Task ANamelessEntryIsDropped()
        => Assert.Single(await _rpc.SaveAsync([New("grief"), New("  "), New("")]));

    [Fact]
    public async Task TwoEntriesSpeltTheSameFoldIntoOne()
    {
        // The exact problem a registry exists to prevent.
        Assert.Single(await _rpc.SaveAsync([New("Flashback"), New("flashback")]));
    }

    [Fact]
    public async Task AnEmptyColourFallsBackRatherThanDrawingNothing()
        => Assert.Equal("#8b8b8b", (await _rpc.SaveAsync([New("grief", "  ")]))[0].Color);

    [Fact]
    public async Task SavingNeedsABook()
    {
        var bare = new KeywordsRpc(new Workspace(Path.Combine(_root, "no-project")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bare.SaveAsync([]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bare.HarvestAsync());
    }

    [Fact]
    public async Task SavingNothingClearsTheVocabulary()
    {
        await _rpc.SaveAsync([New("grief")]);
        Assert.Empty(await _rpc.SaveAsync(null!));
    }

    // ── Grouping ──

    [Fact]
    public async Task AKeywordCanSitUnderAnother()
    {
        var saved = await _rpc.SaveAsync([New("Themes"), New("grief")]);
        var parent = saved[0].Id!;

        var all = await _rpc.SaveAsync(
            [saved[0], saved[1] with { ParentId = parent }]);

        Assert.Equal(parent, all[1].ParentId);
    }

    [Fact]
    public async Task AParentThatIsNotInTheListIsDropped()
    {
        // Otherwise the child hides behind a heading that does not exist.
        var all = await _rpc.SaveAsync([New("grief", parent: "no-such-keyword")]);

        Assert.Equal(string.Empty, all[0].ParentId);
    }

    [Fact]
    public async Task AKeywordCannotBeItsOwnParent()
    {
        var saved = await _rpc.SaveAsync([New("grief")]);

        var all = await _rpc.SaveAsync([saved[0] with { ParentId = saved[0].Id }]);

        Assert.Equal(string.Empty, all[0].ParentId);
    }

    // ── Counts ──

    [Fact]
    public async Task AKeywordSaysHowManyScenesCarryIt()
    {
        Tag(_sceneIds[0], "grief");
        Tag(_sceneIds[1], "grief", "rain");

        var all = await _rpc.SaveAsync([New("grief"), New("rain"), New("unused")]);

        Assert.Equal(2, all.Single(k => k.Name == "grief").SceneCount);
        Assert.Equal(1, all.Single(k => k.Name == "rain").SceneCount);
        Assert.Equal(0, all.Single(k => k.Name == "unused").SceneCount);
    }

    [Fact]
    public async Task ASceneTaggedTwiceOverCountsOnce()
    {
        Tag(_sceneIds[0], "grief", "Grief");

        Assert.Equal(1, (await _rpc.SaveAsync([New("grief")]))[0].SceneCount);
    }

    // ── Renaming ──

    [Fact]
    public async Task RenamingReachesEveryScene()
    {
        Tag(_sceneIds[0], "flashback", "rain");
        Tag(_sceneIds[1], "Flashback");
        var id = (await _rpc.SaveAsync([New("flashback")]))[0].Id!;

        var all = await _rpc.RenameAsync(id, "Flash-back");

        // A registry that only changes itself leaves the scenes saying the old
        // thing, which is the bug rather than the feature.
        Assert.Equal("Flash-back", all[0].Name);
        Assert.Equal(["Flash-back", "rain"], TagsOf(_sceneIds[0]));
        Assert.Equal(["Flash-back"], TagsOf(_sceneIds[1]));
    }

    [Fact]
    public async Task RenamingOntoATagASceneAlreadyHasCollapsesToOne()
    {
        Tag(_sceneIds[0], "grief", "loss");
        var id = (await _rpc.SaveAsync([New("grief")]))[0].Id!;

        await _rpc.RenameAsync(id, "loss");

        Assert.Equal(["loss"], TagsOf(_sceneIds[0]));
    }

    [Fact]
    public async Task RenamingOntoANameAlreadyInTheVocabularyIsRefused()
    {
        var saved = await _rpc.SaveAsync([New("grief"), New("loss")]);

        var all = await _rpc.RenameAsync(saved[0].Id!, "Loss");

        // Two entries with the same name are the thing this was built to stop.
        Assert.Equal(["grief", "loss"], all.Select(k => k.Name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RenamingToNothingIsRefused(string name)
    {
        var id = (await _rpc.SaveAsync([New("grief")]))[0].Id!;

        Assert.Equal("grief", (await _rpc.RenameAsync(id, name))[0].Name);
    }

    [Fact]
    public async Task RenamingSomethingThatIsGoneIsQuiet()
    {
        await _rpc.SaveAsync([New("grief")]);
        Assert.Single(await _rpc.RenameAsync("no-such-keyword", "loss"));
    }

    // ── Deleting ──

    [Fact]
    public async Task DeletingTakesTheWordOffTheScenesToo()
    {
        Tag(_sceneIds[0], "grief", "rain");
        var id = (await _rpc.SaveAsync([New("grief")]))[0].Id!;

        Assert.Empty(await _rpc.DeleteAsync(id));

        // Retiring a word from the list while leaving it written on forty
        // scenes is how a vocabulary drifts back out of control.
        Assert.Equal(["rain"], TagsOf(_sceneIds[0]));
    }

    [Fact]
    public async Task DeletingCanLeaveTheScenesAlone()
    {
        Tag(_sceneIds[0], "grief");
        var id = (await _rpc.SaveAsync([New("grief")]))[0].Id!;

        await _rpc.DeleteAsync(id, clearFromScenes: false);

        Assert.Equal(["grief"], TagsOf(_sceneIds[0]));
    }

    [Fact]
    public async Task DeletingAParentBringsItsChildrenUp()
    {
        var saved = await _rpc.SaveAsync([New("Themes"), New("grief")]);
        var withParent = await _rpc.SaveAsync(
            [saved[0], saved[1] with { ParentId = saved[0].Id }]);

        var all = await _rpc.DeleteAsync(withParent[0].Id!);

        // The child stays in the vocabulary rather than vanishing with the
        // heading it happened to be under.
        Assert.Equal("grief", Assert.Single(all).Name);
        Assert.Equal(string.Empty, all[0].ParentId);
    }

    [Fact]
    public async Task DeletingSomethingThatIsGoneIsQuiet()
    {
        await _rpc.SaveAsync([New("grief")]);
        Assert.Single(await _rpc.DeleteAsync("no-such-keyword"));
    }

    // ── Harvesting ──

    [Fact]
    public async Task HarvestingPicksUpEveryTagAlreadyWritten()
    {
        Tag(_sceneIds[0], "grief", "rain");
        Tag(_sceneIds[1], "siege");

        var all = await _rpc.HarvestAsync();

        // Without this a project with two hundred tags starts on an empty
        // registry, which makes the feature useless to whoever needs it most.
        Assert.Equal(["grief", "rain", "siege"], all.Select(k => k.Name).Order());
    }

    [Fact]
    public async Task HarvestingDoesNotDuplicateWhatIsAlreadyThere()
    {
        Tag(_sceneIds[0], "Grief");
        await _rpc.SaveAsync([New("grief", "#c04040")]);

        var all = await _rpc.HarvestAsync();

        // Spelling variants fold together, which is the first clean-up the
        // registry buys.
        var kept = Assert.Single(all);
        Assert.Equal("grief", kept.Name);
        Assert.Equal("#c04040", kept.Color);
    }

    [Fact]
    public async Task HarvestingNothingChangesNothing()
    {
        await _rpc.SaveAsync([New("grief")]);
        Assert.Single(await _rpc.HarvestAsync());
    }

    // ── Finding the scenes ──

    [Fact]
    public async Task AKeywordCanNameTheScenesThatCarryIt()
    {
        Tag(_sceneIds[0], "Grief");
        var id = (await _rpc.SaveAsync([New("grief")]))[0].Id!;

        var scenes = _rpc.Scenes(id);

        var hit = Assert.Single(scenes);
        Assert.Equal(_sceneIds[0], hit.SceneId);
        Assert.Equal(_chapterGuid, hit.ChapterGuid);
        Assert.Equal("A", hit.Title);
    }

    [Fact]
    public void AKeywordThatIsGoneNamesNoScenes() => Assert.Empty(_rpc.Scenes("no-such-keyword"));

    [Fact]
    public async Task TheVocabularySurvivesReopeningTheProject()
    {
        await _rpc.SaveAsync([New("grief", "#c04040")]);

        await _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!);

        Assert.Equal("grief", Assert.Single(_rpc.List()).Name);
    }
}
