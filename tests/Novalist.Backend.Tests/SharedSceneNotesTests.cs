using System.Text;
using System.Text.Json;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sync.Client.Tests;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class SharedSceneNotesTests
{
    [Fact]
    public async Task SceneNotesShareProseAndKeepTheirIdentityWhenTheSceneMoves()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var owner = new HostedDesktop(Path.Combine(directory.Path, "settings"), identity);
        var project = await owner.Rpc.InvokeAsync<ProjectStateDto>("project/create", directory.Path, "Novel", "Book");
        var chapter = Assert.Single((await owner.Rpc.InvokeAsync<ProjectStateDto>("project/createChapter", "Opening")).Chapters);
        var scene = Assert.Single(Assert.Single((await owner.Rpc.InvokeAsync<ProjectStateDto>("project/createScene", chapter.Guid, "Arrival")).Chapters).Scenes);
        await owner.Rpc.InvokeAsync("scenes/setSynopsis", chapter.Guid, scene.Id, "Original synopsis.");
        await owner.SignInAsync(server.Address);
        var session = await owner.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        var meta = await owner.Rpc.InvokeAsync<JsonElement>("scenes/getMeta", chapter.Guid, scene.Id);
        Assert.True(meta.TryGetProperty("sharedProse", out var prose), meta.ToString());
        var fields = prose.GetProperty("fields");
        Assert.Equal(new[] { "goal", "notes", "outcome", "synopsis" }, fields.EnumerateObject().Select(field => field.Name).Order());
        foreach (var key in new[] { "synopsis", "notes", "goal", "outcome" })
        {
            var item = fields.GetProperty(key).GetGuid();
            await owner.Rpc.InvokeAsync("collaboration/writeScene", session.SessionId, item, Encoding.UTF8.GetBytes($"text:Shared {key}."));
            Assert.Equal($"Shared {key}.", (await owner.Rpc.InvokeAsync<JsonElement>("scenes/getMeta", chapter.Guid, scene.Id)).GetProperty(key).GetString());
        }
        await Assert.ThrowsAsync<RemoteInvocationException>(() => owner.Rpc.InvokeAsync("scenes/setNotes", chapter.Guid, scene.Id, "Stale replacement"));
        var destination = (await owner.Rpc.InvokeAsync<ProjectStateDto>("project/createChapter", "Later")).Chapters.Single(value => value.Title == "Later");
        await owner.Rpc.InvokeAsync("project/moveScenes", new[] { scene.Id }, destination.Guid, 0);
        var moved = await owner.Rpc.InvokeAsync<JsonElement>("scenes/getMeta", destination.Guid, scene.Id);
        Assert.Equal(fields.ToString(), moved.GetProperty("sharedProse").GetProperty("fields").ToString());
        var added = (await owner.Rpc.InvokeAsync<ProjectStateDto>("project/createScene", destination.Guid, "New notes")).Chapters.Single(value => value.Guid == destination.Guid).Scenes.Single(value => value.Title == "New notes");
        Assert.True((await owner.Rpc.InvokeAsync<JsonElement>("scenes/getMeta", destination.Guid, added.Id)).TryGetProperty("sharedProse", out _));
        await owner.Rpc.InvokeAsync("project/deleteScene", destination.Guid, scene.Id);
        await owner.Rpc.InvokeAsync("collaboration/writeScene", session.SessionId, fields.GetProperty("notes").GetGuid(), Encoding.UTF8.GetBytes("text:Late planning text."));
        var recovery = Path.Combine(project.ProjectPath ?? throw new InvalidOperationException("Missing project."), "Recovered edits");
        Assert.Contains(Directory.GetFiles(recovery, "*", SearchOption.AllDirectories), file => File.ReadAllText(file).Contains("Late planning text.", StringComparison.Ordinal));
        Assert.DoesNotContain((await owner.Rpc.InvokeAsync<ProjectStateDto>("project/getState")).Chapters.SelectMany(value => value.Scenes), value => value.Id == scene.Id);
    }
}
