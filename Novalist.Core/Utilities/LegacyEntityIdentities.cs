using System.Globalization;
using System.Text.Json.Nodes;

namespace Novalist.Storage;

internal static class LegacyEntityIdentities
{
    internal static bool Assign(JsonObject entity)
    {
        var changed = AssignItems(entity["stateOverrides"] as JsonArray, "state");
        foreach (var scope in new[] { entity }.Concat((entity["chapterOverrides"] as JsonArray)?.OfType<JsonObject>() ?? []))
            changed |= AssignItems(scope["sections"] as JsonArray, "section");
        return changed;
    }

    private static bool AssignItems(JsonArray? items, string kind)
    {
        if (items is null) return false;
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items.OfType<JsonObject>())
            if (item["id"]?.GetValue<string>() is { Length: > 0 } id && !identities.Add(id))
                throw new InvalidDataException("Entity details need distinct identities.");
        var changed = false;
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is not JsonObject item || !string.IsNullOrWhiteSpace(item["id"]?.GetValue<string>())) continue;
            var identity = "legacy-" + kind + '-' + index.ToString(CultureInfo.InvariantCulture);
            while (!identities.Add(identity)) identity += "-legacy";
            item["id"] = identity;
            changed = true;
        }
        return changed;
    }
}
