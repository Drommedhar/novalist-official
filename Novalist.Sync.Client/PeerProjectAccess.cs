using System.Security.Cryptography;
using NSec.Cryptography;

namespace Novalist.Sync.Client;

public sealed record PeerAddress(string Host, int Port, Guid SessionId, Guid ProjectId, uint Epoch, byte[] CertificateHash);

public sealed class PeerProjectAccess : IDisposable
{
    private readonly byte[] epochKey;
    private readonly byte[] certificate;
    private readonly ProjectMembership membership;
    private readonly Guid deviceId;
    private bool disposed;
    public PeerAddress Address { get; }
    public Guid ProjectId => Address.ProjectId;
    public uint Epoch => Address.Epoch;
    public bool IsOwner { get; }
    public byte[] Certificate => certificate.ToArray();

    internal PeerProjectAccess(PeerAddress address, PeerWelcome welcome, DeviceIdentity device)
    {
        ValidateAddress(address);
        if (welcome.OwnerKey is not { Length: 32 } || welcome.EpochKey is not { Length: 32 } || welcome.Certificate is null)
            throw new InvalidDataException("The peer project credentials are incomplete.");
        membership = new(address.ProjectId, address.Epoch,
            PublicKey.Import(SignatureAlgorithm.Ed25519, welcome.OwnerKey, KeyBlobFormat.RawPublicKey));
        var authorized = membership.VerifyCertificate(device.Id, welcome.Certificate);
        if (!authorized.SigningKey.Equals(device.SigningKey.PublicKey) || !authorized.ReceivingKey.Equals(device.ReceivingKey.PublicKey))
            throw new CryptographicException("The peer project certificate belongs to another device.");
        Address = address with { CertificateHash = address.CertificateHash.ToArray() };
        certificate = welcome.Certificate.ToArray();
        epochKey = welcome.EpochKey.ToArray();
        deviceId = device.Id;
        IsOwner = authorized.Role == ProjectRole.Owner;
    }

    internal static void ValidateAddress(PeerAddress address)
    {
        if (string.IsNullOrWhiteSpace(address.Host) || address.Host.Length > 253 ||
            Uri.CheckHostName(address.Host) == UriHostNameType.Unknown || address.Port is < 1 or > 65535 ||
            address.SessionId == Guid.Empty || address.ProjectId == Guid.Empty || address.CertificateHash is not { Length: 32 })
            throw new ArgumentException("The peer session address is invalid.", nameof(address));
    }

    public EnvelopeCipher CreateCipher()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return new(ProjectId, Epoch, epochKey);
    }

    public CertifiedSessionIdentity CreateSessionIdentity(DeviceIdentity device)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (device.Id != deviceId) throw new CryptographicException("The peer credentials belong to another device.");
        return new(membership, Certificate, device);
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(epochKey);
        disposed = true;
    }
}
