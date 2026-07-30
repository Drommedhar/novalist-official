using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>
/// Free-form Wiki articles: the ones about the world rather than about one
/// entry in it.
///
/// Every article was generated from a Codex entity, so an essay on the economy
/// or on the rules of the magic had to hang off whichever entity it least badly
/// belonged to, or live outside the Wiki in Research. Only Locations nested, so
/// filing one page under another was not possible either.
/// </summary>
public class WikiPageService(IProjectService projectService)
{
    private readonly IProjectService _projectService = projectService;

    private List<WikiPage> Pages => _projectService.CurrentProject?.WikiPages ?? [];

    /// <summary>Every page, parents before children, siblings in their own order.</summary>
    public IReadOnlyList<WikiPage> GetAll()
        => [.. Pages.OrderBy(p => p.Order).ThenBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>One page, or null.</summary>
    public WikiPage? Get(string id) => Pages.FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// Creates or updates a page.
    ///
    /// A page with no title is still saved: a writer who starts an essay and
    /// names it afterwards should not lose the essay.
    /// </summary>
    public async Task<WikiPage> SaveAsync(
        string? id, string title, string? body, string? parentId)
    {
        var project = _projectService.CurrentProject
            ?? throw new InvalidOperationException("No project loaded.");

        var page = project.WikiPages.FirstOrDefault(p => p.Id == id);
        if (page == null)
        {
            page = new WikiPage
            {
                Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString() : id,
                Order = project.WikiPages.Count
            };
            project.WikiPages.Add(page);
        }

        page.Title = (title ?? string.Empty).Trim();
        page.Body = body ?? string.Empty;
        page.ParentId = Reparent(project.WikiPages, page, parentId);
        page.UpdatedAt = DateTime.UtcNow;

        await _projectService.SaveProjectAsync();
        return page;
    }

    /// <summary>
    /// Moves a page under another, or out to the top with an empty parent.
    /// </summary>
    public async Task<WikiPage?> MoveAsync(string id, string? parentId)
    {
        var project = _projectService.CurrentProject
            ?? throw new InvalidOperationException("No project loaded.");

        var page = project.WikiPages.FirstOrDefault(p => p.Id == id);
        if (page == null) return null;

        page.ParentId = Reparent(project.WikiPages, page, parentId);
        page.UpdatedAt = DateTime.UtcNow;
        await _projectService.SaveProjectAsync();
        return page;
    }

    /// <summary>
    /// Deletes a page. Its children move up to where it was rather than
    /// disappearing with it - a page is a container as much as an article, and
    /// deleting the container should not take the writing inside it.
    /// </summary>
    public async Task<bool> DeleteAsync(string id)
    {
        var project = _projectService.CurrentProject
            ?? throw new InvalidOperationException("No project loaded.");

        var page = project.WikiPages.FirstOrDefault(p => p.Id == id);
        if (page == null) return false;

        foreach (var child in project.WikiPages.Where(p => p.ParentId == id))
            child.ParentId = page.ParentId;

        project.WikiPages.Remove(page);
        await _projectService.SaveProjectAsync();
        return true;
    }

    /// <summary>
    /// The parent a page may actually have.
    ///
    /// A page cannot sit under itself or under one of its own descendants: that
    /// makes a ring, and a ring makes the tree unreachable from the top - the
    /// pages inside it are still in the file and can never be opened again.
    /// </summary>
    internal static string Reparent(List<WikiPage> all, WikiPage page, string? parentId)
    {
        var wanted = (parentId ?? string.Empty).Trim();
        if (wanted.Length == 0) return string.Empty;
        if (wanted == page.Id) return page.ParentId;
        if (all.All(p => p.Id != wanted)) return string.Empty;

        // Walk up from the wanted parent. Meeting this page means the wanted
        // parent is below it, and the move would close a ring.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cursor = wanted;
        while (cursor.Length > 0 && seen.Add(cursor))
        {
            if (cursor == page.Id) return page.ParentId;
            cursor = all.FirstOrDefault(p => p.Id == cursor)?.ParentId ?? string.Empty;
        }

        return wanted;
    }
}
