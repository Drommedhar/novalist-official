using Novalist.Core.Services;
using Novalist.Sync.Client;
using Novalist.Sync.Client.Tests;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Sync.IntegrationTests;

public sealed partial class DesktopSharingTests
{
    [Fact]
    public async Task DesktopOwnerCanEndSharedAccessWhileBothParticipantsKeepTheirLocalProjects()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(Path.GetTempPath(), "novalist-desktop-access", Guid.NewGuid().ToString("N"));
        await using var server = new RelayServerFixture();
        await using var issuer = await TestIdentityProvider.StartAsync(ct);
        issuer.Subject = "owner-" + Guid.NewGuid();
        issuer.IssueAccessToken = () => server.Token(issuer.Subject);
        ConfigureDesktopSignIn(server, issuer.Authority);
        try
        {
            var project = new ProjectService(new FileService());
            await project.CreateProjectAsync(folder, "Shared novel", "Book");
            await using var owner = new DesktopParticipant(Path.Combine(folder, "owner-settings"), issuer);
            await owner.Rpc.InvokeWithParameterObjectAsync("project/open", new { path = project.ProjectRoot }, ct);
            await owner.Rpc.InvokeWithParameterObjectAsync("collaboration/account/signIn", new { server = server.Address }, ct);
            await owner.Rpc.InvokeWithParameterObjectAsync("collaboration/create", new { server = server.Address }, ct);
            var link = await owner.Rpc.InvokeWithParameterObjectAsync<string>("collaboration/invite", new { expiresAt = DateTimeOffset.UtcNow.AddHours(1) }, ct);
            issuer.Subject = "guest-" + Guid.NewGuid();
            await using var guest = new DesktopParticipant(Path.Combine(folder, "guest-settings"), issuer);
            await guest.Rpc.InvokeWithParameterObjectAsync("collaboration/account/signIn", new { server = server.Address }, ct);
            var destination = Path.Combine(folder, "Joined novel");
            await guest.Rpc.InvokeWithParameterObjectAsync("collaboration/join", new { server = server.Address, link, destination }, ct);
            await Assert.ThrowsAsync<RemoteInvocationException>(() => guest.Rpc.InvokeAsync("collaboration/members"));
            await Assert.ThrowsAsync<RemoteInvocationException>(() => guest.Rpc.InvokeAsync("collaboration/deleteSharedProject"));
            var members = await owner.Rpc.InvokeAsync<HostedProjectMember[]>("collaboration/members");
            var removed = Assert.Single(members, member => member.Role == "Contributor");
            await owner.Rpc.InvokeWithParameterObjectAsync("collaboration/removeMember", new { memberId = removed.UserId }, ct);
            var guestStatus = await guest.Rpc.InvokeAsync<Novalist.Backend.HostedSessionDto>("collaboration/status");
            for (var attempt = 0; guestStatus.Connection != "AccessEnded" && attempt < 50; attempt++)
            {
                await Task.Delay(20, ct);
                guestStatus = await guest.Rpc.InvokeAsync<Novalist.Backend.HostedSessionDto>("collaboration/status");
            }
            Assert.Equal("AccessEnded", guestStatus.Connection);
            await owner.Rpc.InvokeAsync("collaboration/deleteSharedProject");
            await guest.Rpc.InvokeAsync("collaboration/end");
            Assert.Null(await owner.Rpc.InvokeAsync<Novalist.Backend.HostedSessionDto?>("collaboration/status"));
            Assert.True(File.Exists(Path.Combine(project.ProjectRoot ?? throw new InvalidDataException("Missing retained project."), ".novalist", "project.json")));
            Assert.True(File.Exists(Path.Combine(destination, ".novalist", "project.json")));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
