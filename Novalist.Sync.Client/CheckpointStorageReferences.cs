using System.Text.Json;

namespace Novalist.Sync.Client;

internal static class CheckpointStorageReferences
{
    private enum ResearchType { Note, Link, File, Image, Pdf, Audio, Video }
    private static readonly HashSet<string> Paths = new(StringComparer.OrdinalIgnoreCase)
    {
        "folderName", "worldBibleFolder", "chapterFolder", "characterFolder", "locationFolder",
        "itemFolder", "loreFolder", "imageFolder", "snapshotFolder", "fileName", "coverImage", "bannerImage", "path", "iconPath"
    };
    private static readonly (string Property, string Default)[] EntityFolders =
        [("characterFolder", "Characters"), ("locationFolder", "Locations"), ("itemFolder", "Items"), ("loreFolder", "Lore")];
    private static readonly HashSet<string> ProseDictionaries = new(StringComparer.OrdinalIgnoreCase)
        { "properties", "customProperties", "fields" };

    internal static Task ValidateAsync(string root, IReadOnlyList<CheckpointFile> files, CancellationToken ct) =>
        ValidateAsync(files.Select(file => file.Path), path => File.OpenRead(SharedProjectPath.Resolve(root, path)), ct);

    internal static Task ValidateAsync(IReadOnlyDictionary<string, byte[]> files, CancellationToken ct) =>
        ValidateAsync(files.Keys, path => files.TryGetValue(path, out var bytes) ? new MemoryStream(bytes, writable: false)
            : throw new InvalidDataException("Missing shared project metadata."), ct);

    internal static void ValidateMetadata(JsonElement value, bool research)
    {
        ValidateObject(value);
        if (research) ValidateResearch(value);
    }

    private static async Task ValidateAsync(IEnumerable<string> paths, Func<string, Stream> read, CancellationToken ct)
    {
        using var project = await ReadAsync(read(".novalist/project.json"), ct);
        ValidateObject(project.RootElement);
        ValidateResearch(project.RootElement);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var metadata = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var sceneFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddEntities(project.RootElement, Folder(project.RootElement, "worldBibleFolder", "World Bible"), project.RootElement, directories);
        foreach (var book in Array(project.RootElement, "books"))
        {
            var folder = Folder(book, "folderName", "");
            AddEntities(book, folder, project.RootElement, directories);
            metadata[folder + "/.book/scenes.json"] = true;
            var chapterFolder = Folder(book, "chapterFolder", "Chapters");
            AddDraft(folder, chapterFolder, metadata, directories, sceneFolders);
            var drafts = Array(book, "drafts").ToArray();
            if (drafts.Length == 0) AddDraft(folder + "/Drafts/default", chapterFolder, metadata, directories, sceneFolders);
            foreach (var draft in drafts)
                AddDraft(folder + "/Drafts/" + Folder(draft, "folderName", "default"), chapterFolder, metadata, directories, sceneFolders);
        }
        foreach (var path in paths)
        {
            var parent = path[..Math.Max(0, path.LastIndexOf('/'))];
            var scene = sceneFolders.Any(folder => path.StartsWith(folder, StringComparison.OrdinalIgnoreCase));
            if (scene && path.EndsWith(".novalist", StringComparison.OrdinalIgnoreCase))
                await SharedSceneIdentityValidation.ValidateAsync(read(path), ct);
            var marker = scene && path.EndsWith("/.nvchapter.json", StringComparison.OrdinalIgnoreCase);
            var entity = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && directories.Contains(parent);
            if (!marker && !metadata.ContainsKey(path) && !entity) continue;
            using var document = await ReadAsync(read(path), ct);
            var manifest = metadata.GetValueOrDefault(path);
            if (manifest) ValidateScenes(document.RootElement);
            if (!manifest || entity || marker) ValidateObject(document.RootElement);
        }
    }

    private static void AddEntities(JsonElement scope, string root, JsonElement project, HashSet<string> directories)
    {
        foreach (var (property, fallback) in EntityFolders) directories.Add(root + '/' + Folder(scope, property, fallback));
        foreach (var type in Array(project, "customEntityTypes")) directories.Add(root + '/' + Folder(type, "folderName", ""));
    }

