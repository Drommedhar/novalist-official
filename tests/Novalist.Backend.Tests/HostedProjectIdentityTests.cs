using System.Text.Json.Nodes;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sync.Client.Tests;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class HostedProjectIdentityTests
{
    [Fact]
    public async Task InvitationsRequireTheNamedServerAndDoNotReplaceExistingFolders()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var otherServer = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var owner = new HostedDesktop(Path.Combine(directory.Path, "owner-settings"), identity);
        await owner.Rpc.InvokeAsync("project/create", directory.Path, "Shared", "Book");
        await owner.SignInAsync(server.Address);
        await owner.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        var invitation = await owner.Rpc.InvokeAsync<string>("collaboration/invite", DateTimeOffset.UtcNow.AddHours(1));
        identity.Subject = "guest";
        await using var guest = new HostedDesktop(Path.Combine(directory.Path, "guest-settings"), identity);
        await guest.SignInAsync(otherServer.Address);
        var destination = Path.Combine(directory.Path, "Joined");

        var wrongServer = await Assert.ThrowsAsync<RemoteInvocationException>(() => guest.Rpc.InvokeAsync<HostedSessionDto>("collaboration/join", otherServer.Address, invitation, destination));

        Assert.Contains("server named by this invitation", wrongServer.Message);
        Assert.False(Directory.Exists(destination));
        await guest.SignInAsync(server.Address);
        await Assert.ThrowsAsync<RemoteInvocationException>(() => guest.Rpc.InvokeAsync<HostedSessionDto>("collaboration/join", server.Address, invitation, directory.Path));
        Assert.False(Directory.Exists(destination));
        Assert.False((await guest.Rpc.InvokeAsync<HostedSessionDto>("collaboration/join", server.Address, invitation, destination)).IsOwner);
    }

    [Fact]
    public async Task ReplacingAProjectAtTheSamePathCannotReuseItsSavedSharedConnection()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var desktop = new HostedDesktop(Path.Combine(directory.Path, "settings"), identity);
        var project = await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/create", directory.Path, "Shared", "Book");
        var root = project.ProjectPath ?? throw new InvalidOperationException("Missing project.");
        await desktop.SignInAsync(server.Address);
        await desktop.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        await desktop.Rpc.InvokeAsync("collaboration/end");
        await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.Rpc.InvokeAsync("collaboration/copy", Path.Combine(root, "Nested")));
        await desktop.Rpc.InvokeAsync("project/close");
        var path = Path.Combine(root, ".novalist", "project.json");
        var metadata = JsonNode.Parse(await File.ReadAllTextAsync(path, ct)) ?? throw new InvalidOperationException("Missing project metadata.");
        var replacement = Guid.NewGuid().ToString("D");
        metadata["id"] = replacement;
        await File.WriteAllTextAsync(path, metadata.ToJsonString(), ct);
        await desktop.Rpc.InvokeAsync("project/open", root);

        var saved = await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.Rpc.InvokeAsync<SavedHostedProjectDto>("collaboration/saved"));

        Assert.Contains("different local project", saved.Message);
        await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.Rpc.InvokeAsync<HostedSessionDto>("collaboration/resume"));
        Assert.Null(await desktop.Rpc.InvokeAsync<HostedSessionDto?>("collaboration/status"));
        Assert.Equal(replacement, JsonNode.Parse(await File.ReadAllTextAsync(path, ct))?["id"]?.GetValue<string>());
    }
}
