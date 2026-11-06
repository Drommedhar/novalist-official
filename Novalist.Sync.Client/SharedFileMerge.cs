using System.Text.Json;
using System.Text.Json.Nodes;

namespace Novalist.Sync.Client;

public sealed record SharedFileMergeResult(byte[]? Content, bool Conflict);

public static class SharedFileMerge
{
    public static SharedFileMergeResult Apply(SharedFileChange change, byte[]? current)
    {
        if (Equal(change.Before, change.After) || Equal(current, change.After)) return new(current?.ToArray(), false);
        if (Equal(current, change.Before)) return new(change.After?.ToArray(), false);
        if (change.After is null) return new(null, current is not null);
        if (current is null && change.Before is not null) return new(null, true);
        if (change.Before is null || current is null) return new(current?.ToArray(), true);
        try
        {
            var before = JsonNode.Parse(change.Before);
            var after = JsonNode.Parse(change.After);
            var existing = JsonNode.Parse(current);
            var result = Merge(new(true, before), new(true, after), new(true, existing));
            return new(JsonSerializer.SerializeToUtf8Bytes(result.Value.Node), result.Conflict);
        }
        catch (JsonException) { return new(current.ToArray(), true); }
    }

    internal static bool Equal(byte[]? left, byte[]? right) => left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);
    private readonly record struct Value(bool Present, JsonNode? Node);
    private readonly record struct Result(Value Value, bool Conflict);

    private static Result Merge(Value before, Value after, Value current)
    {
        if (Same(before, after) || Same(current, after)) return new(current, false);
        if (Same(before, current)) return new(after, false);
        if (!after.Present || (!current.Present && before.Present)) return new(new(false, null), true);
        if (before.Node is JsonObject oldObject && after.Node is JsonObject newObject && current.Node is JsonObject currentObject)
            return MergeObject(oldObject, newObject, currentObject);
        if (before.Node is JsonArray oldArray && after.Node is JsonArray newArray && current.Node is JsonArray currentArray &&
            Index(oldArray) is { } oldItems && Index(newArray) is { } newItems && Index(currentArray) is { } currentItems)
            return MergeArray(oldItems, newItems, currentItems);
        return new(current, true);
    }

    private static Result MergeObject(JsonObject before, JsonObject after, JsonObject current)
    {
        var output = new JsonObject();
        var conflict = false;
        foreach (var key in current.Select(pair => pair.Key).Concat(after.Select(pair => pair.Key)).Concat(before.Select(pair => pair.Key)).Distinct(StringComparer.Ordinal))
        {
            var result = Merge(Read(before, key), Read(after, key), Read(current, key));
            conflict |= result.Conflict;
            if (result.Value.Present) output[key] = result.Value.Node?.DeepClone();
        }
        return new(new(true, output), conflict);
    }

    private static Result MergeArray(Dictionary<string, JsonNode?> before, Dictionary<string, JsonNode?> after, Dictionary<string, JsonNode?> current)
    {
        var common = before.Keys.Where(after.ContainsKey).ToHashSet(StringComparer.Ordinal);
        var reordered = !before.Keys.Where(common.Contains).SequenceEqual(after.Keys.Where(common.Contains));
        var order = reordered ? after.Keys.Concat(current.Keys) : current.Keys.Concat(after.Keys);
        var output = new JsonArray();
        var conflict = false;
        foreach (var key in order.Concat(before.Keys).Distinct(StringComparer.Ordinal))
        {
            var result = Merge(Read(before, key), Read(after, key), Read(current, key));
            conflict |= result.Conflict;
            if (result.Value.Present) output.Add(result.Value.Node?.DeepClone());
        }
        return new(new(true, output), conflict);
    }

    private static Dictionary<string, JsonNode?>? Index(JsonArray array)
    {
        var result = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var item in array)
        {
            var identity = item is JsonObject obj
                ? obj.FirstOrDefault(pair => pair.Key.Equals("id", StringComparison.OrdinalIgnoreCase) || pair.Key.Equals("guid", StringComparison.OrdinalIgnoreCase)).Value
                : item;
            if (identity is not JsonValue value || !result.TryAdd(value.ToJsonString(), item)) return null;
        }
        return result;
    }

    private static bool Same(Value left, Value right) => left.Present == right.Present && JsonNode.DeepEquals(left.Node, right.Node);
    private static Value Read(IDictionary<string, JsonNode?> source, string key) => source.TryGetValue(key, out var node) ? new(true, node) : new(false, null);
}
