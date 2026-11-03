namespace Novalist.Sync.Client;

public interface IHostedSignInBrowser
{
    Uri RedirectUri { get; }
    Task<string> AuthenticateAsync(Uri authorizationUrl, CancellationToken ct);
}
