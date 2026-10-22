using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Novalist.Sync.Client;

public sealed record PeerInvitation(PeerAddress Address, byte[] Secret, DateTimeOffset ExpiresAt, CheckpointReference? Checkpoint = null)
{
    private sealed record Link(int Version, PeerInvitation Invitation);

    public string ToLink()
    {
        Validate();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Link(1, this));
        try { return "novalist://collaboration/peer#" + Base64UrlEncoder.Encode(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static PeerInvitation Parse(string link)
    {
        if (link.Length > 8192 || !Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != "novalist" ||
            uri.Host != "collaboration" || uri.AbsolutePath != "/peer" || uri.UserInfo.Length != 0 || uri.Port != -1 ||
            uri.Query.Length != 0 || uri.Fragment.Length < 2)
            throw new InvalidDataException("Use a complete Novalist peer invitation link.");
        var bytes = Base64UrlEncoder.DecodeBytes(uri.Fragment[1..]);
        try
        {
            var value = JsonSerializer.Deserialize<Link>(bytes, new JsonSerializerOptions { MaxDepth = 16 });
            if (value?.Version != 1 || value.Invitation is null) throw new InvalidDataException("Unsupported peer invitation.");
            value.Invitation.Validate();
            return value.Invitation;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private void Validate()
    {
        if (Address is null || Secret is not { Length: 32 } || ExpiresAt <= DateTimeOffset.UnixEpoch)
            throw new InvalidDataException("The peer invitation is incomplete.");
        PeerProjectAccess.ValidateAddress(Address);
        if (Checkpoint is { } checkpoint && (checkpoint.ManifestId == Guid.Empty || checkpoint.Sha256 is not { Length: 64 } || !checkpoint.Sha256.All(Uri.IsHexDigit)))
            throw new InvalidDataException("The peer invitation checkpoint is invalid.");
    }
}
