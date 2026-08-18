using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>One draft as the Drafts view lists it.</summary>
public sealed record DraftRowDto(
    string Id,
    string Name,
    bool IsActive,
    string Notes,
    string CreatedAt,
    string ParentDraftId,
    int Chapters,
    int Scenes);

/// <summary>A scene of some draft, for the transfer picker.</summary>
public sealed record DraftPickSceneDto(string Id, string Title);

/// <summary>A chapter of some draft, with the scenes under it.</summary>
public sealed record DraftChapterDto(string Guid, string Title, DraftPickSceneDto[] Scenes);

/// <summary>A draft's shape, read without switching to it.</summary>
public sealed record DraftStructureDto(string DraftId, string Name, DraftChapterDto[] Chapters);

/// <summary>What a transfer did, for the message the writer is shown.</summary>
public sealed record DraftTransferDto(int Chapters, int Scenes, int Replaced, bool Moved);

/// <summary>
/// Managing the drafts of a book: naming them, ordering them, saying what each
/// one is for, and sending chapters and scenes between them.
///
/// Creating, switching and deleting a draft have their own methods on
/// <c>project/*</c> because they change what the whole app is looking at. What
/// is here is everything that is about the drafts themselves.
/// </summary>
public sealed class DraftsRpc
{
    private readonly Workspace _workspace;

    public DraftsRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private DraftTransferService Transfer => new(_workspace.Projects, _workspace.FileService);

    /// <summary>
    /// Every draft of the open book, in the writer's order, with enough of its
    /// contents counted to tell them apart at a glance.
    /// </summary>
    [JsonRpcMethod("drafts/list")]
    public async Task<DraftRowDto[]> ListAsync()
    {
        var book = _workspace.Projects.ActiveBook;
        if (book == null) return [];

        // Counting means reading the folders of the drafts the writer is not
        // in, which is exactly what the transfer picker already does.
        var rows = new List<DraftRowDto>();
        foreach (var draft in book.Drafts)
        {
            var structure = await Transfer.ReadStructureAsync(draft.Id);
            rows.Add(new DraftRowDto(
                draft.Id,
                draft.Name,
                draft.Id == book.ActiveDraft?.Id,
                draft.Notes ?? string.Empty,
                draft.CreatedAt.ToString("o"),
                draft.ParentDraftId ?? string.Empty,
                structure?.Chapters.Count ?? 0,
                structure?.Chapters.Sum(c => c.Scenes.Count) ?? 0));
        }
        return [.. rows];
    }

    [JsonRpcMethod("drafts/rename")]
    public async Task<DraftRowDto[]> RenameAsync(string draftId, string name)
    {
        await _workspace.Projects.RenameDraftAsync(draftId, name);
        return await ListAsync();
    }

    [JsonRpcMethod("drafts/setNotes")]
    public async Task<DraftRowDto[]> SetNotesAsync(string draftId, string? notes)
    {
        await _workspace.Projects.SetDraftNotesAsync(draftId, notes);
        return await ListAsync();
    }

    [JsonRpcMethod("drafts/reorder")]
    public async Task<DraftRowDto[]> ReorderAsync(string[] orderedDraftIds)
    {
        await _workspace.Projects.ReorderDraftsAsync(orderedDraftIds);
        return await ListAsync();
    }

    /// <summary>
    /// A copy of a draft under a new name. The clone records where it came
    /// from, which is what lets the compare dialog show what the rewrite did.
    /// </summary>
    [JsonRpcMethod("drafts/duplicate")]
    public async Task<DraftRowDto[]> DuplicateAsync(string draftId, string name)
    {
        await _workspace.Projects.CreateDraftAsync(name, draftId);
        return await ListAsync();
    }

    [JsonRpcMethod("drafts/structure")]
    public async Task<DraftStructureDto?> StructureAsync(string draftId)
    {
        var structure = await Transfer.ReadStructureAsync(draftId);
        if (structure == null) return null;

        return new DraftStructureDto(
            structure.DraftId,
            structure.Name,
            [.. structure.Chapters.Select(c => new DraftChapterDto(
                c.Guid,
                c.Title,
                [.. c.Scenes.Select(s => new DraftPickSceneDto(s.Id, s.Title))]))]);
    }

    /// <param name="move">
    /// Whether the source draft gives the content up. False copies, which is
    /// what the interface offers first.
    /// </param>
    [JsonRpcMethod("drafts/transfer")]
    public async Task<DraftTransferDto> TransferContentAsync(
        string fromDraftId,
        string toDraftId,
        string[] chapterGuids,
        string[] sceneIds,
        bool move)
    {
        var result = await Transfer.TransferAsync(
            fromDraftId, toDraftId, chapterGuids, sceneIds, move);
        return new DraftTransferDto(
            result.Chapters, result.Scenes, result.Replaced, result.Moved);
    }
}
