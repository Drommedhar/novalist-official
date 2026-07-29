using Novalist.Core.Models;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Scenes worth starting from, stored with the book.</summary>
public sealed class SceneTemplateRpc
{
    private readonly Workspace _workspace;

    public SceneTemplateRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private BookData Book => _workspace.Projects.ActiveBook
        ?? throw new InvalidOperationException("No project open.");

    [JsonRpcMethod("sceneTemplates/list")]
    public SceneTemplateDto[] List()
        => [.. Book.SceneTemplates.Select(ToDto)];

    /// <summary>
    /// Captures a scene as a template. Made from a real scene rather than
    /// described in a form, because pointing at one that already reads right
    /// is easier than writing down what it would have to be.
    /// </summary>
    [JsonRpcMethod("sceneTemplates/saveFromScene")]
    public async Task<SceneTemplateDto[]> SaveFromSceneAsync(
        string chapterGuid, string sceneId, string name)
    {
        await _workspace.Projects.SaveSceneAsTemplateAsync(chapterGuid, sceneId, name);
        return List();
    }

    [JsonRpcMethod("sceneTemplates/delete")]
    public async Task<SceneTemplateDto[]> DeleteAsync(string id)
    {
        Book.SceneTemplates.RemoveAll(t => t.Id == id);
        await _workspace.Projects.SaveProjectAsync();
        return List();
    }

    private static SceneTemplateDto ToDto(SceneTemplate t) => new(
        t.Id,
        t.Name,
        t.Synopsis,
        t.Pov,
        t.Stage,
        t.LabelKey,
        [.. t.Tags],
        t.PlotlineIds.Count,
        t.Content.Length);
}

/// <summary>
/// A scene template as the pickers show it. The prose is reported as a length
/// rather than sent: the picker only has to say whether there is a shape to
/// start from, and the whole of it can be a page long.
/// </summary>
public sealed record SceneTemplateDto(
    string Id,
    string Name,
    string Synopsis,
    string? Pov,
    string? Stage,
    string? LabelKey,
    string[] Tags,
    int PlotlineCount,
    int ContentLength);
