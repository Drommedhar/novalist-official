using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Authentication;
using Microsoft.Maui.Storage;
using Novalist.Backend;
using Novalist.Sync.Client;

namespace Novalist.Mobile.Services;

public sealed class MobileCollaborationHost(Func<object, CancellationToken, Task<JsonElement>> documents) : ICollaborationHost
{
    public IHostedSignInBrowser SignInBrowser { get; } = new MobileSignInBrowser();

    public async Task<JsonElement> RequestAsync(string method, object parameters, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (method == "collaboration/documents") return await documents(parameters, ct);
        if (method != "collaboration/credentials") throw new InvalidOperationException("Unknown mobile collaboration operation.");
        var request = JsonSerializer.SerializeToElement(parameters);
        var key = request.GetProperty("key").GetString();
        if (string.IsNullOrEmpty(key) || key.Length > 512) throw new InvalidDataException("Invalid credential key.");
        key = "collaboration:" + key;
        string? result = null;
        switch (request.GetProperty("action").GetString())
        {
            case "read": result = await SecureStorage.Default.GetAsync(key); break;
            case "write":
                var value = request.GetProperty("value").GetString() ?? throw new InvalidDataException("Missing credential.");
                await SecureStorage.Default.SetAsync(key, value);
                break;
            case "remove": SecureStorage.Default.Remove(key); break;
            default: throw new InvalidDataException("Unknown credential operation.");
        }
        return JsonSerializer.SerializeToElement(result);
    }
}

internal sealed class MobileSignInBrowser : IHostedSignInBrowser
{
    public Uri RedirectUri { get; } = new("com.novalist.app:/oauth/callback");

    public async Task<string> AuthenticateAsync(Uri authorizationUrl, CancellationToken ct)
    {
        var result = await MainThread.InvokeOnMainThreadAsync(() => WebAuthenticator.Default.AuthenticateAsync(
            new WebAuthenticatorOptions { Url = authorizationUrl, CallbackUrl = RedirectUri }, ct));
        return RedirectUri.AbsoluteUri + "?" + string.Join("&", result.Properties.Select(pair =>
            Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
    }
}
