#if NOVALIST_COLLABORATION
using Novalist.Sync.Client;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed record HostedAccountDto(string Subject, string DisplayName);

public sealed class HostedAccountRpc : IAsyncDisposable
{
    private readonly HostedAccounts accounts;
    private readonly Func<Uri, CancellationToken, Task> openBrowser;
    private readonly Func<Uri, bool> isSharing;
    private readonly object gate = new();
    private CancellationTokenSource? signIn;

    public HostedAccountRpc(HostedAccounts accounts, Func<Uri, CancellationToken, Task> openBrowser, Func<Uri, bool>? isSharing = null)
    {
        this.accounts = accounts;
        this.openBrowser = openBrowser;
        this.isSharing = isSharing ?? (_ => false);
    }

    [JsonRpcMethod("collaboration/account/signIn")]
    public async Task<HostedAccountDto> SignInAsync(Uri server, CancellationToken cancellationToken = default)
    {
        CancellationTokenSource operation;
        lock (gate)
        {
            if (signIn is not null) throw new InvalidOperationException("A sign-in is already in progress.");
            signIn = operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }
        try { return View(await accounts.SignInAsync(server, openBrowser, operation.Token)); }
        finally
        {
            lock (gate) { signIn = null; operation.Dispose(); }
        }
    }

    [JsonRpcMethod("collaboration/account/status")]
    public async Task<HostedAccountDto?> StatusAsync(Uri server, CancellationToken cancellationToken = default)
    {
        var account = await accounts.RestoreAsync(server, cancellationToken);
        if (account is null) return null;
        try
        {
            if (!isSharing(server)) _ = await account.GetAccessTokenAsync(cancellationToken);
            return View(account);
        }
        catch (UnauthorizedAccessException)
        {
            await accounts.SignOutAsync(server, cancellationToken);
            return null;
        }
    }

    [JsonRpcMethod("collaboration/account/signOut")]
    public Task SignOutAsync(Uri server, CancellationToken cancellationToken = default)
    {
        if (isSharing(server)) throw new InvalidOperationException("End the collaboration session before signing out.");
        return accounts.SignOutAsync(server, cancellationToken);
    }

    [JsonRpcMethod("collaboration/account/cancel")]
    public void Cancel() { lock (gate) signIn?.Cancel(); }

    private static HostedAccountDto View(HostedAccount account) => new(account.Subject, account.DisplayName);

    public async ValueTask DisposeAsync()
    {
        Cancel();
        await accounts.DisposeAsync().ConfigureAwait(false);
    }
}
#endif
