using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Internal main-process acknowledgements; renderer IDs cannot impersonate owner zero.</summary>
internal sealed class WorkspaceCoordinationRpc(WorkspaceCoordinator coordinator)
{
    [JsonRpcMethod("workspace/prepared")]
    public void Prepared(string token, bool saved, string? error = null) => coordinator.Prepared(token, saved, error);

    [JsonRpcMethod("workspace/applied")]
    public void Applied(string token) => coordinator.Applied(token);

    [JsonRpcMethod("workspace/clientClosed")]
    public void ClientClosed(string owner) => coordinator.ClientClosed(owner);

    [JsonRpcMethod("workspace/snapshot")]
    public object Snapshot() => coordinator.Snapshot();
}
