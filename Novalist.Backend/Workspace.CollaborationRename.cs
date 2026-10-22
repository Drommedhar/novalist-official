#if NOVALIST_COLLABORATION
using System.Text.Json.Nodes;
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    internal async Task WriteSharedFieldAsync(Guid sessionId, Guid item, Guid focus, string value, CancellationToken ct)
    {
        if (await WriteSharedMetadataAsync(sessionId, item, focus, value, ct)) return;
        var field = await RequireEditableSharedFieldAsync(sessionId, item, focus);
        await field.Fields.ReadAsync(item, ct);
        var rename = await SharedRenameAsync(fieldBindings[item], value);
        await field.Fields.WriteAsync(field.Lease, value, ct);
        if (rename is not null) await RenameSharedProseAsync(rename, CancellationToken.None);
    }

    private sealed record SharedRename(string EntityId, string Previous, string Next);

    private async Task<SharedRename?> SharedRenameAsync(SharedFieldBinding binding, string value)
    {
        if (binding.Custom || binding.Key is not ("name" or "surname")) return null;
        var root = sharedRoot ?? throw new InvalidOperationException("The shared project is no longer open.");
        var entity = JsonNode.Parse(await FileService.ReadTextAsync(SharedProjectPath.Resolve(root, binding.Path))) as JsonObject
            ?? throw new InvalidDataException("Invalid shared Codex entry.");
        var previous = Name(entity);
        entity[PropertyName(entity, binding.Key)] = value;
        var next = Name(entity);
        return string.IsNullOrWhiteSpace(previous) || string.IsNullOrWhiteSpace(next) || previous == next ? null
            : new(Path.GetFileNameWithoutExtension(binding.Path), previous, next);

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
        var items = sharedSceneFiles.Keys.Concat(sharedProse.Values.Where(binding => binding.TextField?.SectionId is not null)
            .Select(binding => binding.ItemId)).ToArray();
        foreach (var item in items)
            await scenes.RenameAsync(item, rename.EntityId, rename.Previous, rename.Next, ct);
    }
}
#endif
