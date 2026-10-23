using System.Text.Json;
using Duende.IdentityModel.OidcClient;

namespace Novalist.Sync.Client;

public sealed class HostedAccount : IAsyncDisposable
{
    private string token;
    private string refresh;
    private DateTimeOffset expires;
    private readonly OidcClient client;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly HttpClient http;
    private readonly HostedSignInOptions settings;
    private readonly string nonce;
    private ICollaborationCredentials? credentials;
    private string credentialKey;
    private bool dirty;
    private readonly object lifecycle = new();
    private readonly CancellationTokenSource lifetime = new();
    private Task? closing;
    private bool forgotten;
    public Uri Authority => settings.Authority;
    public string Subject { get; }
    public string DisplayName { get; }

    internal HostedAccount(HostedAccountState state, string token, DateTimeOffset expires, OidcClient client,
        HttpClient http, (ICollaborationCredentials Store, string Key)? persistence = null)
    {
        settings = state.Settings ?? throw new InvalidDataException("The account has no identity provider.");
        Subject = state.Subject;
        DisplayName = state.DisplayName;
        nonce = state.Nonce;
        refresh = state.RefreshToken;
        this.token = token;
        this.expires = expires;
        this.client = client;
        this.http = http;
        credentials = persistence?.Store;
        credentialKey = persistence?.Key ?? string.Empty;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        using var operation = BeginOperation(ct);
        ct = operation.Token;
        await gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(forgotten, this);
            if (dirty) await PersistAsync(ct);
            if (expires <= DateTimeOffset.UtcNow.AddSeconds(30)) await RefreshAsync(ct);
            return token;
        }
        finally { gate.Release(); }
    }

    internal async Task RememberAsync(ICollaborationCredentials store, string key, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refresh)) throw new InvalidOperationException("This sign-in provider did not grant offline access. Enable refresh tokens for the desktop client.");
        credentials = store;
        credentialKey = key;
        await PersistAsync(ct);
    }

    internal async Task ForgetAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (credentials is not null) await credentials.RemoveAsync(credentialKey, ct);
            forgotten = true;
            dirty = false;
        }
        finally { gate.Release(); }
        await DisposeAsync();
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refresh)) throw new UnauthorizedAccessException("Your sign-in has expired. Please sign in again.");
        var result = await client.RefreshTokenAsync(refresh, cancellationToken: ct);
        if (result.Error == "invalid_grant")
            throw new UnauthorizedAccessException("Your sign-in has expired. Please sign in again.");
        if (result.IsError || string.IsNullOrEmpty(result.AccessToken) || result.ExpiresIn <= 0)
            throw new HttpRequestException("The sign-in provider could not renew your session. Please try again.");
        token = result.AccessToken;
        if (!string.IsNullOrEmpty(result.RefreshToken)) refresh = result.RefreshToken;
        expires = result.AccessTokenExpiration;
        using var saveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await PersistAsync(saveTimeout.Token);
    }

    private async Task PersistAsync(CancellationToken ct)
    {
        if (credentials is null) return;
        dirty = true;
        await credentials.WriteAsync(credentialKey, JsonSerializer.Serialize(new HostedAccountState
        {
            Settings = settings, Subject = Subject, DisplayName = DisplayName, Nonce = nonce, RefreshToken = refresh
        }), ct);
        dirty = false;
    }

    private CancellationTokenSource BeginOperation(CancellationToken ct)
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(closing is not null, this);
            return CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            if (closing is { IsFaulted: true } or { IsCanceled: true }) closing = null;
            return new(closing ??= CloseAsync());
        }
    }

    private async Task CloseAsync()
    {
        lifetime.Cancel();
        await gate.WaitAsync();
        try
        {
            if (dirty)
            {
                using var saveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await PersistAsync(saveTimeout.Token).ConfigureAwait(false);
            }
            token = string.Empty;
            refresh = string.Empty;
            http.Dispose();
            lifetime.Dispose();
        }
        finally { gate.Release(); }
    }
}
