using System.Globalization;
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

    [JsonRpcMethod("research/list")]
    public ResearchItemDto[] ListResearch() =>
        _research.GetAll()
            .OrderBy(r => r.Order)
            .Select(ToDto)
            .ToArray();

    [JsonRpcMethod("research/save")]
    public async Task<ResearchItemDto[]> SaveResearchAsync(
        string? id, string title, string type, string content, string[] tags)
    {
        var existing = id == null ? null : _research.GetAll().FirstOrDefault(r => r.Id == id);
        var item = existing ?? new ResearchItem { Order = _research.GetAll().Count };
        item.Title = title;
        item.Type = Enum.Parse<ResearchItemType>(type);
        item.Content = content;
        item.Tags = tags.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        item.UpdatedAt = DateTime.UtcNow;
        await _research.SaveAsync(item);
        return ListResearch();
    }

    [JsonRpcMethod("research/import")]
    public async Task<ResearchItemDto[]> ImportResearchAsync(string sourcePath)
    {
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        var type = ext == ".pdf"
            ? ResearchItemType.Pdf
            : ImageExtensions.Contains(ext)
                ? ResearchItemType.Image
                : ResearchItemType.File;

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
        var isFile = r.Type is ResearchItemType.File or ResearchItemType.Image or ResearchItemType.Pdf;
        var (size, modified) = isFile && !string.IsNullOrWhiteSpace(r.Content)
            ? ReadMetadata(_research.GetAbsolutePath(r.Content))
            : (string.Empty, string.Empty);
        return new ResearchItemDto(
            r.Id, r.Title, r.Type.ToString(), r.Content, r.Tags.ToArray(), size, modified);
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
    string FileSize, string Modified);

public sealed record GalleryImageDto(string Path, string Url);
