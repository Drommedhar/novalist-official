#if NOVALIST_COLLABORATION
using System.Text.Json;
using Novalist.Sync.Client;

namespace Novalist.Backend;

public interface ICollaborationHost
{
    IHostedSignInBrowser SignInBrowser { get; }
    Task<JsonElement> RequestAsync(string method, object parameters, CancellationToken ct);
}
#endif
