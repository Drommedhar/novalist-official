using System.Security.Cryptography;
using NSec.Cryptography;

namespace Novalist.Sync.Client;

internal sealed record StoredProjectAccess(int Version, string Server, Guid ProjectId, uint Epoch, Guid DeviceId,
    byte[] OwnerKey, byte[] EpochKey, byte[] Certificate, Guid? InvitationId, CheckpointReference? Checkpoint, string? InvitationHash);

public sealed class HostedProjectAccess : IDisposable
{
    private bool disposed;
    internal StoredProjectAccess State { get; }
    private readonly ProjectMembership membership;
    public Uri Server { get; }
    public Guid ProjectId => State.ProjectId;
    public uint Epoch => State.Epoch;
    public byte[] Certificate => State.Certificate.ToArray();
    public CheckpointReference? Checkpoint => State.Checkpoint;
    public Guid? InvitationId => State.InvitationId;
    public bool IsOwner { get; }

    internal HostedProjectAccess(StoredProjectAccess state, DeviceIdentity device)
    {
        if (state.Version != 1 || state.ProjectId == Guid.Empty || state.DeviceId != device.Id ||
            state.OwnerKey is not { Length: 32 } || state.EpochKey is not { Length: 32 } || state.Certificate is null)
            throw new InvalidDataException("The saved project credentials are incomplete.");
        Server = CanonicalServer(new Uri(state.Server, UriKind.Absolute));
        var owner = PublicKey.Import(SignatureAlgorithm.Ed25519, state.OwnerKey, KeyBlobFormat.RawPublicKey);
        membership = new(state.ProjectId, state.Epoch, owner);
        var certificate = membership.VerifyCertificate(device.Id, state.Certificate);
        IsOwner = certificate.Role == ProjectRole.Owner;
        if (!certificate.SigningKey.Equals(device.SigningKey.PublicKey) || !certificate.ReceivingKey.Equals(device.ReceivingKey.PublicKey))
            throw new CryptographicException("The saved project certificate belongs to another device.");
        State = state;
    }

    public EnvelopeCipher CreateCipher()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return new(ProjectId, Epoch, State.EpochKey);
    }

    public CertifiedSessionIdentity CreateSessionIdentity(DeviceIdentity device)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (device.Id != State.DeviceId) throw new CryptographicException("The project credentials belong to another device.");
        return new(membership, Certificate, device);
    }

    internal static Uri CanonicalServer(Uri server)
    {
        HostedSignIn.ValidateAddress(server);
        return new Uri(server.AbsoluteUri.TrimEnd('/') + '/');
    }

    internal void CheckTransport(HostedTransport transport)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (transport.ConnectionIdentity != "hosted:" + Server.AbsoluteUri)
            throw new InvalidOperationException("Connect to this project's collaboration server.");
        if (!transport.AuthenticatesDevice(State.DeviceId))
            throw new InvalidOperationException("Connect with the device that owns these project credentials.");
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(State.EpochKey);
        disposed = true;
    }
}
