#if NOVALIST_COLLABORATION
using Novalist.Sync.Client;

namespace Novalist.Backend;

internal sealed partial class PeerProjectWorkspace
{
    internal IReadOnlyList<HostedProjectMember> Members()
    {
        var runtime = RequireOwner();
        var names = runtime.Session.Presence.Snapshot().GroupBy(item => item.DeviceId)
            .ToDictionary(group => group.Key, group => group.First().Name);
        return runtime.RequireHost().Members().Select(id => new HostedProjectMember(id,
            id == runtime.Device.Id ? runtime.Name : names.GetValueOrDefault(id) ?? id.ToString("N")[..8],
            id == runtime.Device.Id ? "Owner" : "Contributor")).ToArray();
    }

    internal void RemoveMember(Guid member) => RequireOwner().RequireHost().RemoveMember(member);

    internal Task DeleteSharedProjectAsync()
    {
        RequireOwner();
        return workspace.DetachSharedScenesAsync();
    }

    private Runtime RequireOwner()
    {
        var runtime = current ?? throw new InvalidOperationException("Open the shared project first.");
        if (runtime.Host is null) throw new UnauthorizedAccessException("Only the host can manage session access.");
        return runtime;
    }
}
#endif
