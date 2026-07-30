using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>A piece of prose the writer cut but kept.</summary>
public sealed record DarlingDto(
    string Id, string Text, string Source, string Note, DateTime CreatedAt);

/// <summary>
/// Prose the writer cut and did not want to lose.
///
/// Deleted text was recoverable only by opening a snapshot of the whole scene
/// and reading it for the paragraph that used to be there. A paragraph cut
/// because it does not belong in this chapter is not a mistake to undo; it is
/// writing looking for a different home.
/// </summary>
public class DarlingsRpc(Workspace workspace)
{
    private readonly DarlingsService _darlings =
        new(workspace.Projects, workspace.FileService);

    private static DarlingDto[] ToDto(IEnumerable<Darling> all)
        => [.. all.Select(d => new DarlingDto(d.Id, d.Text, d.Source, d.Note, d.CreatedAt))];

    /// <summary>Everything kept, newest first.</summary>
    [JsonRpcMethod("darlings/list")]
    public async Task<DarlingDto[]> ListAsync() => ToDto(await _darlings.ListAsync());

    /// <summary>Keeps a piece of cut prose.</summary>
    [JsonRpcMethod("darlings/keep")]
    public async Task<DarlingDto[]> KeepAsync(string text, string? source = null, string? note = null)
        // Deliberately no logging of any kind here: this method's entire
        // payload is the writer's prose.
        => ToDto(await _darlings.KeepAsync(text, source, note));

    /// <summary>Rewrites what the writer said about a cut.</summary>
    [JsonRpcMethod("darlings/setNote")]
    public async Task<DarlingDto[]> SetNoteAsync(string id, string? note)
        => ToDto(await _darlings.SetNoteAsync(id, note));

    /// <summary>Throws one away for good.</summary>
    [JsonRpcMethod("darlings/remove")]
    public async Task<DarlingDto[]> RemoveAsync(string id)
        => ToDto(await _darlings.RemoveAsync(id));
}
