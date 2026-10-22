using Microsoft.AspNetCore.SignalR;
using Novalist.Sync.Client;

namespace Novalist.Backend.Tests.TestHelpers;

internal sealed partial class HostedSessionServer
{
    private readonly Dictionary<Guid, Guid> Owners = [];
    private readonly Dictionary<Guid, HashSet<Guid>> Members = [];
    private readonly Dictionary<Guid, uint> Epochs = [];
    private readonly Dictionary<Guid, Rotation> Rotations = [];
    private readonly Dictionary<(Guid Project, Guid Device), StoredHostedEpochGrant> Grants = [];
    private sealed record Rotation(BeginHostedEpoch Begin, Guid Grantor, List<SequencedUpdate> Updates, List<HostedEpochGrant> Grants);

    public sealed partial class SessionHub
    {
        public IReadOnlyList<HostedProjectMember> ListProjectMembers(Guid project)
        {
            lock (server.Gate)
            {
                RequireOwner(project);
                return server.Members[project].Select(user => new HostedProjectMember(user, user.ToString("N"),
                    server.Owners[project] == user ? "Owner" : "Contributor")).ToArray();
            }
        }

        public IReadOnlyList<HostedProjectDevice> ListProjectDevices(Guid project)
        {
            lock (server.Gate)
            {
                RequireOwner(project);
                return server.ProjectDevices.Where(pair => pair.Key.Item1 == project).Select(pair => pair.Value).ToArray();
            }
        }

        public async Task DeleteProject(Guid project)
        {
            lock (server.Gate)
            {
                RequireOwner(project);
                server.Members.Remove(project);
                server.Projects.Remove(project);
            }
            await Clients.Group(project.ToString("N")).SendAsync("ProjectAccessEnded", project);
        }

        public void BeginProjectEpoch(BeginHostedEpoch request)
        {
            lock (server.Gate)
            {
                RequireOwner(request.ProjectId);
                if (server.Epochs.GetValueOrDefault(request.ProjectId) != request.PreviousEpoch || server.Projects[request.ProjectId].Count != request.PreviousServerSeq)
                    throw new HubException("The project changed before key rotation.");
                server.Rotations.Add(request.RotationId, new(request, Device, [], []));
            }
        }

        public long StageProjectEpoch(Guid rotationId, UpdateEnvelope envelope)
        {
            lock (server.Gate)
            {
                var rotation = server.Rotations[rotationId];
                RequireOwner(rotation.Begin.ProjectId);
                var sequence = rotation.Begin.PreviousServerSeq + rotation.Updates.Count + 1;
                rotation.Updates.Add(new(sequence, envelope));
                return sequence;
            }
        }

        public void StageProjectEpochGrant(Guid rotationId, HostedEpochGrant grant)
        {
            lock (server.Gate) server.Rotations[rotationId].Grants.Add(grant);
        }

        public async Task CommitProjectEpoch(CommitHostedEpoch request)
        {
            Guid project;
            uint epoch;
            lock (server.Gate)
            {
                var rotation = server.Rotations[request.RotationId];
                project = rotation.Begin.ProjectId;
                RequireOwner(project);
                epoch = server.Epochs[project] = rotation.Begin.PreviousEpoch + 1;
                server.Projects[project].AddRange(rotation.Updates);
                server.Members[project].Remove(request.RemovedMemberId);
                foreach (var grant in rotation.Grants)
                {
                    server.Grants[(project, grant.DeviceId)] = new(epoch, rotation.Begin.PreviousServerSeq + 1, rotation.Grantor, grant.Payload);
                    var previous = server.ProjectDevices[(project, grant.DeviceId)];
                    server.ProjectDevices[(project, grant.DeviceId)] = previous with { Epoch = epoch, Certificate = grant.Certificate };
                }
                foreach (var removed in server.ProjectDevices.Where(pair => pair.Key.Item1 == project && pair.Value.UserId == request.RemovedMemberId).Select(pair => pair.Key).ToArray())
                    server.ProjectDevices.Remove(removed);
            }
            await Clients.Group(MemberGroup(project, request.RemovedMemberId)).SendAsync("ProjectAccessEnded", project);
            await Clients.Group(project.ToString("N")).SendAsync("ProjectEpochChanged", project, epoch);
        }

        public StoredHostedEpochGrant? GetProjectEpochGrant(Guid project, uint afterEpoch)
        {
            lock (server.Gate)
            {
                if (!server.Members.TryGetValue(project, out var members) || !members.Contains(server.Devices[Device])) throw new HubException("Shared access ended.");
                var grant = server.Grants.GetValueOrDefault((project, Device));
                return grant?.Epoch > afterEpoch ? grant : null;
            }
        }

        private void RequireOwner(Guid project)
        {
            if (server.Owners.GetValueOrDefault(project) != server.Devices[Device]) throw new HubException("Owner access required.");
        }

        private static string MemberGroup(Guid project, Guid user) => project.ToString("N") + ":" + user.ToString("N");
    }
}
