#if NOVALIST_COLLABORATION
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Novalist.Core.Models;
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    private readonly Dictionary<Guid, SharedDocumentState> sharedProse = [];

    private async Task NormalizeSharedSectionsAsync(string root, IReadOnlyList<string> paths, CancellationToken ct)
    {
        var fields = await SharedCodexFields.CaptureAsync(root, paths, ct);
        foreach (var relative in fields.Select(field => field.Path).Distinct(StringComparer.Ordinal))
        {
            var path = SharedProjectPath.Resolve(root, relative);
            var entity = JsonNode.Parse(await FileService.ReadTextAsync(path)) as JsonObject ?? throw new InvalidDataException("Invalid Codex entry.");
            if (SharedProseFields.EnsureSectionIdentities(entity)) await FileService.WriteTextAsync(path, entity.ToJsonString());
        }
    }

    private async Task<IReadOnlyList<SharedDocumentState>> CaptureSharedProseAsync(string root, IReadOnlyList<string> paths,
        DesktopSceneDocuments documents, CancellationToken ct)
    {
        var fields = await SharedCodexFields.CaptureAsync(root, paths, ct);
        var result = new List<SharedDocumentState>();
        foreach (var relative in fields.Select(field => field.Path).Distinct(StringComparer.Ordinal))
        {
            var bytes = await FileService.ReadBytesAsync(SharedProjectPath.Resolve(root, relative));
            var entity = JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException("Invalid Codex entry.");
            foreach (var field in SharedProseFields.Enumerate(entity))
            {
                if (sharedProse.Values.Any(binding => binding.Path == relative && binding.TextField == field)) continue;
                var id = Guid.NewGuid();
                var text = SharedProseFields.Target(entity, field)?[field.Key]?.GetValue<string>() ?? "";
                await documents.SeedTextAsync(id, text, ct);
                var snapshot = await documents.CaptureAsync(id, ct);
                result.Add(new(id, relative, snapshot.State) { TextField = field, SourceSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) });
            }
        }
        return result;
    }

    private async Task ProjectSharedProseAsync(string root, SharedDocumentState binding, SharedSceneSnapshot snapshot)
    {
        var field = binding.TextField ?? throw new InvalidDataException("Missing shared prose field.");
        var path = SharedProjectPath.Resolve(root, binding.Path);
        var entity = await FileService.ExistsAsync(path) ? JsonNode.Parse(await FileService.ReadTextAsync(path)) as JsonObject : null;
        var target = entity is null ? null : SharedProseFields.Target(entity, field);
        if (entity is null || target is null || snapshot.Text is null)
        {
            if ((snapshot.RecoveredText ?? snapshot.Text) is { Length: > 0 } recovered)
                await WriteSharedFileRecoveryAsync(root, binding.ItemId.ToString("N"), binding.Path,
                    JsonSerializer.SerializeToUtf8Bytes(new { Field = field, Text = recovered }));
            return;
        }
        if (target[field.Key]?.GetValue<string>() == snapshot.Text) return;
        target[field.Key] = snapshot.Text;
        await FileService.WriteTextAsync(path, entity.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    internal async Task WriteSharedDocumentAsync(Guid sessionId, Guid item, byte[] update, CancellationToken ct)
    {
        var scenes = RequireSharedScenes(sessionId);
        if (sharedProse.TryGetValue(item, out var binding))
        {
            var path = SharedProjectPath.Resolve(sharedRoot ?? throw new InvalidOperationException("The shared project is closed."), binding.Path);
            if (await FileService.ExistsAsync(path))
            {
                var entity = JsonNode.Parse(await FileService.ReadTextAsync(path)) as JsonObject ?? throw new InvalidDataException("Invalid Codex entry.");
                if (entity["locked"]?.GetValue<bool>() == true) throw new InvalidOperationException(Rpc.EntitiesRpc.LockedMessage);
            }
        }
        await scenes.WriteAsync(item, update, ct);
    }

    internal SharedEntityFieldsDto? SharedEntityProse(IEntityData entity)
    {
        var path = SharedEntityPath(entity);
        if (path is null) return null;
        var fields = sharedProse.Values.Where(binding => binding.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(binding => ProseKey(RequireTextField(binding)), binding => binding.ItemId, StringComparer.Ordinal);
        return fields.Count == 0 ? null : new(sharedSession, fields);
    }

    private static string ProseKey(SharedTextField field)
    {
        var key = field.SectionId is null ? field.Key : "section:" + field.SectionId;
        return field.Chapter is null ? key : $"override:{field.Chapter}:{field.Scene ?? ""}:{key}";
    }

    private void MaskSharedProse(string path, JsonObject target, JsonObject source)
    {
        foreach (var binding in sharedProse.Values.Where(binding => binding.Path == path))
        {
            var field = RequireTextField(binding);
            var destination = SharedProseFields.Target(target, field);
            if (destination is not null) destination[field.Key] = SharedProseFields.Target(source, field)?[field.Key]?.DeepClone();
        }
    }

    private static SharedTextField RequireTextField(SharedDocumentState binding) => binding.TextField
        ?? throw new InvalidDataException("Missing shared prose field.");
}
#endif
