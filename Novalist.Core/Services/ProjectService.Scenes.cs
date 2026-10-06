using System.Text.Json;
using System.Text.RegularExpressions;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public partial class ProjectService
{
    /// <param name="template">
    /// A scene to start from. Everything it carries is copied onto the new
    /// scene - synopsis, prose, point of view, stage, label, tags, plotlines -
    /// which is the whole difference between a template and a note about one.
    /// </param>
    public async Task<SceneData> CreateSceneAsync(
        string chapterGuid, string sceneTitle, string date = "", SceneTemplate? template = null)
    {
        if (ActiveBook == null || ActiveBookRoot == null || ScenesManifest == null)
            throw new InvalidOperationException("No book active.");

        var chapter = ActiveBook.Chapters.FirstOrDefault(c => c.Guid == chapterGuid)
            ?? throw new ArgumentException($"Chapter not found: {chapterGuid}");

        if (!ScenesManifest.Chapters.TryGetValue(chapterGuid, out var scenes))
        {
            scenes = new List<SceneData>();
            ScenesManifest.Chapters[chapterGuid] = scenes;
        }

        var nextOrder = scenes.Count > 0 ? scenes.Max(s => s.Order) + 1 : 1;
        var fileName = GetNextSceneFileName(scenes);

        var scene = new SceneData
        {
            Title = sceneTitle,
            Order = nextOrder,
            FileName = fileName,
            ChapterGuid = chapterGuid,
            Date = date
        };

        if (template != null) ApplyTemplate(scene, template);
        scenes.Add(scene);

        var scenePath = GetSceneFilePath(chapter, scene);
        // Born stamped with its identity front-matter, and with the template's
        // prose if there is one.
        await _fileService.WriteTextAsync(
            scenePath, FileFrontMatter.Build(scene.Id) + (template?.Content ?? string.Empty));

        await SaveScenesAsync();

        return scene;
    }

    /// <summary>
    /// Copies a template onto a new scene. Lists are copied rather than shared,
    /// or editing the scene's plotlines would rewrite the template.
    /// </summary>
    private static void ApplyTemplate(SceneData scene, SceneTemplate template)
    {
        scene.Synopsis = string.IsNullOrWhiteSpace(template.Synopsis) ? scene.Synopsis : template.Synopsis;
        scene.Stage = template.Stage;
        scene.LabelKey = template.LabelKey;
        if (template.PlotlineIds.Count > 0) scene.PlotlineIds = [.. template.PlotlineIds];

        if (template.Pov == null && template.Tags.Count == 0) return;
        scene.AnalysisOverrides ??= new SceneAnalysisOverrides();
        if (template.Pov != null) scene.AnalysisOverrides.Pov = template.Pov;
        if (template.Tags.Count > 0) scene.AnalysisOverrides.Tags = [.. template.Tags];
    }

    /// <summary>
    /// Captures a scene as a template: what it is, not what it says. The title
    /// is deliberately not copied - a template named after one scene would put
    /// that scene's name on every scene made from it.
    /// </summary>
    public async Task<SceneTemplate> SaveSceneAsTemplateAsync(
        string chapterGuid, string sceneId, string name)
    {
        if (ActiveBook == null) throw new InvalidOperationException("No book active.");

        var chapter = ActiveBook.Chapters.FirstOrDefault(c => c.Guid == chapterGuid)
            ?? throw new ArgumentException($"Chapter not found: {chapterGuid}");
        var scene = GetScenesForChapter(chapterGuid).FirstOrDefault(s => s.Id == sceneId)
            ?? throw new ArgumentException($"Scene not found: {sceneId}");

        var template = new SceneTemplate
        {
            Name = string.IsNullOrWhiteSpace(name) ? scene.Title : name.Trim(),
            Synopsis = scene.Synopsis ?? string.Empty,
            Content = await ReadSceneContentAsync(chapter, scene),
            Pov = scene.AnalysisOverrides?.Pov,
            Stage = scene.Stage,
            LabelKey = scene.LabelKey,
            Tags = [.. scene.AnalysisOverrides?.Tags ?? []],
            PlotlineIds = [.. scene.PlotlineIds ?? []]
        };

        ActiveBook.SceneTemplates.Add(template);
        await SaveProjectAsync();
        return template;
    }

    public async Task SetSceneDateAsync(string chapterGuid, string sceneId, string date)
    {
        if (ScenesManifest == null) return;

        if (!ScenesManifest.Chapters.TryGetValue(chapterGuid, out var scenes)) return;

        var scene = scenes.FirstOrDefault(s => s.Id == sceneId);
        if (scene == null) return;

        scene.Date = date.Trim();
        await SaveScenesAsync();
    }

    public async Task SetSceneFavoriteAsync(string chapterGuid, string sceneId, bool favorite)
    {
        if (ScenesManifest == null) return;
        if (!ScenesManifest.Chapters.TryGetValue(chapterGuid, out var scenes)) return;
        var scene = scenes.FirstOrDefault(s => s.Id == sceneId);
        if (scene == null) return;
        scene.IsFavorite = favorite;
        await SaveScenesAsync();
    }

    public async Task SetSceneLabelColorAsync(string chapterGuid, string sceneId, string? labelColor)
    {
        if (ScenesManifest == null) return;
        if (!ScenesManifest.Chapters.TryGetValue(chapterGuid, out var scenes)) return;
        var scene = scenes.FirstOrDefault(s => s.Id == sceneId);
        if (scene == null) return;
        scene.LabelColor = string.IsNullOrWhiteSpace(labelColor) ? null : labelColor.Trim();
        await SaveScenesAsync();
    }

    public async Task SetSceneDateRangeAsync(string chapterGuid, string sceneId, StoryDateRange? dateRange)
    {
        if (ScenesManifest == null) return;
        if (!ScenesManifest.Chapters.TryGetValue(chapterGuid, out var scenes)) return;
        var scene = scenes.FirstOrDefault(s => s.Id == sceneId);
        if (scene == null) return;
        scene.DateRange = dateRange?.HasValue == true ? dateRange.Clone() : null;
        if (dateRange?.HasValue == true && !string.IsNullOrWhiteSpace(dateRange.Start))
            scene.Date = dateRange.Start;
        await SaveScenesAsync();
    }

    public async Task SetSceneAnalysisOverridesAsync(string chapterGuid, string sceneId, SceneAnalysisOverrides? overrides)
    {
        if (ScenesManifest == null) return;

        if (!ScenesManifest.Chapters.TryGetValue(chapterGuid, out var scenes)) return;

        var scene = scenes.FirstOrDefault(candidate => candidate.Id == sceneId);
        if (scene == null) return;

        scene.AnalysisOverrides = overrides?.HasValues == true ? overrides.Clone() : null;
        await SaveScenesAsync();
    }

    public async Task DeleteSceneAsync(string chapterGuid, string sceneId)
    {
        if (ScenesManifest == null || ActiveBook == null) return;

        if (ScenesManifest.Chapters.TryGetValue(chapterGuid, out var scenes))
        {
            var scene = scenes.FirstOrDefault(s => s.Id == sceneId);
            if (scene != null)
            {
                var chapter = ActiveBook.Chapters.FirstOrDefault(c => c.Guid == chapterGuid);
                string? sceneFilePath = chapter != null ? GetSceneFilePath(chapter, scene) : null;

                scenes.Remove(scene);
                var ordered = scenes.OrderBy(s => s.Order).ToList();
                for (int i = 0; i < ordered.Count; i++)
                    ordered[i].Order = i + 1;

                if (sceneFilePath != null)
                    await _fileService.DeleteFileAsync(sceneFilePath);
            }
        }

        await SaveScenesAsync();
    }

    public async Task ReorderSceneAsync(string chapterGuid, string sceneId, int newOrder)
    {
        if (ScenesManifest == null) return;

        if (!ScenesManifest.Chapters.TryGetValue(chapterGuid, out var scenes)) return;

        var scene = scenes.FirstOrDefault(s => s.Id == sceneId);
        if (scene == null) return;

        var oldOrder = scene.Order;
        if (oldOrder == newOrder) return;

        foreach (var s in scenes)
        {
            if (s.Id == sceneId)
            {
                s.Order = newOrder;
            }
            else if (oldOrder < newOrder && s.Order > oldOrder && s.Order <= newOrder)
            {
                s.Order--;
            }
            else if (oldOrder > newOrder && s.Order >= newOrder && s.Order < oldOrder)
            {
                s.Order++;
            }
        }

        await SaveScenesAsync();
    }

    public async Task MoveScenesAsync(IReadOnlyList<string> sceneIds, string targetChapterGuid, int targetIndex)
    {
        if (ScenesManifest == null || ActiveBook == null || ActiveBookRoot == null || sceneIds.Count == 0) return;

        var targetChapter = ActiveBook.Chapters.FirstOrDefault(chapter => chapter.Guid == targetChapterGuid);
        if (targetChapter == null) return;

        if (!ScenesManifest.Chapters.TryGetValue(targetChapterGuid, out var targetScenes))
        {
            targetScenes = new List<SceneData>();
            ScenesManifest.Chapters[targetChapterGuid] = targetScenes;
        }

        var sceneSet = new HashSet<string>(sceneIds);
        var moving = new List<(SceneData Scene, string SourceChapterGuid)>();

        foreach (var chapterEntry in ScenesManifest.Chapters)
        {
            foreach (var scene in chapterEntry.Value.Where(scene => sceneSet.Contains(scene.Id)).OrderBy(scene => scene.Order))
            {
                moving.Add((scene, chapterEntry.Key));
            }
        }

        if (moving.Count == 0) return;

        foreach (var chapterEntry in ScenesManifest.Chapters)
        {
            chapterEntry.Value.RemoveAll(scene => sceneSet.Contains(scene.Id));
            ReindexScenes(chapterEntry.Value);
        }

        targetIndex = Math.Clamp(targetIndex, 0, targetScenes.Count);

        foreach (var item in moving)
        {
            if (item.SourceChapterGuid != targetChapterGuid)
            {
                var sourceChapter = ActiveBook.Chapters.FirstOrDefault(chapter => chapter.Guid == item.SourceChapterGuid);
                if (sourceChapter != null)
                {
                    var oldPath = GetSceneFilePath(sourceChapter, item.Scene);
                    item.Scene.FileName = GetNextSceneFileName(targetScenes.Concat(moving.Select(m => m.Scene)).ToList());
                    item.Scene.ChapterGuid = targetChapterGuid;
                    var newPath = GetSceneFilePath(targetChapter, item.Scene);

                    if (await _fileService.ExistsAsync(oldPath))
                        await _fileService.MoveFileAsync(oldPath, newPath);
                }
            }
            else
            {
                item.Scene.ChapterGuid = targetChapterGuid;
            }
        }

        targetScenes.InsertRange(targetIndex, moving.Select(item => item.Scene));
        ReindexScenes(targetScenes);

        await SaveScenesAsync();
    }

    public async Task RenameSceneAsync(string chapterGuid, string sceneId, string newTitle)
    {
        if (ScenesManifest == null) return;

        if (ScenesManifest.Chapters.TryGetValue(chapterGuid, out var scenes))
        {
            var scene = scenes.FirstOrDefault(s => s.Id == sceneId);
            if (scene != null)
            {
                scene.Title = newTitle;
            }
        }

        await SaveScenesAsync();
    }
}
