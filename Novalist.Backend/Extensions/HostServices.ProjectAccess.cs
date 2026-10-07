using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Novalist.Core;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;
using Novalist.Sdk.Services;

namespace Novalist.Backend.Extensions;

public sealed partial class HostServices
{
    // ── IExtensionFileService ──────────────────────────────────────

    Task<string> IExtensionFileService.ReadTextAsync(string path) => _fileService.ReadTextAsync(path);
    Task IExtensionFileService.WriteTextAsync(string path, string content) => _fileService.WriteTextAsync(path, content);
    Task<bool> IExtensionFileService.ExistsAsync(string path) => _fileService.ExistsAsync(path);
    Task<bool> IExtensionFileService.DirectoryExistsAsync(string path) => _fileService.DirectoryExistsAsync(path);
    Task IExtensionFileService.CreateDirectoryAsync(string path) => _fileService.CreateDirectoryAsync(path);
    Task<IReadOnlyList<string>> IExtensionFileService.GetFilesAsync(string directory, string pattern, bool recursive) => _fileService.GetFilesAsync(directory, pattern, recursive);
    Task<IReadOnlyList<string>> IExtensionFileService.GetDirectoriesAsync(string directory) => _fileService.GetDirectoriesAsync(directory);
    string IExtensionFileService.CombinePath(params string[] parts) => _fileService.CombinePath(parts);
    string IExtensionFileService.GetFileName(string path) => _fileService.GetFileName(path);
    string IExtensionFileService.GetFileNameWithoutExtension(string path) => _fileService.GetFileNameWithoutExtension(path);
    string IExtensionFileService.GetDirectoryName(string path) => _fileService.GetDirectoryName(path);

    // ── IExtensionProjectService ───────────────────────────────────

    string? IExtensionProjectService.ProjectRoot => _projectService.ProjectRoot;
    string? IExtensionProjectService.ActiveBookRoot => _projectService.ActiveBookRoot;
    string? IExtensionProjectService.WorldBibleRoot => _projectService.WorldBibleRoot;
    bool IExtensionProjectService.IsProjectLoaded => _projectService.IsProjectLoaded;
    Sdk.Services.SceneInfo? IExtensionProjectService.CurrentScene => _currentScene;

    async Task<string> IExtensionProjectService.ReadSceneContentAsync(string chapterGuid, string sceneId)
    {
        var manifest = _projectService.ScenesManifest;
        if (manifest == null)
            return string.Empty;

        if (!manifest.Chapters.TryGetValue(chapterGuid, out var scenes))
            return string.Empty;

        var scene = scenes.FirstOrDefault(s => s.Id == sceneId);
        if (scene == null)
            return string.Empty;

        var chapter = _projectService.GetChaptersOrdered().FirstOrDefault(c => c.Guid == chapterGuid);
        if (chapter == null)
            return string.Empty;

        return await _projectService.ReadSceneContentAsync(chapter, scene);
    }

    Task<string> IExtensionProjectService.GetSceneSynopsisAsync(string chapterGuid, string sceneId)
    {
        var manifest = _projectService.ScenesManifest;
        if (manifest == null || !manifest.Chapters.TryGetValue(chapterGuid, out var scenes))
            return Task.FromResult(string.Empty);
        var scene = scenes.FirstOrDefault(s => s.Id == sceneId);
        return Task.FromResult(scene?.Synopsis ?? string.Empty);
    }

    async Task IExtensionProjectService.SetSceneSynopsisAsync(string chapterGuid, string sceneId, string synopsis)
    {
        var manifest = _projectService.ScenesManifest;
        if (manifest == null || !manifest.Chapters.TryGetValue(chapterGuid, out var scenes)) return;
        var scene = scenes.FirstOrDefault(s => s.Id == sceneId);
        if (scene == null) return;
        scene.Synopsis = string.IsNullOrWhiteSpace(synopsis) ? null : synopsis.Trim();
        await _projectService.SaveScenesAsync();
    }

    async Task<string> IExtensionProjectService.CreateChapterAsync(string title)
    {
        var chapter = await _projectService.CreateChapterAsync(title ?? string.Empty);
        ProjectStructureChanged?.Invoke();
        return chapter.Guid;
    }

    async Task<string> IExtensionProjectService.CreateSceneAsync(string chapterGuid, string title)
    {
        // A scene under a chapter that does not exist would be unreachable, so
        // the caller gets an empty id rather than an orphan.
        if (!_projectService.GetChaptersOrdered().Any(c => c.Guid == chapterGuid))
            return string.Empty;

        var scene = await _projectService.CreateSceneAsync(chapterGuid, title ?? string.Empty);
        ProjectStructureChanged?.Invoke();
        return scene.Id;
    }

    Task<bool> IExtensionProjectService.IsSceneBusyAsync(string chapterGuid, string sceneId)
        => Task.FromResult(_editing.IsBusy(chapterGuid, sceneId));

    async Task IExtensionProjectService.WriteSceneContentAsync(
        string chapterGuid, string sceneId, string html)
    {
        // Refused rather than merged: the editor holds the newer text and would
        // autosave over this anyway, so the write is not just unsafe, it is
        // pointless. An extension that would rather skip than fail asks first.
        if (_editing.IsBusy(chapterGuid, sceneId))
        {
            throw new InvalidOperationException(
                "That scene is open with unsaved changes; it cannot be written to.");
        }

        var chapter = _projectService.GetChaptersOrdered().FirstOrDefault(c => c.Guid == chapterGuid);
        var scene = chapter == null
            ? null
            : _projectService.GetScenesForChapter(chapterGuid).FirstOrDefault(sc => sc.Id == sceneId);
        if (chapter == null || scene == null) return;

        await _projectService.WriteSceneContentAsync(chapter, scene, html ?? string.Empty);
        // The manifest carries the word count the binder shows, so it has to
        // catch up with what was just written.
        scene.WordCount = Workspace.CountWords(Core.Utilities.TextDiff.StripHtml(html ?? string.Empty));
        await _projectService.SaveScenesAsync();
        // The binder shows word counts, so a prose write moves the shape too.
        ProjectStructureChanged?.Invoke();
    }

    IReadOnlyList<Sdk.Services.ChapterInfo> IExtensionProjectService.GetChaptersOrdered()
    {
        return _projectService.GetChaptersOrdered()
            .Select(c => new Sdk.Services.ChapterInfo
            {
                Guid = c.Guid,
                Title = c.Title,
                Order = c.Order,
                Date = c.Date ?? string.Empty
            })
            .ToList();
    }

    IReadOnlyList<Sdk.Services.SceneInfo> IExtensionProjectService.GetScenesForChapter(string chapterGuid)
    {
        var manifest = _projectService.ScenesManifest;
        if (manifest == null)
            return [];

        if (!manifest.Chapters.TryGetValue(chapterGuid, out var scenes))
            return [];

        var chapter = _projectService.GetChaptersOrdered().FirstOrDefault(c => c.Guid == chapterGuid);
        var chapterTitle = chapter?.Title ?? string.Empty;

        return scenes
            .Select(s => new Sdk.Services.SceneInfo
            {
                Id = s.Id,
                Title = s.Title,
                ChapterGuid = chapterGuid,
                ChapterTitle = chapterTitle,
                WordCount = s.WordCount,
                Inactive = s.Inactive,
                ExcludeFromExport = s.ExcludeFromExport
            })
            .ToList();
    }
}
