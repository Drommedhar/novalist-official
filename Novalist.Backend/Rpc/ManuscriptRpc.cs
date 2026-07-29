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
    public async Task<ManuscriptSectionDto[]> GetAsync(string filterStatus, string[]? sceneIds = null)
    {
        var projects = _workspace.Projects;
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
                    scene.AnalysisOverrides?.Pov));
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
    string? Pov);
