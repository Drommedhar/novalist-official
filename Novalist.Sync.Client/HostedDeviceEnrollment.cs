using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using NSec.Cryptography;

namespace Novalist.Sync.Client;

public static class HostedDeviceEnrollment
{
    public static Task EnrollAsync(Uri server, DeviceIdentity device, DeviceIdentity recovery, HostedAccount account,
        Func<Uri, CancellationToken, Task> openBrowser, CancellationToken ct) =>
        EnrollWithBrowserAsync(server, device, recovery, account,
            (nonce, token) => HostedSignIn.AuthenticateEnrollmentAsync(account, nonce, openBrowser, token), ct);

    public static Task EnrollAsync(Uri server, DeviceIdentity device, DeviceIdentity recovery, HostedAccount account,
        IHostedSignInBrowser browser, CancellationToken ct) =>
        EnrollWithBrowserAsync(server, device, recovery, account,
            (nonce, token) => HostedSignIn.AuthenticateEnrollmentAsync(account, nonce, browser, token), ct);

    private static async Task EnrollWithBrowserAsync(Uri server, DeviceIdentity device, DeviceIdentity recovery, HostedAccount account,
        Func<string, CancellationToken, Task<string>> authenticate, CancellationToken ct)
    {
        if (device.Id == recovery.Id) throw new ArgumentException("The recovery identity must differ from the ordinary device.", nameof(recovery));
        using var http = CreateHttpClient();
        await new HostedDeviceRegistration(CanonicalAddress(server), device)
            .EnrollAsync(http, account.GetAccessTokenAsync, recovery, authenticate, ct);
    }

    public static async Task EnrollAsync(Uri server, DeviceIdentity device, DeviceIdentity recovery,
        string deviceBoundMfaToken, CancellationToken ct)
    {
        if (device.Id == recovery.Id) throw new ArgumentException("The recovery identity must differ from the ordinary device.", nameof(recovery));
        using var http = CreateHttpClient();
        await new HostedDeviceRegistration(CanonicalAddress(server), device)
            .RegisterAsync(http, deviceBoundMfaToken, Guid.Empty, null, recovery, ct);
    }

    public static async Task LinkAsync(Uri server, DeviceIdentity authorizingDevice, DeviceIdentity newDevice,
        string accessToken, CancellationToken ct)
    {
        if (authorizingDevice.Id == newDevice.Id) throw new ArgumentException("Link a different device identity.", nameof(newDevice));
        using var http = CreateHttpClient();
        await new HostedDeviceRegistration(CanonicalAddress(server), newDevice)
            .RegisterAsync(http, accessToken, Guid.Empty, authorizingDevice, null, ct);
    }

    public static string SigningKeyThumbprint(DeviceIdentity device) =>
        SigningKeyThumbprint(device.SigningKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));

    internal static string SigningKeyThumbprint(ReadOnlySpan<byte> publicKey)
    {
        if (publicKey.Length != 32) throw new ArgumentException("An Ed25519 public key must contain 32 bytes.", nameof(publicKey));
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new { crv = "Ed25519", kty = "OKP", x = Base64UrlEncoder.Encode(publicKey.ToArray()) });
        return Base64UrlEncoder.Encode(SHA256.HashData(canonical));
    }

    private static Uri CanonicalAddress(Uri server)
    {
        HostedSignIn.ValidateAddress(server);
        return new(server.AbsoluteUri.TrimEnd('/') + '/');
    }

    private static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false })
        { MaxResponseContentBufferSize = 64 * 1024 };
}
