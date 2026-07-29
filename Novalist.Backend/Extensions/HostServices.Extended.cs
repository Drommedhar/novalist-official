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

/// <summary>
/// The surfaces added so that work the audit placed outside core could actually
/// be written outside core: research items, review remarks and suggested edits,
/// scene metadata, story structure, a command bus, and structural editing.
///
/// Everything here is deliberately narrow. An extension gets the operation it
/// needs and not the project file: nothing hands out a live model it could
/// mutate behind the host's back, and nothing erases anything - the destructive
/// verbs are trash and archive, both of which the writer can undo.
/// </summary>
public sealed partial class HostServices
{
    private ResearchService Research => new(_projectService, _fileService);

    // ── Structural editing (IExtensionProjectService) ──────────────

    async Task<bool> IExtensionProjectService.RenameChapterAsync(string chapterGuid, string title)
    {
        if (!ChapterExists(chapterGuid)) return false;
        await _projectService.RenameChapterAsync(chapterGuid, title ?? string.Empty);
        return true;
    }

    async Task<bool> IExtensionProjectService.RenameSceneAsync(
        string chapterGuid, string sceneId, string title)
    {
        if (!SceneExists(chapterGuid, sceneId)) return false;
        await _projectService.RenameSceneAsync(chapterGuid, sceneId, title ?? string.Empty);
        return true;
    }

    async Task<bool> IExtensionProjectService.MoveSceneAsync(
        string sceneId, string targetChapterGuid, int index)
    {
        if (!ChapterExists(targetChapterGuid)) return false;

        // The scene may be in any chapter, so it is found across the book
        // rather than in the target - moving between chapters is the case that
        // matters and would otherwise be the one that failed.
        var owner = _projectService.GetChaptersOrdered()
            .FirstOrDefault(c => _projectService.GetScenesForChapter(c.Guid)
                .Any(s => s.Id == sceneId));
        if (owner == null) return false;

        await _projectService.MoveScenesAsync([sceneId], targetChapterGuid, Math.Max(0, index));
        return true;
    }

    async Task<bool> IExtensionProjectService.MoveChapterAsync(string chapterGuid, int order)
    {
        if (!ChapterExists(chapterGuid)) return false;
        await _projectService.ReorderChapterAsync(chapterGuid, Math.Max(1, order));
        return true;
    }

    async Task<bool> IExtensionProjectService.SetChapterActAsync(string chapterGuid, string act)
    {
        var chapter = _projectService.GetChaptersOrdered().FirstOrDefault(c => c.Guid == chapterGuid);
        if (chapter == null) return false;

        chapter.Act = (act ?? string.Empty).Trim();
        await _projectService.SaveProjectAsync();
        return true;
    }

    async Task<bool> IExtensionProjectService.TrashChapterAsync(string chapterGuid)
    {
        if (!ChapterExists(chapterGuid)) return false;
        // The core delete is already a move to the trash, so an extension gets
        // the recoverable verb without a second implementation.
        await _projectService.DeleteChapterAsync(chapterGuid);
        return true;
    }

    async Task<bool> IExtensionProjectService.ArchiveSceneAsync(string chapterGuid, string sceneId)
    {
        if (!SceneExists(chapterGuid, sceneId)) return false;
        await _projectService.ArchiveSceneAsync(chapterGuid, sceneId);
        return true;
    }

    private bool ChapterExists(string chapterGuid)
        => _projectService.GetChaptersOrdered().Any(c => c.Guid == chapterGuid);

    private bool SceneExists(string chapterGuid, string sceneId)
        => ChapterExists(chapterGuid)
            && _projectService.GetScenesForChapter(chapterGuid).Any(s => s.Id == sceneId);

    // ── Entity writing (IExtensionEntityService) ───────────────────

