using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Scene content IO for the editor round-trip.</summary>
public sealed class ScenesRpc
{
    private readonly Workspace _workspace;

    public ScenesRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    [JsonRpcMethod("scenes/read")]
    public async Task<SceneContentDto> ReadAsync(string chapterGuid, string sceneId)
    {
        var (chapter, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        var html = await _workspace.Projects.ReadSceneContentAsync(chapter, scene);
        return new SceneContentDto(sceneId, html);
    }

    [JsonRpcMethod("scenes/getMeta")]
    public SceneMetaDto GetMeta(string chapterGuid, string sceneId)
    {
        var (_, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        return new SceneMetaDto(scene.Id, scene.Synopsis, scene.Notes);
    }

    [JsonRpcMethod("scenes/write")]
    public async Task<SceneWriteResultDto> WriteAsync(
        string chapterGuid,
        string sceneId,
        string html,
        string plainText)
    {
        var wordCount = await _workspace.WriteSceneAsync(chapterGuid, sceneId, html, plainText);
        return new SceneWriteResultDto(sceneId, wordCount);
    }
}

public sealed record SceneContentDto(string SceneId, string Html);

public sealed record SceneMetaDto(string SceneId, string? Synopsis, string? Notes);

public sealed record SceneWriteResultDto(string SceneId, int WordCount);
