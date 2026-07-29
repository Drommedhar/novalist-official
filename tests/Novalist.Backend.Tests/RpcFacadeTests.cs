using System.Text.Json;
using Nerdbank.Streams;
using Novalist.Backend;
using Novalist.Backend.Rpc;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Exercises the facades through a real JSON-RPC pair so wire naming
/// (camelCase, method routes) is asserted, not assumed.</summary>
[Collection("BackendStatics")]
public sealed class RpcFacadeTests : IAsyncDisposable
{
    private readonly string _root;
    private readonly BackendHost _host;
    private readonly JsonRpc _client;

    public RpcFacadeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-rpc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _host = new BackendHost(Path.Combine(_root, "settings"));
        var (serverStream, clientStream) = FullDuplexStream.CreatePair();
        _host.Attach(serverStream, serverStream);
        var formatter = new SystemTextJsonFormatter();
        formatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        _client = new JsonRpc(new HeaderDelimitedMessageHandler(clientStream, clientStream, formatter));
        _client.StartListening();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        _host.Dispose();
        await Task.Yield();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private Task<T> InvokeAsync<T>(string method, params object[] args) =>
        _client.InvokeAsync<T>(method, args);

    [Fact]
    public async Task ProjectTemplates_ListAndCreateFromTemplate()
    {
        var templates = await InvokeAsync<ProjectTemplateDto[]>("project/templates");
        Assert.Contains(templates, t => t.Id == "blank");
        Assert.Contains(templates, t => t.Id == "three-act");

        var seeded = await InvokeAsync<ProjectStateDto>(
            "project/create", Path.Combine(_root, "seeded"), "Seeded", "Book", "three-act");
        Assert.True(seeded.Chapters.Count > 0);
        Assert.Contains(seeded.Chapters, c => c.Title == "Setup");

        var blank = await InvokeAsync<ProjectStateDto>(
            "project/create", Path.Combine(_root, "blank"), "Blank", "Book", "no-such-template");
        Assert.Empty(blank.Chapters);
    }

    [Fact]
    public async Task FullProjectFlow_OverTheWire()
    {
        var created = await InvokeAsync<ProjectStateDto>("project/create", _root, "WireNovel", "Book One");
        Assert.True(created.IsLoaded);
        Assert.Equal("WireNovel", created.ProjectName);

        var withChapter = await InvokeAsync<ProjectStateDto>("project/createChapter", "Kapitel Eins");
        var chapter = withChapter.Chapters.Single(c => c.Title == "Kapitel Eins");

        var withScene = await InvokeAsync<ProjectStateDto>("project/createScene", chapter.Guid, "Szene Eins");
        var scene = withScene.Chapters.Single(c => c.Guid == chapter.Guid).Scenes.Single();
        Assert.Equal("Szene Eins", scene.Title);

        var written = await InvokeAsync<SceneWriteResultDto>(
            "scenes/write", chapter.Guid, scene.Id, "<p>Es war einmal ein Wort</p>", "Es war einmal ein Wort");
        Assert.Equal(5, written.WordCount);

        var content = await InvokeAsync<SceneContentDto>("scenes/read", chapter.Guid, scene.Id);
        Assert.Contains("Es war einmal ein Wort", content.Html);

        var reopened = await InvokeAsync<ProjectStateDto>("project/open", created.ProjectPath!);
        Assert.Equal(5, reopened.Chapters.Single(c => c.Guid == chapter.Guid).Scenes.Single().WordCount);

        var state = await InvokeAsync<ProjectStateDto>("project/getState");
        Assert.True(state.IsLoaded);

        var recents = await InvokeAsync<RecentProjectDto[]>("project/recent");
        Assert.Contains(recents, r => r.Name == "WireNovel");
    }

