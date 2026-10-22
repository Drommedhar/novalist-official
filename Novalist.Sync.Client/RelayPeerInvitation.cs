using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Novalist.Sync.Client;

public sealed record RelayPeerInvitation(Uri Server, Guid SessionId, PeerInvitation Invitation)
{
    private sealed record Link(int Version, Uri Server, Guid SessionId, string PeerLink);

    public string ToLink()
    {
        HostedSignIn.ValidateAddress(Server);
        if (SessionId == Guid.Empty) throw new InvalidDataException("The relay session identity is missing.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Link(1, Server, SessionId, Invitation.ToLink()));
        try { return "novalist://collaboration/relay#" + Base64UrlEncoder.Encode(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static RelayPeerInvitation Parse(string link, Uri? expectedServer = null)
    {
        if (link.Length > 16384 || !Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != "novalist" ||
            uri.Host != "collaboration" || uri.AbsolutePath != "/relay" || uri.UserInfo.Length != 0 || uri.Port != -1 ||
            uri.Query.Length != 0 || uri.Fragment.Length < 2)
            throw new InvalidDataException("Use a complete Novalist relay invitation link.");
        var bytes = Base64UrlEncoder.DecodeBytes(uri.Fragment[1..]);
        try
        {
            var value = JsonSerializer.Deserialize<Link>(bytes, new JsonSerializerOptions { MaxDepth = 4 });
            if (value?.Version != 1 || value.Server is null || value.SessionId == Guid.Empty || value.PeerLink is null)
                throw new InvalidDataException("Unsupported relay invitation.");
            HostedSignIn.ValidateAddress(value.Server);
            if (expectedServer is not null && value.Server.AbsoluteUri.TrimEnd('/') != expectedServer.AbsoluteUri.TrimEnd('/'))
                throw new InvalidDataException("The invitation belongs to a different relay server.");
            return new(value.Server, value.SessionId, PeerInvitation.Parse(value.PeerLink));
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
