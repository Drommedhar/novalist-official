using System.Text.Json;
using System.Text.Json.Nodes;

namespace Novalist.Sync.Client;

internal static class SharedProjectMetadata
{
    private static readonly string[] SharedSettings =
        ["author", "disabledContinuityRules", "timeline", "wholeStoryAnalysis", "chapterAnalysis", "wordsPerPage"];
    private static readonly string[] SharedOverrides =
        ["autoReplacementLanguage", "enableBookParagraphSpacing", "enableBookWidth", "bookPageFormat", "bookTextBlockWidth", "bookFontFamily", "bookFontSize"];

    internal static async Task MergeLocalSettingsAsync(string localRoot, string incomingRoot, CancellationToken ct)
    {
        const string relative = ".novalist/settings.json";
        var local = SharedProjectPath.Resolve(localRoot, relative);
        if (!File.Exists(local)) return;
        var incoming = SharedProjectPath.Resolve(incomingRoot, relative);
        var merged = await ReadObjectAsync(local, ct);
        var shared = File.Exists(incoming) ? await ReadObjectAsync(incoming, ct) : new JsonObject();
        MergeProperties(merged, shared, SharedSettings);
        MergeSection(merged, shared, "overrides", SharedOverrides);
        MergeSection(merged, shared, "wordCountGoals", ["projectGoal", "deadline"]);
        await File.WriteAllTextAsync(incoming, merged.ToJsonString(), ct);
    }

    private static async Task<JsonObject> ReadObjectAsync(string path, CancellationToken ct)
    {
        await using var input = File.OpenRead(path);
        if (input.Length > 64 * 1024 * 1024) throw new InvalidDataException("Project settings are too large to merge.");
        return await JsonNode.ParseAsync(input, cancellationToken: ct) as JsonObject
            ?? throw new InvalidDataException("Invalid local project settings.");
    }

    private static void MergeSection(JsonObject local, JsonObject shared, string name, string[] properties)
    {
        var target = local[name] as JsonObject ?? new JsonObject();
        MergeProperties(target, shared[name] as JsonObject ?? new JsonObject(), properties);
        local[name] = target;
    }

    private static void MergeProperties(JsonObject local, JsonObject shared, string[] properties)
    {
        foreach (var property in properties)
        {
            local.Remove(property);
            if (shared.TryGetPropertyValue(property, out var value)) local[property] = value?.DeepClone();
        }
    }

    internal static async Task FilterInstalledAsync(string root, CancellationToken ct)
    {
        const string relative = ".novalist/settings.json";
        var path = SharedProjectPath.Resolve(root, relative);
        if (!File.Exists(path)) return;
        MemoryStream? projected;
        await using (var source = File.OpenRead(path)) projected = await ProjectAsync(relative, source, ct);
        using var content = projected ?? throw new InvalidOperationException("Missing shared settings projection.");
        await using var output = new FileStream(path, FileMode.Truncate, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(output, ct);
        await output.FlushAsync(ct);
        output.Flush(flushToDisk: true);
    }

    internal static async Task<MemoryStream?> ProjectAsync(string path, Stream source, CancellationToken ct)
    {
        if (path != ".novalist/settings.json") return null;
        if (source.Length > 64 * 1024 * 1024) throw new InvalidDataException("Project settings are too large to share.");
        using var document = await JsonDocument.ParseAsync(source, cancellationToken: ct);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid project settings.");
        var shared = new MemoryStream();
        try
        {
            using (var writer = new Utf8JsonWriter(shared))
            {
                writer.WriteStartObject();
                WriteProperties(writer, root, SharedSettings);
                WriteSection(writer, root, "overrides", SharedOverrides);
                WriteSection(writer, root, "wordCountGoals", ["projectGoal", "deadline"]);
                writer.WriteEndObject();
            }
            shared.Position = 0;
            return shared;
        }
        catch { shared.Dispose(); throw; }
    }

    private static void WriteSection(Utf8JsonWriter writer, JsonElement source, string name, string[] properties)
    {
        if (!source.TryGetProperty(name, out var section)) return;
        if (section.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid project settings section.");
        writer.WriteStartObject(name);
        WriteProperties(writer, section, properties);
        writer.WriteEndObject();
    }

    private static void WriteProperties(Utf8JsonWriter writer, JsonElement source, string[] properties)
    {
        foreach (var name in properties)
            if (source.TryGetProperty(name, out var value))
            {
                writer.WritePropertyName(name);
                value.WriteTo(writer);
            }
    }
}
