using System.Security.Cryptography;
using Novalist.Sync.Client;
using NSec.Cryptography;
using Xunit;

namespace Novalist.Sync.IntegrationTests;

public sealed class HostedPresenceTests(CommunityServerFixture server) : IClassFixture<CommunityServerFixture>
{
    [Fact]
    public async Task EncryptedPresenceIsDeliveredLive_AndCannotBeReadAsDocumentContent()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = Guid.NewGuid();
        var device = Guid.NewGuid();
        var ownerToken = server.Token(Guid.NewGuid().ToString());
        var guestToken = server.Token(Guid.NewGuid().ToString());
        await using var owner = new HostedTransport(server.Address, device, () => Task.FromResult(ownerToken));
        await using var guest = new HostedTransport(server.Address, Guid.NewGuid(), () => Task.FromResult(guestToken));
        await owner.ConnectAsync(ct);
        await owner.CreateProjectAsync(project, ct);
        var invitation = Guid.NewGuid();
        await owner.CreateInvitationAsync(project, invitation, DateTimeOffset.UtcNow.AddHours(1), ct);
        await guest.RedeemInvitationAsync(invitation, ct);
        await guest.ConnectAsync(ct);
        await guest.JoinAsync(project, ct);
        using var signing = Key.Create(SignatureAlgorithm.Ed25519);
        using var cipher = new EnvelopeCipher(project, 0, RandomNumberGenerator.GetBytes(32));
        var received = new TaskCompletionSource<UpdateEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        guest.PresenceReceived += envelope => { received.TrySetResult(envelope); return Task.CompletedTask; };
        var message = cipher.SealPresence(new(project, Guid.NewGuid(), 0, device, 1), "relative cursor"u8, signing);

        await owner.PublishPresenceAsync(message, ct);

        var live = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.Equal("relative cursor"u8.ToArray(), cipher.OpenPresence(live, signing.PublicKey));
        Assert.Throws<FormatException>(() => cipher.Open(live, signing.PublicKey));
        var history = new List<SequencedUpdate>();
        await foreach (var update in guest.CatchUpAsync(project, 0, ct)) history.Add(update);
        Assert.Empty(history);
    }
}
