using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Novalist.Sync.Client;

public static partial class SharedMetadataFields
{
    internal static async Task<IReadOnlyList<SharedFieldBinding>> CaptureAsync(string root, IReadOnlyList<string> paths, IReadOnlyList<string> codexPaths, CancellationToken ct)
    {
        var project = JsonNode.Parse(await File.ReadAllBytesAsync(SharedProjectPath.Resolve(root, ".novalist/project.json"), ct)) as JsonObject
            ?? throw new InvalidDataException("Invalid shared project.");
        var scenePaths = ScenePaths(project);
        var result = new List<SharedFieldBinding>();
        foreach (var path in paths)
        {
            if (!codexPaths.Contains(path, StringComparer.Ordinal) && path != ".novalist/project.json" && !scenePaths.Contains(path)) continue;
            var bytes = await File.ReadAllBytesAsync(SharedProjectPath.Resolve(root, path), ct);
            var entity = JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException("Invalid shared metadata.");
            SharedProseFields.EnsureSectionIdentities(entity);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            foreach (var (target, key) in Enumerate(entity, path == ".novalist/project.json", scenePaths.Contains(path), DefinedProperties(project, path)))
                result.Add(new(Guid.NewGuid(), path, key, false, hash) { Metadata = target });
        }
        return result;
    }

    private static IEnumerable<(SharedMetadataTarget Target, string Key)> Enumerate(JsonObject root, bool project, bool scenes, IEnumerable<string> properties)
    {
        if (project)
        {
            foreach (var item in (root["researchItems"] as JsonArray)?.OfType<JsonObject>() ?? [])
                foreach (var key in Research.Keys.Concat(MapKeys(item, "properties")).Concat(properties).Distinct(StringComparer.Ordinal))
                    if (key != "content" || item["type"]?.GetValue<string>() is not ("Note" or "Link"))
                        yield return (new("research", Identity(item)), key);
            yield break;
        }
        if (scenes)
        {
            foreach (var item in Scenes(root))
                foreach (var key in Scene.Keys.Concat(MapKeys(item, "properties")).Concat(properties).Distinct(StringComparer.Ordinal)) yield return (new("scene", Identity(item)), key);
            yield break;
        }
        foreach (var key in Codex.Keys.Concat(MapKeys(root, "customProperties")))
            if ((key != "relationships" || root.ContainsKey(key)) && (key != "group" || root.ContainsKey("entityTypeKey")) &&
                (key != "isWorld" || root.ContainsKey("parent")) && (key != "arc.points" || root.ContainsKey("surname"))) yield return (new("codex"), key);
        foreach (var section in Sections(root))
            foreach (var key in new[] { "title", "aiHidden", "readerHidden" }) yield return (new("section", Identity(section)), key);
        foreach (var image in (root["images"] as JsonArray)?.OfType<JsonObject>() ?? [])
            foreach (var key in new[] { "name", "alt" }) yield return (new("image", image["path"]?.GetValue<string>()), key);
        foreach (var attachment in (root["attachments"] as JsonArray)?.OfType<JsonObject>() ?? [])
            yield return (new("attachment", Identity(attachment)), "name");
        foreach (var state in (root["stateOverrides"] as JsonArray)?.OfType<JsonObject>().Where(state => state["id"]?.GetValue<string>() is { Length: > 0 }) ?? [])
            foreach (var key in new[] { "act", "chapter", "scene", "name", "gone" }) yield return (new("state", Identity(state)), key);
        foreach (var scope in (root["chapterOverrides"] as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            var chapter = scope["chapter"]?.GetValue<string>() ?? throw new InvalidDataException("An override needs its chapter.");
            var scene = scope["scene"]?.GetValue<string>();
            foreach (var key in OverrideText.Concat(new[] { "aliases", "relationships" }).Concat(MapKeys(root, "customProperties")).Concat(MapKeys(scope, "customProperties")).Distinct(StringComparer.Ordinal)) yield return (new("override", Chapter: chapter, Scene: scene), key);
            foreach (var section in Sections(scope))
                foreach (var key in new[] { "title", "aiHidden", "readerHidden" }) yield return (new("section", Identity(section), chapter, scene), key);
            foreach (var image in (scope["images"] as JsonArray)?.OfType<JsonObject>() ?? [])
                yield return (new("image", image["path"]?.GetValue<string>(), chapter, scene), "name");
        }
    }

    private static string Identity(JsonObject item) => item["id"]?.GetValue<string>() is { Length: > 0 } id ? id : throw new InvalidDataException("Shared metadata needs a stable identity.");
    private static IEnumerable<JsonObject> Sections(JsonObject scope) => (scope["sections"] as JsonArray)?.OfType<JsonObject>().Where(section => section["id"]?.GetValue<string>() is { Length: > 0 }) ?? [];
    private static IEnumerable<string> MapKeys(JsonObject scope, string name) => (scope[name] as JsonObject)?.Select(property => name + ':' + property.Key) ?? [];

    private static IEnumerable<string> DefinedProperties(JsonObject project, string path)
    {
        var research = path == ".novalist/project.json";
        foreach (var book in (project["books"] as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            if (!research && !path.StartsWith(book["folderName"]?.GetValue<string>() + '/', StringComparison.Ordinal)) continue;
            foreach (var property in (book["manuscriptProperties"] as JsonArray)?.OfType<JsonObject>() ?? [])
                if (property["scope"]?.GetValue<string>() == (research ? "Research" : "Scene") && property["key"]?.GetValue<string>() is { Length: > 0 } key)
                    yield return "properties:" + key;
        }
    }

    private static HashSet<string> ScenePaths(JsonObject project)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var book in (project["books"] as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            var folder = book["folderName"]?.GetValue<string>() ?? throw new InvalidDataException("A shared book needs its storage folder.");
            var drafts = (book["drafts"] as JsonArray)?.OfType<JsonObject>().ToArray() ?? [];
            foreach (var draft in drafts)
                result.Add(folder + "/Drafts/" + (draft["folderName"]?.GetValue<string>() ?? throw new InvalidDataException("A shared draft needs its storage folder.")) + "/scenes.json");
            if (drafts.Length == 0) result.Add(folder + "/.book/scenes.json");
        }
        return result;
    }
}
