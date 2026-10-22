#if NOVALIST_COLLABORATION
using Novalist.Sync.Client;

namespace Novalist.Backend;

internal sealed partial class HostedProjectWorkspace
{
    internal Task<IReadOnlyList<HostedProjectMember>> MembersAsync(CancellationToken ct)
    {
        var runtime = RequireOwner();
        return runtime.Session.Transport.ListProjectMembersAsync(runtime.Access.ProjectId, ct);
    }

    internal Task RemoveMemberAsync(Guid member, CancellationToken ct)
    {
        var runtime = RequireOwner();
        return runtime.Session.Transport.RemoveProjectMemberAsync(runtime.Access.ProjectId, member, ct);
    }

    internal async Task DeleteSharedProjectAsync(CancellationToken ct)
    {
        var runtime = RequireOwner();
        await runtime.Session.Transport.DeleteProjectAsync(runtime.Access.ProjectId, ct);
        await workspace.DetachSharedScenesAsync();
    }

    private Runtime RequireOwner()
    {
        var runtime = current ?? throw new InvalidOperationException("Open the shared project first.");
        if (!runtime.Access.IsOwner) throw new UnauthorizedAccessException("Only the project owner can manage shared access.");
        return runtime;
    }
}
#endif
