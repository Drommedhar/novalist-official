using Novalist.Core.Models;

namespace Novalist.Core.Services;

public sealed class ResearchService : IResearchService
{
    private const string ResearchFolderName = "Research";

    private readonly IProjectService _projectService;
    private readonly IFileService _fileService;

    public ResearchService(IProjectService projectService, IFileService fileService)
    {
        _projectService = projectService;
        _fileService = fileService;
    }

    public IReadOnlyList<ResearchItem> GetAll()
    {
        var p = _projectService.CurrentProject;
        if (p == null) return Array.Empty<ResearchItem>();
        return p.ResearchItems.OrderBy(r => r.Order).ThenBy(r => r.CreatedAt).ToList();
    }

    /// <param name="previousJson">
    /// What the item said before the caller changed it, from
    /// <see cref="Serialize"/>. Callers edit the very object the project holds,
    /// so the old state has to be taken where the item was still untouched.
    /// Null falls back to comparing what is in the list, which is right for a
    /// caller that built the item from scratch.
    /// </param>
    public async Task SaveAsync(ResearchItem item, string? previousJson = null)
    {
        var p = _projectService.CurrentProject;
        if (p == null) return;
        item.UpdatedAt = DateTime.UtcNow;
        var idx = p.ResearchItems.FindIndex(r => r.Id == item.Id);
        if (idx >= 0)
        {
            // A note pasted over is as lost as a character sheet typed over,
            // and research is where a writer keeps things they cannot rewrite
            // from memory.
            // Compared with the save stamp held level: a version whose only
            // difference is when it was saved is noise, and enough of it pushes
            // the versions that matter out of the list.
            await new EntityHistory(_projectService)
                .RecordAsync(
                    item.Id,
                    previousJson ?? Serialize(p.ResearchItems[idx], item.UpdatedAt),
                    Serialize(item, item.UpdatedAt))
                .ConfigureAwait(false);
            p.ResearchItems[idx] = item;
        }
        else
        {
            item.Order = p.ResearchItems.Count;
            p.ResearchItems.Add(item);
        }
        await _projectService.SaveProjectAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// An item as stored. Indented, because a revision is something a person
    /// may end up reading in a file listing.
    /// </summary>
    /// <param name="stampedAt">
    /// A save time to hold level across both sides of a comparison, so two
    /// items that differ only in when they were saved serialise the same.
    /// </param>
    public static string Serialize(ResearchItem item, DateTime? stampedAt = null)
    {
        var forComparison = stampedAt == null ? item : Clone(item, stampedAt.Value);
        return System.Text.Json.JsonSerializer.Serialize(
            forComparison, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    private static ResearchItem Clone(ResearchItem item, DateTime updatedAt)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(item);
        var copy = System.Text.Json.JsonSerializer.Deserialize<ResearchItem>(json)!;
        copy.UpdatedAt = updatedAt;
        return copy;
    }

    /// <summary>An item's earlier versions, newest first.</summary>
    public IReadOnlyList<EntityRevision> History(string itemId)
        => new EntityHistory(_projectService).List(itemId);

    /// <summary>
    /// Puts an earlier version of an item back. False when the revision is no
    /// longer there.
    /// </summary>
    public async Task<bool> RestoreAsync(string itemId, string revisionId)
    {
        var stored = await new EntityHistory(_projectService)
            .ReadAsync(itemId, revisionId).ConfigureAwait(false);
        if (stored == null) return false;

        var restored = System.Text.Json.JsonSerializer.Deserialize<ResearchItem>(stored);
        if (restored == null) return false;
        restored.Id = itemId;

        await SaveAsync(restored).ConfigureAwait(false);
        return true;
    }

    public async Task DeleteAsync(string itemId)
    {
        var p = _projectService.CurrentProject;
        if (p == null) return;
        p.ResearchItems.RemoveAll(r => r.Id == itemId);
        await _projectService.SaveProjectAsync().ConfigureAwait(false);
    }

    public async Task<string> ImportFileAsync(string sourcePath)
    {
        var root = _projectService.ProjectRoot
            ?? throw new InvalidOperationException("No project loaded.");
        var dir = _fileService.CombinePath(root, ResearchFolderName);
        await _fileService.CreateDirectoryAsync(dir).ConfigureAwait(false);

        var fileName = _fileService.GetFileName(sourcePath);
        var dest = _fileService.CombinePath(dir, fileName);

        // Avoid clobbering existing files: append numeric suffix.
        var attempt = 1;
        while (await _fileService.ExistsAsync(dest).ConfigureAwait(false))
        {
            var stem = _fileService.GetFileNameWithoutExtension(sourcePath);
            var ext = System.IO.Path.GetExtension(sourcePath);
            dest = _fileService.CombinePath(dir, $"{stem} ({attempt}){ext}");
            attempt++;
        }

        var bytes = await System.IO.File.ReadAllBytesAsync(sourcePath).ConfigureAwait(false);
        await System.IO.File.WriteAllBytesAsync(dest, bytes).ConfigureAwait(false);

        return _fileService.CombinePath(ResearchFolderName, _fileService.GetFileName(dest)).Replace('\\', '/');
    }

    public string GetAbsolutePath(string relativePath)
    {
        var root = _projectService.ProjectRoot;
        if (root == null || string.IsNullOrEmpty(relativePath)) return string.Empty;
        return _fileService.CombinePath(root, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
    }
}
