using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace Novalist.Sync.Client;

internal sealed record PeerChallenge(Guid SessionId, byte[] Nonce);
internal sealed record PeerHello(Guid DeviceId, byte[] SigningKey, byte[] ReceivingKey, byte[] Signature,
    byte[]? Invitation, byte[]? Certificate);
internal sealed record PeerWelcome(byte[] OwnerKey, byte[] EpochKey, byte[] Certificate, string? Error = null);
internal sealed record PeerRequest(long Id, string Operation, Guid Project, long After = 0,
    UpdateEnvelope? Envelope = null, FieldLeaseRequest? Lease = null);
internal sealed record PeerMessage(long Id, string Type, string? Error = null, long Sequence = 0,
    SequencedUpdate? Update = null, IReadOnlyList<SequencedUpdate>? Updates = null,
    FieldLeaseGrant? Lease = null, bool Released = false, UpdateEnvelope? Presence = null);

internal static class PeerProtocol
{
    private const int MaxFrame = 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (bytes.Length > MaxFrame) throw new InvalidDataException("The peer message exceeds the frame limit.");
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(prefix, bytes.Length);
        await stream.WriteAsync(prefix, ct);
        await stream.WriteAsync(bytes, ct);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken ct)
    {
        var prefix = new byte[4];
        await stream.ReadExactlyAsync(prefix, ct);
        var size = BinaryPrimitives.ReadInt32BigEndian(prefix);
        if (size is <= 0 or > MaxFrame) throw new InvalidDataException("Invalid peer message size.");
        var bytes = new byte[size];
        await stream.ReadExactlyAsync(bytes, ct);
        return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvalidDataException("Missing peer message.");
    }

    public static byte[] Proof(PeerChallenge challenge, PeerHello hello) =>
        [.. "novalist/v1/peer-auth"u8, .. challenge.SessionId.ToByteArray(bigEndian: true), .. challenge.Nonce,
         .. hello.DeviceId.ToByteArray(bigEndian: true), .. hello.SigningKey, .. hello.ReceivingKey,
         .. SHA256.HashData(hello.Invitation ?? []), .. SHA256.HashData(hello.Certificate ?? [])];
}
