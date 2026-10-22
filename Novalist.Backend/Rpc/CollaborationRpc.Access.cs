#if NOVALIST_COLLABORATION
using Novalist.Sync.Client;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class CollaborationRpc
{
    [JsonRpcMethod("collaboration/members")]
    public Task<IReadOnlyList<HostedProjectMember>> MembersAsync(CancellationToken cancellationToken = default) => Task.FromResult(Peers().Members());

    [JsonRpcMethod("collaboration/removeMember")]
    public Task RemoveMemberAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Peers().RemoveMember(memberId);
        return Task.CompletedTask;
    }

    [JsonRpcMethod("collaboration/deleteSharedProject")]
    public Task DeleteSharedProjectAsync(CancellationToken cancellationToken = default) => Peers().DeleteSharedProjectAsync();
}
#endif
