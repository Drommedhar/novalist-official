#if NOVALIST_COLLABORATION
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Novalist.Core.Models;
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    private async Task<Dictionary<string, byte[]>> ReadSharedFilesAsync(string root, CancellationToken ct)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var path in ProjectContentSelection.Read(root))
            result.Add(path, await SharedProjectFiles.FilterContentAsync(path,
                await FileService.ReadBytesAsync(SharedProjectPath.Resolve(root, path)), ct));
        return result;
    }

    private byte[] MaskSeparatelySharedContent(string path, byte[] value, byte[]? baseline)
    {
        if (baseline is null) return value;
        if (sharedItems.ContainsKey(path)) return baseline;
        var bindings = fieldBindings.Values.Where(binding => binding.Path == path).ToArray();
        if (bindings.Length == 0 && !sharedProse.Values.Any(binding => binding.Path == path) && path != ".novalist/project.json") return value;
        var current = JsonNode.Parse(value) as JsonObject ?? throw new InvalidDataException("Invalid shared metadata.");
        var original = JsonNode.Parse(baseline) as JsonObject ?? throw new InvalidDataException("Invalid shared metadata baseline.");
        foreach (var binding in bindings)
        {
            if (binding.Metadata is not null)
            {
                if (SharedMetadataFields.Target(current, binding) is not null)
                    SharedMetadataFields.Set(current, binding, SharedMetadataFields.Read(original, binding));
                continue;
            }
            var target = binding.Custom ? current[PropertyName(current, "fields")] as JsonObject : current;
            var source = binding.Custom ? original[PropertyName(original, "fields")] as JsonObject : original;
            if (target is null) continue;
            var key = binding.Custom ? binding.Key : PropertyName(target, binding.Key);
            if (source is not null && source.TryGetPropertyValue(binding.Custom ? binding.Key : PropertyName(source, binding.Key), out var originalValue))
                target[key] = originalValue?.DeepClone();
            else target.Remove(key);
        }
        if (path == ".novalist/project.json") PreserveNavigation(current, original);
        MaskSharedProse(path, current, original);
        return Encoding.UTF8.GetBytes(current.ToJsonString());
    }

    private static void PreserveNavigation(JsonObject target, JsonObject source)
    {
        target["activeBookId"] = source["activeBookId"]?.DeepClone();
        if (target["books"] is not JsonArray books || source["books"] is not JsonArray before) return;
        foreach (var book in books.OfType<JsonObject>())
        {
            var original = before.OfType<JsonObject>().SingleOrDefault(item => JsonNode.DeepEquals(item["id"], book["id"]));
            if (original is not null) book["activeDraftId"] = original["activeDraftId"]?.DeepClone();
        }
    }

    private async Task<SharedProjectState> CaptureAddedBindingsAsync(IReadOnlyList<SharedFileChange> changes, CancellationToken ct)
    {
        var root = sharedRoot ?? throw new InvalidOperationException("The shared project is closed.");
        var paths = changes.Where(change => change.After is not null && change.Before is null).Select(change => change.Path).ToArray();
        var documents = new List<SharedDocumentState>();
        var sceneFolders = SharedSceneFolders();
        var scenes = paths.Where(path => path.EndsWith(".novalist", StringComparison.OrdinalIgnoreCase) &&
            sceneFolders.Any(folder => path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (scenes.Length > 0)
        {
            var factory = CreateSharedDocuments ?? throw new InvalidOperationException("Desktop collaboration is unavailable.");
            await using var host = factory(Guid.NewGuid());
            await host.OpenAsync(ct);
            foreach (var path in scenes)
            {
                var bytes = await FileService.ReadBytesAsync(SharedProjectPath.Resolve(root, path));
                var content = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
                if (!FileFrontMatter.TryParse(content, out var marker)) throw new InvalidDataException("A shared scene needs a file identity.");
                var moved = sharedSceneFiles.FirstOrDefault(file => file.Value.SceneId == marker.Id &&
                    changes.Any(change => change.Path == file.Value.Relative && change.After is null));
                var id = moved.Key == Guid.Empty ? Guid.NewGuid() : moved.Key;
                byte[] state;
                if (moved.Key != Guid.Empty) state = (await RequireSharedScenes(sharedSession).ReadAsync(id, ct)).State;
                else
                {
                    await host.SeedAsync(id, FileFrontMatter.Strip(content), ct);
                    state = (await host.CaptureAsync(id, ct)).State;
                }
                documents.Add(new(id, path, state) { SourceSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) });
            }
        }
        var prosePaths = changes.Where(change => change.After is not null).Select(change => change.Path).ToArray();
        var fields = (await SharedCodexFields.CaptureAsync(root, prosePaths, ct)).Where(candidate =>
            !fieldBindings.Values.Any(existing => existing.Path == candidate.Path && existing.Key == candidate.Key && existing.Custom == candidate.Custom && existing.Metadata == candidate.Metadata)).ToArray();
        if (prosePaths.Any(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            var factory = CreateSharedDocuments ?? throw new InvalidOperationException("Desktop collaboration is unavailable.");
            await using var host = factory(Guid.NewGuid());
            await host.OpenAsync(ct);
            documents.AddRange(await CaptureSharedProseAsync(root, prosePaths, host, ct));
        }
        var sourceHashes = changes.Where(change => change.After is not null).ToDictionary(change => change.Path,
            change => Convert.ToHexStringLower(SHA256.HashData(change.After ?? throw new InvalidDataException("Missing shared content."))));
        return new(documents.Select(document => document with { SourceSha256 = sourceHashes[document.Path] }).ToArray(),
            fields.Select(field => field with { SourceSha256 = sourceHashes[field.Path] }).ToArray());
    }
}
#endif
