#if NOVALIST_COLLABORATION
using Novalist.Sync.Client;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class CollaborationRpc
{
    [JsonRpcMethod("collaboration/members")]
    public Task<IReadOnlyList<HostedProjectMember>> MembersAsync(CancellationToken cancellationToken = default) => Hosted().MembersAsync(cancellationToken);

    [JsonRpcMethod("collaboration/removeMember")]
    public Task RemoveMemberAsync(Guid memberId, CancellationToken cancellationToken = default) => Hosted().RemoveMemberAsync(memberId, cancellationToken);

    [JsonRpcMethod("collaboration/deleteSharedProject")]
    public Task DeleteSharedProjectAsync(CancellationToken cancellationToken = default) => Hosted().DeleteSharedProjectAsync(cancellationToken);
}
#endif
