using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Novalist.Sync.Client;

namespace Novalist.Backend.Tests.TestHelpers;

internal sealed partial class HostedSessionServer : IAsyncDisposable
{
    private readonly WebApplication app;
    internal readonly object Gate = new();
    internal readonly Dictionary<Guid, List<SequencedUpdate>> Projects = [];
    internal readonly Dictionary<Guid, Guid> Invitations = [];
    internal readonly Dictionary<Guid, Guid> Devices = [];
    internal readonly Dictionary<string, Guid> Accounts = [];
    internal readonly Dictionary<(Guid, Guid), FieldLeaseGrant> Leases = [];
    internal readonly Dictionary<(Guid, Guid), HostedProjectDevice> ProjectDevices = [];
    internal Uri Address => new(app.Urls.Single());
    internal bool RejectJoins { get; set; }
    internal Func<FieldLeaseRequest, Task>? BeforeFieldPush { get; set; }
    internal Action? ProjectCreated { get; set; }

    private HostedSessionServer(Uri authority)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSingleton(this);
        builder.Services.AddSignalR(options => options.MaximumReceiveMessageSize = 4 * 1024 * 1024).AddMessagePackProtocol();
        app = builder.Build();
        app.MapGet("/client-configuration", () => new { protocolVersion = 1, authority = authority.AbsoluteUri, clientId = "novalist", scope = "openid profile sync offline_access" });
        app.MapGet("/devices/registration", (HttpContext context) => new { userId = Account(context), serverUrl = Address.AbsoluteUri });
        app.MapPut("/devices/verified", (HttpContext context, Registration registration) =>
        {
            lock (Gate) Devices[registration.Keys.DeviceId] = Account(context);
            return Results.Ok();
        });
        app.MapHub<SessionHub>("/sync");
    }

    internal static async Task<HostedSessionServer> StartAsync(Uri authority, CancellationToken ct)
    {
        var server = new HostedSessionServer(authority);
        try { await server.app.StartAsync(ct); return server; }
        catch { await server.DisposeAsync(); throw; }
    }

    private Guid Account(HttpContext context)
    {
        var token = context.Request.Headers.Authorization.ToString();
        lock (Gate)
        {
            if (!Accounts.TryGetValue(token, out var account)) Accounts[token] = account = Guid.NewGuid();
            return account;
        }
    }

    internal sealed record PublicKeys(Guid DeviceId, byte[] SigningPublicKey, byte[] ReceivingPublicKey);
    internal sealed record Registration(PublicKeys Keys, byte[] Signature);

    public async ValueTask DisposeAsync() => await app.DisposeAsync();

    public sealed partial class SessionHub(HostedSessionServer server) : Hub
    {
        private Guid Device => Guid.Parse(Context.GetHttpContext()?.Request.Query["deviceId"].ToString() ?? "");

        public HostedDeviceChallenge GetDeviceChallenge()
        {
            lock (server.Gate) return new(server.Devices[Device], Device, Guid.NewGuid(), server.Address.AbsoluteUri);
        }

        public void AuthenticateDevice(byte[] signature)
        {
            if (signature.Length != 64) throw new HubException("Invalid proof.");
        }

        public void CreateProject(Guid project)
        {
            lock (server.Gate)
            {
                server.Projects.TryAdd(project, []);
                server.Owners.TryAdd(project, server.Devices[Device]);
                server.Members.TryAdd(project, [server.Devices[Device]]);
            }
            server.ProjectCreated?.Invoke();
        }

        public HostedProjectEpoch GetProjectEpoch(Guid project)
        {
            lock (server.Gate) return new(server.Epochs.GetValueOrDefault(project), server.Projects[project].Count);
        }

        public void RegisterProjectDevice(Guid project, uint epoch, byte[] certificate)
        {
            lock (server.Gate) server.ProjectDevices[(project, Device)] = new(server.Devices[Device], Device, epoch, certificate);
        }

        public async Task JoinProject(Guid project)
        {
            lock (server.Gate)
                if (server.RejectJoins || !server.Projects.ContainsKey(project)) throw new HubException("Project unavailable.");
            await Groups.AddToGroupAsync(Context.ConnectionId, project.ToString("N"));
            await Groups.AddToGroupAsync(Context.ConnectionId, MemberGroup(project, server.Devices[Device]));
        }

        public async Task<long> Push(UpdateEnvelope envelope)
        {
            var header = UpdateHeader.Parse(envelope.Header);
            SequencedUpdate update;
            lock (server.Gate)
            {
                var history = server.Projects[header.ProjectId];
                var previous = history.FirstOrDefault(item => item.Envelope.Header.AsSpan().SequenceEqual(envelope.Header));
                if (previous is not null) return previous.ServerSeq;
                update = new(history.Count + 1, envelope);
                history.Add(update);
            }
            await Clients.Group(header.ProjectId.ToString("N")).SendAsync("ReceiveUpdate", update);
            return update.ServerSeq;
        }

        public async IAsyncEnumerable<SequencedUpdate> CatchUp(Guid project, long after, [EnumeratorCancellation] CancellationToken ct)
        {
            SequencedUpdate[] updates;
            lock (server.Gate) updates = server.Projects[project].Where(update => update.ServerSeq > after).ToArray();
            foreach (var update in updates) { ct.ThrowIfCancellationRequested(); yield return update; }
            await Task.CompletedTask;
        }

        public Task PublishPresence(UpdateEnvelope envelope)
        {
            var header = UpdateHeader.Parse(envelope.Header, EnvelopeKind.Presence);
            return Clients.Group(header.ProjectId.ToString("N")).SendAsync("ReceivePresence", envelope);
        }

        public void CreateInvitation(Guid project, HostedInvitationRequest request)
        {
            lock (server.Gate) server.Invitations.Add(request.InvitationId, project);
        }

        public HostedInvitationActivation ActivateInvitation(HostedInvitationActivationRequest request)
        {
            lock (server.Gate)
            {
                var project = server.Invitations[request.InvitationId];
                server.Members[project].Add(server.Devices[Device]);
                return new(project, Device, request.CertificateHash);
            }
        }

        public FieldLeaseGrant AcquireFieldLease(FieldLeaseRequest request)
        {
            lock (server.Gate)
            {
                var key = (request.ProjectId, request.ResourceId);
                var now = DateTimeOffset.UtcNow;
                if (server.Leases.TryGetValue(key, out var held) && held.ExpiresAt > now) return held with { ServerTime = now };
                return server.Leases[key] = new(Device, request.LeaseId, now.AddSeconds(30), now, server.Projects[request.ProjectId].Count);
            }
        }

        public FieldLeaseGrant? RenewFieldLease(FieldLeaseRequest request)
        {
            lock (server.Gate)
            {
                var key = (request.ProjectId, request.ResourceId);
                if (!server.Leases.TryGetValue(key, out var held) || held.DeviceId != Device || held.LeaseId != request.LeaseId) return null;
                var now = DateTimeOffset.UtcNow;
                return server.Leases[key] = held with { ServerTime = now, ExpiresAt = now.AddSeconds(30) };
            }
        }

        public bool ReleaseFieldLease(FieldLeaseRequest request)
        {
            lock (server.Gate)
            {
                var key = (request.ProjectId, request.ResourceId);
                return server.Leases.TryGetValue(key, out var held) && held.DeviceId == Device && held.LeaseId == request.LeaseId && server.Leases.Remove(key);
            }
        }

        public async Task<long> PushFieldEdit(FieldLeaseRequest request, UpdateEnvelope envelope)
        {
            if (server.BeforeFieldPush is { } before) await before(request);
            Task<long> pushed;
            lock (server.Gate)
            {
                if (!server.Leases.TryGetValue((request.ProjectId, request.ResourceId), out var held) || held.DeviceId != Device || held.LeaseId != request.LeaseId)
                    throw new HubException("Editing lease expired.");
                if (request.RequiredLease is { } required && (!server.Leases.TryGetValue((request.ProjectId, required.ResourceId), out var guard) ||
                    guard.DeviceId != Device || guard.LeaseId != required.LeaseId || guard.ExpiresAt <= DateTimeOffset.UtcNow))
                    throw new HubException("Required editing lease expired.");
                pushed = Push(envelope);
            }
            return await pushed;
        }

        public Task<long> PushGuardedFieldEdit(FieldLeaseRequest request, FieldLeaseRequest required, UpdateEnvelope envelope) =>
            PushFieldEdit(request with { RequiredLease = new(required.ResourceId, required.LeaseId) }, envelope);
    }
}