    [Fact]
    public async Task Wiki_ArticleAndIndex_OverTheWire()
    {
        await InvokeAsync<ProjectStateDto>("project/create", _root, "WikiWire", "Book");
        var withChapter = await InvokeAsync<ProjectStateDto>("project/createChapter", "Chapter");
        var chapter = withChapter.Chapters.Single();
        var withScene = await InvokeAsync<ProjectStateDto>("project/createScene", chapter.Guid, "Scene");
        var scene = withScene.Chapters.Single().Scenes.Single();

        var created = await InvokeAsync<JsonElement>("entities/create", "character", "Aldric");
        var id = created.GetProperty("id").GetString()!;

        var span = $"<p><span class=\"nv-entity-mention\" data-entity-id=\"{id}\">Aldric</span> walked.</p>";
        await InvokeAsync<SceneWriteResultDto>("scenes/write", chapter.Guid, scene.Id, span, "Aldric walked.");
        await _client.InvokeAsync("scenes/setSynopsis", chapter.Guid, scene.Id, "Aldric walks in.");
        await _client.InvokeAsync("project/setSceneDateRange", chapter.Guid, scene.Id, "2024-05-01", "", "");

        var article = await InvokeAsync<WikiArticleDto>("wiki/article", "character", id);
        Assert.Equal("Aldric", article.Title);
        var appearance = Assert.Single(article.Appearances);
        Assert.Equal("Scene", appearance.SceneTitle);
        Assert.Equal("Aldric walks in.", appearance.Synopsis);   // camelCase wire field
        Assert.Equal("2024-05-01", appearance.IsoDate);

        var index = await InvokeAsync<WikiIndexDto>("wiki/index");
        var characters = index.Scopes.Single(s => !s.IsWorldBible).Types.Single(t => t.TypeKey == "character");
        Assert.Contains(characters.Entries, e => e.Id == id && e.Title == "Aldric");
    }

    [Fact]
    public async Task RenameProject_ChangesName()
    {
        await InvokeAsync<ProjectStateDto>("project/create", _root, "OldName", "Book");
        var renamed = await InvokeAsync<ProjectStateDto>("project/rename", "NewName");
        Assert.Equal("NewName", renamed.ProjectName);
    }

    [Fact]
    public async Task Reorder_ChaptersAndScenes_AndMoveBetweenChapters()
    {
        await InvokeAsync<ProjectStateDto>("project/create", _root, "OrderNovel", "Book");
        await InvokeAsync<ProjectStateDto>("project/createChapter", "One");
        var state = await InvokeAsync<ProjectStateDto>("project/createChapter", "Two");
        var one = state.Chapters.Single(c => c.Title == "One");
        var two = state.Chapters.Single(c => c.Title == "Two");

        var reordered = await InvokeAsync<ProjectStateDto>("project/reorderChapter", two.Guid, 0);
        Assert.Equal("Two", reordered.Chapters.First().Title);

        await InvokeAsync<ProjectStateDto>("project/createScene", one.Guid, "A");
        var withScenes = await InvokeAsync<ProjectStateDto>("project/createScene", one.Guid, "B");
        var scenesOfOne = withScenes.Chapters.Single(c => c.Guid == one.Guid).Scenes;
        var sceneA = scenesOfOne.Single(s => s.Title == "A");
        var lastOrder = scenesOfOne.Max(s => s.Order);

        var sceneReordered = await InvokeAsync<ProjectStateDto>(
            "project/reorderScene", one.Guid, sceneA.Id, lastOrder);
        Assert.Equal("A", sceneReordered.Chapters.Single(c => c.Guid == one.Guid).Scenes.Last().Title);

        var moved = await InvokeAsync<ProjectStateDto>(
            "project/moveScenes", new[] { sceneA.Id }, two.Guid, 0);
        Assert.Contains(moved.Chapters.Single(c => c.Guid == two.Guid).Scenes, s => s.Title == "A");
        Assert.DoesNotContain(moved.Chapters.Single(c => c.Guid == one.Guid).Scenes, s => s.Title == "A");
    }

    [Fact]
    public async Task BooksAndDrafts_SwitchCreate_OverTheWire()
    {
        await InvokeAsync<ProjectStateDto>("project/create", _root, "MultiBook", "Book One");
        var withBook = await InvokeAsync<ProjectStateDto>("project/createBook", "Book Two");
        Assert.Equal(2, withBook.Books.Count);

        var bookTwo = withBook.Books.Single(b => b.Name == "Book Two");
        var switched = await InvokeAsync<ProjectStateDto>("project/switchBook", bookTwo.Id);
        Assert.Equal(bookTwo.Id, switched.ActiveBookId);

        var drafts = await InvokeAsync<DraftDto[]>("project/drafts");
        Assert.Single(drafts);
        Assert.True(drafts[0].IsActive);

        var created = await InvokeAsync<DraftDto[]>(
            "project/createDraft", "Second draft", drafts[0].Id);
        Assert.Equal(2, created.Length);

        var newDraft = created.Single(d => d.Name == "Second draft");
        await InvokeAsync<ProjectStateDto>("project/switchDraft", newDraft.Id);
        var after = await InvokeAsync<DraftDto[]>("project/drafts");
        Assert.True(after.Single(d => d.Id == newDraft.Id).IsActive);
    }

