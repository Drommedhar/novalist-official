using System.Text.Json;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sync.Client;
using Novalist.Sync.Client.Tests;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class SharedCodexDeletionTests
{
    [Fact]
    public async Task AConfirmedFieldEditArrivingAfterDeletionIsPreservedWithoutRecreatingTheEntry()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var owner = new HostedDesktop(Path.Combine(directory.Path, "owner-settings"), identity);
        var project = await owner.Rpc.InvokeAsync<ProjectStateDto>("project/create", directory.Path, "Shared", "Book");
        var entry = await owner.Rpc.InvokeAsync<JsonElement>("entities/create", "character", "Writer");
        var id = entry.GetProperty("id").GetString();
        await owner.SignInAsync(server.Address);
        await owner.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        var invitation = await owner.Rpc.InvokeAsync<string>("collaboration/invite", DateTimeOffset.UtcNow.AddHours(1));
        identity.Subject = "guest";
        await using var guest = new HostedDesktop(Path.Combine(directory.Path, "guest-settings"), identity);
        await guest.SignInAsync(server.Address);
        var joined = await guest.Rpc.InvokeAsync<HostedSessionDto>("collaboration/join", server.Address, invitation, Path.Combine(directory.Path, "Joined"));
        entry = await guest.Rpc.InvokeAsync<JsonElement>("entities/get", "character", id);
        var field = entry.GetProperty("sharedFields").GetProperty("fields").GetProperty("age").GetGuid();
        var focus = Guid.NewGuid();
        await guest.Rpc.InvokeAsync<SharedFieldFocus>("collaboration/acquireField", joined.SessionId, field, focus);
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.BeforeFieldPush = request =>
        {
            if (request.ResourceId != field) return Task.CompletedTask;
            arrived.TrySetResult();
            return release.Task;
        };
        var write = guest.Rpc.InvokeAsync("collaboration/writeField", joined.SessionId, field, focus, "42 after deletion");
        try
        {
            await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            await owner.Rpc.InvokeAsync("entities/delete", "character", id, false);
        }
        finally { release.TrySetResult(); }
        await write;
        var recovery = Path.Combine(project.ProjectPath ?? throw new InvalidOperationException("Missing project."), "Recovered edits");
        for (var attempt = 0; attempt < 100 && (!Directory.Exists(recovery) || !Directory.GetFiles(recovery, "*", SearchOption.AllDirectories).Any(file => File.ReadAllText(file).Contains("42 after deletion", StringComparison.Ordinal))); attempt++)
            await Task.Delay(20, ct);
        Assert.Contains(Directory.GetFiles(recovery, "*", SearchOption.AllDirectories), file => File.ReadAllText(file).Contains("42 after deletion", StringComparison.Ordinal));
        Assert.Empty((await owner.Rpc.InvokeAsync<JsonElement>("entities/list", "character")).EnumerateArray());
    }
}
