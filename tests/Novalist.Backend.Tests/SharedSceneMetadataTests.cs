using System.Text.Json;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sync.Client;
using Novalist.Sync.Client.Tests;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class SharedSceneMetadataTests
{
    [Fact]
    public async Task LeavingParallelNarrationClearsTheStrandOnlyAfterItsFocusIsReleased()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var desktop = new HostedDesktop(Path.Combine(directory.Path, "settings"), identity);
        await desktop.Rpc.InvokeAsync("project/create", directory.Path, "Shared", "Book");
        var chapter = Assert.Single((await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/createChapter", "Opening")).Chapters);
        await desktop.Rpc.InvokeAsync("project/createScene", chapter.Guid, "Arrival");
        await desktop.SignInAsync(server.Address);
        var session = await desktop.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        var scene = Assert.Single(Assert.Single((await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/getState")).Chapters).Scenes);
        var fields = Assert.IsType<SharedEntityFieldsDto>(scene.SharedFields).Fields;
        var modeFocus = Guid.NewGuid();
        var strandFocus = Guid.NewGuid();
        modeFocus = Guid.NewGuid();
        await desktop.Rpc.InvokeAsync("collaboration/acquireField", session.SessionId, fields["narrativeMode"], modeFocus);
        await desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, fields["narrativeMode"], modeFocus, "\"parallel\"");
        strandFocus = Guid.NewGuid();
        await desktop.Rpc.InvokeAsync("collaboration/acquireField", session.SessionId, fields["strand"], strandFocus);
        await desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, fields["strand"], strandFocus, "\"Mira\"");

        await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, fields["narrativeMode"], modeFocus, "\"linear\""));

        var retained = await desktop.Rpc.InvokeAsync<SharedFieldFocus>("collaboration/acquireField", session.SessionId, fields["narrativeMode"], modeFocus);
        Assert.Equal("\"parallel\"", retained.Value);
        await desktop.Rpc.InvokeAsync("collaboration/releaseField", session.SessionId, fields["strand"], strandFocus);
        await desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, fields["narrativeMode"], modeFocus, "\"linear\"");
        strandFocus = Guid.NewGuid();
        var cleared = await desktop.Rpc.InvokeAsync<SharedFieldFocus>("collaboration/acquireField", session.SessionId, fields["strand"], strandFocus);
        Assert.Equal("null", cleared.Value);
        await desktop.Rpc.InvokeAsync("collaboration/releaseField", session.SessionId, fields["strand"], strandFocus);
        await desktop.Rpc.InvokeAsync("collaboration/releaseField", session.SessionId, fields["narrativeMode"], modeFocus);
    }

    [Fact]
    public async Task ANewSharedSceneIncludesItsInitialPointOfViewBeforeItsFieldsAreBound()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var desktop = new HostedDesktop(Path.Combine(directory.Path, "settings"), identity);
        await desktop.Rpc.InvokeAsync("project/create", directory.Path, "Shared", "Book");
        var chapter = Assert.Single((await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/createChapter", "Opening")).Chapters);
        await desktop.SignInAsync(server.Address);
        await desktop.Rpc.InvokeAsync("collaboration/create", server.Address);

        var created = await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/createScene", chapter.Guid, "Arrival", null, "Mira");

        var scene = Assert.Single(Assert.Single(created.Chapters).Scenes);
        Assert.Equal("Mira", (await desktop.Rpc.InvokeAsync<SceneEditDto>("project/getSceneEdit", chapter.Guid, scene.Id)).Pov);
        var state = await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/getState");
        Assert.Contains("analysisOverrides.pov", Assert.IsType<SharedEntityFieldsDto>(Assert.Single(Assert.Single(state.Chapters).Scenes).SharedFields).Fields.Keys);
    }

    [Fact]
    public async Task ADateRangeMirrorsItsStartOnlyAfterBothDateFieldsCanBeLocked()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var desktop = new HostedDesktop(Path.Combine(directory.Path, "settings"), identity);
        await desktop.Rpc.InvokeAsync("project/create", directory.Path, "Shared", "Book");
        var chapter = Assert.Single((await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/createChapter", "Opening")).Chapters);
        await desktop.Rpc.InvokeAsync("project/createScene", chapter.Guid, "Arrival");
        await desktop.SignInAsync(server.Address);
        var session = await desktop.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        var scene = Assert.Single(Assert.Single((await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/getState")).Chapters).Scenes);
        var fields = Assert.IsType<SharedEntityFieldsDto>(scene.SharedFields).Fields;
        var rangeFocus = Guid.NewGuid();
        var dateFocus = Guid.NewGuid();
        rangeFocus = Guid.NewGuid();
        await desktop.Rpc.InvokeAsync("collaboration/acquireField", session.SessionId, fields["dateRange"], rangeFocus);
        dateFocus = Guid.NewGuid();
        await desktop.Rpc.InvokeAsync("collaboration/acquireField", session.SessionId, fields["date"], dateFocus);
        var range = JsonSerializer.Serialize(new { start = "2031-04-06", end = "2031-04-07", note = "Arrival" });

        await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, fields["dateRange"], rangeFocus, range));

        Assert.Equal("", (await desktop.Rpc.InvokeAsync<SceneEditDto>("project/getSceneEdit", chapter.Guid, scene.Id)).DateStart);
        await desktop.Rpc.InvokeAsync("collaboration/releaseField", session.SessionId, fields["date"], dateFocus);
        await desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, fields["dateRange"], rangeFocus, range);
        await desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, fields["dateRange"], rangeFocus, "null");
        await desktop.Rpc.InvokeAsync("collaboration/releaseField", session.SessionId, fields["dateRange"], rangeFocus);
        dateFocus = Guid.NewGuid();
        var mirrored = await desktop.Rpc.InvokeAsync<SharedFieldFocus>("collaboration/acquireField", session.SessionId, fields["date"], dateFocus);
        Assert.Equal("\"2031-04-06\"", mirrored.Value);
        Assert.Equal("", (await desktop.Rpc.InvokeAsync<SceneEditDto>("project/getSceneEdit", chapter.Guid, scene.Id)).DateStart);
        await desktop.Rpc.InvokeAsync("collaboration/releaseField", session.SessionId, fields["date"], dateFocus);
    }
}
