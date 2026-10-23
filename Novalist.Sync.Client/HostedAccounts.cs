using System.Text.Json;

namespace Novalist.Sync.Client;

public interface ICollaborationCredentials
{
    Task<string?> ReadAsync(string key, CancellationToken ct);
    Task WriteAsync(string key, string value, CancellationToken ct);
    Task RemoveAsync(string key, CancellationToken ct);
}

public sealed class HostedAccounts : IAsyncDisposable
{
    private readonly ICollaborationCredentials credentials;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, HostedAccount> accounts = [];
    private readonly object lifecycle = new();
    private readonly CancellationTokenSource lifetime = new();
    private Task? closing;
    private readonly IHostedSignInBrowser? browser;

    public HostedAccounts(ICollaborationCredentials credentials, IHostedSignInBrowser? browser = null)
    {
        this.credentials = credentials;
        this.browser = browser;
    }

    public async Task<HostedAccount> SignInAsync(Uri server, Func<Uri, CancellationToken, Task> openBrowser, CancellationToken ct)
    {
        var key = AccountKey(server);
        using var operation = BeginOperation(ct);
        ct = operation.Token;
        await gate.WaitAsync(ct);
        try
        {
            if (accounts.ContainsKey(key)) throw new InvalidOperationException("Sign out of this server before signing in again.");
            var settings = await HostedSignIn.DiscoverAsync(server, ct);
            var account = browser is null ? await HostedSignIn.SignInAsync(settings, openBrowser, ct)
                : await HostedSignIn.SignInAsync(settings, browser, ct);
            try
            {
                await account.RememberAsync(credentials, key, ct);
                ct.ThrowIfCancellationRequested();
                accounts.Add(key, account);
                return account;
            }
            catch { await account.DisposeAsync(); throw; }
        }
        finally { gate.Release(); }
    }

    public async Task<HostedAccount?> RestoreAsync(Uri server, CancellationToken ct)
    {
        var key = AccountKey(server);
        using var operation = BeginOperation(ct);
        ct = operation.Token;
        await gate.WaitAsync(ct);
        try
        {
            if (accounts.TryGetValue(key, out var existing)) return existing;
            var saved = await credentials.ReadAsync(key, ct);
            ct.ThrowIfCancellationRequested();
            if (saved is null) return null;
            var state = JsonSerializer.Deserialize<HostedAccountState>(saved);
            if (state is not { Version: 1, Settings: not null } || string.IsNullOrWhiteSpace(state.Subject) ||
                string.IsNullOrEmpty(state.RefreshToken) || string.IsNullOrEmpty(state.Nonce))
                throw new InvalidDataException("The saved sign-in is incomplete. Please sign in again.");
            var account = HostedSignIn.Restore(state, credentials, key);
            accounts.Add(key, account);
            return account;
        }
        finally { gate.Release(); }
    }

    public async Task SignOutAsync(Uri server, CancellationToken ct)
    {
        var key = AccountKey(server);
        using var operation = BeginOperation(ct);
        ct = operation.Token;
        await gate.WaitAsync(ct);
        try
        {
            if (accounts.TryGetValue(key, out var account)) await account.ForgetAsync(ct);
            else await credentials.RemoveAsync(key, ct);
            accounts.Remove(key);
        }
        finally { gate.Release(); }
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
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var account in accounts.Values) await account.DisposeAsync().ConfigureAwait(false);
            accounts.Clear();
            lifetime.Dispose();
        }
        finally { gate.Release(); }
    }

    private static string AccountKey(Uri server)
    {
        HostedSignIn.ValidateAddress(server);
        return "hosted-account:" + server.AbsoluteUri.TrimEnd('/');
    }
}

internal sealed class HostedAccountState
{
    public int Version { get; init; } = 1;
    public HostedSignInOptions? Settings { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Nonce { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
}
