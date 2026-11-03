using System.Security.Cryptography;
using Microsoft.AspNetCore.SignalR.Client;
using NSec.Cryptography;

namespace Novalist.Sync.Client;

internal sealed class HostedDeviceAuthentication(DeviceIdentity device, Uri server)
{
    private Guid userId;

    public async Task RegisterAsync(HttpClient http, string token, CancellationToken ct) =>
        userId = await new HostedDeviceRegistration(server, device).RegisterAsync(http, token, userId, null, null, ct);

    public async Task ProveAsync(HubConnection hub, CancellationToken ct)
    {
        var challenge = await hub.InvokeAsync<HostedDeviceChallenge>("GetDeviceChallenge", ct);
        if (challenge.ServerUrl != server.AbsoluteUri || challenge.UserId != userId || challenge.DeviceId != device.Id || challenge.Nonce == Guid.Empty)
            throw new CryptographicException("The device challenge belongs to another account or device.");
        byte[] encoded = [.. "novalist/v1/device-connect"u8, .. HostedDeviceRegistration.EncodeServerUrl(server), .. userId.ToByteArray(bigEndian: true),
            .. device.Id.ToByteArray(bigEndian: true), .. challenge.Nonce.ToByteArray(bigEndian: true)];
        await hub.InvokeAsync("AuthenticateDevice", SignatureAlgorithm.Ed25519.Sign(device.SigningKey, encoded), ct);
    }
}
