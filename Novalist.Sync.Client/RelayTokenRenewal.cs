using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;

namespace Novalist.Sync.Client;

internal static class RelayTokenRenewal
{
    public static async Task RunAsync(Uri server, ClientWebSocket socket, Func<CancellationToken, Task<string>>? token,
        Func<CancellationToken, Task> operation, CancellationToken ct)
    {
        if (token is null || socket.HttpResponseHeaders is not { } headers ||
            !headers.TryGetValue("X-Novalist-Relay-Connection", out var values))
        {
            await operation(ct);
            return;
        }
        if (!Guid.TryParse(values.Single(), out var connection) || connection == Guid.Empty)
            throw new InvalidDataException("The relay returned an invalid renewal identifier.");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var traffic = operation(lifetime.Token);
        var renewal = RenewAsync(new Uri(new Uri(server.AbsoluteUri.TrimEnd('/') + '/'),
            $"relay/v1/connections/{connection:N}/token"), token, lifetime.Token);
        try { await await Task.WhenAny(traffic, renewal); }
        finally
        {
            await lifetime.CancelAsync();
            try { await Task.WhenAll(traffic, renewal); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        }
    }

    private static async Task RenewAsync(Uri endpoint, Func<CancellationToken, Task<string>> token, CancellationToken ct)
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
        string? previous = null;
        while (true)
        {
            try
            {
                var current = await token(ct);
                if (current != previous)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", current);
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                        throw new UnauthorizedAccessException("Relay authorization ended. Please sign in again.");
                    response.EnsureSuccessStatusCode();
                    previous = current;
                }
            }
            catch (HttpRequestException) { /* The relay bounds how long authorization can remain unverified. */ }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            await Task.Delay(TimeSpan.FromSeconds(15), ct);
        }
    }
}
