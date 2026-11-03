using System.Security.Cryptography;
using System.Text.Json;
using NSec.Cryptography;

namespace Novalist.Sync.Client;

public sealed class DeviceIdentityStore(ICollaborationCredentials credentials)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<DeviceIdentity> GetOrCreateAsync(Uri authority, string subject, CancellationToken ct)
    {
        HostedSignIn.ValidateAddress(authority);
        if (string.IsNullOrWhiteSpace(subject)) throw new ArgumentException("An authenticated account is required.", nameof(subject));
        var key = "device:" + JsonSerializer.Serialize(new[] { authority.AbsoluteUri.TrimEnd('/'), subject });
        await gate.WaitAsync(ct);
        try
        {
            var saved = await credentials.ReadAsync(key, ct);
            if (saved is not null) return Load(saved);
            using var signing = Key.Create(SignatureAlgorithm.Ed25519, new() { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
            using var receiving = Key.Create(KeyAgreementAlgorithm.X25519, new() { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
            var state = new StoredDevice
            {
                Id = DeviceIdentity.IdForKeys(signing.PublicKey, receiving.PublicKey),
                Signing = signing.Export(KeyBlobFormat.RawPrivateKey), Receiving = receiving.Export(KeyBlobFormat.RawPrivateKey)
            };
            try
            {
                await credentials.WriteAsync(key, JsonSerializer.Serialize(state), ct);
                return Import(state);
            }
            finally { Clear(state); }
        }
        finally { gate.Release(); }
    }

    private static DeviceIdentity Load(string saved)
    {
        var state = JsonSerializer.Deserialize<StoredDevice>(saved);
        if (state is null) throw new InvalidDataException("The saved device identity is missing.");
        try
        {
            if (state.Version != 2 || state.Id == Guid.Empty || state.Signing is not { Length: 32 } || state.Receiving is not { Length: 32 })
                throw new InvalidDataException("The saved device identity is incomplete. Recover this device before sharing.");
            return Import(state);
        }
        finally { Clear(state); }
    }

    private static DeviceIdentity Import(StoredDevice state)
    {
        var signing = Key.Import(SignatureAlgorithm.Ed25519, state.Signing, KeyBlobFormat.RawPrivateKey);
        try
        {
            var receiving = Key.Import(KeyAgreementAlgorithm.X25519, state.Receiving, KeyBlobFormat.RawPrivateKey);
            try
            {
                if (state.Id != DeviceIdentity.IdForKeys(signing.PublicKey, receiving.PublicKey))
                    throw new InvalidDataException("The saved device identity does not match its keys.");
                return new(state.Id, signing, receiving);
            }
            catch { receiving.Dispose(); throw; }
        }
        catch { signing.Dispose(); throw; }
    }

    private static void Clear(StoredDevice state)
    {
        if (state.Signing is not null) CryptographicOperations.ZeroMemory(state.Signing);
        if (state.Receiving is not null) CryptographicOperations.ZeroMemory(state.Receiving);
    }

    private sealed class StoredDevice
    {
        public int Version { get; init; } = 2;
        public Guid Id { get; init; }
        public byte[] Signing { get; init; } = [];
        public byte[] Receiving { get; init; } = [];
    }
}

public sealed class DeviceIdentity : IDisposable
{
    public Guid Id { get; }
    public Key SigningKey { get; }
    public Key ReceivingKey { get; }

    internal DeviceIdentity(Guid id, Key signingKey, Key receivingKey)
    {
        Id = id;
        SigningKey = signingKey;
        ReceivingKey = receivingKey;
    }

    internal static Guid IdForKeys(PublicKey signing, PublicKey receiving)
    {
        var hash = SHA256.HashData([.. "novalist/v1/device-id"u8,
            .. signing.Export(KeyBlobFormat.RawPublicKey), .. receiving.Export(KeyBlobFormat.RawPublicKey)]);
        hash[6] = (byte)((hash[6] & 0x0f) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }

    public void Dispose() { SigningKey.Dispose(); ReceivingKey.Dispose(); }
}
