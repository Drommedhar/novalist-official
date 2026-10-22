using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sync.Client;
using Novalist.Sync.Client.Tests;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class HostedProjectOwnerAccessTests
{
    [Fact]
    public async Task RemovalRefreshesRemainingWritersKeysAndDeletionKeepsEveryoneLocalCopies()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        identity.IssueAccessToken = () => identity.Subject;
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var owner = new HostedDesktop(Path.Combine(directory.Path, "owner-settings"), identity);
        var project = await owner.Rpc.InvokeAsync<ProjectStateDto>("project/create", directory.Path, "Shared", "Book");
        await owner.SignInAsync(server.Address);
        var original = await owner.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        await using var removed = new HostedDesktop(Path.Combine(directory.Path, "removed-settings"), identity);
        var removedRoot = Path.Combine(directory.Path, "Removed copy");
        await InviteAsync(owner, removed, identity, server.Address, "removed", removedRoot);
        var member = Assert.Single(await owner.Rpc.InvokeAsync<HostedProjectMember[]>("collaboration/members"), value => value.Role == "Contributor");
        await using var retained = new HostedDesktop(Path.Combine(directory.Path, "retained-settings"), identity);
        var retainedRoot = Path.Combine(directory.Path, "Retained copy");
        await InviteAsync(owner, retained, identity, server.Address, "retained", retainedRoot);
        await Assert.ThrowsAsync<RemoteInvocationException>(() => retained.Rpc.InvokeAsync("collaboration/members"));
        await Assert.ThrowsAsync<RemoteInvocationException>(() => retained.Rpc.InvokeAsync("collaboration/deleteSharedProject"));

        await owner.Rpc.InvokeAsync("collaboration/removeMember", member.UserId);

        var refreshedOwner = await owner.Rpc.InvokeAsync<HostedSessionDto>("collaboration/status");
        Assert.Equal(original.ProjectId, refreshedOwner.ProjectId);
        Assert.NotEqual(original.SessionId, refreshedOwner.SessionId);
        Assert.Equal("Connected", refreshedOwner.Connection);
        await WaitForConnectionAsync(removed, "AccessEnded");
        await WaitForConnectionAsync(retained, "KeyRefreshRequired");
        await removed.Rpc.InvokeAsync("collaboration/end");
        await Assert.ThrowsAsync<RemoteInvocationException>(() => removed.Rpc.InvokeAsync("collaboration/resume"));
        await owner.Rpc.InvokeAsync("collaboration/end");
        await retained.Rpc.InvokeAsync("collaboration/end");
        var resumed = await retained.Rpc.InvokeAsync<HostedSessionDto>("collaboration/resume");
        Assert.Equal(original.ProjectId, resumed.ProjectId);
        var edited = await retained.Rpc.InvokeAsync<ProjectStateDto>("project/createChapter", "Owner offline after key refresh");
        Assert.Equal("Owner offline after key refresh", Assert.Single(edited.Chapters).Title);
        await owner.Rpc.InvokeAsync("collaboration/resume");
        Assert.Equal("Owner offline after key refresh", Assert.Single((await owner.Rpc.InvokeAsync<ProjectStateDto>("project/getState")).Chapters).Title);
        await owner.Rpc.InvokeAsync("collaboration/deleteSharedProject");
        Assert.Null(await owner.Rpc.InvokeAsync<HostedSessionDto?>("collaboration/status"));
        await WaitForConnectionAsync(retained, "AccessEnded");
        Assert.All(new[] { project.ProjectPath, retainedRoot, removedRoot }, root => Assert.True(File.Exists(Path.Combine(root ?? "", ".novalist", "project.json"))));
    }

    private static async Task InviteAsync(HostedDesktop owner, HostedDesktop guest, TestIdentityProvider identity, Uri server, string subject, string root)
    {
        var invitation = await owner.Rpc.InvokeAsync<string>("collaboration/invite", DateTimeOffset.UtcNow.AddHours(1));
        identity.Subject = subject;
        await guest.SignInAsync(server);
        await guest.Rpc.InvokeAsync("collaboration/join", server, invitation, root);
    }

    private static async Task WaitForConnectionAsync(HostedDesktop desktop, string expected)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if ((await desktop.Rpc.InvokeAsync<HostedSessionDto>("collaboration/status")).Connection == expected) return;
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        Assert.Fail("The expected shared access change did not arrive.");
    }
}
