using Novalist.Core.Models;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Places in the project worth coming back to.
///
/// The favourite flag and saved lists answer "which scenes match this query".
/// A bookmark answers a different question - the paragraph where she finds out,
/// the entry I keep re-reading, the day the siege starts - and had nowhere to be
/// recorded, so people kept them in a scene called Notes.
/// </summary>
public sealed class BookmarksRpc
{
    private readonly Workspace _workspace;

    public BookmarksRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    /// <summary>
    /// Every bookmark, grouped sets first and loose ones last.
    ///
    /// The order is decided here rather than in the panel so every surface that
    /// shows bookmarks shows them the same way round. A named set is a
    /// deliberate act; the loose ones are the pile it was made out of.
    /// </summary>
    [JsonRpcMethod("bookmarks/list")]
    public BookmarkDto[] List()
        => [.. (_workspace.Projects.CurrentProject?.Bookmarks ?? [])
            .OrderBy(b => string.IsNullOrWhiteSpace(b.Group) ? 1 : 0)
            .ThenBy(b => b.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Order)
            .Select(ToDto)];

    /// <summary>
    /// Adds a bookmark, or updates one by id. A blank label falls back to the
    /// kind, so a bookmark made in one keystroke is still findable in the list.
    /// </summary>
    [JsonRpcMethod("bookmarks/save")]
    public async Task<BookmarkDto[]> SaveAsync(BookmarkDto bookmark)
    {
        var project = _workspace.Projects.CurrentProject
            ?? throw new InvalidOperationException("No project is open.");

        var existing = project.Bookmarks.FirstOrDefault(
            b => string.Equals(b.Id, bookmark.Id, StringComparison.Ordinal));
        if (existing == null)
        {
            existing = new Bookmark { Order = project.Bookmarks.Count };
            project.Bookmarks.Add(existing);
        }

        existing.Kind = Enum.TryParse<BookmarkKind>(bookmark.Kind, true, out var kind)
            ? kind
            : BookmarkKind.Scene;
        existing.Label = string.IsNullOrWhiteSpace(bookmark.Label)
            ? existing.Kind.ToString()
            : bookmark.Label.Trim();
        existing.Group = (bookmark.Group ?? string.Empty).Trim();
        existing.ChapterGuid = bookmark.ChapterGuid ?? string.Empty;
        existing.TargetId = bookmark.TargetId ?? string.Empty;
        existing.TargetType = bookmark.TargetType ?? string.Empty;
        existing.AnchorText = (bookmark.AnchorText ?? string.Empty).Trim();
        existing.StoryDate = (bookmark.StoryDate ?? string.Empty).Trim();

        await _workspace.Projects.SaveProjectAsync();
        return List();
    }

    [JsonRpcMethod("bookmarks/delete")]
    public async Task<BookmarkDto[]> DeleteAsync(string id)
    {
        var project = _workspace.Projects.CurrentProject;
        if (project == null) return [];

        if (project.Bookmarks.RemoveAll(
                b => string.Equals(b.Id, id, StringComparison.Ordinal)) > 0)
        {
            await _workspace.Projects.SaveProjectAsync();
        }
        return List();
    }

    /// <summary>
    /// The groups this project uses, so the picker offers what is there rather
    /// than asking for the same name to be spelled the same way twice.
    /// </summary>
    /// <summary>
    /// A few lines of whatever the bookmark points at.
    ///
    /// A bookmark that only navigates makes you go and look to remember why you
    /// kept it, which for a list of thirty means thirty trips. The preview is
    /// text rather than a rendered view: a paragraph of the scene is what tells
    /// somebody whether this is the passage they meant, and an embedded editor
    /// would be a second place the prose could be edited from.
    /// </summary>
    [JsonRpcMethod("bookmarks/preview")]
    public async Task<string> PreviewAsync(string bookmarkId)
    {
        var mark = (_workspace.Projects.CurrentProject?.Bookmarks ?? [])
            .FirstOrDefault(b => b.Id == bookmarkId);
        if (mark == null) return string.Empty;

        return mark.Kind switch
        {
            BookmarkKind.Scene => await ScenePreviewAsync(mark),
            BookmarkKind.Chapter => await ChapterPreviewAsync(mark),
            BookmarkKind.Entity => EntityPreview(mark),
            _ => string.Empty
        };
    }

