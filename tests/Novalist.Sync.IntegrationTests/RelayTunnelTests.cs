using System.Net;
using System.Net.WebSockets;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Novalist.Sync.Client;
using NSubstitute;
using Xunit;

namespace Novalist.Sync.IntegrationTests;

public sealed class RelayTunnelTests
{
    [Fact]
    public async Task ClosingOneGuestRouteDoesNotFailTheHostSession()
    {
        await using var server = new RelayServerFixture();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        await using var host = await RelayPeerTunnel.HostAsync(server.Address, ((IPEndPoint)listener.LocalEndpoint).Port,
            _ => Task.FromResult(server.Token("owner")), ct);
        using var guestSocket = new ClientWebSocket();
        guestSocket.Options.SetRequestHeader("Authorization", "Bearer " + server.Token("first-guest"));
        await guestSocket.ConnectAsync(new UriBuilder(server.Address)
            { Scheme = "ws", Path = $"/relay/v1/sessions/{host.SessionId}/guest" }.Uri, ct);
        using var firstAccepted = await listener.AcceptTcpClientAsync(ct);
        guestSocket.Abort();
        Assert.Equal(0, await firstAccepted.GetStream().ReadAsync(new byte[1], ct));
        await using var second = await RelayPeerTunnel.ConnectAsync(server.Address, host.SessionId,
            _ => Task.FromResult(server.Token("second-guest")), ct);
        using var secondClient = new TcpClient();
        await secondClient.ConnectAsync(IPAddress.Loopback, second.LocalPort, ct);
        using var secondAccepted = await listener.AcceptTcpClientAsync(ct);
        await secondAccepted.GetStream().WriteAsync(new byte[] { 42 }, ct);
        var response = new byte[1];
        await secondClient.GetStream().ReadExactlyAsync(response, ct);
        Assert.Equal(42, response[0]);
        Assert.Null(host.Failure);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistingPinnedTlsPeerProtocolRunsThroughRelayWithoutServerProjectState(bool requireAuthentication)
    {
        var folder = Path.Combine(Path.GetTempPath(), "novalist-relay-test", Guid.NewGuid().ToString("N"));
        try
        {
            await using var server = new RelayServerFixture(requireAuthentication);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            var ct = deadline.Token;
            var credentials = Substitute.For<ICollaborationCredentials>();
            credentials.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));
            var identities = new DeviceIdentityStore(credentials);
            using var owner = await identities.GetOrCreateAsync(server.Address, "owner", ct);
            using var writer = await identities.GetOrCreateAsync(server.Address, "writer", ct);
            await using var peerHost = await TcpPeerHost.StartAsync(new(IPAddress.Loopback, 0), folder,
                new(Guid.NewGuid(), 0, RandomNumberGenerator.GetBytes(32), owner), ct);
            var invitation = peerHost.CreateInvitation("127.0.0.1");
            await using var hostTunnel = await RelayPeerTunnel.HostAsync(server.Address, invitation.Address.Port,
                requireAuthentication ? _ => Task.FromResult(server.Token("owner")) : null, ct);
            await using var writerTunnel = await RelayPeerTunnel.ConnectAsync(server.Address, hostTunnel.SessionId,
                requireAuthentication ? _ => Task.FromResult(server.Token("writer")) : null, ct);
            var localInvitation = invitation with { Address = invitation.Address with { Port = writerTunnel.LocalPort } };
            await using var writerWire = await TcpPeerTransport.JoinInvitationAsync(localInvitation, writer, ct);
            using var ownerAccess = peerHost.CreateOwnerAccess("127.0.0.1");
            await using var ownerWire = new TcpPeerTransport(ownerAccess, owner);
            using var ownerCipher = ownerAccess.CreateCipher();
            var ownerIdentity = ownerAccess.CreateSessionIdentity(owner);
            using var writerCipher = writerWire.Access.CreateCipher();
            var writerIdentity = writerWire.Access.CreateSessionIdentity(writer);
            using var ownerSequence = new DeviceSequence(Path.Combine(folder, "owner-sequence.sqlite"), owner.Id);
            using var writerSequence = new DeviceSequence(Path.Combine(folder, "writer-sequence.sqlite"), writer.Id);
            using var ownerJournal = new SessionJournal(Path.Combine(folder, "owner.sqlite"), Guid.NewGuid(), ownerAccess.ProjectId, ownerSequence);
            using var writerJournal = new SessionJournal(Path.Combine(folder, "writer.sqlite"), Guid.NewGuid(), ownerAccess.ProjectId, writerSequence);
            await using var ownerSync = new CollaborationSession(new(ownerAccess.ProjectId, owner.Id, 0), ownerJournal, ownerCipher, ownerIdentity, ownerWire);
            await using var writerSync = new CollaborationSession(new(ownerAccess.ProjectId, writer.Id, 0), writerJournal, writerCipher, writerIdentity, writerWire);
            await ownerSync.SynchronizeAsync(true, ct);
            var item = Guid.NewGuid();
            ownerSync.Save(item, "Only peer endpoints read this"u8.ToArray());
            await ownerSync.SynchronizeAsync(false, ct);
            await writerSync.SynchronizeAsync(false, ct);
            Assert.Equal("Only peer endpoints read this"u8.ToArray(), Assert.Single(writerSync.ReadItem(item)));
            writerSync.Save(item, "A relayed edit"u8.ToArray());
            await writerSync.SynchronizeAsync(false, ct);
            await ownerSync.SynchronizeAsync(false, ct);
            Assert.Equal("A relayed edit"u8.ToArray(), ownerSync.ReadItem(item)[^1]);
            Assert.Null(hostTunnel.Failure);
            Assert.Null(writerTunnel.Failure);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task AGuestCannotAttachTheHostSideOfAnotherWritersRoute()
    {
        await using var server = new RelayServerFixture();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        using var control = new ClientWebSocket();
        control.Options.SetRequestHeader("Authorization", "Bearer " + server.Token("owner"));
        Uri Endpoint(string path) => new UriBuilder(server.Address) { Scheme = "ws", Path = path }.Uri;
        await control.ConnectAsync(Endpoint("/relay/v1/host"), ct);
        var buffer = new byte[64];
        var sessionMessage = await control.ReceiveAsync(buffer, ct);
        var session = Encoding.ASCII.GetString(buffer, 0, sessionMessage.Count);
        using var guest = new ClientWebSocket();
        guest.Options.SetRequestHeader("Authorization", "Bearer " + server.Token("guest"));
        var joining = guest.ConnectAsync(Endpoint($"/relay/v1/sessions/{session}/guest"), ct);
        var routeMessage = await control.ReceiveAsync(buffer, ct);
        var route = Encoding.ASCII.GetString(buffer, 0, routeMessage.Count);
        using var intruder = new ClientWebSocket();
        intruder.Options.SetRequestHeader("Authorization", "Bearer " + server.Token("guest"));
        await Assert.ThrowsAsync<WebSocketException>(() => intruder.ConnectAsync(Endpoint($"/relay/v1/sessions/{session}/routes/{route}"), ct));
        using var owner = new ClientWebSocket();
        owner.Options.SetRequestHeader("Authorization", "Bearer " + server.Token("owner"));
        await owner.ConnectAsync(Endpoint($"/relay/v1/sessions/{session}/routes/{route}"), ct);
        await joining;
        await guest.SendAsync(new byte[] { 1, 2, 3 }, WebSocketMessageType.Binary, true, ct);
        var received = await owner.ReceiveAsync(buffer, ct);
        Assert.Equal(new byte[] { 1, 2, 3 }, buffer[..received.Count]);
    }

    [Fact]
    public async Task HostDisconnectClosesExistingRoutesAndPreventsNewJoins()
    {
        await using var server = new RelayServerFixture();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var ct = deadline.Token;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        await using var host = await RelayPeerTunnel.HostAsync(server.Address, ((IPEndPoint)listener.LocalEndpoint).Port,
            _ => Task.FromResult(server.Token("owner")), ct);
        await using var guest = await RelayPeerTunnel.ConnectAsync(server.Address, host.SessionId,
            _ => Task.FromResult(server.Token("guest")), ct);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, guest.LocalPort, ct);
        using var accepted = await listener.AcceptTcpClientAsync(ct);
        await accepted.GetStream().WriteAsync(new byte[] { 42 }, ct);
        var buffer = new byte[1];
        await client.GetStream().ReadExactlyAsync(buffer, ct);
        await host.DisposeAsync();
        Assert.Equal(0, await client.GetStream().ReadAsync(buffer, ct));
        await Assert.ThrowsAsync<WebSocketException>(() => RelayPeerTunnel.ConnectAsync(server.Address, host.SessionId,
            _ => Task.FromResult(server.Token("late-guest")), ct));
    }

