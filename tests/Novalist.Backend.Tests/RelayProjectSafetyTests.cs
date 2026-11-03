using System.Text.Json.Nodes;
using System.Text.Json;
using Novalist.Backend.Rpc;
using Novalist.Sync.Client;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sync.Client.Tests;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class RelayProjectSafetyTests
{
    [Fact]
    public async Task AConnectionCannotBeUsedWithAnotherServerOrADifferentProjectAtTheSamePath()
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
        var wrongServer = new Uri(server.Address, "another/");
        var error = await Assert.ThrowsAsync<RemoteInvocationException>(() => guest.Rpc.InvokeAsync("collaboration/join", wrongServer, link, root));
        Assert.Contains("server named by this invitation", error.Message);
        Assert.False(Directory.Exists(root));
        await guest.Rpc.InvokeAsync("collaboration/join", server.Address, link, root);
        await guest.Rpc.InvokeAsync("collaboration/end");
        var path = Path.Combine(root, ".novalist", "project.json");
        var original = await File.ReadAllBytesAsync(path, ct);
        var replaced = JsonNode.Parse(original)!.AsObject();
        replaced["id"] = Guid.NewGuid().ToString();
        await File.WriteAllTextAsync(path, replaced.ToJsonString(), ct);
        error = await Assert.ThrowsAsync<RemoteInvocationException>(() => guest.Rpc.InvokeAsync("collaboration/resume"));
        Assert.Contains("different local project", error.Message);
        Assert.Equal(replaced.ToJsonString(), await File.ReadAllTextAsync(path, ct));
        await File.WriteAllBytesAsync(path, original, ct);
        Assert.NotNull(await guest.Rpc.InvokeAsync<SavedHostedProjectDto>("collaboration/saved"));
        Assert.Equal("Connected", (await guest.Rpc.InvokeAsync<HostedSessionDto>("collaboration/resume")).Connection);
    }
    [Fact]
    public async Task ANewInvitationRestoresAccessToCurrentResearchWithoutChangingTheRetainedCopy()
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
        var retainedRoot = directory.Combine("Retained copy");
        await guest.Rpc.InvokeAsync("collaboration/join", server.Address, link, retainedRoot);
        await guest.Rpc.InvokeAsync("research/save", null, "Before removal", "Note", "Earlier shared writing.", Array.Empty<string>(), Array.Empty<string>());
        using (var beforeTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            beforeTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            while ((await owner.Rpc.InvokeAsync<ResearchItemDto[]>("research/list")).Length == 0) await Task.Delay(20, beforeTimeout.Token);
        }
        var member = Assert.Single(await owner.Rpc.InvokeAsync<HostedProjectMember[]>("collaboration/members"), item => item.Role == "Contributor");
        link = await owner.Rpc.InvokeAsync<string>("collaboration/invite", DateTimeOffset.UtcNow.AddHours(1));
        identity.Subject = "remaining";
        await using var remaining = new HostedDesktop(directory.Combine("remaining-settings"), identity);
        await remaining.SignInAsync(server.Address);
        await remaining.Rpc.InvokeAsync("collaboration/join", server.Address, link, directory.Combine("Remaining copy"));
        await owner.Rpc.InvokeAsync("collaboration/removeMember", member.UserId);
        await remaining.Rpc.InvokeAsync("research/save", null, "After removal", "Note", "Still writing together.", Array.Empty<string>(), Array.Empty<string>());
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while ((await owner.Rpc.InvokeAsync<ResearchItemDto[]>("research/list")).Length < 2) await Task.Delay(20, timeout.Token);
        await guest.Rpc.InvokeAsync("collaboration/end");
        await guest.Rpc.InvokeAsync("research/save", null, "Private retained writing", "Note", "Keep this local draft.", Array.Empty<string>(), Array.Empty<string>());
        var metadataPath = Path.Combine(retainedRoot, ".novalist", "project.json");
        var retained = await File.ReadAllBytesAsync(metadataPath, ct);
        link = await owner.Rpc.InvokeAsync<string>("collaboration/invite", DateTimeOffset.UtcNow.AddHours(1));
        var joined = await guest.Rpc.InvokeAsync<HostedSessionDto>("collaboration/join", server.Address, link, directory.Combine("Rejoined"));
        Assert.Equal("Connected", joined.Connection);
        Assert.Contains(await guest.Rpc.InvokeAsync<ResearchItemDto[]>("research/list"), item => item.Title == "After removal");
        await guest.Rpc.InvokeAsync("research/save", null, "After new invitation", "Note", "Shared again.", Array.Empty<string>(), Array.Empty<string>());
        using var rejoinedTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        rejoinedTimeout.CancelAfter(TimeSpan.FromSeconds(5));
        while ((await owner.Rpc.InvokeAsync<ResearchItemDto[]>("research/list")).Length < 3) await Task.Delay(20, rejoinedTimeout.Token);
        Assert.Equal(retained, await File.ReadAllBytesAsync(metadataPath, ct));
    }

}
