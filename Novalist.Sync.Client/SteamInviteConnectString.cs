using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Novalist.Sync.Client;

public static class SteamInviteConnectString
{
    public const string Prefix = "novalist://collaboration/steam-invite#";
    private const int PayloadLength = 119;

    public static string Encode(SteamPeerInvitation invitation)
    {
        _ = SteamPeerInvitation.Parse(invitation.ToLink());
        if (invitation.Invitation.Checkpoint is not null)
            throw new InvalidDataException("Steam friend invitations require a live host.");
        using var stream = new MemoryStream(PayloadLength);
        using var writer = new BinaryWriter(stream);
        var peer = invitation.Invitation;
        writer.Write((byte)1);
        writer.Write(invitation.HostSteamId);
        writer.Write(peer.Address.SessionId.ToByteArray());
        writer.Write(peer.Address.ProjectId.ToByteArray());
        writer.Write(peer.Address.Epoch);
        writer.Write(peer.Address.CertificateHash);
        writer.Write(peer.Secret);
        writer.Write(peer.ExpiresAt.ToUnixTimeSeconds());
        writer.Write((ushort)peer.Address.Port);
        var bytes = stream.GetBuffer();
        try { return Prefix + Base64UrlEncoder.Encode(bytes, 0, (int)stream.Length); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static SteamPeerInvitation Decode(string connectString)
    {
        if (!connectString.StartsWith(Prefix, StringComparison.Ordinal) || connectString.Length >= 256)
            throw new InvalidDataException("Use a Novalist Steam friend invitation.");
        var bytes = Base64UrlEncoder.DecodeBytes(connectString[Prefix.Length..]);
        try
        {
            if (bytes.Length != PayloadLength || bytes[0] != 1)
                throw new InvalidDataException("Unsupported Steam friend invitation.");
            using var reader = new BinaryReader(new MemoryStream(bytes, writable: false));
            reader.ReadByte();
            var host = reader.ReadUInt64();
            var session = new Guid(reader.ReadBytes(16));
            var project = new Guid(reader.ReadBytes(16));
            var epoch = reader.ReadUInt32();
            var certificate = reader.ReadBytes(32);
            var secret = reader.ReadBytes(32);
            var expires = DateTimeOffset.FromUnixTimeSeconds(reader.ReadInt64());
            var port = reader.ReadUInt16();
            var invitation = new SteamPeerInvitation(host, new(new("127.0.0.1", port, session, project, epoch, certificate), secret, expires));
            return SteamPeerInvitation.Parse(invitation.ToLink());
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
