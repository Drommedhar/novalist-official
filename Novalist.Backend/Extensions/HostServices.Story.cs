using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;
using Novalist.Sdk.Services;

namespace Novalist.Backend.Extensions;

public sealed partial class HostServices
{
    // ── Story structure (IExtensionStoryService) ───────────────────

    BookDetailInfo? IExtensionStoryService.GetBookDetail()
    {
        var book = _projectService.ActiveBook;
        if (book == null) return null;

        var premise = book.Premise ?? new Core.Models.StoryPremise();
        var publishing = book.Publishing ?? new Core.Models.PublishingMetadata();
        return new BookDetailInfo
        {
            Id = book.Id,
            Name = book.Name,
            Author = book.Author,
            NarrativePerson = book.NarrativePerson,
            Tense = book.Tense,
            StructureTemplateId = book.StructureTemplateId,
            Premise = new BookPremiseInfo
            {
                Logline = premise.Logline,
                Paragraph = premise.Paragraph,
                // Copied rather than handed over: the caller holding the book's
                // own dictionary could write into the open project through a
                // read-only call.
                Acts = new Dictionary<string, string>(premise.Acts ?? []),
                Genre = premise.Genre,
                Audience = premise.Audience,
                Comparables = premise.Comparables,
                Setting = premise.Setting,
                Blurb = premise.Blurb,
                Synopsis = premise.Synopsis
            },
            Publishing = new BookPublishingInfo
            {
                Isbn = publishing.Isbn,
                Publisher = publishing.Publisher,
                Description = publishing.Description,
                Subjects = [.. publishing.Subjects ?? []],
                Rights = publishing.Rights,
                PublicationDate = publishing.PublicationDate,
                SeriesName = publishing.SeriesName,
                SeriesPosition = publishing.SeriesPosition
            }
        };
    }

    SceneDetailInfo? IExtensionStoryService.GetSceneDetail(string chapterGuid, string sceneId)
    {
        var chapter = _projectService.GetChaptersOrdered().FirstOrDefault(c => c.Guid == chapterGuid);
        var scene = FindScene(chapterGuid, sceneId);
        if (chapter == null || scene == null) return null;

        var overrides = scene.AnalysisOverrides;
        return new SceneDetailInfo
        {
            Id = scene.Id,
            Title = scene.Title,
            ChapterGuid = chapterGuid,
            Order = scene.Order,
            WordCount = scene.WordCount,
            Pov = overrides?.Pov ?? string.Empty,
            Synopsis = scene.Synopsis ?? string.Empty,
            Notes = scene.Notes ?? string.Empty,
            Intensity = overrides?.Intensity,
            Emotion = overrides?.Emotion ?? string.Empty,
            Conflict = overrides?.Conflict ?? string.Empty,
            Stage = scene.Stage ?? string.Empty,
            Inactive = scene.Inactive,
            Tags = [.. overrides?.Tags ?? []],
            PlotlineIds = [.. scene.PlotlineIds ?? []],
            Cast = [.. scene.Cast ?? []],
            FocusEntityId = scene.FocusEntityId ?? string.Empty,
            DateStart = scene.DateRange?.Start ?? string.Empty,
            DateEnd = scene.DateRange?.End ?? string.Empty,
            NarrativeMode = scene.NarrativeMode ?? string.Empty,
            Act = chapter.Act ?? string.Empty,
            Properties = new Dictionary<string, string>(scene.Properties ?? [])
        };
    }

    ChapterDetailInfo? IExtensionStoryService.GetChapterDetail(string chapterGuid)
    {
        var chapter = _projectService.GetChaptersOrdered().FirstOrDefault(c => c.Guid == chapterGuid);
        if (chapter == null) return null;

        var scenes = _projectService.GetScenesForChapter(chapterGuid);
        return new ChapterDetailInfo
        {
            Guid = chapter.Guid,
            Title = chapter.Title,
            Order = chapter.Order,
            Status = chapter.Status.ToString(),
            Act = chapter.Act ?? string.Empty,
            Date = chapter.Date ?? string.Empty,
            DateStart = chapter.DateRange?.Start ?? string.Empty,
            DateEnd = chapter.DateRange?.End ?? string.Empty,
            Description = chapter.Description ?? string.Empty,
            WordTarget = chapter.WordTarget,
            // Summed rather than stored: the chapter carries a target, not a
            // total, and a stale total is worse than none.
            WordCount = scenes.Sum(s => s.WordCount),
            SceneIds = [.. scenes.OrderBy(s => s.Order).Select(s => s.Id)],
            Properties = new Dictionary<string, string>(chapter.Properties ?? [])
        };
    }

