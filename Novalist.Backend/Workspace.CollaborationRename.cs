#if NOVALIST_COLLABORATION
using System.Text.Json.Nodes;
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    internal async Task WriteSharedFieldAsync(Guid sessionId, Guid item, Guid focus, string value, CancellationToken ct)
    {
        await using var graph = await AcquireSharedGraphAsync(sessionId, item, focus, ct);
        await WriteSharedFieldCoreAsync(sessionId, item, focus, value, graph?.Condition, ct);
    }

    private async Task WriteSharedFieldCoreAsync(Guid sessionId, Guid item, Guid focus, string value, FieldLeaseCondition? required, CancellationToken ct)
    {
        if (await WriteSharedMetadataAsync(sessionId, item, focus, value, ct, required)) return;
        var field = await RequireEditableSharedFieldAsync(sessionId, item, focus);
        await field.Fields.ReadAsync(item, ct);
        value = await NormalizeSharedParentAsync(fieldBindings[item], value);
        var rename = await SharedRenameAsync(fieldBindings[item], value);
        if (rename is not null) await ValidateSharedRenameGraphAsync(rename, fieldBindings[item].Path);
        var references = new List<SharedRenameReference>();
        try
        {
            if (rename is not null) await AcquireRenameReferencesAsync(rename, references, ct);
            await field.Fields.WriteAsync(field.Lease with { RequiredLease = required }, value, ct);
            foreach (var reference in references)
                await field.Fields.WriteAsync(reference.Lease with { RequiredLease = required }, reference.Value, CancellationToken.None);
            if (rename is not null) await RenameSharedProseAsync(rename, CancellationToken.None);
        }
        finally
        {
            await Task.WhenAll(references.Select(reference => field.Session.ReleaseFieldAsync(reference.Lease, CancellationToken.None)));
        }
    }

    private sealed record SharedRename(string EntityId, string Previous, string Next, string? BookFolder);
    private sealed record SharedRenameReference(FieldLeaseRequest Lease, string Value);

    private async Task AcquireRenameReferencesAsync(SharedRename rename, List<SharedRenameReference> acquired, CancellationToken ct)
    {
        foreach (var binding in ActiveFieldBindings.Where(binding => IsRenameReference(binding) && RenameReferenceInScope(rename, binding.Path)))
        {
            if (!await FileService.ExistsAsync(SharedProjectPath.Resolve(sharedRoot ?? throw new InvalidOperationException("The shared project is closed."), binding.Path))) continue;
            var field = RequireSharedField(sharedSession, binding.ItemId, Guid.NewGuid());
            if (RenamedReferenceValue(binding, await field.Fields.ReadAsync(binding.ItemId, ct), rename) is null) continue;
            await RequireEditableSharedFieldAsync(sharedSession, binding.ItemId, field.Lease.LeaseId);
            var focused = await field.Fields.AcquireAsync(field.Lease, ct);
            if (!focused.Lease.Acquired)
            {
                var participants = PeerProjects?.IsActive == true ? PeerProjects.Participants(sharedSession) : HostedProjects?.Participants(sharedSession);
                var writer = participants?.FirstOrDefault(participant => participant.DeviceId == focused.Lease.DeviceId)?.Name;
                throw new InvalidOperationException(writer is null ? "A reference to this entry is being edited. Retry the rename after the field is released."
                    : $"{writer} is editing a reference to this entry. Retry the rename after the field is released.");
            }
            acquired.Add(new(field.Lease, RenamedReferenceValue(binding, focused.Value, rename) ?? focused.Value));
        }
    }

    private static bool IsRenameReference(SharedFieldBinding binding) => !binding.Custom &&
        (binding.Metadata is null && binding.Key == "parent" || binding.Metadata?.Kind is "codex" or "override" && binding.Key == "relationships" ||
         binding.Metadata?.Kind == "scene" && binding.Key == "analysisOverrides.pov");

    private static string? RenamedReferenceValue(SharedFieldBinding binding, string value, SharedRename rename)
    {
        if (binding.Metadata is null) return value == rename.Previous ? rename.Next : null;
        var node = SharedMetadataFields.ParseValue(binding, value);
        if (binding.Key == "analysisOverrides.pov")
            return node?.GetValue<string>() == rename.Previous ? JsonValue.Create(rename.Next).ToJsonString() : null;
        if (node is not JsonArray relationships) return null;
        var changed = false;
        foreach (var relationship in relationships.OfType<JsonObject>())
        {
            if (relationship["target"]?.GetValue<string>() != rename.Previous) continue;
            relationship["target"] = rename.Next;
            changed = true;
        }
        return changed ? relationships.ToJsonString() : null;
    }

    private async Task<SharedRename?> SharedRenameAsync(SharedFieldBinding binding, string value)
    {
        if (binding.Custom || binding.Metadata is not null || binding.Key is not ("name" or "surname")) return null;
        var root = sharedRoot ?? throw new InvalidOperationException("The shared project is no longer open.");
        var entity = JsonNode.Parse(await FileService.ReadTextAsync(SharedProjectPath.Resolve(root, binding.Path))) as JsonObject
            ?? throw new InvalidDataException("Invalid shared Codex entry.");
        var previous = Name(entity);
        entity[PropertyName(entity, binding.Key)] = value;
        var next = Name(entity);
        return string.IsNullOrWhiteSpace(previous) || string.IsNullOrWhiteSpace(next) || previous == next ? null
            : new(Path.GetFileNameWithoutExtension(binding.Path), previous, next,
                Projects.CurrentProject?.Books.SingleOrDefault(book => binding.Path.StartsWith(book.FolderName + '/', StringComparison.OrdinalIgnoreCase))?.FolderName);

        static string Name(JsonObject entity)
        {
            var first = entity[PropertyName(entity, "name")]?.GetValue<string>() ?? "";
            var surname = entity[PropertyName(entity, "surname")]?.GetValue<string>() ?? "";
            return surname.Length == 0 ? first : $"{first} {surname}";
        }
    }

    private async Task RenameSharedProseAsync(SharedRename rename, CancellationToken ct)
    {
        var scenes = RequireSharedScenes(sharedSession);
        var items = sharedSceneFiles.Keys.Concat(ActiveSharedProse.Where(binding => binding.TextField?.SectionId is not null && RenameReferenceInScope(rename, binding.Path))
            .Select(binding => binding.ItemId)).ToArray();
        foreach (var item in items)
            await scenes.RenameAsync(item, rename.EntityId, rename.Previous, rename.Next, ct);
    }

    private bool RenameReferenceInScope(SharedRename rename, string path)
    {
        var project = Projects.CurrentProject ?? throw new InvalidOperationException("The shared project is no longer open.");
        return rename.BookFolder is null || path.StartsWith(rename.BookFolder + '/', StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(project.WorldBibleFolder + '/', StringComparison.OrdinalIgnoreCase);
    }
}
#endif
