using System.Text.Json;

namespace Novalist.Sync.Client;

internal static class SharedProjectMetadata
{
    private static readonly string[] SharedSettings =
        ["author", "disabledContinuityRules", "timeline", "wholeStoryAnalysis", "chapterAnalysis", "wordsPerPage"];
    private static readonly string[] BookFormatting =
        ["enableBookParagraphSpacing", "enableBookWidth", "bookPageFormat", "bookTextBlockWidth", "bookFontFamily", "bookFontSize"];

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
                WriteSection(writer, root, "overrides", BookFormatting);
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
