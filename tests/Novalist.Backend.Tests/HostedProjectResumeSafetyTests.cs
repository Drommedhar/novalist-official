using System.Text.Json;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sync.Client.Tests;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class HostedProjectResumeSafetyTests
{
    [Fact]
    public async Task ReopenedProjectRequiresItsOriginalAccountAndProjectCredentials()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        var settings = Path.Combine(directory.Path, "settings");
        var credentials = new Dictionary<string, string>();
        HostedSessionDto original;
        string root;
        await using (var first = new HostedDesktop(settings, identity, credentials))
        {
            var project = await first.Rpc.InvokeAsync<ProjectStateDto>("project/create", directory.Path, "Shared", "Book");
            root = project.ProjectPath ?? throw new InvalidOperationException("Missing project.");
            await first.SignInAsync(server.Address);
            original = await first.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
            await first.Rpc.InvokeAsync("collaboration/end");
        }

        await using var reopened = new HostedDesktop(settings, identity, credentials);
        Assert.Null(await reopened.Rpc.InvokeAsync<SavedHostedProjectDto?>("collaboration/saved"));
        await reopened.Rpc.InvokeAsync("project/open", root);
        Assert.Equal(original.ProjectId, (await reopened.Rpc.InvokeAsync<SavedHostedProjectDto>("collaboration/saved")).ProjectId);
        await reopened.Rpc.InvokeAsync("collaboration/account/signOut", server.Address);
        var signedOut = await Assert.ThrowsAsync<RemoteInvocationException>(() => reopened.Rpc.InvokeAsync<HostedSessionDto>("collaboration/resume"));
        Assert.Contains("Sign in", signedOut.Message);
        identity.Subject = "different-writer";
        await reopened.SignInAsync(server.Address);
        var wrongAccount = await Assert.ThrowsAsync<RemoteInvocationException>(() => reopened.Rpc.InvokeAsync<HostedSessionDto>("collaboration/resume"));
        Assert.Contains("account that joined", wrongAccount.Message);
        await reopened.Rpc.InvokeAsync("collaboration/account/signOut", server.Address);
        identity.Subject = "writer";
        await reopened.SignInAsync(server.Address);
        var projectCredential = Assert.Single(credentials, item => item.Key.StartsWith("project:", StringComparison.Ordinal));
        credentials.Remove(projectCredential.Key);
        var missingKey = await Assert.ThrowsAsync<RemoteInvocationException>(() => reopened.Rpc.InvokeAsync<HostedSessionDto>("collaboration/resume"));
        Assert.Contains("saved project credentials", missingKey.Message);
        credentials.Add(projectCredential.Key, projectCredential.Value);
        var resumed = await reopened.Rpc.InvokeAsync<HostedSessionDto>("collaboration/resume");
        Assert.Equal(original.ProjectId, resumed.ProjectId);
        Assert.NotEqual(original.SessionId, resumed.SessionId);

        await reopened.Rpc.InvokeAsync("collaboration/end");
        var copy = Path.Combine(directory.Path, "Independent");
        await reopened.Rpc.InvokeAsync("collaboration/copy", copy);
        await reopened.Rpc.InvokeAsync("project/open", copy);
        Assert.Null(await reopened.Rpc.InvokeAsync<SavedHostedProjectDto?>("collaboration/saved"));
    }

    [Fact]
    public async Task InvalidLocalSettingsPreventResumeWithoutReplacingTheLocalProject()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var desktop = new HostedDesktop(Path.Combine(directory.Path, "settings"), identity);
        var project = await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/create", directory.Path, "Shared", "Book");
        await desktop.SignInAsync(server.Address);
        await desktop.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        await desktop.Rpc.InvokeAsync("collaboration/end");
        await desktop.Rpc.InvokeAsync("project/createChapter", "Only local");
        var path = Path.Combine(project.ProjectPath ?? throw new InvalidOperationException("Missing project."), ".novalist", "settings.json");
        var original = await File.ReadAllTextAsync(path, ct);
        await File.WriteAllTextAsync(path, "{invalid", ct);

        await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.Rpc.InvokeAsync<HostedSessionDto>("collaboration/resume"));

        Assert.Equal("{invalid", await File.ReadAllTextAsync(path, ct));
        Assert.Equal("Only local", Assert.Single((await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/getState")).Chapters).Title);
        Assert.Null(await desktop.Rpc.InvokeAsync<HostedSessionDto?>("collaboration/status"));
        await File.WriteAllTextAsync(path, original, ct);
        var resumed = await desktop.Rpc.InvokeAsync<HostedSessionDto>("collaboration/resume");
        Assert.Equal("Connected", resumed.Connection);
        Assert.Empty((await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/getState")).Chapters);
        using var restored = JsonDocument.Parse(await File.ReadAllTextAsync(path, ct));
        Assert.Equal(JsonValueKind.Object, restored.RootElement.ValueKind);
    }

    [Fact]
    public async Task FailureLoadingANewerSharedSceneRestoresTheRetainedProjectAndAllowsRetry()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var desktop = new HostedDesktop(Path.Combine(directory.Path, "settings"), identity);
        await desktop.Rpc.InvokeAsync("project/create", directory.Path, "Shared", "Book");
        var chapter = Assert.Single((await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/createChapter", "Opening")).Chapters);
        await desktop.SignInAsync(server.Address);
        await desktop.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        var scene = Assert.Single(Assert.Single((await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/createScene", chapter.Guid, "Added later")).Chapters).Scenes);
        var added = await desktop.Rpc.InvokeAsync<SceneContentDto>("scenes/read", chapter.Guid, scene.Id);
        await desktop.Rpc.InvokeAsync("collaboration/end");
        await desktop.Rpc.InvokeAsync("scenes/write", chapter.Guid, scene.Id, "<p>Local retained draft.</p>", "Local retained draft.");
        desktop.Documents.RejectLoadItem = added.Shared?.ItemId ?? throw new InvalidOperationException("Missing shared document.");

        await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.Rpc.InvokeAsync<HostedSessionDto>("collaboration/resume"));

        Assert.Null(await desktop.Rpc.InvokeAsync<HostedSessionDto?>("collaboration/status"));
        Assert.Contains("Local retained draft.", (await desktop.Rpc.InvokeAsync<SceneContentDto>("scenes/read", chapter.Guid, scene.Id)).Html);
        await desktop.Rpc.InvokeAsync("collaboration/resume");
        var restored = await desktop.Rpc.InvokeAsync<SceneContentDto>("scenes/read", chapter.Guid, scene.Id);
        Assert.DoesNotContain("Local retained draft.", restored.Html);
        Assert.NotNull(restored.Shared);
    }
}
