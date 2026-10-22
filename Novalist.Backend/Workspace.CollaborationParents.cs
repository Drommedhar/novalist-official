#if NOVALIST_COLLABORATION
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    private sealed record SharedGraphLease(CollaborationSession Session, FieldLeaseRequest Request) : IAsyncDisposable
    {
        internal FieldLeaseCondition Condition => new(Request.ResourceId, Request.LeaseId);
        public async ValueTask DisposeAsync() => await Session.ReleaseFieldAsync(Request, CancellationToken.None);
    }

    private async Task<SharedGraphLease?> AcquireSharedGraphAsync(Guid sessionId, Guid item, Guid focus, CancellationToken ct)
    {
        var field = await RequireEditableSharedFieldAsync(sessionId, item, focus);
        var binding = fieldBindings[item];
        if (binding.Custom || !(binding.Metadata is null && binding.Key is "name" or "surname" or "parent" ||
            binding.Metadata?.Kind == "codex" && binding.Key == "isWorld")) return null;
        var context = field.Session.Context;
        var resource = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"novalist:location-graph:v1:{context.ProjectId:N}"))[..16]);
        var request = new FieldLeaseRequest(context.ProjectId, resource, context.Epoch, Guid.NewGuid());
        var grant = await field.Session.AcquireFieldAsync(request, ct);
        if (!grant.Acquired)
        {
            var participants = PeerProjects?.IsActive == true ? PeerProjects.Participants(sessionId) : HostedProjects?.Participants(sessionId);
            var writer = participants?.FirstOrDefault(participant => participant.DeviceId == grant.DeviceId)?.Name;
            throw new InvalidOperationException(writer is null
                ? "A name or relationship change is in progress. Retry after it finishes."
                : $"{writer} is changing names or relationships. Retry after their change finishes.");
        }
        var lease = new SharedGraphLease(field.Session, request);
        try
        {
            if (sharedProjectFiles is { } files) await files.RefreshAsync(ct);
            await field.Fields.RefreshAsync(ct);
            return lease;
        }
        catch { await lease.DisposeAsync(); throw; }
    }

    private async Task<string> NormalizeSharedParentAsync(SharedFieldBinding binding, string value)
    {
        if (binding.Custom || binding.Metadata is not null || binding.Key != "parent") return value;
        var places = await ReadSharedPlacesAsync(binding.Path);
        var child = places.Single(place => place.Id == Path.GetFileNameWithoutExtension(binding.Path));
        if (!PlaceHierarchy.CanReparent(places, child, value))
            throw new InvalidOperationException("That parent would create an invalid location hierarchy.");
        return value.Trim();
    }

    private async Task ValidateSharedRenameGraphAsync(SharedRename rename, string path)
    {
        var project = Projects.CurrentProject ?? throw new InvalidOperationException("The shared project is no longer open.");
        IEnumerable<string> scopes = rename.BookFolder is null ? project.Books.Select(book => book.FolderName + '/').DefaultIfEmpty(path) : [path];
        foreach (var scope in scopes)
        {
            var places = await ReadSharedPlacesAsync(scope);
            foreach (var place in places)
            {
                if (place.Id == rename.EntityId) place.Name = rename.Next;
                if (place.Parent == rename.Previous) place.Parent = rename.Next;
            }
            if (places.Any(place => !PlaceHierarchy.CanReparent(places, place, place.Parent)))
                throw new InvalidOperationException("That name would create an invalid location hierarchy.");
        }
    }

    private async Task<List<LocationData>> ReadSharedPlacesAsync(string entityPath)
    {
        var root = sharedRoot ?? throw new InvalidOperationException("The shared project is no longer open.");
        var project = Projects.CurrentProject ?? throw new InvalidOperationException("The shared project is no longer open.");
        var book = project.Books.SingleOrDefault(book => entityPath.StartsWith(book.FolderName + '/', StringComparison.OrdinalIgnoreCase)) ?? Projects.ActiveBook;
        var places = new List<LocationData>();
        foreach (var candidate in ActiveFieldBindings.Where(field => !field.Custom && field.Metadata is null && field.Key == "parent"))
        {
            if (!candidate.Path.StartsWith(book?.FolderName + '/', StringComparison.OrdinalIgnoreCase) &&
                !candidate.Path.StartsWith(project.WorldBibleFolder + '/', StringComparison.OrdinalIgnoreCase)) continue;
            var path = SharedProjectPath.Resolve(root, candidate.Path);
            if (!await FileService.ExistsAsync(path)) continue;
            places.Add(JsonSerializer.Deserialize<LocationData>(await FileService.ReadTextAsync(path))
                ?? throw new InvalidDataException("Invalid shared location."));
        }
        return places;
    }
}
#endif
