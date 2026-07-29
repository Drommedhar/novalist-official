using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Codex names sitting in prose as plain text, and turning them into mentions.
/// </summary>
public sealed class UnlinkedMentionRpc
{
    private readonly Workspace _workspace;

    public UnlinkedMentionRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private UnlinkedMentionService Service
        => new(_workspace.Projects, new EntityService(_workspace.Projects));

    /// <summary>
    /// Every unlinked occurrence in the book. Reads every scene, so it is asked
    /// for when the panel opens rather than kept live.
    /// </summary>
    [JsonRpcMethod("mentions/unlinked")]
    public async Task<UnlinkedMentionDto[]> FindAsync()
        => [.. (await Service.FindAsync()).Select(m => new UnlinkedMentionDto(
            m.ChapterGuid, m.ChapterTitle, m.SceneId, m.SceneTitle,
            m.EntityId, m.EntityName, m.TypeKey, m.Count, m.Context))];

    /// <summary>
    /// Turns every plain occurrence of one entity's names in one scene into a
    /// real mention, and returns what is still unlinked afterwards.
    /// </summary>
    [JsonRpcMethod("mentions/link")]
    public async Task<UnlinkedMentionDto[]> LinkAsync(
        string chapterGuid, string sceneId, string entityId)
    {
        await Service.LinkAsync(chapterGuid, sceneId, entityId);
        return await FindAsync();
    }
}

/// <summary>One place a Codex name appears in prose without being a mention.</summary>
public sealed record UnlinkedMentionDto(
    string ChapterGuid,
    string ChapterTitle,
    string SceneId,
    string SceneTitle,
    string EntityId,
    string EntityName,
    string TypeKey,
    int Count,
    string Context);
