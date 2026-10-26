using System.Text.Json;

namespace Novalist.Core.Models;

public static class EntityReferenceList
{
    public static string[] Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var ids = JsonSerializer.Deserialize<string[]>(value);
        if (ids == null || ids.Any(string.IsNullOrWhiteSpace))
            throw new JsonException("A reference list must contain nonempty entry IDs.");
        return ids.Distinct(StringComparer.Ordinal).ToArray();
    }
}
