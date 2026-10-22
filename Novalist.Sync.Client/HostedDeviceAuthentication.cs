using System.Net.Http.Headers;
using System.Buffers.Binary;
using System.Text;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.SignalR.Client;
using NSec.Cryptography;

namespace Novalist.Sync.Client;

internal sealed class HostedDeviceAuthentication(DeviceIdentity device, Uri server)
{
    private Guid userId;

    public async Task RegisterAsync(HttpClient http, string token, CancellationToken ct)
    {
        using var discovery = new HttpRequestMessage(HttpMethod.Get, new Uri(server, "devices/registration"));
        discovery.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var discovered = await http.SendAsync(discovery, ct);
        discovered.EnsureSuccessStatusCode();
        var registration = await discovered.Content.ReadFromJsonAsync<RegistrationContext>(ct);
        if (registration is null || registration.ServerUrl != server.AbsoluteUri || registration.UserId == Guid.Empty ||
            (userId != Guid.Empty && registration.UserId != userId))
            throw new CryptographicException("The server returned a different device account.");
        var keys = new PublicKeys(device.Id, device.SigningKey.PublicKey.Export(KeyBlobFormat.RawPublicKey), device.ReceivingKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));
        byte[] encoded = [.. "novalist/v1/device-register"u8, .. EncodeServerUrl(), .. registration.UserId.ToByteArray(bigEndian: true), .. device.Id.ToByteArray(bigEndian: true),
            .. keys.SigningPublicKey, .. keys.ReceivingPublicKey];
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(server, "devices/verified"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { keys, signature = SignatureAlgorithm.Ed25519.Sign(device.SigningKey, encoded) });
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        userId = registration.UserId;
    }

    public async Task ProveAsync(HubConnection hub, CancellationToken ct)
    {
        var challenge = await hub.InvokeAsync<HostedDeviceChallenge>("GetDeviceChallenge", ct);
        if (challenge.ServerUrl != server.AbsoluteUri || challenge.UserId != userId || challenge.DeviceId != device.Id || challenge.Nonce == Guid.Empty)
            throw new CryptographicException("The device challenge belongs to another account or device.");
        byte[] encoded = [.. "novalist/v1/device-connect"u8, .. EncodeServerUrl(), .. userId.ToByteArray(bigEndian: true),
            .. device.Id.ToByteArray(bigEndian: true), .. challenge.Nonce.ToByteArray(bigEndian: true)];
        await hub.InvokeAsync("AuthenticateDevice", SignatureAlgorithm.Ed25519.Sign(device.SigningKey, encoded), ct);
    }

    private byte[] EncodeServerUrl()
    {
        var value = Encoding.UTF8.GetBytes(server.AbsoluteUri);
        var encoded = new byte[2 + value.Length];
        BinaryPrimitives.WriteUInt16BigEndian(encoded, checked((ushort)value.Length));
        value.CopyTo(encoded, 2);
        return encoded;
    }

    private sealed record RegistrationContext(Guid UserId, string ServerUrl);
    private sealed record PublicKeys(Guid DeviceId, byte[] SigningPublicKey, byte[] ReceivingPublicKey);
}
