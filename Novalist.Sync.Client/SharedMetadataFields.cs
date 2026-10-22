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
        var key = target?.Kind is "section" or "state" or "image" or "attachment" ? $"{target.Kind}:{target.Identity}:{binding.Key}" : binding.Key;
        return target?.Chapter is null ? key : $"override:{target.Chapter}:{target.Scene ?? ""}:{key}";
    }

    public static void Validate(SharedFieldBinding binding)
    {
        var target = binding.Metadata;
        if (binding.Custom || target is null || (target.Chapter is null && target.Scene is not null) ||
            (target.Kind is "scene" or "research" or "section" or "state" or "image" or "attachment" && string.IsNullOrWhiteSpace(target.Identity)) ||
            (target.Kind is "codex" or "override" && target.Identity is not null) ||
            (target.Kind == "override" && string.IsNullOrWhiteSpace(target.Chapter)) ||
            (target.Kind is "codex" or "scene" or "research" or "state" or "attachment" && target.Chapter is not null) || ValueKind(binding) is null)
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
            "state" => (root["stateOverrides"] as JsonArray)?.OfType<JsonObject>().SingleOrDefault(item => item["id"]?.GetValue<string>() == target.Identity),
            "image" => (scope?["images"] as JsonArray)?.OfType<JsonObject>().SingleOrDefault(item => item["path"]?.GetValue<string>() == target.Identity),
            "attachment" => (root["attachments"] as JsonArray)?.OfType<JsonObject>().SingleOrDefault(item => item["id"]?.GetValue<string>() == target.Identity),
            "research" => (root["researchItems"] as JsonArray)?.OfType<JsonObject>().SingleOrDefault(item => item["id"]?.GetValue<string>() == target.Identity &&
                (binding.Key != "content" || item["type"]?.GetValue<string>() is not ("Note" or "Link"))),
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

    public static void Copy(JsonObject source, JsonObject destination, SharedFieldBinding binding)
    {
        var original = Target(source, binding);
        var target = Target(destination, binding);
        if (target is null) return;
        var (container, key) = Property(binding.Key);
        var scope = target;
        if (container is not null)
        {
            original = original?[container] as JsonObject;
            if (target[container] is null)
            {
                if (original?.ContainsKey(key) != true) return;
                target[container] = new JsonObject();
            }
            target = target[container] as JsonObject ?? throw new InvalidDataException("Invalid shared metadata container.");
        }
        if (original is not null && original.TryGetPropertyValue(key, out var value)) target[key] = value?.DeepClone();
        else target.Remove(key);
        if (container is not null && target.Count == 0 && Target(source, binding) is { } originalScope && originalScope[container] is null)
        {
            if (originalScope.ContainsKey(container)) scope[container] = null;
            else scope.Remove(container);
        }
    }

    public static JsonNode? ParseValue(SharedFieldBinding binding, string value)
    {
        Validate(binding);
        try
        {
            var parsed = JsonNode.Parse(value);
            if (binding.Metadata?.Kind == "attachment" && (parsed is null || !IsText(parsed) || string.IsNullOrWhiteSpace(parsed.GetValue<string>())))
                throw new InvalidDataException("An attachment needs a name.");
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
        if (key.StartsWith("match.", StringComparison.Ordinal)) return ("match", key[6..]);
        if (key == "arc.points") return ("arc", "points");
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