    private static void AddDraft(string root, string chapterFolder, Dictionary<string, bool> metadata, HashSet<string> directories, HashSet<string> sceneFolders)
    {
        metadata[root + "/draft.json"] = false;
        metadata[root + "/scenes.json"] = true;
        directories.Add(root + "/Maps");
        directories.Add(root + "/Canvases");
        sceneFolders.Add(root + '/' + chapterFolder + '/');
    }

    private static void ValidateObject(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) ValidateObject(item);
            return;
        }
        if (value.ValueKind != JsonValueKind.Object) return;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new InvalidDataException("Ambiguous project metadata properties.");
            if (Paths.Contains(property.Name)) ValidatePath(property.Value, allowEmpty: true);
            else if (property.Name.Equals("id", StringComparison.OrdinalIgnoreCase) || property.Name.Equals("guid", StringComparison.OrdinalIgnoreCase))
            {
                var id = ValidatePath(property.Value, allowEmpty: true);
                if (id.Contains('/')) throw new InvalidDataException("A project identity cannot contain a storage path.");
            }
            else if (!ProseDictionaries.Contains(property.Name) || property.Value.ValueKind != JsonValueKind.Object ||
                property.Value.EnumerateObject().Any(item => item.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))) ValidateObject(property.Value);
        }
    }

    private static void ValidateScenes(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Select(property => property.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() != value.EnumerateObject().Count())
            throw new InvalidDataException("Invalid or ambiguous shared scene manifest.");
        if (Property(value, "chapters") is { } chapters)
        {
            if (chapters.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid shared scene buckets.");
            foreach (var chapter in chapters.EnumerateObject()) ValidateObject(chapter.Value);
        }
        foreach (var scene in Array(value, "archived")) ValidateObject(scene);
    }

    private static void ValidateResearch(JsonElement project)
    {
        foreach (var item in Array(project, "researchItems"))
        {
            if (Property(item, "type") is not { } type) continue;
            if (type.ValueKind is not (JsonValueKind.String or JsonValueKind.Number) ||
                !Enum.TryParse<ResearchType>(type.ValueKind == JsonValueKind.String ? type.GetString() : type.GetRawText(), true, out var kind) ||
                !Enum.IsDefined(kind)) throw new InvalidDataException("Invalid shared research type.");
            if (kind is not (ResearchType.Note or ResearchType.Link) && Property(item, "content") is { } content)
                ValidatePath(content, allowEmpty: true);
        }
    }

    private static string ValidatePath(JsonElement value, bool allowEmpty)
    {
        if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException("Invalid project storage reference.");
        var path = value.GetString() ?? throw new InvalidDataException("Missing project storage reference.");
        if (!allowEmpty || path.Length != 0) SharedProjectPath.Validate(path);
        return path;
    }

    private static string Folder(JsonElement value, string name, string fallback)
    {
        var path = Property(value, name) is { } found ? ValidatePath(found, allowEmpty: false) : fallback;
        SharedProjectPath.Validate(path);
        return path;
    }

    private static IEnumerable<JsonElement> Array(JsonElement value, string name)
    {
        if (Property(value, name) is not { } found) return [];
        if (found.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid project metadata collection.");
        return found.EnumerateArray();
    }

    private static JsonElement? Property(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object
        ? value.EnumerateObject().Where(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(property => (JsonElement?)property.Value).FirstOrDefault()
        : throw new InvalidDataException("Invalid project metadata object.");

    private static async Task<JsonDocument> ReadAsync(Stream source, CancellationToken ct)
    {
        await using var stream = source;
        if (stream.Length > 64 * 1024 * 1024) throw new InvalidDataException("Project metadata is too large to share.");
        try { return await JsonDocument.ParseAsync(stream, cancellationToken: ct); }
        catch (JsonException ex) { throw new InvalidDataException("Invalid shared project metadata.", ex); }
    }
}
