using Novalist.Core.Models;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// The labels a scene can carry, and putting one on a scene.
///
/// A scene has held a label colour since long before anything read it: a bare
/// hex string with no name and no surface that showed it. A colour nobody named
/// tells a reader nothing, so a label has a name first and a colour second.
/// </summary>
public sealed class SceneLabelRpc
{
    private readonly Workspace _workspace;

    public SceneLabelRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    [JsonRpcMethod("labels/list")]
    public SceneLabelDto[] List()
        => [.. (_workspace.Projects.ActiveBook?.SceneLabels ?? [])
            .Select(l => new SceneLabelDto(l.Key, l.Label, l.Color))];

    /// <summary>
    /// Replaces the label list. A label with no name is dropped, and a
    /// duplicate key would make a scene's label ambiguous.
    /// </summary>
    [JsonRpcMethod("labels/set")]
    public async Task<SceneLabelDto[]> SetAsync(SceneLabelDto[] labels)
    {
        var book = _workspace.Projects.ActiveBook
            ?? throw new InvalidOperationException("No active book.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        book.SceneLabels = [.. (labels ?? [])
            .Where(l => !string.IsNullOrWhiteSpace(l.Key) && !string.IsNullOrWhiteSpace(l.Label))
            .Where(l => seen.Add(l.Key!.Trim()))
            .Select(l => new SceneLabel
            {
                Key = l.Key!.Trim(),
                Label = l.Label!.Trim(),
                Color = string.IsNullOrWhiteSpace(l.Color) ? "#8b8b8b" : l.Color!.Trim()
            })];

        await _workspace.Projects.SaveProjectAsync();
        await PruneAsync(book.SceneLabels);
        return List();
    }

    [JsonRpcMethod("labels/setScene")]
    public async Task<BulkResultDto> SetSceneLabelAsync(string sceneId, string? labelKey)
    {
        var scene = AllScenes().FirstOrDefault(s => s.Id == sceneId)
            ?? throw new InvalidOperationException($"Unknown scene '{sceneId}'.");

        var key = (labelKey ?? string.Empty).Trim();
        // A label that is not in the book's list would draw as nothing, which
        // reads as no label rather than as a mistake.
        scene.LabelKey = key.Length > 0
                         && (_workspace.Projects.ActiveBook?.SceneLabels ?? [])
                             .Any(l => string.Equals(l.Key, key, StringComparison.OrdinalIgnoreCase))
            ? key
            : null;
        await _workspace.Projects.SaveScenesAsync();
        return new BulkResultDto(1, _workspace.BuildState());
    }

    /// <summary>
    /// Drops label keys whose label is gone. Left behind they would colour
    /// nothing while still travelling with the project.
    /// </summary>
    private async Task PruneAsync(IReadOnlyList<SceneLabel> labels)
    {
        var keys = labels.Select(l => l.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var touched = false;
        foreach (var scene in AllScenes())
        {
            if (scene.LabelKey == null || keys.Contains(scene.LabelKey)) continue;
            scene.LabelKey = null;
            touched = true;
        }
        if (touched) await _workspace.Projects.SaveScenesAsync();
    }

    private IEnumerable<SceneData> AllScenes()
    {
        var manifest = _workspace.Projects.ScenesManifest;
        if (manifest == null) return [];
        return manifest.Chapters.SelectMany(c => c.Value).Concat(manifest.Archived);
    }
}

/// <summary>One label a scene can carry.</summary>
public sealed record SceneLabelDto(string? Key, string? Label, string? Color);
