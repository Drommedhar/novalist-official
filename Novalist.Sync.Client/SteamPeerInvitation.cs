using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Novalist.Sync.Client;

public sealed record SteamPeerInvitation(ulong HostSteamId, PeerInvitation Invitation)
{
    private sealed record Link(int Version, ulong HostSteamId, string PeerLink);

    public string ToLink()
    {
        if (HostSteamId == 0) throw new InvalidDataException("The Steam host identity is missing.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Link(1, HostSteamId, Invitation.ToLink()));
        try { return "novalist://collaboration/steam#" + Base64UrlEncoder.Encode(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static SteamPeerInvitation Parse(string link)
    {
        if (link.Length > 16384 || !Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != "novalist" ||
            uri.Host != "collaboration" || uri.AbsolutePath != "/steam" || uri.UserInfo.Length != 0 || uri.Port != -1 ||
            uri.Query.Length != 0 || uri.Fragment.Length < 2)
            throw new InvalidDataException("Use a complete Novalist Steam invitation link.");
        var bytes = Base64UrlEncoder.DecodeBytes(uri.Fragment[1..]);
        try
        {
            var value = JsonSerializer.Deserialize<Link>(bytes, new JsonSerializerOptions { MaxDepth = 4 });
            if (value?.Version != 1 || value.HostSteamId == 0 || value.PeerLink is null)
                throw new InvalidDataException("Unsupported Steam invitation.");
            return new(value.HostSteamId, PeerInvitation.Parse(value.PeerLink));
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
