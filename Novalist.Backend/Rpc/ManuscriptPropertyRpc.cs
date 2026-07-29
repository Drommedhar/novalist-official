using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Fields the writer added to scenes and chapters, and the values on each one.
///
/// The Codex has had typed custom properties for a long time; the manuscript
/// had a closed field set, so anything the writer wanted to track about a scene
/// had to be smuggled through tags - where nothing downstream could sort, group
/// or total it.
/// </summary>
public sealed class ManuscriptPropertyRpc
{
    private readonly Workspace _workspace;

    public ManuscriptPropertyRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private ManuscriptPropertyService Service => new(_workspace.Projects);

    [JsonRpcMethod("manuscriptProps/definitions")]
    public ManuscriptPropertyDto[] Definitions() => [.. Service.Definitions().Select(ToDto)];

    [JsonRpcMethod("manuscriptProps/setDefinitions")]
    public async Task<ManuscriptPropertyDto[]> SetDefinitionsAsync(ManuscriptPropertyDto[] definitions)
    {
        var saved = await Service.SetDefinitionsAsync((definitions ?? []).Select(FromDto));
        return [.. saved.Select(ToDto)];
    }

    [JsonRpcMethod("manuscriptProps/setSceneValue")]
    public async Task<Dictionary<string, string>> SetSceneValueAsync(
        string sceneId, string key, string? value)
    {
        var values = await Service.SetSceneValueAsync(sceneId, key, value);
        return values.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    [JsonRpcMethod("manuscriptProps/chapterValues")]
    public Dictionary<string, string> ChapterValues(string chapterGuid)
        => Service.ChapterValues(chapterGuid).ToDictionary(kv => kv.Key, kv => kv.Value);

    [JsonRpcMethod("manuscriptProps/setChapterValue")]
    public async Task<Dictionary<string, string>> SetChapterValueAsync(
        string chapterGuid, string key, string? value)
    {
        var values = await Service.SetChapterValueAsync(chapterGuid, key, value);
        return values.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>
    /// Every scene's values in one call, keyed by scene id. The outliner draws
    /// a column per property over a whole book, and asking scene by scene would
    /// be one round trip per row.
    /// </summary>
    [JsonRpcMethod("manuscriptProps/allSceneValues")]
    public Dictionary<string, Dictionary<string, string>> AllSceneValues()
    {
        var manifest = _workspace.Projects.ScenesManifest;
        if (manifest == null) return [];
        return manifest.Chapters
            .SelectMany(c => c.Value)
            .Where(s => s.Properties is { Count: > 0 })
            .ToDictionary(s => s.Id, s => new Dictionary<string, string>(s.Properties!));
    }

    private static ManuscriptPropertyDto ToDto(ManuscriptPropertyDefinition d) => new(
        d.Key, d.Label, d.Type.ToString(), d.EnumOptions?.ToArray() ?? [],
        d.Scope.ToString(), d.ShowInOutliner);

    private static ManuscriptPropertyDefinition FromDto(ManuscriptPropertyDto d) => new()
    {
        Key = d.Key ?? string.Empty,
        Label = d.Label ?? string.Empty,
        Type = Enum.TryParse<CustomPropertyType>(d.Type, true, out var type)
            ? type
            : CustomPropertyType.String,
        EnumOptions = d.EnumOptions?.ToList(),
        Scope = Enum.TryParse<ManuscriptPropertyScope>(d.Scope, true, out var scope)
            ? scope
            : ManuscriptPropertyScope.Scene,
        ShowInOutliner = d.ShowInOutliner
    };
}

/// <summary>One manuscript property definition, as the renderer sees it.</summary>
public sealed record ManuscriptPropertyDto(
    string Key,
    string Label,
    string Type,
    string[] EnumOptions,
    string Scope,
    bool ShowInOutliner);
