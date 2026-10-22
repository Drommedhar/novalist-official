using System.IO.Compression;
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
    public async Task HostedCollaboratorsKeepWorkingAfterOwnerLeavesAndOwnerResumesWithARecoveryCopy()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        identity.IssueAccessToken = () => identity.Subject;
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var owner = new HostedDesktop(Path.Combine(directory.Path, "owner-settings"), identity);
        var scene = await CreateProjectAsync(owner, directory.Path);
        await owner.SignInAsync(server.Address);
        var ownerSession = await owner.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        var invitation = await owner.Rpc.InvokeAsync<string>("collaboration/invite", DateTimeOffset.UtcNow.AddHours(1));
        await owner.Rpc.InvokeAsync("collaboration/end");
        identity.Subject = "guest";
        await using var guest = new HostedDesktop(Path.Combine(directory.Path, "guest-settings"), identity);
        await guest.SignInAsync(server.Address);
        var guestSession = await guest.Rpc.InvokeAsync<HostedSessionDto>("collaboration/join", server.Address, invitation, Path.Combine(directory.Path, "Joined"));
        Assert.Equal(ownerSession.ProjectId, guestSession.ProjectId);
        Assert.False(guestSession.IsOwner);
        var content = await guest.Rpc.InvokeAsync<JsonElement>("scenes/read", scene.Chapter, scene.Id);
        var item = content.GetProperty("shared").GetProperty("itemId").GetGuid();
        await owner.Rpc.InvokeAsync("scenes/write", scene.Chapter, scene.Id, "<p>Closed-session draft.</p>", "Closed-session draft.");
        await owner.Rpc.InvokeAsync("settings/updateProject", new { editorFontSize = 28 });
        await guest.Rpc.InvokeAsync("collaboration/writeScene", guestSession.SessionId, item, Encoding.UTF8.GetBytes("<p>Guest writes while owner is away.</p>"));
        var resumed = await owner.Rpc.InvokeAsync<HostedSessionDto>("collaboration/resume");
        Assert.Equal(ownerSession.ProjectId, resumed.ProjectId);
        Assert.NotEqual(ownerSession.SessionId, resumed.SessionId);
        await WaitForSceneAsync(owner, scene, "Guest writes while owner is away.");
        Assert.NotNull(resumed.RecoveryPath);
        using var recovery = ZipFile.OpenRead(resumed.RecoveryPath);
        var retained = recovery.Entries.Single(entry => entry.FullName.EndsWith(".novalist", StringComparison.Ordinal));
        using var reader = new StreamReader(retained.Open());
        Assert.Contains("Closed-session draft.", await reader.ReadToEndAsync(ct));
        var settings = await owner.Rpc.InvokeAsync<JsonElement>("settings/get");
        Assert.Equal(28, settings.GetProperty("effective").GetProperty("editorFontSize").GetDouble());
        Assert.Equal(resumed.ProjectId, (await owner.Rpc.InvokeAsync<SavedHostedProjectDto>("collaboration/saved")).ProjectId);
        var view = Guid.NewGuid();
        await owner.Rpc.InvokeAsync("collaboration/presence", resumed.SessionId, item, view, null, true);
        await WaitForPresenceAsync(guest, guestSession.SessionId, item, view);
    }

    [Fact]
    public async Task FailedResumeRestoresLocalChaptersProseAndPersonalSettingsBeforeRetry()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var desktop = new HostedDesktop(Path.Combine(directory.Path, "settings"), identity);
        var scene = await CreateProjectAsync(desktop, directory.Path);
        await desktop.SignInAsync(server.Address);
        await desktop.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        await desktop.Rpc.InvokeAsync("collaboration/end");
        await desktop.Rpc.InvokeAsync("project/createChapter", "Local chapter");
        await desktop.Rpc.InvokeAsync("scenes/write", scene.Chapter, scene.Id, "<p>Kept after failed resume.</p>", "Kept after failed resume.");
        await desktop.Rpc.InvokeAsync("settings/updateProject", new { editorFontSize = 31 });
        desktop.Documents.RejectNextLoad = true;

        await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.Rpc.InvokeAsync<HostedSessionDto>("collaboration/resume"));

        Assert.Null(await desktop.Rpc.InvokeAsync<HostedSessionDto?>("collaboration/status"));
        await WaitForSceneAsync(desktop, scene, "Kept after failed resume.");
        var project = await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/getState");
        Assert.Contains(project.Chapters, chapter => chapter.Title == "Local chapter");
        var settings = await desktop.Rpc.InvokeAsync<JsonElement>("settings/get");
        Assert.Equal(31, settings.GetProperty("effective").GetProperty("editorFontSize").GetDouble());
        var resumed = await desktop.Rpc.InvokeAsync<HostedSessionDto>("collaboration/resume");
        Assert.Equal("Connected", resumed.Connection);
        await WaitForSceneAsync(desktop, scene, "Initial draft.");
        Assert.Single((await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/getState")).Chapters);
    }

    [Fact]
    public async Task ServerRejectingCreateAndJoinLeavesTheDesktopUsableForRetry()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var owner = new HostedDesktop(Path.Combine(directory.Path, "owner-settings"), identity);
        await CreateProjectAsync(owner, directory.Path);
        await owner.SignInAsync(server.Address);
        server.RejectJoins = true;
        await Assert.ThrowsAsync<RemoteInvocationException>(() => owner.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address));
        Assert.Null(await owner.Rpc.InvokeAsync<HostedSessionDto?>("collaboration/status"));
        server.RejectJoins = false;
        await owner.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        var invitation = await owner.Rpc.InvokeAsync<string>("collaboration/invite", DateTimeOffset.UtcNow.AddHours(1));
        identity.Subject = "guest";
        await using var guest = new HostedDesktop(Path.Combine(directory.Path, "guest-settings"), identity);
        await guest.SignInAsync(server.Address);
        server.RejectJoins = true;
        var destination = Path.Combine(directory.Path, "Joined");
        await Assert.ThrowsAsync<RemoteInvocationException>(() => guest.Rpc.InvokeAsync<HostedSessionDto>("collaboration/join", server.Address, invitation, destination));
        Assert.False(Directory.Exists(destination));
        server.RejectJoins = false;
        var joined = await guest.Rpc.InvokeAsync<HostedSessionDto>("collaboration/join", server.Address, invitation, destination);
        Assert.False(joined.IsOwner);
    }

    private static async Task<(string Chapter, string Id)> CreateProjectAsync(HostedDesktop desktop, string parent)
    {
        await desktop.Rpc.InvokeAsync("project/create", parent, "Shared", "Book");
        var chapter = Assert.Single((await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/createChapter", "Opening")).Chapters);
        var project = await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/createScene", chapter.Guid, "Arrival");
        var scene = Assert.Single(Assert.Single(project.Chapters).Scenes);
        await desktop.Rpc.InvokeAsync("scenes/write", chapter.Guid, scene.Id, "<p>Initial draft.</p>", "Initial draft.");
        return (chapter.Guid, scene.Id);
    }

    private static async Task WaitForSceneAsync(HostedDesktop desktop, (string Chapter, string Id) scene, string text)
    {
        var ct = TestContext.Current.CancellationToken;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var content = await desktop.Rpc.InvokeAsync<SceneContentDto>("scenes/read", scene.Chapter, scene.Id);
            if (content.Html.Contains(text, StringComparison.Ordinal)) return;
            await Task.Delay(20, ct);
        }
        Assert.Fail("The shared scene did not arrive.");
    }

    private static async Task WaitForPresenceAsync(HostedDesktop desktop, Guid session, Guid item, Guid view)
    {
        var ct = TestContext.Current.CancellationToken;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var participants = await desktop.Rpc.InvokeAsync<ParticipantPresence[]>("collaboration/participants", session);
            if (participants.Any(participant => participant.ItemId == item && participant.ViewId == view && participant.Name.Length > 0)) return;
            await Task.Delay(20, ct);
        }
        Assert.Fail("The shared participant did not arrive.");
    }
}