    [Fact]
    public async Task RelayRequiresAuthenticationAndAnOnlineHost()
    {
        await using var server = new RelayServerFixture();
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<WebSocketException>(() => RelayPeerTunnel.HostAsync(server.Address, 12345,
            _ => Task.FromResult("invalid"), ct));
        await Assert.ThrowsAsync<WebSocketException>(() => RelayPeerTunnel.ConnectAsync(server.Address, Guid.NewGuid(),
            _ => Task.FromResult(server.Token("guest")), ct));
        using var anonymous = server.CreateClient();
        foreach (var endpoint in new[] { "/relay/v1/host", $"/relay/v1/sessions/{Guid.NewGuid()}/guest",
            $"/relay/v1/sessions/{Guid.NewGuid()}/routes/{Guid.NewGuid()}" })
        {
            using var response = await anonymous.GetAsync(endpoint, ct);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task AuthenticatedGuestExchangesOpaqueBytesWithHostOverRealWebSockets()
    {
        await using var server = new RelayServerFixture();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var ct = deadline.Token;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        await using var host = await RelayPeerTunnel.HostAsync(server.Address, ((IPEndPoint)listener.LocalEndpoint).Port,
            _ => Task.FromResult(server.Token("owner")), ct);
        await using var guest = await RelayPeerTunnel.ConnectAsync(server.Address, host.SessionId,
            _ => Task.FromResult(server.Token("guest")), ct);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, guest.LocalPort, ct);
        using var accepted = await listener.AcceptTcpClientAsync(ct);
        var payload = RandomNumberGenerator.GetBytes(256 * 1024);
        var received = new byte[payload.Length];
        var writing = client.GetStream().WriteAsync(payload, ct).AsTask();
        await accepted.GetStream().ReadExactlyAsync(received, ct);
        await writing;
        Assert.Equal(payload, received);
        await accepted.GetStream().WriteAsync("reply"u8.ToArray(), ct);
        var reply = new byte[5];
        await client.GetStream().ReadExactlyAsync(reply, ct);
        Assert.Equal("reply"u8.ToArray(), reply);
        Assert.Null(host.Failure);
        Assert.Null(guest.Failure);
    }

    [Fact]
    public async Task RelayStartsWithoutDatabaseAndDoesNotExposeProjectStorage()
    {
        await using var server = new RelayServerFixture();
        var ct = TestContext.Current.CancellationToken;
        using var http = server.CreateClient();
        foreach (var endpoint in new[] { "/sync/negotiate", "/projects/" + Guid.NewGuid() + "/invitations", "/invitations/redeem" })
        {
            using var response = await http.PostAsync(endpoint, null, ct);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        using var host = new ClientWebSocket();
        host.Options.SetRequestHeader("Authorization", "Bearer " + server.Token("owner"));
        await host.ConnectAsync(new UriBuilder(server.Address) { Scheme = "ws", Path = "/relay/v1/host" }.Uri, ct);
        var buffer = new byte[64];
        var received = await host.ReceiveAsync(buffer, ct);
        Assert.True(Guid.TryParse(Encoding.ASCII.GetString(buffer, 0, received.Count), out _));
    }
}
