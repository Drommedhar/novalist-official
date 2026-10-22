using System.Buffers.Binary;

namespace Novalist.Sync.Client;

// Wire format: QF0xB/novalist-sync-server, protocol v1, docs/design.md section 7.
public sealed record UpdateEnvelope(byte[] Header, byte[] Nonce, byte[] Ciphertext, byte[] Signature);

public sealed record SequencedUpdate(long ServerSeq, UpdateEnvelope Envelope);

public enum EnvelopeKind : byte { Update = 1, Presence = 3 }

public sealed record UpdateHeader(Guid ProjectId, Guid ItemId, uint Epoch, Guid SenderDeviceId, ulong SenderSeq)
{
    public const int Size = 62;

    public byte[] Encode(EnvelopeKind kind = EnvelopeKind.Update)
    {
        Validate();
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var bytes = new byte[Size];
        bytes[0] = 1;
        bytes[1] = (byte)kind;
        ProjectId.TryWriteBytes(bytes.AsSpan(2, 16), bigEndian: true, out _);
        ItemId.TryWriteBytes(bytes.AsSpan(18, 16), bigEndian: true, out _);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(34, 4), Epoch);
        SenderDeviceId.TryWriteBytes(bytes.AsSpan(38, 16), bigEndian: true, out _);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(54, 8), SenderSeq);
        return bytes;
    }

    public static UpdateHeader Parse(ReadOnlySpan<byte> bytes, EnvelopeKind kind = EnvelopeKind.Update)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (bytes.IsEmpty || bytes[0] != 1) throw new FormatException("Unsupported collaboration protocol.");
        if (bytes.Length != Size || bytes[1] != (byte)kind) throw new FormatException("Invalid message header.");
        var header = new UpdateHeader(
            new Guid(bytes.Slice(2, 16), bigEndian: true),
            new Guid(bytes.Slice(18, 16), bigEndian: true),
            BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(34, 4)),
            new Guid(bytes.Slice(38, 16), bigEndian: true),
            BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(54, 8)));
        header.Validate();
        return header;
    }

    private void Validate()
    {
        if (ProjectId == Guid.Empty || ItemId == Guid.Empty || SenderDeviceId == Guid.Empty || SenderSeq == 0 || SenderSeq > long.MaxValue)
            throw new FormatException("Invalid update identity or sequence.");
    }
}
