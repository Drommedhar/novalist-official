#if NOVALIST_COLLABORATION
using System.Text.Json;
using System.Text.Json.Nodes;
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    private async Task ProjectMetadataFieldAsync(string root, SharedFieldBinding binding, string value)
    {
        await PrepareSharedProjectionPathAsync(binding.Path);
        var path = SharedProjectPath.Resolve(root, binding.Path);
        var previous = await FileService.ExistsAsync(path) ? await FileService.ReadTextAsync(path) : null;
        var entity = previous is null ? null : JsonNode.Parse(previous) as JsonObject;
        if (entity is null || SharedMetadataFields.Target(entity, binding) is null)
        {
            await WriteSharedFileRecoveryAsync(root, binding.ItemId.ToString("N"), binding.Path,
                JsonSerializer.SerializeToUtf8Bytes(new { binding.Key, binding.Metadata, Value = value }));
            return;
        }
        SharedMetadataFields.Set(entity, binding, SharedMetadataFields.ParseValue(binding, value));
        var next = entity.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        if (binding.Metadata?.Kind is "codex" or "override" or "section")
        {
            var id = entity["id"]?.GetValue<string>() ?? throw new InvalidDataException("The shared Codex identity is missing.");
            await FieldHistory(root, binding).RecordAsync(id, previous ?? "", next);
        }
        await FileService.WriteTextAsync(path, next);
        if (binding.Metadata?.Kind is "research" or "scene") await ReloadSharedWorkspaceAsync();
        if (Coordinator?.Notify is { } notify)
            await notify("collaboration/projectUpdated", new { sessionId = sharedSession, state = BuildState() });
    }

    internal SharedEntityFieldsDto? SharedSceneMetadata(string id)
    {
        if (Projects.ActiveDraftRoot is not { } draft || sharedRoot is not { } root) return null;
        var path = Path.GetRelativePath(root, Path.Combine(draft, "scenes.json")).Replace('\\', '/');
        return SharedMetadata("scene", id, path);
    }

    internal SharedEntityFieldsDto? SharedResearchMetadata(string id) => SharedMetadata("research", id, ".novalist/project.json");

    private SharedEntityFieldsDto? SharedMetadata(string kind, string id, string path)
    {
        if (sharedFields is null || Projects.ProjectRoot != sharedRoot) return null;
        var fields = fieldBindings.Values.Where(binding => binding.Path == path && binding.Metadata is { } target && target.Kind == kind && target.Identity == id)
            .ToDictionary(binding => binding.Key, binding => binding.ItemId, StringComparer.Ordinal);
        return fields.Count == 0 ? null : new(sharedSession, fields);
    }
}
#endif
