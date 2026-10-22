using System.Security.Cryptography;
using System.Net.Http.Json;
using Duende.IdentityModel.Client;
using Duende.IdentityModel.OidcClient;

namespace Novalist.Sync.Client;

public sealed record HostedSignInOptions(Uri Authority, string ClientId, string Scope);

public static class HostedSignIn
{
    public static async Task<HostedSignInOptions> DiscoverAsync(Uri server, CancellationToken ct)
    {
        ValidateAddress(server);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 64 * 1024 };
        var endpoint = new Uri(new Uri(server.AbsoluteUri.TrimEnd('/') + '/'), "client-configuration");
        using var response = await http.GetAsync(endpoint, ct);
        if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.ServiceUnavailable)
            throw new InvalidOperationException("This server has not enabled desktop sign-in.");
        response.EnsureSuccessStatusCode();
        var configuration = await response.Content.ReadFromJsonAsync<NativeConfiguration>(ct);
        if (configuration is not { ProtocolVersion: 1, Authority: not null, ClientId: not null, Scope: not null })
            throw new InvalidDataException("The server returned an unsupported sign-in configuration.");
        var settings = new HostedSignInOptions(configuration.Authority, configuration.ClientId, configuration.Scope);
        Validate(settings);
        return settings;
    }

    public static async Task<HostedAccount> SignInAsync(HostedSignInOptions settings,
        Func<Uri, CancellationToken, Task> openBrowser, CancellationToken ct)
    {
        Validate(settings);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        ct = timeout.Token;
        await using var callback = await LoopbackSignIn.StartAsync(ct);
        var http = CreateHttpClient();
        try
        {
            var nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            var options = Options(settings, nonce, null, http);
            options.RedirectUri = callback.Address.AbsoluteUri;
            var client = new OidcClient(options);
            var state = await client.PrepareLoginAsync(new Parameters { { "nonce", nonce } }, ct);
            callback.Expect(state.State);
            await openBrowser(new Uri(state.StartUrl), ct);
            var response = await callback.ReceiveAsync(ct);
            var login = await client.ProcessResponseAsync(response, state, cancellationToken: ct);
            if (login.IsError || string.IsNullOrEmpty(login.IdentityToken) || login.User.FindFirst("sub") is not { } subject)
                throw new InvalidOperationException("Sign-in could not be verified. Please sign in again.");
            options.IdentityTokenValidator = new HostedIdentityValidator(nonce, subject.Value);
            return new HostedAccount(new()
            {
                Settings = settings, Subject = subject.Value, DisplayName = login.User.FindFirst("name")?.Value ?? subject.Value,
                Nonce = nonce, RefreshToken = login.RefreshToken ?? string.Empty
            }, login.AccessToken, login.AccessTokenExpiration, client, http);
        }
        catch { http.Dispose(); throw; }
    }

    private static void Validate(HostedSignInOptions settings)
    {
        ValidateAddress(settings.Authority);
        if (string.IsNullOrWhiteSpace(settings.ClientId) || !settings.Scope.Split(' ').Contains("openid"))
            throw new ArgumentException("A native client ID and the openid scope are required.", nameof(settings));
    }

    internal static void ValidateAddress(Uri url)
    {
        if (!url.IsAbsoluteUri || (url.Scheme != "https" && !(url.Scheme == "http" && url.IsLoopback)) ||
            url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0)
            throw new ArgumentException("Use an HTTPS address, or HTTP on loopback for local tests.", nameof(url));
    }

    private sealed record NativeConfiguration(byte ProtocolVersion, Uri? Authority, string? ClientId, string? Scope);

    internal static HostedAccount Restore(HostedAccountState state, ICollaborationCredentials credentials, string key)
    {
        var settings = state.Settings ?? throw new InvalidDataException("The saved sign-in has no identity provider.");
        Validate(settings);
        var http = CreateHttpClient();
        var client = new OidcClient(Options(settings, state.Nonce, state.Subject, http));
        return new(state, string.Empty, DateTimeOffset.MinValue, client, http, (credentials, key));
    }

    private static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 1024 * 1024 };

    private static OidcClientOptions Options(HostedSignInOptions settings, string nonce, string? subject, HttpClient http) => new()
    {
        Authority = settings.Authority.AbsoluteUri.TrimEnd('/'), ClientId = settings.ClientId, Scope = settings.Scope,
        LoadProfile = false, IdentityTokenValidator = new HostedIdentityValidator(nonce, subject),
        HttpClientFactory = _ => http, Policy = new() { RequireIdentityTokenSignature = true }
    };
}
