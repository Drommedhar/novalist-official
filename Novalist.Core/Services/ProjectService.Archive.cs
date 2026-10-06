using System.Text.Json;
using System.Text.RegularExpressions;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public partial class ProjectService
{
    private string GetArchiveFolderPath()
    {
        if (ActiveBook == null) throw new InvalidOperationException("No active book.");
        var root = ActiveDraftRoot ?? ActiveBookRoot
            ?? throw new InvalidOperationException("No active book root.");
        return _fileService.CombinePath(root, ActiveBook.ChapterFolder, ArchiveFolderName);
    }

    public string GetArchivedSceneFilePath(SceneData scene)
    {
        return _fileService.CombinePath(GetArchiveFolderPath(), scene.FileName);
    }

    public async Task<string> ReadArchivedSceneContentAsync(SceneData scene)
    {
        var path = GetArchivedSceneFilePath(scene);
        if (await _fileService.ExistsAsync(path))
            return FileFrontMatter.Strip(await _fileService.ReadTextAsync(path));
        return string.Empty;
    }

    public IReadOnlyList<SceneData> GetArchivedScenes()
        => ScenesManifest?.Archived ?? new List<SceneData>();

    public async Task ArchiveSceneAsync(string chapterGuid, string sceneId)
    {
        if (ScenesManifest == null || ActiveBook == null || ActiveBookRoot == null) return;
        if (!ScenesManifest.Chapters.TryGetValue(chapterGuid, out var scenes)) return;

        var scene = scenes.FirstOrDefault(s => s.Id == sceneId);
        if (scene == null) return;

        var chapter = ActiveBook.Chapters.FirstOrDefault(c => c.Guid == chapterGuid);
        if (chapter == null) return;

        var sourcePath = GetSceneFilePath(chapter, scene);
        var archiveFolder = GetArchiveFolderPath();
        await _fileService.CreateDirectoryAsync(archiveFolder);

        // Resolve filename collisions in the archive folder by suffixing with the scene id.
        var targetFileName = scene.FileName;
        var targetPath = _fileService.CombinePath(archiveFolder, targetFileName);
        if (await _fileService.ExistsAsync(targetPath))
        {
            var ext = Path.GetExtension(scene.FileName);
            var stem = Path.GetFileNameWithoutExtension(scene.FileName);
            targetFileName = $"{stem}-{scene.Id}{ext}";
            targetPath = _fileService.CombinePath(archiveFolder, targetFileName);
        }

        if (await _fileService.ExistsAsync(sourcePath))
            await _fileService.MoveFileAsync(sourcePath, targetPath);

        scene.FileName = targetFileName;
        scene.OriginChapterGuid = chapterGuid;
        // Recorded before the removal, or the index is the one it no longer has.
        scene.OriginIndex = scenes.IndexOf(scene);
        scene.ArchivedAt = DateTime.UtcNow;
        scene.ChapterGuid = string.Empty;

        scenes.Remove(scene);
        ReindexScenes(scenes);
        ScenesManifest.Archived.Add(scene);

        await SaveScenesAsync();
    }

    /// <summary>
    /// Brings an archived scene back.
    ///
    /// An empty <paramref name="targetChapterGuid"/> means "where it came
    /// from", which is what a writer restoring something almost always wants.
    /// Landing in the origin chapter with no index asked for puts the scene
    /// back in the slot it left, rather than at the end where every restore
    /// used to arrive.
    /// </summary>
    public async Task RestoreArchivedSceneAsync(
        string sceneId, string targetChapterGuid, int? targetIndex)
    {
        if (ScenesManifest == null || ActiveBook == null || ActiveBookRoot == null) return;

        var scene = ScenesManifest.Archived.FirstOrDefault(s => s.Id == sceneId);
        if (scene == null) return;

        var asked = string.IsNullOrWhiteSpace(targetChapterGuid)
            ? scene.OriginChapterGuid ?? string.Empty
            : targetChapterGuid;
        var origin = scene.OriginChapterGuid;

        var targetChapter = ActiveBook.Chapters.FirstOrDefault(c => c.Guid == asked);
        // The chapter it came from can have been deleted since. Falling back to
        // the first chapter beats refusing to restore: the scene exists and the
        // writer asked for it back.
        if (targetChapter == null && string.IsNullOrWhiteSpace(targetChapterGuid))
            targetChapter = ActiveBook.Chapters.OrderBy(c => c.Order).FirstOrDefault();
        if (targetChapter == null) return;

        // Captured here: the fields that say where the scene came from are
        // cleared further down, before the insert position is worked out.
        var homeIndex = targetChapter.Guid == origin ? scene.OriginIndex : null;
        targetChapterGuid = targetChapter.Guid;

        if (!ScenesManifest.Chapters.TryGetValue(targetChapterGuid, out var targetScenes))
        {
            targetScenes = new List<SceneData>();
            ScenesManifest.Chapters[targetChapterGuid] = targetScenes;
        }

        var sourcePath = GetArchivedSceneFilePath(scene);
        // Generate a fresh, non-colliding filename in the target chapter.
        var newFileName = GetNextSceneFileName(targetScenes);
        scene.FileName = newFileName;
        scene.ChapterGuid = targetChapterGuid;
        scene.ArchivedAt = null;
        scene.OriginChapterGuid = null;
        scene.OriginIndex = null;

        var targetPath = GetSceneFilePath(targetChapter, scene);

        await _fileService.CreateDirectoryAsync(GetChapterFolderPath(targetChapter));
        if (await _fileService.ExistsAsync(sourcePath))
            await _fileService.MoveFileAsync(sourcePath, targetPath);

        // Its own slot when it is going home and nobody named one; the end
        // otherwise, because a scene arriving in a chapter it never lived in
        // has no position of its own to claim.
        var wanted = targetIndex ?? homeIndex;
        var insertAt = wanted.HasValue
            ? Math.Clamp(wanted.Value, 0, targetScenes.Count)
            : targetScenes.Count;
        targetScenes.Insert(insertAt, scene);
        ReindexScenes(targetScenes);
        ScenesManifest.Archived.Remove(scene);

        await SaveScenesAsync();
    }

    public async Task DeleteArchivedSceneAsync(string sceneId)
    {
        if (ScenesManifest == null) return;

        var scene = ScenesManifest.Archived.FirstOrDefault(s => s.Id == sceneId);
        if (scene == null) return;

        var path = GetArchivedSceneFilePath(scene);
        if (await _fileService.ExistsAsync(path))
            await _fileService.DeleteFileAsync(path);

        ScenesManifest.Archived.Remove(scene);
        await SaveScenesAsync();
    }
}
