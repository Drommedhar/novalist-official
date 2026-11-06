using System.Text;
using System.Text.Json;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sync.Client;
using Novalist.Sync.Client.Tests;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class HostedProjectLifecycleTests
{
    [Fact]
    public async Task RelaySharingRequiresTheHostAndKeepsLocalCopiesWithoutStorageRequests()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var owner = new HostedDesktop(directory.Combine("owner-settings"), identity);
        var project = await owner.Rpc.InvokeAsync<ProjectStateDto>("project/create", directory.Path, "Shared", "Book");
        var chapter = Assert.Single((await owner.Rpc.InvokeAsync<ProjectStateDto>("project/createChapter", "Opening")).Chapters);
        var scene = Assert.Single(Assert.Single((await owner.Rpc.InvokeAsync<ProjectStateDto>("project/createScene", chapter.Guid, "Arrival")).Chapters).Scenes);
        await owner.Rpc.InvokeAsync("scenes/write", chapter.Guid, scene.Id, "<p>Initial draft.</p>", "Initial draft.");
        await owner.SignInAsync(server.Address);
        var session = await owner.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        var invitation = await owner.Rpc.InvokeAsync<string>("collaboration/invite", DateTimeOffset.UtcNow.AddHours(1));
        identity.Subject = "guest";
        await using var guest = new HostedDesktop(directory.Combine("guest-settings"), identity);
        await guest.SignInAsync(server.Address);
        var root = directory.Combine("Joined");
        var joined = await guest.Rpc.InvokeAsync<HostedSessionDto>("collaboration/join", server.Address, invitation, root);
        Assert.Equal("hosted", joined.Mode);
        Assert.Equal(session.ProjectId, joined.ProjectId);
        var item = (await guest.Rpc.InvokeAsync<SceneContentDto>("scenes/read", chapter.Guid, scene.Id)).Shared!.ItemId;
        await guest.Rpc.InvokeAsync("collaboration/writeScene", joined.SessionId, item, Encoding.UTF8.GetBytes("<p>Guest writes live.</p>"));
        await WaitForSceneAsync(owner, chapter.Guid, scene.Id, "Guest writes live.");
        await guest.Rpc.InvokeAsync("collaboration/end");
        Assert.Equal(joined.ProjectId, (await guest.Rpc.InvokeAsync<SavedHostedProjectDto>("collaboration/saved")).ProjectId);
        var resumed = await guest.Rpc.InvokeAsync<HostedSessionDto>("collaboration/resume");
        Assert.Equal(session.ProjectId, resumed.ProjectId);
        await guest.Rpc.InvokeAsync("collaboration/end");
        var unused = await owner.Rpc.InvokeAsync<string>("collaboration/invite", DateTimeOffset.UtcNow.AddHours(1));
        await owner.Rpc.InvokeAsync("collaboration/end");
        await Assert.ThrowsAsync<RemoteInvocationException>(() => guest.Rpc.InvokeAsync("collaboration/resume"));
        await Assert.ThrowsAsync<RemoteInvocationException>(() => guest.Rpc.InvokeAsync("collaboration/join", server.Address, unused, directory.Combine("Offline")));
        Assert.False(Directory.Exists(directory.Combine("Offline")));
        Assert.Contains("Guest writes live.", (await guest.Rpc.InvokeAsync<SceneContentDto>("scenes/read", chapter.Guid, scene.Id)).Html);
        Assert.Equal(0, server.StorageRequests);
        Assert.True(File.Exists(Path.Combine(project.ProjectPath!, ".novalist", "project.json")));
    }

    [Fact]
    public async Task RelayRejectingConnectionsLeavesTheDesktopUsableForRetry()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var owner = new HostedDesktop(directory.Combine("settings"), identity);
        await owner.Rpc.InvokeAsync("project/create", directory.Path, "Shared", "Book");
        await owner.SignInAsync(server.Address);
        server.RejectJoins = true;
        await Assert.ThrowsAsync<RemoteInvocationException>(() => owner.Rpc.InvokeAsync("collaboration/create", server.Address));
        Assert.Empty(owner.Progress);
        Assert.Null(await owner.Rpc.InvokeAsync<HostedSessionDto?>("collaboration/status"));
        server.RejectJoins = false;
        Assert.Equal("Connected", (await owner.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address)).Connection);
        Assert.Equal(0, server.StorageRequests);
    }

    [Fact]
    public async Task SharingReportsProgressAndCanBeCancelledWithoutBlockingTheWorkspace()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var owner = new HostedDesktop(directory.Combine("settings"), identity);
        await owner.Rpc.InvokeAsync("project/create", directory.Path, "Shared", "Book");
        var chapter = Assert.Single((await owner.Rpc.InvokeAsync<ProjectStateDto>("project/createChapter", "Opening")).Chapters);
        await owner.Rpc.InvokeAsync("project/createScene", chapter.Guid, "Arrival");
        await owner.SignInAsync(server.Address);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.Documents.BeforeSeed = async token => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); };
        var sharing = owner.Rpc.InvokeAsync("collaboration/create", server.Address);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.Contains(owner.Progress, progress => progress.Phase == "scanning");
        await owner.Rpc.InvokeAsync("collaboration/cancel").WaitAsync(TimeSpan.FromSeconds(5), ct);
        await Assert.ThrowsAnyAsync<Exception>(() => sharing.WaitAsync(TimeSpan.FromSeconds(5), ct));
        Assert.Null(await owner.Rpc.InvokeAsync<HostedSessionDto?>("collaboration/status"));
        owner.Documents.BeforeSeed = null;
        await owner.Rpc.InvokeAsync("collaboration/create", server.Address);
        Assert.Contains(owner.Progress, progress => progress.CompletedFiles > 0 && progress.TotalFiles > 0);
        Assert.Contains(owner.Progress, progress => progress.Phase == "complete");
        Assert.Equal(0, server.StorageRequests);
    }

    [Fact]
    public async Task OnlyTheHostCanRemoveWritersAndEndingTheSessionRetainsEveryoneFiles()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var owner = new HostedDesktop(directory.Combine("owner-settings"), identity);
        await owner.Rpc.InvokeAsync("project/create", directory.Path, "Shared", "Book");
        await owner.SignInAsync(server.Address);
        await owner.Rpc.InvokeAsync("collaboration/create", server.Address);
        var link = await owner.Rpc.InvokeAsync<string>("collaboration/invite", DateTimeOffset.UtcNow.AddHours(1));
        identity.Subject = "guest";
        await using var guest = new HostedDesktop(directory.Combine("guest-settings"), identity);
        await guest.SignInAsync(server.Address);
        var root = directory.Combine("Guest copy");
        await guest.Rpc.InvokeAsync("collaboration/join", server.Address, link, root);
        await Assert.ThrowsAsync<RemoteInvocationException>(() => guest.Rpc.InvokeAsync("collaboration/members"));
        await Assert.ThrowsAsync<RemoteInvocationException>(() => guest.Rpc.InvokeAsync("collaboration/deleteSharedProject"));
        var members = await owner.Rpc.InvokeAsync<HostedProjectMember[]>("collaboration/members");
        var member = Assert.Single(members, item => item.Role == "Contributor");
        await Assert.ThrowsAsync<RemoteInvocationException>(() => owner.Rpc.InvokeAsync("collaboration/removeMember", Assert.Single(members, item => item.Role == "Owner").UserId));
        await owner.Rpc.InvokeAsync("collaboration/removeMember", member.UserId);
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            while ((await guest.Rpc.InvokeAsync<HostedSessionDto>("collaboration/status")).Connection != "AccessEnded")
                await Task.Delay(20, timeout.Token);
        }
        Assert.Single(await owner.Rpc.InvokeAsync<HostedProjectMember[]>("collaboration/members"));
        await guest.Rpc.InvokeAsync("collaboration/end");
        await Assert.ThrowsAsync<RemoteInvocationException>(() => guest.Rpc.InvokeAsync("collaboration/resume"));
        await owner.Rpc.InvokeAsync("collaboration/deleteSharedProject");
        Assert.Null(await owner.Rpc.InvokeAsync<HostedSessionDto?>("collaboration/status"));
        Assert.True(File.Exists(Path.Combine(root, ".novalist", "project.json")));
        Assert.Equal(0, server.StorageRequests);
    }

    private static async Task WaitForSceneAsync(HostedDesktop desktop, string chapter, string scene, string text)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!(await desktop.Rpc.InvokeAsync<SceneContentDto>("scenes/read", chapter, scene)).Html.Contains(text, StringComparison.Ordinal))
            await Task.Delay(20, timeout.Token);
    }
}
