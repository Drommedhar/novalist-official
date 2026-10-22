using System.Text.Json.Nodes;

namespace Novalist.Sync.Client;

public static class SharedProseFields
{
    public static void Validate(SharedTextField? field)
    {
        if (field is null) return;
        if (field.Container is not null)
        {
            if (field.Container != "arc" || field.Key is not ("start" or "end" or "want" or "need") || field.SectionId is not null ||
                field.Chapter is not null || field.Scene is not null || field.ResearchItemId is not null || field.StateOverrideId is not null)
                throw new InvalidDataException("Invalid shared arc prose field.");
            return;
        }
        if (field.StateOverrideId is not null)
        {
            if (string.IsNullOrWhiteSpace(field.StateOverrideId) || field.Key is not ("description" or "note") ||
                field.SectionId is not null || field.Chapter is not null || field.Scene is not null || field.ResearchItemId is not null)
                throw new InvalidDataException("Invalid shared state prose field.");
            return;
        }
        if (field.ResearchItemId is not null)
        {
            if (field.Key != "content" || string.IsNullOrWhiteSpace(field.ResearchItemId) ||
                field.SectionId is not null || field.Chapter is not null || field.Scene is not null)
                throw new InvalidDataException("Invalid shared research field.");
            return;
        }
        if ((field.SectionId is null && field.Key is not ("description" or "distinguishingFeatures")) ||
            (field.SectionId is not null && (field.Key != "content" || string.IsNullOrWhiteSpace(field.SectionId))) ||
            (field.Chapter is null && field.Scene is not null))
            throw new InvalidDataException("Invalid shared prose field.");
    }

    public static JsonObject? Target(JsonObject entity, SharedTextField field, bool create = false)
    {
        Validate(field);
        if (field.Container == "arc")
        {
            if (entity["arc"] is null)
            {
                var empty = new JsonObject();
                if (create) entity["arc"] = empty;
                return empty;
            }
            return entity["arc"] as JsonObject ?? throw new InvalidDataException("Invalid shared character arc.");
        }
        if (field.StateOverrideId is { } stateId)
            return (entity["stateOverrides"] as JsonArray)?.OfType<JsonObject>().SingleOrDefault(item => item["id"]?.GetValue<string>() == stateId);
        if (field.ResearchItemId is { } researchId)
            return ResearchNotes(entity).SingleOrDefault(item => item["id"]?.GetValue<string>() == researchId);
        var scope = field.Chapter is null ? entity : (entity["chapterOverrides"] as JsonArray)?.OfType<JsonObject>()
            .SingleOrDefault(value => value["chapter"]?.GetValue<string>() == field.Chapter && value["scene"]?.GetValue<string>() == field.Scene);
        return field.SectionId is null ? scope : (scope?["sections"] as JsonArray)?.OfType<JsonObject>()
            .SingleOrDefault(section => section["id"]?.GetValue<string>() == field.SectionId);
    }

    public static IReadOnlyList<SharedTextField> Enumerate(JsonObject entity)
    {
        var result = new List<SharedTextField>();
        if (entity.ContainsKey("surname"))
            foreach (var key in new[] { "start", "end", "want", "need" }) result.Add(new(key, Container: "arc"));
        foreach (var item in (entity["stateOverrides"] as JsonArray)?.OfType<JsonObject>() ?? [])
            foreach (var key in new[] { "description", "note" })
                result.Add(new(key, StateOverrideId: item["id"]?.GetValue<string>() ?? throw new InvalidDataException("A shared state needs an identity.")));
        foreach (var item in ResearchNotes(entity))
            result.Add(new("content", ResearchItemId: item["id"]?.GetValue<string>() ?? throw new InvalidDataException("A shared research note needs an identity.")));
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

    private static IEnumerable<JsonObject> ResearchNotes(JsonObject entity) =>
        ((entity["researchItems"] as JsonArray)?.OfType<JsonObject>() ?? [])
        .Where(item => item["type"]?.GetValue<string>() is "Note" or "Link");

    public static bool EnsureSectionIdentities(JsonObject entity)
    {
        var changed = false;
        var scopes = new[] { entity }.Concat((entity["chapterOverrides"] as JsonArray)?.OfType<JsonObject>() ?? []);
        var stateIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var state in (entity["stateOverrides"] as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            var id = state["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(id)) { id = Guid.NewGuid().ToString("N"); state["id"] = id; changed = true; }
            if (!stateIds.Add(id)) throw new InvalidDataException("Shared states need distinct identities.");
        }
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
