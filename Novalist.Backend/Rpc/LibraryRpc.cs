using System.Globalization;
using Novalist.Backend.Extensions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Project image gallery and research library.</summary>
public sealed class LibraryRpc
{
    private readonly EntityService _entities;
    private readonly ResearchService _research;
    private readonly PictureCatalogService _catalog;
    private readonly Workspace _workspace;

    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };

    // What the app can actually play. An extension it cannot play is a File,
    // which opens externally rather than showing dead controls.
    private static readonly HashSet<string> AudioExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".m4a", ".wav", ".ogg", ".flac", ".aac" };

    private static readonly HashSet<string> VideoExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".webm", ".m4v", ".mov", ".ogv" };

    public LibraryRpc(Workspace workspace)
    {
        _workspace = workspace;
        _entities = new EntityService(workspace.Projects);
        _research = new ResearchService(workspace.Projects, workspace.FileService);
        _catalog = new PictureCatalogService(workspace.Projects, workspace.FileService);
    }

    [JsonRpcMethod("gallery/list")]
    public GalleryImageDto[] ListImages() =>
        _entities.GetProjectImages()
            .Select(path => new GalleryImageDto(path, _entities.ResolveProjectRelativeImage(path)))
            .ToArray();

    /// <summary>
    /// Copies an image into the book's image folder and reports both the path
    /// a scene stores and the URL the renderer displays. Two forms because the
    /// stored one has to survive the project moving to another machine.
    /// </summary>
    [JsonRpcMethod("gallery/import")]
    public async Task<GalleryImageDto> ImportImageAsync(string path)
    {
        var stored = await _entities.ImportImageAsync(path);
        return new GalleryImageDto(stored, _entities.ResolveProjectRelativeImage(stored));
    }

    /// <summary>
    /// The project-relative path of the active book's folder, which is what a
    /// book-relative image path hangs off when the editor resolves one.
    /// </summary>
    /// <summary>
    /// The pictures, with whatever the writer has filed them under.
    ///
    /// The Gallery could search file names and nothing else, so four hundred
    /// references were navigable only by whatever the browser called them when
    /// they were saved.
    /// </summary>
    [JsonRpcMethod("gallery/catalog")]
    public async Task<GalleryCatalogDto> CatalogAsync()
    {
        var catalog = await _catalog.LoadAsync();
        var filed = catalog.Entries.ToDictionary(
            e => e.Path, StringComparer.OrdinalIgnoreCase);

        return new GalleryCatalogDto(
            [.. _entities.GetProjectImages().Select(path =>
            {
                filed.TryGetValue(path, out var entry);
                return new GalleryFiledImageDto(
                    path,
                    _entities.ResolveProjectRelativeImage(path),
                    entry?.Collection ?? string.Empty,
                    [.. entry?.Tags ?? []]);
            })],
            [.. PictureCatalogService.Collections(catalog)],
            [.. PictureCatalogService.Tags(catalog)]);
    }

    /// <summary>Files a picture into a collection, or out of one when empty.</summary>
    [JsonRpcMethod("gallery/setCollection")]
    public async Task<GalleryCatalogDto> SetCollectionAsync(string imagePath, string? collection)
    {
        await _catalog.SetCollectionAsync(imagePath, collection);
        return await CatalogAsync();
    }

    /// <summary>Replaces what a picture is tagged with.</summary>
    [JsonRpcMethod("gallery/setTags")]
    public async Task<GalleryCatalogDto> SetTagsAsync(string imagePath, string[]? tags)
    {
        await _catalog.SetTagsAsync(imagePath, tags);
        return await CatalogAsync();
    }

    [JsonRpcMethod("gallery/base")]
    public string ImageBase()
    {
        // Resolving a bare file name gives the book folder plus that name;
        // dropping the name leaves exactly the prefix a stored path hangs off.
        var url = _entities.ResolveProjectRelativeImage("x.png");
        var cut = url.LastIndexOf('/');
        return cut < 0 ? string.Empty : url[..(cut + 1)];
    }

    [JsonRpcMethod("research/list")]
    public ResearchItemDto[] ListResearch() =>
        _research.GetAll()
            .OrderBy(r => r.Order)
            .Select(ToDto)
            .ToArray();

    [JsonRpcMethod("research/save")]
    public async Task<ResearchItemDto[]> SaveResearchAsync(
        string? id, string title, string type, string content, string[] tags,
        string[]? entityRefs = null)
    {
        var existing = id == null ? null : _research.GetAll().FirstOrDefault(r => r.Id == id);
        var item = existing ?? new ResearchItem { Order = _research.GetAll().Count };
        item.Title = title;
        item.Type = Enum.Parse<ResearchItemType>(type);
        item.Content = content;
        item.Tags = tags.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (entityRefs != null)
            item.EntityRefs = entityRefs.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().ToList();
        item.UpdatedAt = DateTime.UtcNow;
        await _research.SaveAsync(item);
        return ListResearch();
    }

    /// <summary>
    /// Files a jotted-down thought straight into the project without asking where
    /// it belongs. The note lands in the research library carrying the reserved
    /// <see cref="ResearchItem.InboxTag"/>, which the Research view surfaces as an
    /// "Inbox" so it can be filed properly later. The title is the first line
    /// (trimmed to a sensible length); the body is the whole text.
    /// </summary>
    [JsonRpcMethod("research/quickCapture")]
    public async Task<ResearchItemDto[]> QuickCaptureAsync(string text)
    {
        var body = (text ?? string.Empty).Trim();
        if (body.Length == 0)
            throw new InvalidOperationException("Nothing to capture.");

        await _research.SaveAsync(new ResearchItem
        {
            Title = DeriveTitle(body),
            Type = ResearchItemType.Note,
            Content = body,
            Tags = [ResearchItem.InboxTag],
            Order = _research.GetAll().Count
        });
        Log.Info($"research/quickCapture len={body.Length}.");
        return ListResearch();
    }

    // ── The scratchpad: notes that outlive every project ──

    /// <summary>Loose notes, newest first. Readable with no project open.</summary>
    [JsonRpcMethod("scratchpad/list")]
    public ScratchpadNoteDto[] ListScratchpad()
        => [.. _workspace.Scratchpad.GetAll().Select(
            n => new ScratchpadNoteDto(n.Id, n.Text, n.CreatedAt.ToString("o")))];

    /// <summary>
    /// Jots something down with no project involved.
    ///
    /// Quick Capture writes into the open project's research inbox, so a thought
    /// that arrives before the right project is open had nowhere to go - which is
    /// exactly when thoughts arrive.
    /// </summary>
    [JsonRpcMethod("scratchpad/add")]
    public async Task<ScratchpadNoteDto[]> AddScratchpadAsync(string text)
    {
        await _workspace.Scratchpad.AddAsync(text);
        return ListScratchpad();
    }

    [JsonRpcMethod("scratchpad/delete")]
    public async Task<ScratchpadNoteDto[]> DeleteScratchpadAsync(string id)
    {
        await _workspace.Scratchpad.RemoveAsync(id);
        return ListScratchpad();
    }

    /// <summary>
    /// Moves a scratchpad note into the open project's research inbox, where it
    /// can be filed like anything else. Throws with no project open, because
    /// there is nowhere to put it and silently doing nothing would look like it
    /// worked.
    /// </summary>
    [JsonRpcMethod("scratchpad/fileIntoProject")]
    public async Task<ScratchpadNoteDto[]> FileScratchpadAsync(string id)
    {
        if (_workspace.Projects.CurrentProject == null)
            throw new InvalidOperationException("No project is open.");

        var note = _workspace.Scratchpad.Find(id)
            ?? throw new InvalidOperationException("No such note.");

        await QuickCaptureAsync(note.Text);
        await _workspace.Scratchpad.RemoveAsync(id);
        return ListScratchpad();
    }

    /// <summary>First line of the capture, collapsed and clipped to a title-sized
    /// string. Never empty — callers guarantee non-blank input.</summary>
    internal static string DeriveTitle(string body)
    {
        var firstLine = body
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.Trim().Length > 0)
            ?.Trim() ?? body;
        return firstLine.Length <= 60 ? firstLine : firstLine[..60].TrimEnd() + "...";
    }

    /// <summary>Looks up the page title behind a URL so a link item can carry a
    /// readable name. Returns null when offline or the title cannot be read; the
    /// caller then keeps the URL as the title.</summary>
    [JsonRpcMethod("research/fetchLinkTitle")]
    public async Task<string?> FetchLinkTitleAsync(string url, CancellationToken cancellationToken)
    {
        var title = await new LinkTitleService().FetchTitleAsync(url, cancellationToken);
        Log.Info($"research/fetchLinkTitle resolved={title != null}.");
        return title;
    }

    [JsonRpcMethod("research/import")]
    public async Task<ResearchItemDto[]> ImportResearchAsync(string sourcePath)
    {
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        var type = ext switch
        {
            ".pdf" => ResearchItemType.Pdf,
            _ when ImageExtensions.Contains(ext) => ResearchItemType.Image,
            _ when AudioExtensions.Contains(ext) => ResearchItemType.Audio,
            _ when VideoExtensions.Contains(ext) => ResearchItemType.Video,
            _ => ResearchItemType.File
        };

        var rel = await _research.ImportFileAsync(sourcePath);
        var item = new ResearchItem
        {
            Title = Path.GetFileNameWithoutExtension(sourcePath),
            Type = type,
            Content = rel
        };
        await _research.SaveAsync(item);
        return ListResearch();
    }

    /// <summary>
    /// Where an item stands and what the writer thinks of it.
    ///
    /// Separate from research/save because this is what gets changed while
    /// reading down the shelf - a status and a star, without opening the editor
    /// and without touching the prose.
    /// </summary>
    [JsonRpcMethod("research/setLifecycle")]
    public async Task<ResearchItemDto[]> SetLifecycleAsync(string id, string? status, int? rating)
    {
        var item = _research.GetAll().FirstOrDefault(r => r.Id == id);
        if (item == null) return ListResearch();

        if (status != null)
        {
            item.Status = Enum.TryParse<ResearchStatus>(status, true, out var parsed)
                ? parsed
                : ResearchStatus.None;
        }
        // Zero is "unrated", which is a real answer and how a rating is undone.
        if (rating.HasValue) item.Rating = Math.Clamp(rating.Value, 0, 5);

        item.UpdatedAt = DateTime.UtcNow;
        await _research.SaveAsync(item);
        return ListResearch();
    }

    /// <summary>
    /// Links two research items, both ways.
    ///
    /// A one-way link is discoverable only from the item that has it, and the
    /// one worth finding is usually the other end - the question that this
    /// source answers is what somebody is reading when they need the source.
    /// </summary>
    [JsonRpcMethod("research/link")]
    public async Task<ResearchItemDto[]> LinkResearchAsync(string id, string otherId, bool linked)
    {
        if (string.Equals(id, otherId, StringComparison.Ordinal)) return ListResearch();

        var item = _research.GetAll().FirstOrDefault(r => r.Id == id);
        var other = _research.GetAll().FirstOrDefault(r => r.Id == otherId);
        if (item == null || other == null) return ListResearch();

        foreach (var (from, to) in new[] { (item, otherId), (other, id) })
        {
            var links = from.RelatedIds ?? [];
            if (linked && !links.Contains(to, StringComparer.Ordinal)) links.Add(to);
            if (!linked) links.RemoveAll(x => string.Equals(x, to, StringComparison.Ordinal));
            from.RelatedIds = links.Count > 0 ? links : null;
            from.UpdatedAt = DateTime.UtcNow;
            await _research.SaveAsync(from);
        }

        return ListResearch();
    }

    [JsonRpcMethod("research/delete")]
    public async Task<ResearchItemDto[]> DeleteResearchAsync(string id)
    {
        await _research.DeleteAsync(id);

        // Links to it go too. A reference to an item that is gone reads as a
        // source somebody can still open, which it is not.
        foreach (var other in _research.GetAll())
        {
            if (other.RelatedIds?.Remove(id) != true) continue;
            if (other.RelatedIds.Count == 0) other.RelatedIds = null;
            await _research.SaveAsync(other);
        }

        return ListResearch();
    }

    private ResearchItemDto ToDto(ResearchItem r)
    {
        var isFile = r.Type is ResearchItemType.File or ResearchItemType.Image
            or ResearchItemType.Pdf or ResearchItemType.Audio or ResearchItemType.Video;
        var (size, modified) = isFile && !string.IsNullOrWhiteSpace(r.Content)
            ? ReadMetadata(_research.GetAbsolutePath(r.Content))
            : (string.Empty, string.Empty);
        return new ResearchItemDto(
            r.Id, r.Title, r.Type.ToString(), r.Content, r.Tags.ToArray(), size, modified,
            r.EntityRefs.ToArray(), r.Status.ToString(), r.Rating,
            r.RelatedIds?.ToArray() ?? []);
    }

    // Reads on-disk file metadata for imported research files. A missing target
    // (item points at a file that was removed) yields empty strings so the UI
    // simply omits the metadata line.
    private static (string Size, string Modified) ReadMetadata(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
            return (string.Empty, string.Empty);
        var info = new FileInfo(absolutePath);
        return (FormatSize(info.Length),
            info.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
    }

    // Human-readable byte size. Kept internal + static so the branch table is
    // unit-testable without materializing multi-megabyte fixture files.
    internal static string FormatSize(long bytes)
    {
        if (bytes < 0) return string.Empty;
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return (bytes / 1024.0).ToString("F1", CultureInfo.InvariantCulture) + " KB";
        return (bytes / 1024.0 / 1024.0).ToString("F2", CultureInfo.InvariantCulture) + " MB";
    }
}

/// <summary>One loose note kept outside every project.</summary>
public sealed record ScratchpadNoteDto(string Id, string Text, string CreatedAt);

public sealed record ResearchItemDto(
    string Id, string Title, string Type, string Content, IReadOnlyList<string> Tags,
    string FileSize, string Modified, IReadOnlyList<string> EntityRefs,
    /// <summary>"None", "Open", "InProgress" or "Resolved".</summary>
    string Status,
    /// <summary>0 for unrated, 1-5 otherwise.</summary>
    int Rating,
    /// <summary>Ids of other research items this one refers to.</summary>
    IReadOnlyList<string> RelatedIds);

public sealed record GalleryImageDto(string Path, string Url);

/// <summary>A picture with whatever the writer has filed it under.</summary>
public sealed record GalleryFiledImageDto(
    string Path, string Url, string Collection, string[] Tags);

/// <summary>
/// The pictures and the vocabulary over them. The collection and tag lists
/// come back with the images so a picker can offer what is already in use
/// rather than asking the writer to remember how they spelled it last time.
/// </summary>
public sealed record GalleryCatalogDto(
    GalleryFiledImageDto[] Images, string[] Collections, string[] Tags);