    async Task<bool> IExtensionStoryService.SetChapterStatusAsync(string chapterGuid, string status)
    {
        var chapter = _projectService.GetChaptersOrdered().FirstOrDefault(c => c.Guid == chapterGuid);
        if (chapter == null) return false;
        if (!Enum.TryParse<ChapterStatus>(status, ignoreCase: true, out var parsed)) return false;

        chapter.Status = parsed;
        await _projectService.SaveProjectAsync();
        ProjectStructureChanged?.Invoke();
        return true;
    }

    async Task<bool> IExtensionStoryService.SetSceneMetadataAsync(
        string chapterGuid, string sceneId, SceneMetadataPatch patch)
    {
        var scene = FindScene(chapterGuid, sceneId);
        if (scene == null || patch == null) return false;

        if (patch.Synopsis != null) scene.Synopsis = patch.Synopsis;
        if (patch.Notes != null) scene.Notes = patch.Notes;
        if (patch.Stage != null) scene.Stage = patch.Stage;
        if (patch.NarrativeMode != null) scene.NarrativeMode = patch.NarrativeMode;
        if (patch.Inactive.HasValue) scene.Inactive = patch.Inactive.Value;

        if (patch.DateStart != null || patch.DateEnd != null)
        {
            scene.DateRange ??= new StoryDateRange();
            if (patch.DateStart != null) scene.DateRange.Start = patch.DateStart;
            if (patch.DateEnd != null) scene.DateRange.End = patch.DateEnd;
        }

        // The analysis values live in the override block, which is what "the
        // writer said so" means - the host's own detection is not stored there
        // and is not something an extension should be able to forge.
        if (patch.Pov != null || patch.Emotion != null || patch.Conflict != null
            || patch.Intensity.HasValue || patch.Tags != null)
        {
            scene.AnalysisOverrides ??= new SceneAnalysisOverrides();
            if (patch.Pov != null) scene.AnalysisOverrides.Pov = patch.Pov;
            if (patch.Emotion != null) scene.AnalysisOverrides.Emotion = patch.Emotion;
            if (patch.Conflict != null) scene.AnalysisOverrides.Conflict = patch.Conflict;
            if (patch.Intensity.HasValue) scene.AnalysisOverrides.Intensity = patch.Intensity;
            if (patch.Tags != null) scene.AnalysisOverrides.Tags = [.. patch.Tags];
        }

        if (patch.Properties != null)
        {
            scene.Properties ??= [];
            foreach (var (key, value) in patch.Properties)
            {
                if (value == null) scene.Properties.Remove(key);
                else scene.Properties[key] = value;
            }
        }

        await _projectService.SaveScenesAsync();
        ProjectStructureChanged?.Invoke();
        return true;
    }

    string IExtensionStoryService.GetCellNote(string chapterGuid, string sceneId, string plotlineId)
    {
        var scene = FindScene(chapterGuid, sceneId);
        return scene?.PlotlineNotes != null
            && scene.PlotlineNotes.TryGetValue(plotlineId, out var note)
            ? note
            : string.Empty;
    }

    async Task<bool> IExtensionStoryService.SetCellNoteAsync(
        string chapterGuid, string sceneId, string plotlineId, string note)
    {
        if (FindScene(chapterGuid, sceneId) == null) return false;
        await new Core.Services.PlotlineService(_projectService)
            .SetCellNoteAsync(chapterGuid, sceneId, plotlineId, note);
        ProjectStructureChanged?.Invoke();
        return true;
    }

    IReadOnlyList<SmartListInfo> IExtensionStoryService.GetSmartLists()
        => [.. (_projectService.CurrentProject?.SmartLists ?? [])
            .Select(list => new SmartListInfo
            {
                Id = list.Id,
                Name = list.Name,
                Match = list.Match.ToString(),
                Rules = [.. (list.Rules ?? []).Select(rule => new SmartListRuleInfo
                {
                    Field = rule.Field,
                    Op = rule.Op.ToString(),
                    Value = rule.Value
                })]
            })];

