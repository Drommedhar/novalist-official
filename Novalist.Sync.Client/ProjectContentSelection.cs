using System.Text.Json;

namespace Novalist.Sync.Client;

public static class ProjectContentSelection
{
    public static IReadOnlyList<string> Read(string root)
    {
        using var metadata = JsonDocument.Parse(File.ReadAllBytes(SharedProjectPath.Resolve(root, ".novalist/project.json")));
        var project = metadata.RootElement;
        var files = new HashSet<string>(StringComparer.Ordinal);
        if (!project.TryGetProperty("books", out var books) || books.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The project has no book manifest.");
        var worldBible = ReadFolder(project, "worldBibleFolder");
        var excluded = new List<string> { worldBible + "/Snapshots" };
        foreach (var book in books.EnumerateArray())
        {
            var folder = ReadFolder(book, "folderName");
            var snapshots = book.TryGetProperty("snapshotFolder", out _) ? ReadFolder(book, "snapshotFolder") : "Snapshots";
            excluded.Add(folder + '/' + snapshots);
            if (book.TryGetProperty("drafts", out var drafts))
            {
                if (drafts.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid book drafts.");
                foreach (var draft in drafts.EnumerateArray())
                    excluded.Add(folder + "/Drafts/" + ReadFolder(draft, "folderName") + '/' + snapshots);
            }
        }
        var privateFolders = excluded.ToArray();
        foreach (var file in SharedProjectPath.MetadataFiles) AddFile(root, ".novalist/" + file, files);
        foreach (var directory in SharedProjectPath.MetadataDirectories) Walk(root, ".novalist/" + directory, files, privateFolders);
        foreach (var book in books.EnumerateArray()) Walk(root, ReadFolder(book, "folderName"), files, privateFolders);
        Walk(root, worldBible, files, privateFolders);
        Walk(root, "Research", files, privateFolders);
        return files.Order(StringComparer.Ordinal).ToArray();
    }

    private static string ReadFolder(JsonElement metadata, string name)
    {
        if (!metadata.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException("A project storage folder is missing.");
        var folder = value.GetString() ?? throw new InvalidDataException("Invalid project storage folder.");
        SharedProjectPath.Validate(folder);
        return folder;
    }

    private static void Walk(string root, string relative, HashSet<string> files, string[] excluded)
    {
        if (excluded.Any(folder => relative.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith(folder + '/', StringComparison.OrdinalIgnoreCase))) return;
        var full = SharedProjectPath.Resolve(root, relative);
        if (!Directory.Exists(full)) return;
        foreach (var entry in Directory.EnumerateFileSystemEntries(full))
        {
            var name = Path.GetFileName(entry);
            if ((name.StartsWith('.') && name is not ".book" and not ".nvchapter.json") || name.Equals("extensions", StringComparison.OrdinalIgnoreCase)) continue;
            var path = relative + '/' + name;
            if (excluded.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
            var resolved = SharedProjectPath.Resolve(root, path);
            if (Directory.Exists(resolved)) Walk(root, path, files, excluded);
            else AddFile(root, path, files);
        }
    }

    private static void AddFile(string root, string path, HashSet<string> files)
    {
        if (File.Exists(SharedProjectPath.Resolve(root, path))) files.Add(path);
    }
}
