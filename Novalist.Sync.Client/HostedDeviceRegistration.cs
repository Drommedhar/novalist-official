using System.Buffers.Binary;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using NSec.Cryptography;

namespace Novalist.Sync.Client;

internal sealed class HostedDeviceRegistration(Uri server, DeviceIdentity device)
{
    public async Task<Guid> RegisterAsync(HttpClient http, string token, Guid expectedUser,
        DeviceIdentity? linkedBy, DeviceIdentity? recovery, CancellationToken ct)
    {
        var user = await DiscoverAsync(http, token, expectedUser, ct);
        await SendAsync(http, token, CreateRegistration(user, linkedBy, recovery), ct);
        return user;
    }

    public async Task EnrollAsync(HttpClient http, Func<CancellationToken, Task<string>> getToken, DeviceIdentity recovery,
        Func<string, CancellationToken, Task<string>> authenticate, CancellationToken ct)
    {
        var token = await getToken(ct);
        var user = await DiscoverAsync(http, token, Guid.Empty, ct);
        var registration = CreateRegistration(user, null, recovery);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(server, "devices/enrollment"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { registration.Keys, recoveryKeys = ExportKeys(recovery) });
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var challenge = await response.Content.ReadFromJsonAsync<EnrollmentChallenge>(ct);
        if (challenge is null || challenge.Id == Guid.Empty || string.IsNullOrEmpty(challenge.Nonce) || challenge.Nonce.Length > 256 ||
            challenge.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidDataException("The server returned an invalid enrollment challenge.");
        var identityToken = await authenticate(challenge.Nonce, ct);
        await SendAsync(http, await getToken(ct), registration with { EnrollmentProof = new(challenge.Id, identityToken) }, ct);
    }

    private Registration CreateRegistration(Guid user, DeviceIdentity? linkedBy, DeviceIdentity? recovery)
    {
        var keys = ExportKeys(device);
        var signature = SignatureAlgorithm.Ed25519.Sign(device.SigningKey, RegistrationProof(user, keys));
        var link = linkedBy is null ? null : LinkSignature(user, linkedBy, keys);
        var backup = recovery is null ? null : CreateRecoveryRegistration(user, recovery);
        return new(keys, signature, linkedBy?.Id, link, backup);
    }

    private async Task SendAsync(HttpClient http, string token, Registration registration, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(server, "devices/verified"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(registration);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<Guid> DiscoverAsync(HttpClient http, string token, Guid expectedUser, CancellationToken ct)
    {
        using var discovery = new HttpRequestMessage(HttpMethod.Get, new Uri(server, "devices/registration"));
        discovery.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(discovery, ct);
        response.EnsureSuccessStatusCode();
        var registration = await response.Content.ReadFromJsonAsync<RegistrationContext>(ct);
        if (registration is null || registration.ServerUrl != server.AbsoluteUri || registration.UserId == Guid.Empty ||
            (expectedUser != Guid.Empty && registration.UserId != expectedUser))
            throw new CryptographicException("The server returned a different device account.");
        return registration.UserId;
    }

    private RecoveryRegistration CreateRecoveryRegistration(Guid user, DeviceIdentity recovery)
    {
        var keys = ExportKeys(recovery);
        return new(keys, SignatureAlgorithm.Ed25519.Sign(recovery.SigningKey, RegistrationProof(user, keys)), LinkSignature(user, device, keys));
    }

    private byte[] RegistrationProof(Guid user, PublicKeys keys) =>
        [.. "novalist/v1/device-register"u8, .. EncodeServerUrl(server), .. user.ToByteArray(bigEndian: true), .. keys.DeviceId.ToByteArray(bigEndian: true),
            .. keys.SigningPublicKey, .. keys.ReceivingPublicKey];

    private byte[] LinkSignature(Guid user, DeviceIdentity linkedBy, PublicKeys keys) =>
        SignatureAlgorithm.Ed25519.Sign(linkedBy.SigningKey,
            [.. "novalist/v1/device-link"u8, .. linkedBy.Id.ToByteArray(bigEndian: true), .. RegistrationProof(user, keys)]);

    internal static byte[] EncodeServerUrl(Uri server)
    {
        var value = Encoding.UTF8.GetBytes(server.AbsoluteUri);
        var encoded = new byte[2 + value.Length];
        BinaryPrimitives.WriteUInt16BigEndian(encoded, checked((ushort)value.Length));
        value.CopyTo(encoded, 2);
        return encoded;
    }

    private static PublicKeys ExportKeys(DeviceIdentity identity) => new(identity.Id,
        identity.SigningKey.PublicKey.Export(KeyBlobFormat.RawPublicKey), identity.ReceivingKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));

    private sealed record RegistrationContext(Guid UserId, string ServerUrl);
    private sealed record PublicKeys(Guid DeviceId, byte[] SigningPublicKey, byte[] ReceivingPublicKey);
    private sealed record RecoveryRegistration(PublicKeys Keys, byte[] Signature, byte[] LinkingSignature);
    private sealed record Registration(PublicKeys Keys, byte[] Signature, Guid? LinkedByDeviceId, byte[]? LinkingSignature,
        RecoveryRegistration? RecoveryDevice, EnrollmentProof? EnrollmentProof = null);
    private sealed record EnrollmentChallenge(Guid Id, string Nonce, DateTimeOffset ExpiresAt);
    private sealed record EnrollmentProof(Guid ChallengeId, string IdentityToken);
}
