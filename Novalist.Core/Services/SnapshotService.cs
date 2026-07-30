using System.Text.Json;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public sealed class SnapshotService : ISnapshotService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IProjectService _projectService;
    private readonly IFileService _fileService;

    public SnapshotService(IProjectService projectService, IFileService fileService)
    {
        _projectService = projectService;
        _fileService = fileService;
    }

    public async Task<SceneSnapshot> TakeAsync(ChapterData chapter, SceneData scene, string label)
    {
        var content = await _projectService.ReadSceneContentAsync(chapter, scene);
        var snapshot = new SceneSnapshot
        {
            SceneId = scene.Id,
            ChapterGuid = chapter.Guid,
            CreatedAt = DateTime.UtcNow,
            Label = label ?? string.Empty,
            WordCount = scene.WordCount,
            Content = content,
            Meta = CaptureMeta(scene)
        };

        var dir = await EnsureSceneDirAsync(scene);
        var fileName = $"{snapshot.CreatedAt:yyyyMMdd-HHmmssfff}-{snapshot.Id}.json";
        var path = _fileService.CombinePath(dir, fileName);
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await _fileService.WriteTextAsync(path, json);
        return snapshot;
    }

    /// <summary>
    /// The scene as it stands, for a snapshot to restore later. Copies the
    /// lists rather than referencing them: a snapshot that shared the scene's
    /// plotline list would follow every later edit to it.
    /// </summary>
    private static SceneSnapshotMeta CaptureMeta(SceneData scene) => new()
    {
        Title = scene.Title,
        Synopsis = scene.Synopsis,
        Notes = scene.Notes,
        Pov = scene.AnalysisOverrides?.Pov,
        Stage = scene.Stage,
        LabelKey = scene.LabelKey,
        StoryDate = scene.Date,
        PlotlineIds = scene.PlotlineIds == null ? null : [.. scene.PlotlineIds],
        Tags = scene.AnalysisOverrides?.Tags == null ? null : [.. scene.AnalysisOverrides.Tags]
    };

    public async Task<IReadOnlyList<SceneSnapshot>> ListAsync(SceneData scene)
    {
        var dir = GetSceneDir(scene);
        if (dir == null || !await _fileService.DirectoryExistsAsync(dir))
            return Array.Empty<SceneSnapshot>();

        var files = await _fileService.GetFilesAsync(dir, "*.json");
        var result = new List<SceneSnapshot>(files.Count);
        foreach (var file in files)
        {
            try
            {
                var json = await _fileService.ReadTextAsync(file);
                var snap = JsonSerializer.Deserialize<SceneSnapshot>(json);
                if (snap != null)
                    result.Add(snap);
            }
            catch { }
        }

        return result.OrderByDescending(s => s.CreatedAt).ToList();
    }

    public async Task<SceneSnapshot?> LoadAsync(SceneData scene, string snapshotId)
    {
        var snapshots = await ListAsync(scene);
        return snapshots.FirstOrDefault(s => string.Equals(s.Id, snapshotId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> RestoreAsync(ChapterData chapter, SceneData scene, string snapshotId)
    {
        var snap = await LoadAsync(scene, snapshotId);
        if (snap == null)
            return false;

        // Auto-snapshot the current state before restore so the user can undo.
        await TakeAsync(chapter, scene, "Auto-snapshot before restore");
        await _projectService.WriteSceneContentAsync(chapter, scene, snap.Content);
        scene.WordCount = snap.WordCount;
        ApplyMeta(scene, snap.Meta);
        await _projectService.SaveScenesAsync();
        return true;
    }

    /// <summary>
    /// Puts the scene's own fields back. A snapshot taken before snapshots
    /// carried them has no meta at all, and a field it does not carry is left
    /// as it is - restoring an old prose version must not blank a synopsis
    /// written since.
    /// </summary>
    private static void ApplyMeta(SceneData scene, SceneSnapshotMeta? meta)
    {
        if (meta == null) return;
        if (meta.Title != null) scene.Title = meta.Title;
        if (meta.Synopsis != null) scene.Synopsis = meta.Synopsis;
        if (meta.Notes != null) scene.Notes = meta.Notes;
        if (meta.Stage != null) scene.Stage = meta.Stage;
        if (meta.LabelKey != null) scene.LabelKey = meta.LabelKey;
        if (meta.StoryDate != null) scene.Date = meta.StoryDate;
        if (meta.PlotlineIds != null) scene.PlotlineIds = [.. meta.PlotlineIds];
        if (meta.Pov != null || meta.Tags != null)
        {
            scene.AnalysisOverrides ??= new SceneAnalysisOverrides();
            if (meta.Pov != null) scene.AnalysisOverrides.Pov = meta.Pov;
            if (meta.Tags != null) scene.AnalysisOverrides.Tags = [.. meta.Tags];
        }
    }

    public async Task DeleteAsync(SceneData scene, string snapshotId)
    {
        var dir = GetSceneDir(scene);
        if (dir == null || !await _fileService.DirectoryExistsAsync(dir))
            return;

        var files = await _fileService.GetFilesAsync(dir, "*.json");
        foreach (var file in files)
        {
            var name = _fileService.GetFileNameWithoutExtension(file);
            if (name.EndsWith(snapshotId, StringComparison.OrdinalIgnoreCase))
            {
                await _fileService.DeleteFileAsync(file);
                return;
            }
        }
    }

    public async Task<IReadOnlyList<ProjectSnapshot>> ListAllAsync()
    {
        var rows = new List<ProjectSnapshot>();
        foreach (var chapter in _projectService.GetChaptersOrdered())
        {
            foreach (var scene in _projectService.GetScenesForChapter(chapter.Guid))
            {
                foreach (var snapshot in await ListAsync(scene))
                {
                    rows.Add(new ProjectSnapshot(
                        snapshot, chapter.Guid, chapter.Title, scene.Id, scene.Title));
                }
            }
        }
        return [.. rows.OrderByDescending(r => r.Snapshot.CreatedAt)];
    }

    public async Task<bool> RenameAsync(SceneData scene, string snapshotId, string label)
    {
        var dir = GetSceneDir(scene);
        if (dir == null || !await _fileService.DirectoryExistsAsync(dir)) return false;

        foreach (var file in await _fileService.GetFilesAsync(dir, "*.json"))
        {
            var name = _fileService.GetFileNameWithoutExtension(file);
            if (!name.EndsWith(snapshotId, StringComparison.OrdinalIgnoreCase)) continue;

            SceneSnapshot? snapshot;
            try
            {
                snapshot = JsonSerializer.Deserialize<SceneSnapshot>(
                    await _fileService.ReadTextAsync(file));
            }
            catch (JsonException)
            {
                return false;
            }
            if (snapshot == null) return false;

            snapshot.Label = label ?? string.Empty;
            await _fileService.WriteTextAsync(file, JsonSerializer.Serialize(snapshot, JsonOptions));
            return true;
        }
        return false;
    }

    public async Task<int> DeleteByLabelAsync(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return 0;

        var removed = 0;
        foreach (var chapter in _projectService.GetChaptersOrdered())
            foreach (var scene in _projectService.GetScenesForChapter(chapter.Guid))
                foreach (var snapshot in await ListAsync(scene))
                {
                    if (!string.Equals(snapshot.Label, label, StringComparison.Ordinal)) continue;
                    await DeleteAsync(scene, snapshot.Id);
                    removed++;
                }
        return removed;
    }

    public async Task<int> PruneAsync(int keepPerScene, int olderThanDays, bool dropOrphans)
    {
        var root = SnapshotRoot();
        if (root == null || !await _fileService.DirectoryExistsAsync(root)) return 0;

        var live = new Dictionary<string, SceneData>(StringComparer.OrdinalIgnoreCase);
        foreach (var chapter in _projectService.GetChaptersOrdered())
            foreach (var scene in _projectService.GetScenesForChapter(chapter.Guid))
                live[scene.Id] = scene;

        var removed = 0;
        var cutoff = olderThanDays > 0
            ? DateTime.UtcNow.AddDays(-olderThanDays)
            : (DateTime?)null;

        foreach (var dir in await _fileService.GetDirectoriesAsync(root))
        {
            var sceneId = _fileService.GetFileName(dir);
            if (!live.TryGetValue(sceneId, out var scene))
            {
                // A folder whose scene is gone: nothing can ever reach these
                // again, which is why they pile up unnoticed.
                if (!dropOrphans) continue;
                removed += (await _fileService.GetFilesAsync(dir, "*.json")).Count;
                await _fileService.DeleteDirectoryAsync(dir);
                continue;
            }

            var snapshots = await ListAsync(scene);
            for (var i = 0; i < snapshots.Count; i++)
            {
                var tooMany = keepPerScene > 0 && i >= keepPerScene;
                var tooOld = cutoff != null && snapshots[i].CreatedAt < cutoff;
                if (!tooMany && !tooOld) continue;
                await DeleteAsync(scene, snapshots[i].Id);
                removed++;
            }
        }
        return removed;
    }

    /// <summary>The book's snapshot folder, or null with no project open.</summary>
    private string? SnapshotRoot()
    {
        var book = _projectService.ActiveBook;
        var root = _projectService.ActiveDraftRoot ?? _projectService.ActiveBookRoot;
        return book == null || root == null
            ? null
            : _fileService.CombinePath(root, book.SnapshotFolder);
    }

    private string? GetSceneDir(SceneData scene)
    {
        var book = _projectService.ActiveBook;
        var root = _projectService.ActiveDraftRoot ?? _projectService.ActiveBookRoot;
        if (book == null || root == null)
            return null;

        return _fileService.CombinePath(root, book.SnapshotFolder, scene.Id);
    }

    private async Task<string> EnsureSceneDirAsync(SceneData scene)
    {
        var dir = GetSceneDir(scene)
            ?? throw new InvalidOperationException("No active project/book.");
        await _fileService.CreateDirectoryAsync(dir);
        return dir;
    }
}