    [Fact]
    public async Task DeleteDraft_RemovesDraft_OverTheWire()
    {
        await InvokeAsync<ProjectStateDto>("project/create", _root, "DraftNovel", "Book");
        var drafts = await InvokeAsync<DraftDto[]>("project/drafts");
        var created = await InvokeAsync<DraftDto[]>("project/createDraft", "Throwaway", drafts[0].Id);
        Assert.Equal(2, created.Length);

        var throwaway = created.Single(d => d.Name == "Throwaway");
        var remaining = await InvokeAsync<DraftDto[]>("project/deleteDraft", throwaway.Id);
        Assert.Single(remaining);
        Assert.DoesNotContain(remaining, d => d.Id == throwaway.Id);
    }

    [Fact]
    public async Task SceneEdit_PovAndDateRange_RoundTrips_OverTheWire()
    {
        await InvokeAsync<ProjectStateDto>("project/create", _root, "DateNovel", "Book");
        var withChapter = await InvokeAsync<ProjectStateDto>("project/createChapter", "Chapter");
        var chapter = withChapter.Chapters.Single();
        var withScene = await InvokeAsync<ProjectStateDto>("project/createScene", chapter.Guid, "Scene");
        var scene = withScene.Chapters.Single().Scenes.Single();

        var empty = await InvokeAsync<SceneEditDto>("project/getSceneEdit", chapter.Guid, scene.Id);
        Assert.Equal("", empty.Pov);
        Assert.Equal("", empty.DateStart);

        await _client.InvokeAsync("scenes/setPov", chapter.Guid, scene.Id, "Alice");
        var withPov = await InvokeAsync<SceneEditDto>("project/getSceneEdit", chapter.Guid, scene.Id);
        Assert.Equal("Alice", withPov.Pov);

        var set = await InvokeAsync<SceneEditDto>(
            "project/setSceneDateRange", chapter.Guid, scene.Id, "1999-01-02", "1999-01-05", "Winter");
        Assert.Equal("1999-01-02", set.DateStart);
        Assert.Equal("1999-01-05", set.DateEnd);
        Assert.Equal("Winter", set.DateNote);
        Assert.Equal("Alice", set.Pov);

        var cleared = await InvokeAsync<SceneEditDto>(
            "project/setSceneDateRange", chapter.Guid, scene.Id, "", "", "");
        Assert.Equal("", cleared.DateStart);
        Assert.Equal("", cleared.DateEnd);
        Assert.Equal("", cleared.DateNote);
    }

    [Fact]
    public async Task StructureEdits_RenameStatusDelete_OverTheWire()
    {
        await InvokeAsync<ProjectStateDto>("project/create", _root, "EditNovel", "Book");
        var s1 = await InvokeAsync<ProjectStateDto>("project/createChapter", "Old Title");
        var chapter = s1.Chapters.Single();
        await InvokeAsync<ProjectStateDto>("project/createScene", chapter.Guid, "Old Scene");

        var renamed = await InvokeAsync<ProjectStateDto>("project/renameChapter", chapter.Guid, "New Title");
        Assert.Equal("New Title", renamed.Chapters.Single().Title);

        var scene = renamed.Chapters.Single().Scenes.Single();
        var sceneRenamed = await InvokeAsync<ProjectStateDto>(
            "project/renameScene", chapter.Guid, scene.Id, "New Scene");
        Assert.Equal("New Scene", sceneRenamed.Chapters.Single().Scenes.Single().Title);

        var statusSet = await InvokeAsync<ProjectStateDto>(
            "project/setChapterStatus", chapter.Guid, "FirstDraft");
        Assert.Equal("FirstDraft", statusSet.Chapters.Single().Status);

        var actSet = await InvokeAsync<ProjectStateDto>(
            "project/setChapterAct", chapter.Guid, "Act I");
        Assert.Equal("Act I", actSet.Chapters.Single().Act);

        var opener = await InvokeAsync<ProjectStateDto>(
            "project/setChapterOpener", chapter.Guid, "  Ashport, 1893  ", true);
        Assert.Equal("Ashport, 1893", opener.Chapters.Single().Subtitle);
        Assert.True(opener.Chapters.Single().HideHeading);

        // A blank subtitle is no subtitle, not an empty line under the title.
        var cleared = await InvokeAsync<ProjectStateDto>(
            "project/setChapterOpener", chapter.Guid, "   ", false);
        Assert.Null(cleared.Chapters.Single().Subtitle);

        var sceneDeleted = await InvokeAsync<ProjectStateDto>(
            "project/deleteScene", chapter.Guid, scene.Id);
        Assert.Empty(sceneDeleted.Chapters.Single().Scenes);

        var chapterDeleted = await InvokeAsync<ProjectStateDto>("project/deleteChapter", chapter.Guid);
        Assert.Empty(chapterDeleted.Chapters);
    }
}
