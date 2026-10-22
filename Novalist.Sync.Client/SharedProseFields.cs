using System.Text.Json.Nodes;

namespace Novalist.Sync.Client;

public static class SharedProseFields
{
    public static void Validate(SharedTextField? field)
    {
        if (field is null) return;
        if ((field.SectionId is null && field.Key is not ("description" or "distinguishingFeatures")) ||
            (field.SectionId is not null && (field.Key != "content" || string.IsNullOrWhiteSpace(field.SectionId))) ||
            (field.Chapter is null && field.Scene is not null))
            throw new InvalidDataException("Invalid shared prose field.");
    }

    public static JsonObject? Target(JsonObject entity, SharedTextField field)
    {
        Validate(field);
        var scope = field.Chapter is null ? entity : (entity["chapterOverrides"] as JsonArray)?.OfType<JsonObject>()
            .SingleOrDefault(value => value["chapter"]?.GetValue<string>() == field.Chapter && value["scene"]?.GetValue<string>() == field.Scene);
        return field.SectionId is null ? scope : (scope?["sections"] as JsonArray)?.OfType<JsonObject>()
            .SingleOrDefault(section => section["id"]?.GetValue<string>() == field.SectionId);
    }

    public static IReadOnlyList<SharedTextField> Enumerate(JsonObject entity)
    {
        var result = new List<SharedTextField>();
        Add(entity, null, null);
        foreach (var scope in (entity["chapterOverrides"] as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            var chapter = scope["chapter"]?.GetValue<string>() ?? throw new InvalidDataException("A character override needs its chapter.");
            Add(scope, chapter, scope["scene"]?.GetValue<string>());
        }
        return result;

        void Add(JsonObject scope, string? chapter, string? scene)
        {
            foreach (var key in new[] { "description", "distinguishingFeatures" })
                if (scope.ContainsKey(key) && (chapter is null || scope[key] is not null)) result.Add(new(key, Chapter: chapter, Scene: scene));
            foreach (var section in (scope["sections"] as JsonArray)?.OfType<JsonObject>() ?? [])
                result.Add(new("content", section["id"]?.GetValue<string>() ?? throw new InvalidDataException("A shared section needs an identity."), chapter, scene));
        }
    }

    public static bool EnsureSectionIdentities(JsonObject entity)
    {
        var changed = false;
        var scopes = new[] { entity }.Concat((entity["chapterOverrides"] as JsonArray)?.OfType<JsonObject>() ?? []);
        foreach (var scope in scopes)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var section in (scope["sections"] as JsonArray)?.OfType<JsonObject>() ?? [])
            {
                var id = section["id"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(id)) { id = Guid.NewGuid().ToString("N"); section["id"] = id; changed = true; }
                if (!ids.Add(id)) throw new InvalidDataException("Shared sections need distinct identities.");
            }
        }
        return changed;
    }
}
