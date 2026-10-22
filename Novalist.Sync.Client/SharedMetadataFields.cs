using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Novalist.Sync.Client;

public sealed record SharedMetadataTarget(string Kind, string? Identity = null, string? Chapter = null, string? Scene = null);

public static partial class SharedMetadataFields
{
    public static string BindingKey(SharedFieldBinding binding)
    {
        var target = binding.Metadata;
        var key = target?.Kind == "section" ? $"section:{target.Identity}:{binding.Key}" : binding.Key;
        return target?.Chapter is null ? key : $"override:{target.Chapter}:{target.Scene ?? ""}:{key}";
    }

    public static void Validate(SharedFieldBinding binding)
    {
        var target = binding.Metadata;
        if (binding.Custom || target is null || (target.Chapter is null && target.Scene is not null) ||
            (target.Kind is "scene" or "research" or "section" && string.IsNullOrWhiteSpace(target.Identity)) ||
            (target.Kind is "codex" or "override" && target.Identity is not null) ||
            (target.Kind == "override" && string.IsNullOrWhiteSpace(target.Chapter)) ||
            (target.Kind is "codex" or "scene" or "research" && target.Chapter is not null) || ValueKind(binding) is null)
            throw new InvalidDataException("Invalid shared metadata field.");
    }

    public static JsonObject? Target(JsonObject root, SharedFieldBinding binding)
    {
        Validate(binding);
        var target = binding.Metadata ?? throw new InvalidDataException("Missing shared metadata target.");
        var scope = target.Chapter is null ? root : (root["chapterOverrides"] as JsonArray)?.OfType<JsonObject>()
            .SingleOrDefault(item => item["chapter"]?.GetValue<string>() == target.Chapter && item["scene"]?.GetValue<string>() == target.Scene);
        return target.Kind switch
        {
            "codex" or "override" => scope,
            "section" => (scope?["sections"] as JsonArray)?.OfType<JsonObject>().SingleOrDefault(item => item["id"]?.GetValue<string>() == target.Identity),
            "research" => (root["researchItems"] as JsonArray)?.OfType<JsonObject>().SingleOrDefault(item => item["id"]?.GetValue<string>() == target.Identity),
            "scene" => Scenes(root).SingleOrDefault(item => item["id"]?.GetValue<string>() == target.Identity),
            _ => throw new InvalidDataException("Invalid shared metadata target.")
        };
    }

    public static JsonNode? Read(JsonObject root, SharedFieldBinding binding)
    {
        var target = Target(root, binding);
        var (container, key) = Property(binding.Key);
        return container is null ? target?[key] : (target?[container] as JsonObject)?[key];
    }

    public static void Set(JsonObject root, SharedFieldBinding binding, JsonNode? value)
    {
        var target = Target(root, binding) ?? throw new InvalidDataException("The shared metadata item was deleted.");
        var (container, key) = Property(binding.Key);
        if (container is not null)
        {
            if (target[container] is null) { if (value is null) return; target[container] = new JsonObject(); }
            target = target[container] as JsonObject ?? throw new InvalidDataException("Invalid shared metadata container.");
        }
        if (value is null) target.Remove(key);
        else target[key] = value.DeepClone();
    }

    public static JsonNode? ParseValue(SharedFieldBinding binding, string value)
    {
        Validate(binding);
        try
        {
            var parsed = JsonNode.Parse(value);
            if (parsed is null) return null;
            if (!ValidValue(ValueKind(binding) ?? throw new InvalidDataException("Invalid shared metadata field."), parsed))
                throw new InvalidDataException("Invalid shared metadata value.");
            return parsed;
        }
        catch (JsonException error) { throw new InvalidDataException("Invalid shared metadata value.", error); }
    }

    private static bool IsText(JsonNode? value) => value is JsonValue text && text.TryGetValue<string>(out _);

    private static bool IsRelationship(JsonNode? value) => value is JsonObject row &&
        row.All(property => property.Key is "role" or "target" or "category" or "inverseRole" && IsText(property.Value));

    private static (string? Container, string Key) Property(string key)
    {
        if (key.StartsWith("analysisOverrides.", StringComparison.Ordinal)) return ("analysisOverrides", key[18..]);
        if (key.StartsWith("customProperties:", StringComparison.Ordinal)) return ("customProperties", key[17..]);
        if (key.StartsWith("properties:", StringComparison.Ordinal)) return ("properties", key[11..]);
        return (null, key);
    }

    private static IEnumerable<JsonObject> Scenes(JsonObject root) => (root["chapters"] as JsonObject)?.SelectMany(chapter =>
        (chapter.Value as JsonArray)?.OfType<JsonObject>() ?? []) ?? [];

    internal static async Task<string> ReadAsync(string root, SharedFieldBinding binding, bool verifySource, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(SharedProjectPath.Resolve(root, binding.Path), ct);
        if (verifySource && !string.Equals(binding.SourceSha256, Convert.ToHexStringLower(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Shared metadata changed during capture. Its local edits have been kept.");
        var entity = JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException("Invalid shared metadata.");
        return Read(entity, binding)?.ToJsonString() ?? "null";
    }
}