    async Task<bool> IExtensionEntityService.SaveEntityAsync(
        string typeKey,
        string entityId,
        string? name,
        string? description,
        IReadOnlyList<Sdk.Services.CustomEntitySectionInfo>? sections)
    {
        if (string.IsNullOrWhiteSpace(entityId)) return false;

        // Per type rather than through the shared interface: name, description
        // and sections are not on IEntityData, and a character's name is two
        // fields where every other kind has one.
        switch ((typeKey ?? string.Empty).ToLowerInvariant())
        {
            case "character":
            {
                var entity = (await _entityService.LoadCharactersAsync())
                    .FirstOrDefault(e => e.Id == entityId);
                if (entity == null) return false;
                if (!string.IsNullOrWhiteSpace(name)) ApplyPersonName(entity, name!);
                // A character has no description field, so it becomes a Notes
                // section - the same place CreateEntityAsync puts one, or the
                // two calls would disagree about where a description lives.
                entity.Sections = Merge(entity.Sections, WithDescription(sections, description));
                await _entityService.SaveCharacterAsync(entity);
                break;
            }
            case "location":
            {
                var entity = (await _entityService.LoadLocationsAsync())
                    .FirstOrDefault(e => e.Id == entityId);
                if (entity == null) return false;
                if (!string.IsNullOrWhiteSpace(name)) entity.Name = name!;
                if (description != null) entity.Description = description;
                entity.Sections = Merge(entity.Sections, sections);
                await _entityService.SaveLocationAsync(entity);
                break;
            }
            case "item":
            {
                var entity = (await _entityService.LoadItemsAsync())
                    .FirstOrDefault(e => e.Id == entityId);
                if (entity == null) return false;
                if (!string.IsNullOrWhiteSpace(name)) entity.Name = name!;
                if (description != null) entity.Description = description;
                entity.Sections = Merge(entity.Sections, sections);
                await _entityService.SaveItemAsync(entity);
                break;
            }
            case "lore":
            {
                var entity = (await _entityService.LoadLoreAsync())
                    .FirstOrDefault(e => e.Id == entityId);
                if (entity == null) return false;
                if (!string.IsNullOrWhiteSpace(name)) entity.Name = name!;
                if (description != null) entity.Description = description;
                entity.Sections = Merge(entity.Sections, sections);
                await _entityService.SaveLoreAsync(entity);
                break;
            }
            default:
            {
                // An unregistered type throws inside the entity service. That is
                // right for core, where the type list is known, and wrong across
                // the SDK boundary: an extension passing a stale key should be
                // told no, not handed an exception.
                if (!_entityService.GetCustomEntityTypes()
                        .Any(t => string.Equals(t.TypeKey, typeKey, StringComparison.OrdinalIgnoreCase)))
                    return false;

                var entity = (await _entityService.LoadCustomEntitiesAsync(typeKey ?? string.Empty))
                    .FirstOrDefault(e => e.Id == entityId);
                if (entity == null) return false;
                if (!string.IsNullOrWhiteSpace(name)) entity.Name = name!;
                // Custom entities have no description field either; their shape
                // is whatever the type that registered them defined.
                entity.Sections = Merge(entity.Sections, WithDescription(sections, description));
                await _entityService.SaveCustomEntityAsync(entity);
                break;
            }
        }

        EntityRefreshRequested?.Invoke();
        return true;
    }

    /// <summary>
    /// Folds a description into the section list for the two kinds that have no
    /// description field of their own.
    /// </summary>
    private static IReadOnlyList<Sdk.Services.CustomEntitySectionInfo> WithDescription(
        IReadOnlyList<Sdk.Services.CustomEntitySectionInfo>? sections, string? description)
    {
        var all = new List<Sdk.Services.CustomEntitySectionInfo>(sections ?? []);
        if (!string.IsNullOrWhiteSpace(description))
            all.Add(new Sdk.Services.CustomEntitySectionInfo
            {
                Title = "Notes",
                Content = description!
            });
        return all;
    }

    /// <summary>
    /// A section already there is replaced, anything else appended. Filling in
    /// one part of an entry must not wipe the parts the caller said nothing
    /// about - a questionnaire covering childhood should not erase appearance.
    /// </summary>
    private static List<EntitySection> Merge(
        List<EntitySection>? existing,
        IReadOnlyList<Sdk.Services.CustomEntitySectionInfo>? incoming)
    {
        var merged = existing ?? [];
        foreach (var section in incoming ?? [])
        {
            var match = merged.FirstOrDefault(
                s => string.Equals(s.Title, section.Title, StringComparison.OrdinalIgnoreCase));
            if (match != null) match.Content = section.Content;
            else merged.Add(new EntitySection { Title = section.Title, Content = section.Content });
        }
        return merged;
    }

    /// <summary>
    /// A character's name is two fields. Splitting on the last space is what the
    /// Codex does itself, so a name written back reads as it would if typed.
    /// </summary>
    private static void ApplyPersonName(CharacterData character, string name)
    {
        var trimmed = name.Trim();
        var split = trimmed.LastIndexOf(' ');
        if (split <= 0)
        {
            character.Name = trimmed;
            character.Surname = string.Empty;
            return;
        }
        character.Name = trimmed[..split];
        character.Surname = trimmed[(split + 1)..];
    }

