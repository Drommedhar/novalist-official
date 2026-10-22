#if NOVALIST_COLLABORATION
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Novalist.Sync.Client;

namespace Novalist.Backend;

internal sealed partial class PeerProjectWorkspace
{
    private sealed record SavedConnection(string ProjectIdentity, string Link);

    internal async Task<SavedHostedProjectDto?> SavedAsync()
    {
        if (workspace.Projects.ProjectRoot is not { } root) return null;
        var saved = await SavedConnectionAsync(root, CancellationToken.None);
        if (saved is null) return null;
        var invitation = RelayPeerInvitation.Parse(saved.Link);
        return new(invitation.Server.AbsoluteUri, invitation.Invitation.Address.ProjectId, null);
    }

    internal async Task<HostedSessionDto> ResumeAsync(CancellationToken ct)
    {
        workspace.RequireNoSharedSession();
        var root = workspace.Projects.ProjectRoot ?? throw new InvalidOperationException("Open a local project first.");
        var saved = await SavedConnectionAsync(root, ct)
            ?? throw new InvalidOperationException("This copy has no live host connection. Ask the host to start a session and send a new invitation.");
        var invitation = RelayPeerInvitation.Parse(saved.Link);
        return await JoinAsync(invitation.Server, saved.Link, root, ct);
    }

    private Task RememberAsync(RelayPeerInvitation invitation, string root, CancellationToken ct) =>
        credentials.WriteAsync(ConnectionKey(root), JsonSerializer.Serialize(new SavedConnection(ProjectIdentity(root), invitation.ToLink())), ct);

    private async Task<SavedConnection?> SavedConnectionAsync(string root, CancellationToken ct)
    {
        var json = await credentials.ReadAsync(ConnectionKey(root), ct);
        if (json is null) return null;
        var saved = JsonSerializer.Deserialize<SavedConnection>(json)
            ?? throw new InvalidDataException("The saved live host connection is incomplete.");
        if (saved.ProjectIdentity != ProjectIdentity(root))
            throw new InvalidDataException("The saved connection belongs to a different local project.");
        return saved;
    }

    private static string ProjectIdentity(string root)
    {
        using var metadata = JsonDocument.Parse(File.ReadAllBytes(SharedProjectPath.Resolve(root, ".novalist/project.json")));
        return metadata.RootElement.GetProperty("id").GetString() ?? throw new InvalidDataException("The project identity is missing.");
    }

    private static string ConnectionKey(string root)
    {
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (OperatingSystem.IsWindows()) canonical = canonical.ToUpperInvariant();
        return "relay-session:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
#endif
