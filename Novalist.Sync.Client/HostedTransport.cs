using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Novalist.Sync.Client;

public sealed partial class HostedTransport : IUpdateTransport, IPresenceTransport, IFieldLeaseTransport
{
    private readonly HubConnection hub;
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false })
        { MaxResponseContentBufferSize = 64 * 1024 };
    private readonly Uri baseAddress;
    private readonly Uri registration;
    private readonly Func<Task<string>> accessToken;
    private readonly Guid deviceId;
    private readonly HostedDeviceAuthentication? authentication;
    private volatile bool ready;

    public event Func<SequencedUpdate, Task>? Received;
    public event Func<UpdateEnvelope, Task>? PresenceReceived;
    public event Action<Guid>? AccessEnded;
    public bool IsConnected => ready && hub.State == HubConnectionState.Connected;
    public string ConnectionIdentity { get; }
    internal bool AuthenticatesDevice(Guid expected) => authentication is not null && deviceId == expected;

    public HostedTransport(Uri server, DeviceIdentity device, Func<Task<string>> accessToken)
        : this(server, device.Id, accessToken) => authentication = new(device, baseAddress);

    public HostedTransport(Uri server, Guid deviceId, Func<Task<string>> accessToken)
    {
        if (!server.IsAbsoluteUri || (server.Scheme != "https" && !(server.Scheme == "http" && server.IsLoopback)) ||
            server.UserInfo.Length != 0 || server.Query.Length != 0 || server.Fragment.Length != 0)
            throw new ArgumentException("Use an HTTPS server URL, or HTTP on loopback for local tests.", nameof(server));
        if (deviceId == Guid.Empty) throw new ArgumentException("A device identity is required.", nameof(deviceId));
        this.accessToken = accessToken;
        this.deviceId = deviceId;
        baseAddress = new Uri(server.AbsoluteUri.TrimEnd('/') + '/');
        ConnectionIdentity = "hosted:" + baseAddress.AbsoluteUri;
        registration = new Uri(baseAddress, $"devices/{deviceId:D}");
        hub = new HubConnectionBuilder()
            .WithUrl(new Uri(baseAddress, $"sync?deviceId={deviceId:D}"), options =>
            {
                options.AccessTokenProvider = async () => await accessToken();
                options.HttpMessageHandlerFactory = handler =>
                {
                    if (handler is HttpClientHandler clientHandler) clientHandler.AllowAutoRedirect = false;
                    return handler;
                };
            })
            .AddMessagePackProtocol()
            .Build();
        hub.Closed += _ => { ready = false; return Task.CompletedTask; };
        hub.On<Guid>("ProjectAccessEnded", project => { ready = false; AccessEnded?.Invoke(project); });
        hub.On<Guid, uint>("ProjectEpochChanged", (project, epoch) => { ready = false; EpochChanged?.Invoke(project, epoch); });
        hub.On<SequencedUpdate>("ReceiveUpdate", async update =>
        {
            if (Received is { } handlers)
                foreach (var handler in handlers.GetInvocationList().Cast<Func<SequencedUpdate, Task>>())
                    await handler(update);
        });
        hub.On<UpdateEnvelope>("ReceivePresence", async envelope =>
        {
            if (PresenceReceived is { } handlers)
                foreach (var handler in handlers.GetInvocationList().Cast<Func<UpdateEnvelope, Task>>())
                    await handler(envelope);
        });
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (IsConnected) return;
        if (authentication is not null) await authentication.RegisterAsync(http, await accessToken(), cancellationToken);
        else
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, registration);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await accessToken());
            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        try
        {
            await hub.StartAsync(cancellationToken);
            if (authentication is not null) await authentication.ProveAsync(hub, cancellationToken);
            ready = true;
        }
        catch { await hub.StopAsync(CancellationToken.None); throw; }
    }

    public Task CreateProjectAsync(Guid project, CancellationToken cancellationToken) =>
        hub.InvokeAsync("CreateProject", project, cancellationToken);

    public async Task JoinAsync(Guid project, CancellationToken cancellationToken)
    {
        await hub.InvokeAsync("JoinProject", project, cancellationToken);
        await CheckProjectEpochAsync(project, cancellationToken);
    }

    public Task<long> PushAsync(UpdateEnvelope envelope, CancellationToken cancellationToken) =>
        hub.InvokeAsync<long>("Push", envelope, cancellationToken);

    public Task PublishPresenceAsync(UpdateEnvelope envelope, CancellationToken cancellationToken) =>
        hub.InvokeAsync("PublishPresence", envelope, cancellationToken);

    public IAsyncEnumerable<SequencedUpdate> CatchUpAsync(Guid project, long after, CancellationToken cancellationToken) =>
        hub.StreamAsync<SequencedUpdate>("CatchUp", project, after, cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        ready = false;
        return hub.StopAsync(cancellationToken);
    }

    public async Task CreateInvitationAsync(Guid project, Guid invitation, DateTimeOffset expiresAt, CancellationToken ct)
    {
        if (authentication is not null)
        {
            await hub.InvokeAsync("CreateInvitation", project, new HostedInvitationRequest(invitation, expiresAt), ct);
            return;
        }
        using var response = await PostAsync($"projects/{project:D}/invitations", new { invitationId = invitation, expiresAt }, ct);
    }

    public async Task<Guid> RedeemInvitationAsync(Guid invitation, CancellationToken ct)
    {
        if (authentication is not null) return ValidateGrant(await hub.InvokeAsync<HostedInvitationGrant>("RedeemInvitation", invitation, ct));
        using var response = await PostAsync("invitations/redeem", new { invitationId = invitation }, ct);
        var grant = await response.Content.ReadFromJsonAsync<HostedInvitationGrant>(ct);
        return ValidateGrant(grant);
    }

    public Task RevokeInvitationAsync(Guid project, Guid invitation, CancellationToken ct)
    {
        if (authentication is null) throw new InvalidOperationException("Invitation revocation requires a verified device.");
        return hub.InvokeAsync("RevokeInvitation", project, invitation, ct);
    }

    private static Guid ValidateGrant(HostedInvitationGrant? grant) => grant is { ProjectId: var project } && project != Guid.Empty
        ? project : throw new InvalidDataException("The server returned an invalid invitation grant.");

    public async Task<HostedInvitationActivation> ActivateInvitationAsync(Guid expectedProject, Guid invitation,
        byte[] certificateHash, CancellationToken ct)
    {
        if (authentication is null) throw new InvalidOperationException("Invitation activation requires a verified device.");
        if (expectedProject == Guid.Empty || invitation == Guid.Empty)
            throw new ArgumentException("A project and invitation identity are required.");
        if (certificateHash.Length != 32) throw new ArgumentException("A SHA-256 certificate hash is required.", nameof(certificateHash));
        var fingerprint = certificateHash.ToArray();
        var activation = await hub.InvokeAsync<HostedInvitationActivation>("ActivateInvitation",
            new HostedInvitationActivationRequest(invitation, fingerprint), ct);
        if (activation is null || activation.ProjectId != expectedProject || activation.DeviceId != deviceId ||
            activation.CertificateHash is null || !activation.CertificateHash.AsSpan().SequenceEqual(fingerprint))
            throw new InvalidDataException("The server returned an invalid invitation activation.");
        return activation;
    }

    private async Task<HttpResponseMessage> PostAsync<T>(string path, T body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await accessToken());
        request.Content = JsonContent.Create(body);
        var response = await http.SendAsync(request, ct);
        try { response.EnsureSuccessStatusCode(); return response; }
        catch { response.Dispose(); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        await hub.DisposeAsync();
        http.Dispose();
    }
}