    /// <summary>
    /// The passage the bookmark marks, or the opening of the scene when the
    /// prose it named has since been rewritten.
    /// </summary>
    private async Task<string> ScenePreviewAsync(Bookmark mark)
    {
        var chapter = _workspace.Projects.GetChaptersOrdered()
            .FirstOrDefault(c => c.Guid == mark.ChapterGuid);
        if (chapter == null) return string.Empty;
        var scene = _workspace.Projects.GetScenesForChapter(chapter.Guid)
            .FirstOrDefault(s => s.Id == mark.TargetId);
        if (scene == null) return string.Empty;

        var text = Core.Utilities.TextDiff.StripHtml(
            await _workspace.Projects.ReadSceneContentAsync(chapter, scene));
        return Core.Services.BookmarkPreview.Extract(text, mark.AnchorText);
    }

    private async Task<string> ChapterPreviewAsync(Bookmark mark)
    {
        var chapter = _workspace.Projects.GetChaptersOrdered()
            .FirstOrDefault(c => c.Guid == mark.ChapterGuid);
        var first = chapter == null
            ? null
            : _workspace.Projects.GetScenesForChapter(chapter.Guid).FirstOrDefault();
        if (chapter == null || first == null) return string.Empty;

        var text = Core.Utilities.TextDiff.StripHtml(
            await _workspace.Projects.ReadSceneContentAsync(chapter, first));
        return Core.Services.BookmarkPreview.Extract(text, null);
    }

    private string EntityPreview(Bookmark mark)
    {
        var entities = new Core.Services.EntityService(_workspace.Projects);
        var all = new List<IEntityData>();
        all.AddRange(entities.LoadCharactersAsync().GetAwaiter().GetResult());
        all.AddRange(entities.LoadLocationsAsync().GetAwaiter().GetResult());
        all.AddRange(entities.LoadItemsAsync().GetAwaiter().GetResult());
        all.AddRange(entities.LoadLoreAsync().GetAwaiter().GetResult());
        foreach (var typeDef in entities.GetCustomEntityTypes())
            all.AddRange(entities.LoadCustomEntitiesAsync(typeDef.TypeKey).GetAwaiter().GetResult());

        var entity = all.FirstOrDefault(e => e.Id == mark.TargetId);
        // Description lives on each concrete type rather than on the interface,
        // so the shapes are matched here rather than widening it for one reader.
        var description = entity switch
        {
            null => string.Empty,
            CharacterData c => c.Role,
            LocationData l => l.Description,
            ItemData i => i.Description,
            LoreData lo => lo.Description,
            // Custom types are the last arm rather than a named case with an
            // unreachable default after it: the set is closed and a dead
            // branch cannot be tested.
            _ => ((CustomEntityData)entity).Fields.Values.FirstOrDefault() ?? string.Empty
        };
        return Core.Services.BookmarkPreview.Extract(description, null);
    }

    [JsonRpcMethod("bookmarks/groups")]
    public string[] Groups()
        => [.. (_workspace.Projects.CurrentProject?.Bookmarks ?? [])
            .Select(b => b.Group)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)];

    private static BookmarkDto ToDto(Bookmark b) => new(
        b.Id, b.Kind.ToString(), b.Label, b.Group, b.ChapterGuid,
        b.TargetId, b.TargetType, b.AnchorText, b.StoryDate, b.Order);
}

/// <summary>
/// One bookmark. <c>Kind</c> is "Scene", "Chapter", "Entity", "Research",
/// "StoryDate" or "MapPin", and decides which of the target fields mean anything.
/// </summary>
public sealed record BookmarkDto(
    string Id,
    string Kind,
    string Label,
    string? Group,
    string? ChapterGuid,
    string? TargetId,
    string? TargetType,
    /// <summary>The passage inside a scene, as text - offsets drift as prose is
    /// edited above them, text either matches or opens the scene.</summary>
    string? AnchorText,
    string? StoryDate,
    int Order);
