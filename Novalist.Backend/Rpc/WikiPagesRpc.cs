using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>An article about the world rather than about one entry in it.</summary>
public sealed record WikiPageDto(
    string Id, string Title, string ParentId, string Body, int Order, DateTime UpdatedAt);

/// <summary>
/// Free-form Wiki articles, nested to any depth.
///
/// Every article was generated from a Codex entity, so an essay on how the
/// economy works had to hang off whichever entity it least badly belonged to,
/// or live in Research outside the Wiki entirely.
/// </summary>
public class WikiPagesRpc(Workspace workspace)
{
    private readonly WikiPageService _pages = new(workspace.Projects);

    private static WikiPageDto ToDto(Core.Models.WikiPage p)
        => new(p.Id, p.Title, p.ParentId, p.Body, p.Order, p.UpdatedAt);

    /// <summary>Every page. The tree is built by the renderer from the parents.</summary>
    [JsonRpcMethod("pages/list")]
    public WikiPageDto[] List() => [.. _pages.GetAll().Select(ToDto)];

    /// <summary>One page, or null when it is gone.</summary>
    [JsonRpcMethod("pages/get")]
    public WikiPageDto? Get(string id)
    {
        var page = _pages.Get(id);
        return page == null ? null : ToDto(page);
    }

    /// <summary>
    /// Creates or updates a page. A page with no title is still saved: a writer
    /// who starts an essay and names it afterwards should not lose the essay.
    /// </summary>
    [JsonRpcMethod("pages/save")]
    public async Task<WikiPageDto[]> SaveAsync(
        string? id, string title, string? body = null, string? parentId = null)
    {
        await _pages.SaveAsync(id, title, body, parentId);
        return List();
    }

    /// <summary>Moves a page under another, or out to the top level.</summary>
    [JsonRpcMethod("pages/move")]
    public async Task<WikiPageDto[]> MoveAsync(string id, string? parentId)
    {
        await _pages.MoveAsync(id, parentId);
        return List();
    }

    /// <summary>
    /// Deletes a page. Its children move up to where it was rather than
    /// vanishing with it.
    /// </summary>
    [JsonRpcMethod("pages/delete")]
    public async Task<WikiPageDto[]> DeleteAsync(string id)
    {
        await _pages.DeleteAsync(id);
        return List();
    }
}
