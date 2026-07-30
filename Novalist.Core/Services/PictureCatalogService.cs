using System.Text.Json;
using System.Text.Json.Serialization;

namespace Novalist.Core.Services;

/// <summary>What the writer has said about one picture.</summary>
public sealed class PictureEntry
{
    /// <summary>The stored path, which is what a scene or an entry points at.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// The one collection it belongs to, or empty for none.
    ///
    /// One rather than several, and a name rather than a folder on disk: an
    /// image already lives somewhere, and moving files around to file them
    /// would break every scene and entry pointing at the old path.
    /// </summary>
    [JsonPropertyName("collection")]
    public string Collection { get; set; } = string.Empty;

    /// <summary>Whatever else it is, in the writer's words. Never null.</summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    /// <summary>True when nothing has been said about this picture.</summary>
    [JsonIgnore]
    public bool IsEmpty => Collection.Length == 0 && Tags.Count == 0;
}

/// <summary>
/// Everything the writer has said about the pictures in a project.
///
/// Named for pictures rather than for the Gallery because the extension
/// marketplace already owns the word gallery in this codebase, and two things
/// called GalleryEntry in one namespace is how the wrong one gets imported.
/// </summary>
public sealed class PictureCatalog
{
    [JsonPropertyName("entries")]
    public List<PictureEntry> Entries { get; set; } = [];
}

/// <summary>
/// Collections and tags over the project's pictures.
///
/// The Gallery could search file names and nothing else, so a folder of four
/// hundred references was navigable only by whatever the browser happened to
/// call the file when it was saved.
///
/// The catalogue is a sidecar rather than a folder layout on disk. An image is
/// already pointed at by scenes, entries, banners and map layers by its path;
/// filing it into a folder would move it and break every one of them.
/// </summary>
public class PictureCatalogService(IProjectService projectService, IFileService fileService)
{
    private const string FileName = "gallery.json";

    private readonly IProjectService _projectService = projectService;
    private readonly IFileService _fileService = fileService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private string? CatalogPath => _projectService.ProjectRoot == null
        ? null
        : _fileService.CombinePath(_projectService.ProjectRoot, ".novalist", FileName);

    /// <summary>
    /// The catalogue as stored. A project that has never filed a picture has
    /// no file, which is an empty catalogue rather than an error.
    /// </summary>
    public async Task<PictureCatalog> LoadAsync()
    {
        var path = CatalogPath;
        if (path == null || !await _fileService.ExistsAsync(path)) return new PictureCatalog();
        try
        {
            var json = await _fileService.ReadTextAsync(path);
            return JsonSerializer.Deserialize<PictureCatalog>(json, JsonOptions) ?? new PictureCatalog();
        }
        catch (JsonException)
        {
            // A corrupt sidecar loses the filing, which is recoverable. Refusing
            // to open the Gallery over it is not.
            return new PictureCatalog();
        }
    }

    /// <summary>Files a picture into a collection, or out of one with an empty name.</summary>
    public async Task<PictureCatalog> SetCollectionAsync(string imagePath, string? collection)
    {
        var catalog = await LoadAsync();
        Entry(catalog, imagePath).Collection = (collection ?? string.Empty).Trim();
        return await SaveAsync(catalog);
    }

    /// <summary>Replaces what a picture is tagged with.</summary>
    public async Task<PictureCatalog> SetTagsAsync(string imagePath, IEnumerable<string>? tags)
    {
        var catalog = await LoadAsync();
        Entry(catalog, imagePath).Tags =
        [
            .. (tags ?? [])
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                // The same tag twice is one tag, and case is a typo rather
                // than a distinction anybody meant to draw.
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
        ];
        return await SaveAsync(catalog);
    }

    /// <summary>Every collection in use, in the order a picker should list them.</summary>
    public static IReadOnlyList<string> Collections(PictureCatalog catalog)
        => [.. catalog.Entries
            .Select(e => e.Collection)
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>Every tag in use, in the order a picker should list them.</summary>
    public static IReadOnlyList<string> Tags(PictureCatalog catalog)
        => [.. catalog.Entries
            .SelectMany(e => e.Tags)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)];

    private static PictureEntry Entry(PictureCatalog catalog, string imagePath)
    {
        var normalised = imagePath.Replace('\\', '/');
        var existing = catalog.Entries.FirstOrDefault(
            e => string.Equals(e.Path, normalised, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;

        var created = new PictureEntry { Path = normalised };
        catalog.Entries.Add(created);
        return created;
    }

    private async Task<PictureCatalog> SaveAsync(PictureCatalog catalog)
    {
        // A picture nobody has said anything about does not need a row. Keeping
        // one would grow the file by every image ever right-clicked.
        catalog.Entries.RemoveAll(e => e.IsEmpty);
        catalog.Entries.Sort((a, b) =>
            string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));

        var path = CatalogPath;
        if (path != null)
            await _fileService.WriteTextAsync(path, JsonSerializer.Serialize(catalog, JsonOptions));
        return catalog;
    }
}
