using Microsoft.AspNetCore.SignalR.Client;

namespace Novalist.Sync.Client;

public sealed record HostedProjectMember(Guid UserId, string DisplayName, string Role);

public sealed partial class HostedTransport
{
    public Task<IReadOnlyList<HostedProjectMember>> ListProjectMembersAsync(Guid project, CancellationToken ct)
    {
        RequireVerifiedAccess();
        return hub.InvokeAsync<IReadOnlyList<HostedProjectMember>>("ListProjectMembers", project, ct);
    }

    public Task RemoveProjectMemberAsync(Guid project, Guid member, CancellationToken ct)
    {
        RequireVerifiedAccess();
        return hub.InvokeAsync("RemoveProjectMember", project, member, ct);
    }

    public Task DeleteProjectAsync(Guid project, CancellationToken ct)
    {
        RequireVerifiedAccess();
        return hub.InvokeAsync("DeleteProject", project, ct);
    }

    private void RequireVerifiedAccess()
    {
        if (authentication is null) throw new InvalidOperationException("Managing project access requires a verified device.");
    }
}
