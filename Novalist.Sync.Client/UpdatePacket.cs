using System.Buffers.Binary;

namespace Novalist.Sync.Client;

internal sealed record UpdatePacket(Guid MessageId, int Length, int Index, int Capacity, byte[] Data)
{
    private const int HeaderSize = 25;
    private const int ChunkSize = EnvelopeCipher.MaxPayloadBytes - HeaderSize;
    private const int MaxMessageSize = 64 * 1024 * 1024;

    public static IReadOnlyList<byte[]> Split(byte[] update, int maxPacketBytes = EnvelopeCipher.MaxPayloadBytes)
    {
        if (update.Length == 0 || update.Length > MaxMessageSize) throw new ArgumentOutOfRangeException(nameof(update));
        if (maxPacketBytes <= HeaderSize + 4 || maxPacketBytes > EnvelopeCipher.MaxPayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(maxPacketBytes));
        var capacity = maxPacketBytes - HeaderSize - 4;
        var message = Guid.NewGuid();
        var packets = new List<byte[]>();
        for (var offset = 0; offset < update.Length; offset += capacity)
        {
            var count = Math.Min(capacity, update.Length - offset);
            var packet = new byte[HeaderSize + 4 + count];
            packet[0] = 2;
            message.TryWriteBytes(packet.AsSpan(1, 16), bigEndian: true, out _);
            BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(17, 4), update.Length);
            BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(21, 4), offset / capacity);
            BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(25, 4), capacity);
            update.AsSpan(offset, count).CopyTo(packet.AsSpan(HeaderSize + 4));
            packets.Add(packet);
        }
        return packets;
    }

    public static UpdatePacket Parse(byte[] bytes)
    {
        if (bytes.Length <= HeaderSize || bytes[0] is not (1 or 2)) throw new InvalidDataException("Unsupported collaboration packet.");
        var headerSize = bytes[0] == 1 ? HeaderSize : HeaderSize + 4;
        if (bytes.Length <= headerSize) throw new InvalidDataException("Incomplete collaboration packet.");
        var capacity = bytes[0] == 1 ? ChunkSize : BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(25, 4));
        if (capacity <= 0 || capacity > EnvelopeCipher.MaxPayloadBytes - headerSize)
            throw new InvalidDataException("Invalid collaboration packet capacity.");
        var id = new Guid(bytes.AsSpan(1, 16), bigEndian: true);
        var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(17, 4));
        var index = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(21, 4));
        if (id == Guid.Empty || length <= 0 || length > MaxMessageSize || index < 0 || index >= (length + capacity - 1) / capacity)
            throw new InvalidDataException("Invalid collaboration packet size or index.");
        if (bytes.Length - headerSize != Math.Min(capacity, length - index * capacity))
            throw new InvalidDataException("Incomplete collaboration packet.");
        return new(id, length, index, capacity, bytes[headerSize..]);
    }

    public static byte[]? Assemble(IEnumerable<UpdatePacket> packets)
    {
        var parts = new Dictionary<int, UpdatePacket>();
        UpdatePacket? first = null;
        foreach (var packet in packets)
        {
            first ??= packet;
            if (packet.MessageId != first.MessageId || packet.Length != first.Length || packet.Capacity != first.Capacity)
                throw new InvalidDataException("Inconsistent collaboration packet identity.");
            if (parts.TryGetValue(packet.Index, out var duplicate) && !duplicate.Data.AsSpan().SequenceEqual(packet.Data))
                throw new InvalidDataException("Conflicting collaboration packet.");
            parts[packet.Index] = packet;
        }
        if (first is null || parts.Count != (first.Length + first.Capacity - 1) / first.Capacity) return null;
        var update = new byte[first.Length];
        foreach (var part in parts.Values) part.Data.CopyTo(update, part.Index * first.Capacity);
        return update;
    }
}
