using Novalist.Core.Models;
using Novalist.Sdk.Services;

namespace Novalist.Backend.Extensions;

/// <summary>
/// The project as files and stored versions. Snapshots, drafts other than the
/// open one, and the raw project folder - the three things backup, comparison
/// and archive tooling needs and none of which the SDK could reach.
/// </summary>
public sealed partial class HostServices
{
    private Core.Services.SnapshotService Snapshots
        => new(_projectService, _fileService);

    async Task<IReadOnlyList<SnapshotInfo>> IExtensionArchiveService.ListSnapshotsAsync(
        string chapterGuid, string sceneId)
    {
        var scene = FindScene(chapterGuid, sceneId);
        if (scene == null) return [];
        return [.. (await Snapshots.ListAsync(scene)).Select(s => new SnapshotInfo
        {
            Id = s.Id,
            ChapterGuid = s.ChapterGuid,
            SceneId = s.SceneId,
            Label = s.Label,
            CreatedAt = s.CreatedAt,
            WordCount = s.WordCount
        })];
    }

    async Task<string?> IExtensionArchiveService.ReadSnapshotAsync(
        string chapterGuid, string sceneId, string snapshotId)
    {
        var scene = FindScene(chapterGuid, sceneId);
        if (scene == null) return null;
        return (await Snapshots.LoadAsync(scene, snapshotId))?.Content;
    }

    async Task<string?> IExtensionArchiveService.TakeSnapshotAsync(
        string chapterGuid, string sceneId, string label)
    {
        var chapter = _projectService.GetChaptersOrdered().FirstOrDefault(c => c.Guid == chapterGuid);
        var scene = FindScene(chapterGuid, sceneId);
        if (chapter == null || scene == null) return null;
        return (await Snapshots.TakeAsync(chapter, scene, label ?? string.Empty)).Id;
    }

    async Task<bool> IExtensionArchiveService.RestoreSnapshotAsync(
        string chapterGuid, string sceneId, string snapshotId)
    {
        var chapter = _projectService.GetChaptersOrdered().FirstOrDefault(c => c.Guid == chapterGuid);
        var scene = FindScene(chapterGuid, sceneId);
        if (chapter == null || scene == null) return false;
        // The same refusal prose writes get: the editor holds newer text and
        // would autosave over the restore, so it would not survive anyway.
        if (_editing.IsBusy(chapterGuid, sceneId)) return false;

        var restored = await Snapshots.RestoreAsync(chapter, scene, snapshotId);
        if (restored) ProjectStructureChanged?.Invoke();
        return restored;
    }

    async Task<IReadOnlyList<ChapterInfo>> IExtensionArchiveService.GetChaptersOfDraftAsync(
        string draftId)
    {
        var book = _projectService.ActiveBook;
        if (book == null || book.Drafts.All(d => d.Id != draftId)) return [];

        var chapters = await ChaptersOfDraftAsync(book, draftId);
        return [.. chapters.OrderBy(c => c.Order).Select(c => new ChapterInfo
        {
            Guid = c.Guid,
            Title = c.Title,
            Order = c.Order,
            Date = c.Date ?? string.Empty
        })];
    }

    async Task<string?> IExtensionArchiveService.ReadSceneOfDraftAsync(
        string draftId, string chapterGuid, string sceneId)
    {
        var book = _projectService.ActiveBook;
        if (book == null || book.Drafts.All(d => d.Id != draftId)) return null;

        // The read itself has to happen inside the swap: the scene's folder is
        // resolved from the book's active draft, so looking the scene up in one
        // draft and then reading it back outside returns the open draft's text
        // under the other draft's name - the exact wrong answer for the
        // comparison this call exists to make possible.
        return await AsDraftAsync(book, draftId, async () =>
        {
            var chapter = (await _projectService.LoadChaptersForAsync(book))
                .FirstOrDefault(c => c.Guid == chapterGuid);
            if (chapter == null) return null;

            var manifest = await _projectService.LoadScenesManifestForAsync(book);
            var scene = manifest != null && manifest.Chapters.TryGetValue(chapterGuid, out var scenes)
                ? scenes.FirstOrDefault(s => s.Id == sceneId)
                : null;
            if (scene == null) return null;

            return await _projectService.ReadSceneContentForAsync(book, chapter, scene);
        });
    }

    /// <summary>
    /// A draft other than the open one is read by pointing the book at it for
    /// the length of the read and putting it back. Cheaper and far less
    /// error-prone than a second set of per-draft path helpers, and the caller
    /// never sees a project that moved.
    /// </summary>
    private Task<List<ChapterData>> ChaptersOfDraftAsync(BookData book, string draftId)
        => AsDraftAsync(book, draftId, () => _projectService.LoadChaptersForAsync(book));

    private static async Task<T> AsDraftAsync<T>(BookData book, string draftId, Func<Task<T>> read)
    {
        var previous = book.ActiveDraftId;
        book.ActiveDraftId = draftId;
        try { return await read(); }
        finally { book.ActiveDraftId = previous; }
    }

    IReadOnlyList<ProjectFileInfo> IExtensionArchiveService.ListProjectFiles()
    {
        var root = _projectService.ProjectRoot;
        if (root == null || !Directory.Exists(root)) return [];

        return [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Select(f => new ProjectFileInfo
            {
                // Forward slashes so a path written on Windows still means the
                // same file to something reading the archive elsewhere.
                RelativePath = Path.GetRelativePath(root, f.FullName)
                    .Replace(Path.DirectorySeparatorChar, '/'),
                SizeBytes = f.Length,
                ModifiedAt = f.LastWriteTimeUtc
            })
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal)];
    }

    async Task<byte[]?> IExtensionArchiveService.ReadProjectFileAsync(string relativePath)
    {
        var full = ResolveInProject(relativePath);
        return full != null && File.Exists(full) ? await File.ReadAllBytesAsync(full) : null;
    }

    /// <summary>
    /// An absolute path inside the project, or null. A relative path that
    /// climbs out would otherwise let an extension read anything on the
    /// machine through a call meant for the project's own files.
    /// </summary>
    private string? ResolveInProject(string relativePath)
    {
        var root = _projectService.ProjectRoot;
        if (root == null || string.IsNullOrWhiteSpace(relativePath)) return null;
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        return full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)
            ? full
            : null;
    }
}
