using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Word targets on scenes, chapters and acts, and where each stands.</summary>
public sealed class WordTargetRpc
{
    private readonly Workspace _workspace;

    public WordTargetRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private WordTargetService Service => new(_workspace.Projects);

    /// <summary>Every part with a target, in reading order. Drives the binder
    /// bars and the Outliner's target column.</summary>
    [JsonRpcMethod("targets/all")]
    public WordTargetDto[] All()
        => [.. Service.All().Select(ToDto)];

    [JsonRpcMethod("targets/setScene")]
    public async Task<WordTargetDto[]> SetSceneAsync(string chapterGuid, string sceneId, int? target)
    {
        await Service.SetSceneTargetAsync(chapterGuid, sceneId, target);
        return All();
    }

    [JsonRpcMethod("targets/setChapter")]
    public async Task<WordTargetDto[]> SetChapterAsync(string chapterGuid, int? target)
    {
        await Service.SetChapterTargetAsync(chapterGuid, target);
        return All();
    }

    [JsonRpcMethod("targets/setAct")]
    public async Task<WordTargetDto[]> SetActAsync(string actName, int? target)
    {
        await Service.SetActTargetAsync(actName, target);
        return All();
    }

    private static WordTargetDto ToDto(WordTargetProgress p)
        => new(p.Kind, p.Id, p.Title, p.Words, p.Target, p.Explicit, p.Remaining, p.Overrun);
}

/// <summary>
/// One part's progress. <c>Explicit</c> distinguishes a target the writer set
/// here from one aggregated from below, so the UI can say which it is showing.
/// </summary>
public sealed record WordTargetDto(
    string Kind,
    string Id,
    string Title,
    int Words,
    int Target,
    bool Explicit,
    int Remaining,
    int Overrun);
