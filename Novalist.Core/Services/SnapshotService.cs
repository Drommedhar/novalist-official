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
            Content = content
        };

        var dir = await EnsureSceneDirAsync(scene);
        var fileName = $"{snapshot.CreatedAt:yyyyMMdd-HHmmssfff}-{snapshot.Id}.json";
        var path = _fileService.CombinePath(dir, fileName);
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await _fileService.WriteTextAsync(path, json);
        return snapshot;
    }

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
        await _projectService.SaveScenesAsync();
        return true;
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
