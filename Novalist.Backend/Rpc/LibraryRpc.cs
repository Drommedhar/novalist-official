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
        _entities = new EntityService(workspace.Projects);
        _research = new ResearchService(workspace.Projects, workspace.FileService);
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

    [JsonRpcMethod("research/delete")]
    public async Task<ResearchItemDto[]> DeleteResearchAsync(string id)
    {
        await _research.DeleteAsync(id);
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
            r.EntityRefs.ToArray());
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

public sealed record ResearchItemDto(
    string Id, string Title, string Type, string Content, IReadOnlyList<string> Tags,
    string FileSize, string Modified, IReadOnlyList<string> EntityRefs);

public sealed record GalleryImageDto(string Path, string Url);
