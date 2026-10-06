using System.Text.Json;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public sealed record StructuredImportEntry(string SourceId, JsonElement Document, string? Folder = null);
public sealed record StructuredImportResult(string SourceId, string? Target, string? Id, string Status, string? Error);
public sealed record ImportImageAttachment(string Target, string Id, int Added, int Total);
public sealed class ImportEntryLockedException() : InvalidOperationException("This Codex entry is locked. Unlock it in Novalist before attaching images.");

/// <summary>
/// Accepts portable import documents without reading the agent's source files.
/// Stable source IDs make retries safe across requests and API restarts.
/// Callers serialize access with other project operations.
/// </summary>
public sealed class StructuredImportService
{
    public const int MaxEntries = 40;
    public const int MaxImageBytes = ImportImageStore.MaxImageBytes;
    public const int MaxImagesPerEntry = ImportImageStore.MaxImagesPerEntry;
    public static IReadOnlyCollection<string> ImageContentTypes => ImportImageStore.ContentTypes;
    private readonly IProjectService _projects;
    private readonly IFileService _files;
    private readonly FolderImportService _writer;
    private readonly ImportImageStore _images;
    private static readonly JsonSerializerOptions EntityJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public StructuredImportService(IProjectService projects, IFileService files, string sectionTitle)
    {
        _projects = projects;
        _files = files;
        _writer = new FolderImportService(projects, files);
        _writer.ConfigureStructured(sectionTitle);
        var scope = _writer.EnsureScope();
        _images = new(scope.Book, scope.BookRoot, files);
    }

    public FolderImportSchema Schema() => new(_projects.ActiveBook, new EntityService(_projects).GetCustomEntityTypes());

    public Task<ImportImageUpload> UploadImageAsync(byte[] content, string contentType)
    {
        _writer.EnsureScope();
        return _images.UploadAsync(content, contentType.Split(';')[0].Trim().ToLowerInvariant());
    }

    /// <summary>Add missing pictures to a previous import without replacing any authored fields.</summary>
    public async Task<ImportImageAttachment> AttachImagesAsync(string target, string id, IReadOnlyList<ImportImageReference> images)
    {
        var scope = _writer.EnsureScope();
        if (target is "scene" or "research" || !Schema().Targets.Contains(target, StringComparer.Ordinal))
            throw new ArgumentException("Choose a Codex target returned by GET /v1/types, whose schema includes data.images.");
        if (!Guid.TryParseExact(id, "D", out var guid)) throw new ArgumentException("Use the id returned by POST /v1/import for the entry receiving images.");
        var book = scope.Book;
        var entities = new EntityService(_projects);
        var (model, folder) = target switch
        {
            "character" => (typeof(CharacterData), book.CharacterFolder),
            "location" => (typeof(LocationData), book.LocationFolder),
            "item" => (typeof(ItemData), book.ItemFolder),
            "lore" => (typeof(LoreData), book.LoreFolder),
            _ => (typeof(CustomEntityData), entities.GetCustomEntityTypes().Single(type => type.TypeKey == target).FolderName)
        };
        id = guid.ToString();
        var path = Path.Combine(scope.BookRoot, folder, id + ".json");
        if (!await _files.ExistsAsync(path)) throw new KeyNotFoundException("The Codex entry was not found in this book. Use its imported or skipped result id.");
        var entity = (IEntityData?)JsonSerializer.Deserialize(await _files.ReadTextAsync(path), model, EntityJsonOptions);
        if (entity == null || !entity.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The saved Codex entry could not be read.");
        if (entity.Locked) throw new ImportEntryLockedException();
        var resolved = await _images.ResolveAsync(images);
        var existing = entity.Images;
        var paths = existing.Select(image => image.Path.Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
        var added = resolved.Where(image => paths.Add(image.Path)).ToArray();
        if (added.Length > 0)
        {
            existing.AddRange(added);
            switch (entity)
            {
                case CharacterData character: await entities.SaveCharacterAsync(character); break;
                case LocationData location: await entities.SaveLocationAsync(location); break;
                case ItemData item: await entities.SaveItemAsync(item); break;
                case LoreData lore: await entities.SaveLoreAsync(lore); break;
                case CustomEntityData custom: await entities.SaveCustomEntityAsync(custom); break;
            }
        }
        return new(target, id, added.Length, existing.Count);
    }

    public async Task<StructuredImportResult[]> ImportAsync(IReadOnlyList<StructuredImportEntry> entries, bool validateOnly = false)
    {
        if (entries.Count is < 1 or > MaxEntries) throw new ArgumentException($"Send between 1 and {MaxEntries} entries per request.");
        _writer.RefreshStructuredState();
        var schema = Schema();
        var parser = new FolderImportParser(schema);
        var results = new List<StructuredImportResult>(entries.Count);
        foreach (var entry in entries)
        {
            string? target = null;
            if (string.IsNullOrWhiteSpace(entry.SourceId) || entry.SourceId.Length > 2048 || entry.Folder?.Length > 256)
            {
                results.Add(new(entry.SourceId, null, null, "invalid", "sourceId must contain 1–2048 characters; folder may contain at most 256 characters."));
                continue;
            }
            try
            {
                if (entry.Document.ValueKind != JsonValueKind.Object || !entry.Document.TryGetProperty("target", out var node)
                    || node.ValueKind != JsonValueKind.String || node.GetString() is not { } parsedTarget
                    || !schema.Targets.Contains(parsedTarget, StringComparer.Ordinal))
                    throw new FormatException("Choose a target returned by GET /v1/types.");
                target = parsedTarget;
                var document = parser.Parse("entry.json", entry.Document.GetRawText(), target);
                await _images.ResolveAsync(document.ImageReferences);
                results.Add(validateOnly ? new(entry.SourceId, target, null, "valid", null)
                    : await _writer.ImportStructuredAsync(entry, document));
            }
            catch (ImportImageNotFoundException exception)
            {
                results.Add(new(entry.SourceId, target, null, "invalid", exception.Message));
            }
            catch (FormatException)
            {
                results.Add(new(entry.SourceId, target, null, "invalid", "The document does not match the current target schema. Fetch GET /v1/schema and correct its fields."));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                results.Add(new(entry.SourceId, target, null, "failed", "The entry could not be saved. Check available disk space and file permissions, then retry with the same sourceId."));
            }
        }
        if (!validateOnly) await _writer.SavePendingAsync();
        return [.. results];

    }
}