    async Task<IReadOnlyList<MapInfo>> IExtensionStoryService.GetMapsAsync()
    {
        var maps = new List<MapInfo>();
        var service = new Core.Services.MapService(_projectService, _fileService);
        foreach (var reference in _projectService.ActiveBook?.Maps ?? [])
        {
            // A map lives in its own file, and one that failed to load should
            // cost the caller that map rather than all of them.
            var map = await service.LoadMapAsync(reference.Id);
            if (map == null) continue;
            maps.Add(new MapInfo
            {
                Id = map.Id,
                Name = map.Name,
                ImagePaths = [.. MapImagePaths(map.Layers).Concat(map.Pins
                    .Select(pin => pin.IconPath ?? string.Empty)).Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)],
                Pins = [.. (map.Pins ?? []).Select(pin => new MapPinInfo
                {
                    Id = pin.Id,
                    Label = pin.Label,
                    X = pin.X,
                    Y = pin.Y,
                    EntityId = pin.EntityId ?? string.Empty,
                    EntityType = pin.EntityType ?? string.Empty,
                    TargetMapId = pin.TargetMapId ?? string.Empty
                })]
            });
        }
        return maps;
    }

    private static IEnumerable<string> MapImagePaths(IEnumerable<MapLayerNode> layers)
    {
        foreach (var layer in layers)
        {
            foreach (var image in layer.Images) yield return image.Path;
            foreach (var path in MapImagePaths(layer.Children)) yield return path;
        }
    }

    IReadOnlyList<ActInfo> IExtensionStoryService.GetActs()
        => [.. _projectService.GetChaptersOrdered()
            .Where(c => !string.IsNullOrWhiteSpace(c.Act))
            .GroupBy(c => c.Act!, StringComparer.Ordinal)
            .Select(g => new ActInfo
            {
                Name = g.Key,
                ChapterGuids = [.. g.OrderBy(c => c.Order).Select(c => c.Guid)]
            })];

    IReadOnlyList<PlotlineInfo> IExtensionStoryService.GetPlotlines()
        => [.. (_projectService.ActiveBook?.Plotlines ?? [])
            .OrderBy(p => p.Order)
            .Select(p => new PlotlineInfo
            {
                Id = p.Id,
                Name = p.Name,
                Color = p.Color,
                Description = p.Description
            })];

    async Task<string> IExtensionStoryService.CreatePlotlineAsync(
        string name, string color, string description)
    {
        var book = _projectService.ActiveBook;
        if (book == null) return string.Empty;

        var plotline = new PlotlineData
        {
            Name = name ?? string.Empty,
            Description = description ?? string.Empty,
            Order = book.Plotlines.Count == 0 ? 1 : book.Plotlines.Max(p => p.Order) + 1
        };
        if (!string.IsNullOrWhiteSpace(color)) plotline.Color = color;

        book.Plotlines.Add(plotline);
        await _projectService.SaveProjectAsync();
        return plotline.Id;
    }

    async Task<bool> IExtensionStoryService.SetScenePlotlinesAsync(
        string chapterGuid, string sceneId, IReadOnlyList<string> plotlineIds)
    {
        var scene = FindScene(chapterGuid, sceneId);
        if (scene == null) return false;

        scene.PlotlineIds = [.. plotlineIds ?? []];
        await _projectService.SaveScenesAsync();
        return true;
    }

    IReadOnlyList<TimelineEventInfo> IExtensionStoryService.GetTimelineEvents()
        => [.. (_projectService.ProjectSettings?.Timeline?.ManualEvents ?? [])
            .Select(e => new TimelineEventInfo
            {
                Id = e.Id,
                Title = e.Title,
                Date = e.Date,
                Description = e.Description,
                CategoryId = e.CategoryId,
                LinkedChapterGuid = e.LinkedChapterGuid
            })];

    async Task<string> IExtensionStoryService.SaveTimelineEventAsync(TimelineEventInfo story)
    {
        var timeline = _projectService.ProjectSettings?.Timeline;
        if (timeline == null) return string.Empty;

        var existing = string.IsNullOrEmpty(story.Id)
            ? null
            : timeline.ManualEvents.FirstOrDefault(e => e.Id == story.Id);
        var data = existing ?? new TimelineManualEvent
        {
            Id = string.IsNullOrEmpty(story.Id) ? Guid.NewGuid().ToString() : story.Id,
            Order = timeline.ManualEvents.Count == 0
                ? 1
                : timeline.ManualEvents.Max(e => e.Order) + 1
        };

        data.Title = story.Title ?? string.Empty;
        data.Date = story.Date ?? string.Empty;
        data.Description = story.Description ?? string.Empty;
        data.CategoryId = string.IsNullOrWhiteSpace(story.CategoryId) ? "plot" : story.CategoryId;
        data.LinkedChapterGuid = story.LinkedChapterGuid ?? string.Empty;

        if (existing == null) timeline.ManualEvents.Add(data);
        await _projectService.SaveProjectSettingsAsync();
        return data.Id;
    }

    async Task<bool> IExtensionStoryService.DeleteTimelineEventAsync(string eventId)
    {
        var timeline = _projectService.ProjectSettings?.Timeline;
        var story = timeline?.ManualEvents.FirstOrDefault(e => e.Id == eventId);
        if (timeline == null || story == null) return false;

        timeline.ManualEvents.Remove(story);
        await _projectService.SaveProjectSettingsAsync();
        return true;
    }
}
