using System.Text;
using System.Text.Json;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sync.Client.Tests;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class SharedProjectEvolutionTests
{
    [Fact]
    public async Task NewScenesKeepTheirLiveDocumentWhenMovedAndPreserveLateEditsAfterDeletion()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var owner = new HostedDesktop(Path.Combine(directory.Path, "owner-settings"), identity);
        await owner.Rpc.InvokeAsync("project/create", directory.Path, "Shared", "Book");
        await owner.SignInAsync(server.Address);
        var session = await owner.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        var invitation = await owner.Rpc.InvokeAsync<string>("collaboration/invite", DateTimeOffset.UtcNow.AddHours(1));
        identity.Subject = "guest";
        await using var guest = new HostedDesktop(Path.Combine(directory.Path, "guest-settings"), identity);
        await guest.SignInAsync(server.Address);
        var joinedRoot = Path.Combine(directory.Path, "Joined");
        var joined = await guest.Rpc.InvokeAsync<HostedSessionDto>("collaboration/join", server.Address, invitation, joinedRoot);

        var chapter = Assert.Single((await owner.Rpc.InvokeAsync<ProjectStateDto>("project/createChapter", "Opening")).Chapters);
        var scene = Assert.Single(Assert.Single((await owner.Rpc.InvokeAsync<ProjectStateDto>("project/createScene", chapter.Guid, "New scene")).Chapters).Scenes);
        await WaitAsync(async () => (await guest.Rpc.InvokeAsync<ProjectStateDto>("project/getState")).Chapters.Any(item => item.Scenes.Any(value => value.Id == scene.Id)));
        var document = (await guest.Rpc.InvokeAsync<SceneContentDto>("scenes/read", chapter.Guid, scene.Id)).Shared ?? throw new InvalidOperationException("Missing shared document.");
        await guest.Rpc.InvokeAsync("collaboration/writeScene", joined.SessionId, document.ItemId, Encoding.UTF8.GetBytes("<p>Written in the new scene.</p>"));
        await WaitAsync(async () => (await owner.Rpc.InvokeAsync<SceneContentDto>("scenes/read", chapter.Guid, scene.Id)).Html.Contains("Written in the new scene.", StringComparison.Ordinal));

        var destination = (await owner.Rpc.InvokeAsync<ProjectStateDto>("project/createChapter", "Later")).Chapters.Single(item => item.Title == "Later");
        await owner.Rpc.InvokeAsync("project/moveScenes", new[] { scene.Id }, destination.Guid, 0);
        await WaitAsync(async () => (await guest.Rpc.InvokeAsync<ProjectStateDto>("project/getState")).Chapters.Any(item => item.Guid == destination.Guid && item.Scenes.Any(value => value.Id == scene.Id)));
        var moved = await guest.Rpc.InvokeAsync<SceneContentDto>("scenes/read", destination.Guid, scene.Id);
        Assert.Equal(document.ItemId, moved.Shared?.ItemId);
        Assert.Contains("Written in the new scene.", moved.Html);
        await owner.Rpc.InvokeAsync("project/deleteScene", destination.Guid, scene.Id);
        await WaitAsync(async () => !(await guest.Rpc.InvokeAsync<ProjectStateDto>("project/getState")).Chapters.SelectMany(item => item.Scenes).Any(item => item.Id == scene.Id));
        await guest.Rpc.InvokeAsync("collaboration/writeScene", joined.SessionId, document.ItemId, Encoding.UTF8.GetBytes("<p>Late writing survives deletion.</p>"));
        await WaitAsync(() => Task.FromResult(ContainsRecovery(joinedRoot, "Late writing survives deletion.")));
        var final = await guest.Rpc.InvokeAsync<ProjectStateDto>("project/getState");
        Assert.DoesNotContain(final.Chapters.SelectMany(item => item.Scenes), item => item.Id == scene.Id);
        Assert.Equal(session.ProjectId, joined.ProjectId);
    }

    [Fact]
    public async Task NewCodexProseAndSectionsSyncRespectEntryLocksAndRecoverDeletedSections()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var desktop = new HostedDesktop(Path.Combine(directory.Path, "settings"), identity);
        var project = await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/create", directory.Path, "Shared", "Book");
        await desktop.SignInAsync(server.Address);
        var session = await desktop.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        var created = await desktop.Rpc.InvokeAsync<JsonElement>("entities/create", "character", "Writer");
        var id = created.GetProperty("id").GetString();
        var entity = await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "character", id);
        var prose = entity.GetProperty("sharedProse").GetProperty("fields").GetProperty("distinguishingFeatures").GetGuid();
        await desktop.Rpc.InvokeAsync("collaboration/writeScene", session.SessionId, prose, Encoding.UTF8.GetBytes("text:New shared description."));
        Assert.Equal("New shared description.", (await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "character", id)).GetProperty("distinguishingFeatures").GetString());
        await desktop.Rpc.InvokeAsync("entities/setLocked", "character", id, true);
        await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.Rpc.InvokeAsync("collaboration/writeScene", session.SessionId, prose, Encoding.UTF8.GetBytes("text:Forbidden")));
        await desktop.Rpc.InvokeAsync("entities/setLocked", "character", id, false);
        await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.Rpc.InvokeAsync("entities/restoreRevision", "character", id, "unused"));

        await desktop.Rpc.InvokeAsync("entities/updateLists", "character", id, null, new[] { new EntitySectionDto("Notes", "Original section", "notes") }, null);
        entity = await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "character", id);
        Assert.True(entity.GetProperty("sharedProse").GetProperty("fields").TryGetProperty("section:notes", out var sectionField), entity.ToString());
        var section = sectionField.GetGuid();
        await desktop.Rpc.InvokeAsync("collaboration/writeScene", session.SessionId, section, Encoding.UTF8.GetBytes("text:Live section content."));
        Assert.Equal("Live section content.", (await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "character", id)).GetProperty("sections")[0].GetProperty("content").GetString());
        await desktop.Rpc.InvokeAsync("entities/updateLists", "character", id, null, Array.Empty<EntitySectionDto>(), null);
        await desktop.Rpc.InvokeAsync("collaboration/writeScene", session.SessionId, section, Encoding.UTF8.GetBytes("text:Late section writing."));
        Assert.Empty((await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "character", id)).GetProperty("sections").EnumerateArray());
        Assert.True(ContainsRecovery(project.ProjectPath ?? throw new InvalidOperationException("Missing project."), "Late section writing."));
    }

    private static bool ContainsRecovery(string root, string text)
    {
        var path = Path.Combine(root, "Recovered edits");
        return Directory.Exists(path) && Directory.GetFiles(path, "*", SearchOption.AllDirectories)
            .Any(file => File.ReadAllText(file).Contains(text, StringComparison.Ordinal));
    }

    private static async Task WaitAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await condition()) return;
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        Assert.Fail("The committed shared change did not arrive.");
    }
}