    // ── Research (IExtensionResearchService) ───────────────────────

    IReadOnlyList<ResearchItemInfo> IExtensionResearchService.GetAll()
        => [.. Research.GetAll().Select(ToInfo)];

    async Task<string> IExtensionResearchService.SaveAsync(ResearchItemInfo item)
    {
        var research = Research;
        var existing = string.IsNullOrEmpty(item.Id)
            ? null
            : research.GetAll().FirstOrDefault(i => i.Id == item.Id);

        var data = existing ?? new ResearchItem();
        if (!string.IsNullOrEmpty(item.Id)) data.Id = item.Id;
        data.Title = item.Title ?? string.Empty;
        data.Type = Enum.TryParse<ResearchItemType>(item.Type, true, out var type)
            ? type
            : ResearchItemType.Note;
        data.Content = item.Content ?? string.Empty;
        data.Tags = [.. item.Tags ?? []];
        data.EntityRefs = [.. item.EntityRefs ?? []];
        data.UpdatedAt = DateTime.UtcNow;

        await research.SaveAsync(data);
        return data.Id;
    }

    async Task<bool> IExtensionResearchService.DeleteAsync(string itemId)
    {
        var research = Research;
        if (research.GetAll().All(i => i.Id != itemId)) return false;
        await research.DeleteAsync(itemId);
        return true;
    }

    Task<string> IExtensionResearchService.ImportFileAsync(string sourcePath)
        => Research.ImportFileAsync(sourcePath);

    private static ResearchItemInfo ToInfo(ResearchItem item) => new()
    {
        Id = item.Id,
        Title = item.Title,
        Type = item.Type.ToString(),
        Content = item.Content,
        Tags = [.. item.Tags],
        EntityRefs = [.. item.EntityRefs]
    };

    // ── Review (IExtensionReviewService) ───────────────────────────

    Task<IReadOnlyList<SceneCommentInfo>> IExtensionReviewService.GetCommentsAsync(
        string chapterGuid, string sceneId)
    {
        var scene = FindScene(chapterGuid, sceneId);
        IReadOnlyList<SceneCommentInfo> comments = scene == null
            ? []
            : [.. (scene.Comments ?? []).Select(c => new SceneCommentInfo
            {
                Id = c.Id,
                AnchorText = c.AnchorText,
                Text = c.Text,
                Author = c.Author ?? string.Empty,
                Resolved = c.Resolved
            })];
        return Task.FromResult(comments);
    }

    async Task<string> IExtensionReviewService.AddCommentAsync(
        string chapterGuid, string sceneId, string anchorText, string text, string author)
    {
        var scene = FindScene(chapterGuid, sceneId);
        if (scene == null) return string.Empty;

        var comment = new SceneComment
        {
            AnchorText = anchorText ?? string.Empty,
            Text = text ?? string.Empty,
            Author = string.IsNullOrWhiteSpace(author) ? null : author
        };
        (scene.Comments ??= []).Add(comment);
        await _projectService.SaveScenesAsync();
        return comment.Id;
    }

    async Task<bool> IExtensionReviewService.SetCommentResolvedAsync(
        string chapterGuid, string sceneId, string commentId, bool resolved)
    {
        var comment = FindScene(chapterGuid, sceneId)?.Comments?
            .FirstOrDefault(c => c.Id == commentId);
        if (comment == null) return false;

        comment.Resolved = resolved;
        await _projectService.SaveScenesAsync();
        return true;
    }

    async Task<bool> IExtensionReviewService.DeleteCommentAsync(
        string chapterGuid, string sceneId, string commentId)
    {
        var scene = FindScene(chapterGuid, sceneId);
        var comment = scene?.Comments?.FirstOrDefault(c => c.Id == commentId);
        if (scene == null || comment == null) return false;

        scene.Comments!.Remove(comment);
        await _projectService.SaveScenesAsync();
        return true;
    }

