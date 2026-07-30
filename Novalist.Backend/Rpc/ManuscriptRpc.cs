using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Full-manuscript payload for the manuscript/corkboard/outliner view.</summary>
public sealed class ManuscriptRpc
{
    private readonly Workspace _workspace;

    public ManuscriptRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    [JsonRpcMethod("manuscript/get")]
    /// <summary>
    /// The book as continuous text. With <paramref name="sceneIds"/> it is only
    /// those scenes instead - one POV's thread, a search result, whatever the
    /// writer has selected - stitched in reading order. Reading a chosen run as
    /// prose is the only way to hear whether it holds together.
    /// </summary>
    /// <param name="character">Entity id. A scene passes when its cast or its
    /// point of view names that character.</param>
    /// <param name="location">Entity id, matched against the scene's cast, which
    /// is where a scene records the places in it as well as the people.</param>
    /// <param name="plotline">Plotline id the scene serves.</param>
    /// <param name="stage">The scene's own stage key.</param>
    public async Task<ManuscriptSectionDto[]> GetAsync(
        string filterStatus, string[]? sceneIds = null,
        string? character = null, string? location = null,
        string? plotline = null, string? stage = null)
    {
        var projects = _workspace.Projects;
        // Names, because a scene's point of view is stored as one while the
        // filter passes an id: a picker that lists entries has to hand back
        // something the scene can be matched on either way.
        var entityNames = await EntityNamesAsync(character, location);
        var book = projects.ActiveBook ?? throw new InvalidOperationException("No project open.");
        var manifest = projects.ScenesManifest;
        var chosen = sceneIds is { Length: > 0 }
            ? new HashSet<string>(sceneIds, StringComparer.Ordinal)
            : null;

        var sections = new List<ManuscriptSectionDto>();
        foreach (var chapter in book.Chapters.OrderBy(c => c.Order))
        {
            // A chosen set says exactly which scenes to read; a status filter
            // over the top of it would drop scenes the writer just picked.
            if (chosen == null && filterStatus != "All" &&
                !string.Equals(chapter.Status.ToString(), filterStatus, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var scenes = (manifest?.Chapters.GetValueOrDefault(chapter.Guid) ?? [])
                .Where(s => s.ArchivedAt == null)
                .Where(s => chosen == null || chosen.Contains(s.Id))
                // A chosen set is an explicit answer; the shared filter narrows
                // the book rather than overriding what the writer just picked.
                .Where(s => chosen != null || Passes(s, character, location, plotline, stage, entityNames))
                .OrderBy(s => s.Order)
                .ToList();
            if (scenes.Count == 0) continue;

            var sceneDtos = new List<ManuscriptSceneDto>();
            foreach (var scene in scenes)
            {
                var html = await projects.ReadSceneContentAsync(chapter, scene);
                sceneDtos.Add(new ManuscriptSceneDto(
                    scene.Id,
                    scene.Title,
                    html,
                    scene.WordCount,
                    scene.Synopsis,
                    scene.AnalysisOverrides?.Pov,
                    scene.Goal,
                    scene.Outcome,
                    scene.Inactive));
            }

            sections.Add(new ManuscriptSectionDto(
                chapter.Guid,
                chapter.Title,
                chapter.Status.ToString(),
                chapter.Act,
                sceneDtos));
        }
        return sections.ToArray();
    }

    /// <summary>
    /// True when a scene survives the shared filter. Every part is optional and
    /// they combine: two chips narrow further rather than widening.
    /// </summary>
    private static bool Passes(
        Core.Models.SceneData scene, string? character, string? location,
        string? plotline, string? stage, IReadOnlyDictionary<string, string> names)
    {
        if (!string.IsNullOrWhiteSpace(stage)
            && !string.Equals(scene.Stage ?? string.Empty, stage, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(plotline)
            && !(scene.PlotlineIds ?? []).Contains(plotline, StringComparer.Ordinal))
            return false;

        if (!string.IsNullOrWhiteSpace(character) && !Names(scene, character, names)) return false;
        if (!string.IsNullOrWhiteSpace(location) && !Names(scene, location, names)) return false;

        return true;
    }

    /// <summary>
    /// True when a scene names this entry - in the cast the writer confirmed,
    /// or as the point of view, which is stored as a name rather than an id.
    /// </summary>
    private static bool Names(
        Core.Models.SceneData scene, string entityId, IReadOnlyDictionary<string, string> names)
    {
        if ((scene.Cast ?? []).Contains(entityId, StringComparer.Ordinal)) return true;

        return names.TryGetValue(entityId, out var name)
               && !string.IsNullOrWhiteSpace(scene.AnalysisOverrides?.Pov)
               && string.Equals(scene.AnalysisOverrides.Pov, name, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// The display names of the entries being filtered on, keyed by id. Loaded
    /// only when something is actually being filtered, so the common case -
    /// no filter - reads no Codex at all.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> EntityNamesAsync(
        string? character, string? location)
    {
        if (string.IsNullOrWhiteSpace(character) && string.IsNullOrWhiteSpace(location))
            return new Dictionary<string, string>();

        var entities = new EntityService(_workspace.Projects);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in await entities.LoadCharactersAsync()) names[c.Id] = c.DisplayName;
        foreach (var l in await entities.LoadLocationsAsync()) names[l.Id] = l.Name;
        return names;
    }

    [JsonRpcMethod("scenes/setPov")]
    public async Task SetPovAsync(string chapterGuid, string sceneId, string pov)
    {
        var (_, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        if (string.IsNullOrWhiteSpace(pov))
        {
            if (scene.AnalysisOverrides != null)
            {
                scene.AnalysisOverrides.Pov = null;
                if (!scene.AnalysisOverrides.HasValues) scene.AnalysisOverrides = null;
            }
        }
        else
        {
            scene.AnalysisOverrides ??= new Novalist.Core.Models.SceneAnalysisOverrides();
            scene.AnalysisOverrides.Pov = pov;
        }
        await _workspace.Projects.SaveScenesAsync();
    }
}

public sealed record ManuscriptSectionDto(
    string ChapterGuid,
    string ChapterTitle,
    string Status,
    string Act,
    IReadOnlyList<ManuscriptSceneDto> Scenes);

public sealed record ManuscriptSceneDto(
    string SceneId,
    string Title,
    string Html,
    int WordCount,
    string? Synopsis,
    string? Pov,
    /// <summary>What the viewpoint wants here. Authored, never inferred.</summary>
    string? Goal,
    /// <summary>What they are left with. Authored, never inferred.</summary>
    string? Outcome,
    /// <summary>True when the scene is out of the book but still in the plan.</summary>
    bool Inactive);
