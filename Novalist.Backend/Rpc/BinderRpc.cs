using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Pinning a chapter or a scene to the top of the binder.
///
/// <c>IsFavorite</c> has been on the model, saved to disk and settable through
/// <c>ProjectService</c> since long before this file existed - and nothing over
/// the wire ever called it, so no writer could set one. A flag that only the
/// tests can reach is not a feature.
/// </summary>
public sealed class BinderRpc
{
    private readonly Workspace _workspace;

    public BinderRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    /// <summary>Pins or unpins a chapter. Returns the state so the binder redraws.</summary>
    [JsonRpcMethod("binder/pinChapter")]
    public async Task<object> PinChapterAsync(string chapterGuid, bool pinned)
    {
        await _workspace.Projects.SetChapterFavoriteAsync(chapterGuid, pinned);
        return _workspace.BuildState();
    }

    /// <summary>Pins or unpins a scene.</summary>
    [JsonRpcMethod("binder/pinScene")]
    public async Task<object> PinSceneAsync(string chapterGuid, string sceneId, bool pinned)
    {
        await _workspace.Projects.SetSceneFavoriteAsync(chapterGuid, sceneId, pinned);
        return _workspace.BuildState();
    }

    /// <summary>
    /// The book's threads, so the binder can offer a filter that names them.
    /// The scene rows carry plotline ids already; without the names they would
    /// be a filter over opaque guids.
    /// </summary>
    [JsonRpcMethod("binder/plotlines")]
    public BinderPlotlineDto[] Plotlines()
        => [.. (_workspace.Projects.ActiveBook?.Plotlines ?? [])
            .OrderBy(p => p.Order)
            .Select(p => new BinderPlotlineDto(p.Id, p.Name, p.Color))];
}

/// <summary>One thread, named, for the binder's filter.</summary>
public sealed record BinderPlotlineDto(string Id, string Name, string Color);