    async Task<bool> IExtensionReviewService.SuggestEditAsync(
        string chapterGuid, string sceneId, string anchorText, string replacement, string author)
    {
        var chapter = _projectService.GetChaptersOrdered().FirstOrDefault(c => c.Guid == chapterGuid);
        var scene = FindScene(chapterGuid, sceneId);
        if (chapter == null || scene == null || string.IsNullOrEmpty(anchorText)) return false;

        var html = await _projectService.ReadSceneContentAsync(chapter, scene);
        var at = html.IndexOf(anchorText, StringComparison.Ordinal);
        // Nowhere honest to attach a proposal about words that are not there.
        if (at < 0) return false;

        var stamp = DateTime.UtcNow.ToString("o");
        var id = Guid.NewGuid().ToString("N")[..8];
        var proposal = TrackedChanges.Deletion(id + "d", anchorText, author ?? string.Empty, stamp)
            + (string.IsNullOrEmpty(replacement)
                ? string.Empty
                : TrackedChanges.Insertion(id + "i", replacement, author ?? string.Empty, stamp));

        var updated = html[..at] + proposal + html[(at + anchorText.Length)..];
        await _projectService.WriteSceneContentAsync(chapter, scene, updated);
        scene.WordCount = Workspace.CountWords(TextDiff.StripHtml(updated));
        await _projectService.SaveScenesAsync();
        return true;
    }

    async Task<int> IExtensionReviewService.PendingSuggestionCountAsync(
        string chapterGuid, string sceneId)
    {
        var chapter = _projectService.GetChaptersOrdered().FirstOrDefault(c => c.Guid == chapterGuid);
        var scene = FindScene(chapterGuid, sceneId);
        if (chapter == null || scene == null) return 0;

        return TrackedChanges.Count(await _projectService.ReadSceneContentAsync(chapter, scene));
    }

    private SceneData? FindScene(string chapterGuid, string sceneId)
        => !ChapterExists(chapterGuid)
            ? null
            : _projectService.GetScenesForChapter(chapterGuid).FirstOrDefault(s => s.Id == sceneId);

    // ── Story structure (IExtensionStoryService) ───────────────────

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
            Tags = [.. overrides?.Tags ?? []],
            PlotlineIds = [.. scene.PlotlineIds ?? []],
            DateStart = scene.DateRange?.Start ?? string.Empty,
            DateEnd = scene.DateRange?.End ?? string.Empty,
            NarrativeMode = scene.NarrativeMode ?? string.Empty,
            Act = chapter.Act ?? string.Empty,
            Properties = new Dictionary<string, string>(scene.Properties ?? [])
        };
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

    // ── Commands and export hooks (IHostServices) ──────────────────

    private readonly Dictionary<string, (HostCommandInfo Info, Func<string?, Task> Handler)> _commands
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IExportPostProcessor> _exportPostProcessors = [];

    public IReadOnlyList<HostCommandInfo> GetCommands()
        => [.. _commands.Values.Select(c => c.Info).OrderBy(c => c.Id, StringComparer.Ordinal)];

    public async Task<bool> InvokeCommandAsync(string commandId, string? argumentsJson = null)
    {
        if (!_commands.TryGetValue(commandId ?? string.Empty, out var command)) return false;
        await command.Handler(argumentsJson);
        return true;
    }

    public void RegisterCommand(HostCommandInfo command, Func<string?, Task> handler)
    {
        if (command == null || string.IsNullOrWhiteSpace(command.Id) || handler == null) return;
        _commands[command.Id] = (command, handler);
    }

    public void UnregisterCommand(string commandId)
        => _commands.Remove(commandId ?? string.Empty);

    public void RegisterExportPostProcessor(IExportPostProcessor processor)
    {
        if (processor != null && !_exportPostProcessors.Contains(processor))
            _exportPostProcessors.Add(processor);
    }

    public void UnregisterExportPostProcessor(IExportPostProcessor processor)
        => _exportPostProcessors.Remove(processor);

    /// <summary>
    /// Runs every post-export check that applies to a format. Used by the host
    /// after writing an export; a processor that throws is reported as a failed
    /// check rather than taking the export down with it - the file is already
    /// written and is probably fine.
    /// </summary>
    internal async Task<IReadOnlyList<(string Name, ExportCheckResult Result)>> RunExportChecksAsync(
        string outputPath, string formatKey, CancellationToken cancellationToken = default)
    {
        var results = new List<(string, ExportCheckResult)>();
        foreach (var processor in _exportPostProcessors)
        {
            if (processor.Formats.Count > 0
                && !processor.Formats.Contains(formatKey, StringComparer.OrdinalIgnoreCase))
                continue;

            try
            {
                results.Add((processor.DisplayName,
                    await processor.CheckAsync(outputPath, formatKey, cancellationToken)));
            }
            catch (Exception ex)
            {
                results.Add((processor.DisplayName, new ExportCheckResult
                {
                    Ok = false,
                    Problems = [ex.GetType().Name]
                }));
            }
        }
        return results;
    }
}
