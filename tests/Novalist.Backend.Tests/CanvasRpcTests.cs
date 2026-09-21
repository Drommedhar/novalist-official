using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Planning boards over RPC, including promoting a card into a scene.</summary>
public sealed class CanvasRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly CanvasRpc _rpc;

    public CanvasRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-canvas-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "CanvasNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new CanvasRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static CanvasDto WithCard(CanvasDto canvas, CanvasCardDto card) =>
        canvas with { Cards = [.. canvas.Cards, card] };

    private static CanvasCardDto Card(string id, string title, string text = "") =>
        new(id, title, text, 0, 0, 200, 120, "", "", "", "");

    [Fact]
    public async Task Create_ThenList_ShowsTheBoard()
    {
        var created = await _rpc.CreateAsync("Act One");

        Assert.Equal("Act One", created.Name);
        var list = _rpc.List();
        Assert.Single(list);
        Assert.Equal(created.Id, list[0].Id);
    }

    [Fact]
    public async Task Create_BlankName_FallsBackToADefault()
    {
        var created = await _rpc.CreateAsync("   ");
        Assert.Equal("Board", created.Name);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsCardsAndConnectors()
    {
        var created = await _rpc.CreateAsync("Board");
        var withCards = created with
        {
            Cards = [Card("c1", "First", "Body"), Card("c2", "Second")],
            Connectors = [new CanvasConnectorDto("k1", "c1", "c2", "leads to", "right", "left")]
        };
        await _rpc.SaveAsync(withCards);

        var loaded = await _rpc.LoadAsync(created.Id);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Cards.Length);
        Assert.Equal("Body", loaded.Cards[0].Text);
        Assert.Equal("leads to", loaded.Connectors[0].Label);
        Assert.Equal("right", loaded.Connectors[0].FromSide);
        Assert.Equal("left", loaded.Connectors[0].ToSide);
    }

    [Fact]
    public async Task Load_UnknownBoard_IsNull() =>
        Assert.Null(await _rpc.LoadAsync("nope"));

    [Fact]
    public async Task Delete_RemovesTheBoard()
    {
        var created = await _rpc.CreateAsync("Board");

        Assert.True(await _rpc.DeleteAsync(created.Id));
        Assert.Empty(_rpc.List());
    }

    [Fact]
    public async Task Delete_UnknownBoard_IsFalse() =>
        Assert.False(await _rpc.DeleteAsync("nope"));

    [Fact]
    public async Task PromoteCard_CreatesASceneAndLinksTheCard()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var created = await _rpc.CreateAsync("Board");
        await _rpc.SaveAsync(WithCard(created, Card("c1", "The confrontation", "She finally says it.")));

        var updated = await _rpc.PromoteCardAsync(created.Id, "c1", chapter.Guid);

        Assert.NotNull(updated);
        var card = updated!.Cards.Single();
        Assert.NotEmpty(card.SceneId);
        Assert.Equal(chapter.Guid, card.ChapterGuid);

        var scene = _workspace.Projects.GetScenesForChapter(chapter.Guid).Single();
        Assert.Equal("The confrontation", scene.Title);
        // The card's body is a description of the scene, so it becomes the
        // synopsis rather than the prose.
        Assert.Equal("She finally says it.", scene.Synopsis);
    }

    [Fact]
    public async Task PromoteCard_BlankTitle_GetsAPlaceholder()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var created = await _rpc.CreateAsync("Board");
        await _rpc.SaveAsync(WithCard(created, Card("c1", "   ")));

        await _rpc.PromoteCardAsync(created.Id, "c1", chapter.Guid);

        Assert.Equal("Untitled", _workspace.Projects.GetScenesForChapter(chapter.Guid).Single().Title);
    }

    [Fact]
    public async Task PromoteCard_Twice_DoesNotCreateASecondScene()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var created = await _rpc.CreateAsync("Board");
        await _rpc.SaveAsync(WithCard(created, Card("c1", "Once")));

        await _rpc.PromoteCardAsync(created.Id, "c1", chapter.Guid);
        await _rpc.PromoteCardAsync(created.Id, "c1", chapter.Guid);

        Assert.Single(_workspace.Projects.GetScenesForChapter(chapter.Guid));
    }

    [Fact]
    public async Task LinkedCard_LoadFollowsSceneRenamesAndMoves()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var other = await _workspace.Projects.CreateChapterAsync("Two");
        var board = await _rpc.CreateAsync("Board");
        await _rpc.SaveAsync(WithCard(board, Card("c1", "Original")));
        var promoted = (await _rpc.PromoteCardAsync(board.Id, "c1", chapter.Guid))!;
        var sceneId = promoted.Cards.Single().SceneId;
        await _workspace.Projects.RenameSceneAsync(chapter.Guid, sceneId, "New scene title");
        await _workspace.Projects.MoveScenesAsync([sceneId], other.Guid, 0);

        var loaded = (await _rpc.LoadAsync(board.Id))!.Cards.Single();
        Assert.Equal("New scene title", loaded.Title);
        Assert.Equal(other.Guid, loaded.ChapterGuid);
        Assert.Equal(sceneId, loaded.SceneId);
        // A layout save from an older board must not undo a manuscript rename.
        await _rpc.SaveAsync(promoted with { PanX = 100 });
        Assert.Equal("New scene title", _workspace.Projects.GetScenesForChapter(other.Guid).Single().Title);
        Assert.Equal("New scene title", (await _rpc.LoadAsync(board.Id))!.Cards.Single().Title);
    }

    [Fact]
    public async Task LinkedCard_ExplicitTitleEditRenamesSceneAndSurvivesReopening()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var board = await _rpc.CreateAsync("Board");
        await _rpc.SaveAsync(WithCard(board, Card("c1", "Original", "Initial synopsis")));
        var promoted = (await _rpc.PromoteCardAsync(board.Id, "c1", chapter.Guid))!;
        var edited = promoted with { Cards = [promoted.Cards.Single() with { Title = "New title", Text = "Board-only notes" }] };
        await _rpc.SaveAsync(edited, new() { ["c1"] = "New title" });

        var root = _workspace.Projects.ProjectRoot!;
        _workspace.CloseProject();
        await _workspace.OpenProjectAsync(root);
        var scene = _workspace.Projects.GetScenesForChapter(chapter.Guid).Single();
        Assert.Equal("New title", scene.Title);
        Assert.Equal("Initial synopsis", scene.Synopsis);
        var card = (await _rpc.LoadAsync(board.Id))!.Cards.Single();
        Assert.Equal("New title", card.Title);
        Assert.Equal("Board-only notes", card.Text);
        await _rpc.PromoteCardAsync(board.Id, "c1", chapter.Guid);
        Assert.Single(_workspace.Projects.GetScenesForChapter(chapter.Guid));
    }

    [Fact]
    public async Task LinkedCard_ArchivedSceneStopsBeingLinked_AndCanBeRecreated()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var board = await _rpc.CreateAsync("Board");
        await _rpc.SaveAsync(WithCard(board, Card("c1", "Original")));
        var promoted = (await _rpc.PromoteCardAsync(board.Id, "c1", chapter.Guid))!;
        var archived = _workspace.Projects.GetScenesForChapter(chapter.Guid).Single();
        archived.ArchivedAt = DateTime.UtcNow;
        await _workspace.Projects.SaveScenesAsync();
        Assert.Empty((await _rpc.LoadAsync(board.Id))!.Cards.Single().SceneId);
        await _rpc.SaveAsync(promoted, new() { ["c1"] = "Do not rename the archive" });
        Assert.Equal("Original", archived.Title);
        var recreated = (await _rpc.PromoteCardAsync(board.Id, "c1", chapter.Guid))!.Cards.Single();
        Assert.NotEmpty(recreated.SceneId);
        Assert.NotEqual(archived.Id, recreated.SceneId);
    }

    [Fact]
    public async Task UnlinkedCard_TitleEditAndMissingSceneRemainBoardOnly()
    {
        var board = await _rpc.CreateAsync("Board");
        var withCards = board with { Cards = [Card("c1", "Loose"), Card("c2", "Missing") with { SceneId = "gone" }] };
        await _rpc.SaveAsync(withCards, new() { ["c1"] = "Loose", ["c2"] = "Missing" });
        Assert.All((await _rpc.LoadAsync(board.Id))!.Cards, card => Assert.Empty(card.SceneId));
        Assert.Empty(_workspace.BuildState().Chapters);
    }

    [Fact]
    public async Task PromoteCard_UnknownCard_IsNull()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var created = await _rpc.CreateAsync("Board");

        Assert.Null(await _rpc.PromoteCardAsync(created.Id, "missing", chapter.Guid));
    }

    [Fact]
    public async Task PromoteCard_UnknownBoard_IsNull()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        Assert.Null(await _rpc.PromoteCardAsync("nope", "c1", chapter.Guid));
    }

    [Fact]
    public async Task PromoteCard_WithoutBodyText_LeavesTheSynopsisEmpty()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var created = await _rpc.CreateAsync("Board");
        await _rpc.SaveAsync(WithCard(created, Card("c1", "Just a title")));

        await _rpc.PromoteCardAsync(created.Id, "c1", chapter.Guid);

        var scene = _workspace.Projects.GetScenesForChapter(chapter.Guid).Single();
        Assert.True(string.IsNullOrEmpty(scene.Synopsis));
    }
}
