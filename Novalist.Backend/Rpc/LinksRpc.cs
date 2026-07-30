using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>One link, with the thing at the other end resolved.</summary>
public sealed record SceneLinkDto(
    string Id, string Kind, string TargetId, string TargetTitle, string Note);

/// <summary>
/// A scene that points here, named well enough to open.
///
/// The chapter is carried because a scene title alone does not locate a scene -
/// three chapters can each have a scene called "Arrival".
/// </summary>
public sealed record BacklinkDto(
    string ChapterGuid, string ChapterTitle, string SceneId, string SceneTitle, string Note);

/// <summary>
/// Links from a scene to another scene, a research item or a Codex entry, and
/// the same links read backwards.
///
/// Research items could already point at each other, both ways. Scenes could
/// point at nothing: a scene that answers another scene, or leans on one
/// research note, could only say so as prose in its own notes - which nothing
/// could follow, and which the other end never knew about.
/// </summary>
public class LinksRpc(Workspace workspace)
{
    private readonly Workspace _workspace = workspace;

    /// <summary>What this scene points at.</summary>
    [JsonRpcMethod("links/list")]
    public async Task<SceneLinkDto[]> ListAsync(string chapterGuid, string sceneId)
    {
        var (_, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        var titles = await TitlesAsync();
        return [.. (scene.Links ?? []).Select(l => ToDto(l, titles))];
    }

    /// <summary>
    /// Points this scene at something.
    ///
    /// A scene cannot point at itself, and the same target twice is one link -
    /// a list that says "see Arrival" twice is a list nobody trusts.
    /// </summary>
    [JsonRpcMethod("links/add")]
    public async Task<SceneLinkDto[]> AddAsync(
        string chapterGuid, string sceneId, string kind, string targetId, string? note = null)
    {
        if (!LinkKinds.IsKnown(kind))
            throw new InvalidOperationException($"Unknown link kind '{kind}'.");
        if (string.IsNullOrWhiteSpace(targetId))
            throw new InvalidOperationException("A link needs something to point at.");

        var (_, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        if (string.Equals(kind, LinkKinds.Scene, StringComparison.OrdinalIgnoreCase)
            && string.Equals(targetId, sceneId, StringComparison.Ordinal))
            throw new InvalidOperationException("A scene cannot point at itself.");

        var links = scene.Links ?? [];
        var existing = links.FirstOrDefault(l =>
            string.Equals(l.Kind, kind, StringComparison.OrdinalIgnoreCase)
            && string.Equals(l.TargetId, targetId, StringComparison.Ordinal));
        if (existing != null)
        {
            // Adding one that is already there rewrites the reason rather than
            // making a second row saying the same thing.
            existing.Note = (note ?? existing.Note).Trim();
        }
        else
        {
            links.Add(new SceneLink
            {
                Kind = kind.ToLowerInvariant(),
                TargetId = targetId,
                Note = (note ?? string.Empty).Trim()
            });
        }

        scene.Links = links;
        await _workspace.Projects.SaveScenesAsync();
        return await ListAsync(chapterGuid, sceneId);
    }

    /// <summary>Rewrites why a link is there.</summary>
    [JsonRpcMethod("links/setNote")]
    public async Task<SceneLinkDto[]> SetNoteAsync(
        string chapterGuid, string sceneId, string linkId, string? note)
    {
        var (_, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        var link = (scene.Links ?? []).FirstOrDefault(l => l.Id == linkId);
        if (link != null)
        {
            link.Note = (note ?? string.Empty).Trim();
            await _workspace.Projects.SaveScenesAsync();
        }
        return await ListAsync(chapterGuid, sceneId);
    }

    /// <summary>Takes a link off a scene.</summary>
    [JsonRpcMethod("links/remove")]
    public async Task<SceneLinkDto[]> RemoveAsync(string chapterGuid, string sceneId, string linkId)
    {
        var (_, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        var links = scene.Links ?? [];
        if (links.RemoveAll(l => l.Id == linkId) > 0)
        {
            scene.Links = links.Count > 0 ? links : null;
            await _workspace.Projects.SaveScenesAsync();
        }
        return await ListAsync(chapterGuid, sceneId);
    }

    /// <summary>
    /// Every scene pointing at this thing.
    ///
    /// The half that makes a link worth making: a research note has no way to
    /// know which scenes lean on it, and a scene has no way to know which
    /// scenes answer it, unless the link can be read backwards.
    /// </summary>
    [JsonRpcMethod("links/backlinks")]
    public BacklinkDto[] Backlinks(string kind, string targetId)
    {
        var found = new List<BacklinkDto>();
        foreach (var chapter in _workspace.Projects.GetChaptersOrdered())
            foreach (var scene in _workspace.Projects.GetScenesForChapter(chapter.Guid))
                foreach (var link in scene.Links ?? [])
                {
                    if (!string.Equals(link.Kind, kind, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.Equals(link.TargetId, targetId, StringComparison.Ordinal)) continue;
                    found.Add(new BacklinkDto(
                        chapter.Guid, chapter.Title, scene.Id, scene.Title, link.Note));
                }
        return [.. found];
    }

    /// <summary>
    /// Titles for everything a link can point at, keyed by id.
    ///
    /// Built once per call rather than resolved per link: a scene with eight
    /// links would otherwise load the Codex eight times.
    /// </summary>
    private async Task<Dictionary<string, string>> TitlesAsync()
    {
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var chapter in _workspace.Projects.GetChaptersOrdered())
            foreach (var scene in _workspace.Projects.GetScenesForChapter(chapter.Guid))
                titles[scene.Id] = $"{chapter.Title} - {scene.Title}";

        var research = new ResearchService(_workspace.Projects, _workspace.FileService);
        foreach (var item in research.GetAll()) titles[item.Id] = item.Title;

        var entities = new EntityService(_workspace.Projects);
        foreach (var c in await entities.LoadCharactersAsync()) titles[c.Id] = c.DisplayName;
        foreach (var l in await entities.LoadLocationsAsync()) titles[l.Id] = l.Name;
        foreach (var i in await entities.LoadItemsAsync()) titles[i.Id] = i.Name;
        foreach (var l in await entities.LoadLoreAsync()) titles[l.Id] = l.Name;

        return titles;
    }

    private static SceneLinkDto ToDto(SceneLink link, Dictionary<string, string> titles)
        => new(link.Id, link.Kind, link.TargetId,
            // A target that is gone keeps its row and says so, rather than
            // vanishing: a link that disappears silently is a link the writer
            // never finds out they lost.
            titles.GetValueOrDefault(link.TargetId, string.Empty),
            link.Note);
}
