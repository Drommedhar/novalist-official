using System.Text.Json;
using Nerdbank.Streams;
using Novalist.Backend;
using Novalist.Backend.Rpc;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Exercises the facades through a real JSON-RPC pair so wire naming
/// (camelCase, method routes) is asserted, not assumed.</summary>
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

        var sceneDeleted = await InvokeAsync<ProjectStateDto>(
            "project/deleteScene", chapter.Guid, scene.Id);
        Assert.Empty(sceneDeleted.Chapters.Single().Scenes);

        var chapterDeleted = await InvokeAsync<ProjectStateDto>("project/deleteChapter", chapter.Guid);
        Assert.Empty(chapterDeleted.Chapters);
    }
}
