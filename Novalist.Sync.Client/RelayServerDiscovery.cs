using System.Net.Http.Json;

namespace Novalist.Sync.Client;

public sealed record RelayServerConfiguration(bool AuthenticationRequired, HostedSignInOptions? SignIn);

public static class RelayServerDiscovery
{
    public static async Task<RelayServerConfiguration> DiscoverAsync(Uri server, CancellationToken ct)
    {
        HostedSignIn.ValidateAddress(server);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 64 * 1024 };
        var endpoint = new Uri(new Uri(server.AbsoluteUri.TrimEnd('/') + '/'), "client-configuration");
        using var response = await http.GetAsync(endpoint, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException("This address does not provide Novalist desktop sign-in. Use the collaboration server address, not the sign-in provider address.");
        if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            throw new InvalidOperationException("This server has not enabled desktop sign-in.");
        response.EnsureSuccessStatusCode();
        var configuration = await response.Content.ReadFromJsonAsync<NativeConfiguration>(ct);
        if (configuration is not { ProtocolVersion: 1 })
            throw new InvalidDataException("The server returned an unsupported collaboration configuration.");
        if (configuration.AuthenticationRequired is false) return new(false, null);
        if (configuration is not { Authority: not null, ClientId: not null, Scope: not null })
            throw new InvalidDataException("The server returned an incomplete sign-in configuration.");
        var settings = new HostedSignInOptions(configuration.Authority, configuration.ClientId, configuration.Scope);
        HostedSignIn.Validate(settings);
        return new(true, settings);
    }

    private sealed record NativeConfiguration(byte ProtocolVersion, Uri? Authority, string? ClientId, string? Scope,
        bool? AuthenticationRequired);
}
