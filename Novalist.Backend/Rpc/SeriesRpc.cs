using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// The project above the book: every book at once, and where the shared Codex
/// entries appear across them.
///
/// Every analytical read path in Novalist goes through the active book, so a
/// World Bible character in a trilogy showed one book's worth of appearances
/// and a writer planning a series had nowhere to see the series.
/// </summary>
public sealed class SeriesRpc
{
    private readonly Workspace _workspace;

    public SeriesRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    /// <summary>
    /// Every book with its size and shape, and every shared Codex entry with
    /// the books it appears in.
    ///
    /// Reading another book means opening it - scene paths hang off the active
    /// book's folder, so there is no read-only way in - and the writer is put
    /// back in the book they were in when it finishes.
    /// </summary>
    /// <summary>
    /// Sets who wrote one book, for an anthology whose volumes are by
    /// different people. Empty means the project's author, which is the answer
    /// for every book that is not part of a collection.
    /// </summary>
    [JsonRpcMethod("series/setBookAuthor")]
    public async Task<SeriesOverviewDto> SetBookAuthorAsync(string bookId, string author)
    {
        var project = _workspace.Projects.CurrentProject
            ?? throw new InvalidOperationException("No project open.");
        var book = project.Books.FirstOrDefault(b => b.Id == bookId);
        if (book != null)
        {
            book.Author = (author ?? string.Empty).Trim();
            await _workspace.Projects.SaveProjectAsync();
        }
        return await OverviewAsync();
    }

    [JsonRpcMethod("series/overview")]
    public async Task<SeriesOverviewDto> OverviewAsync()
    {
        var projects = _workspace.Projects;
        var project = projects.CurrentProject
            ?? throw new InvalidOperationException("No project open.");

        var entities = new EntityService(projects);
        var names = await SharedEntityNamesAsync(entities);

        var books = new List<SeriesBookDto>();
        var appearances = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var openedWith = project.ActiveBookId;

        try
        {
            foreach (var book in project.Books.ToList())
            {
                if (project.Books.Count > 1) await projects.SwitchBookAsync(book.Id);

                var chapters = projects.GetChaptersOrdered();
                var scenes = chapters
                    .SelectMany(c => projects.GetScenesForChapter(c.Guid))
                    .Where(s => s.ArchivedAt == null)
                    .ToList();

                books.Add(new SeriesBookDto(
                    book.Id,
                    book.Name,
                    book.Author,
                    chapters.Count,
                    scenes.Count,
                    scenes.Sum(s => s.WordCount),
                    scenes.Count(s => !string.IsNullOrEmpty(s.Stage))));

                foreach (var id in await BookAppearancesAsync(chapters, scenes))
                {
                    if (!names.ContainsKey(id)) continue;
                    if (!appearances.TryGetValue(id, out var inBooks))
                        appearances[id] = inBooks = [];
                    if (!inBooks.Contains(book.Id)) inBooks.Add(book.Id);
                }
            }
        }
        finally
        {
            if (project.ActiveBookId != openedWith && openedWith != null)
                await projects.SwitchBookAsync(openedWith);
        }

        var rows = appearances
            .Select(pair => new SeriesEntityDto(
                pair.Key,
                names[pair.Key],
                [.. pair.Value],
                pair.Value.Count))
            .OrderByDescending(r => r.BookCount)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SeriesOverviewDto([.. books], rows);
    }

    /// <summary>
    /// Names of the entries a series can share - the World Bible ones. A book's
    /// own entries cannot appear in another book, so listing them here would be
    /// a row of one every time.
    /// </summary>
    private static async Task<Dictionary<string, string>> SharedEntityNamesAsync(EntityService entities)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in await entities.LoadCharactersAsync())
            if (c.IsWorldBible) names[c.Id] = EntityResolveIndex.Compose(c.Name, c.Surname);
        foreach (var l in await entities.LoadLocationsAsync())
            if (l.IsWorldBible) names[l.Id] = l.Name;
        foreach (var i in await entities.LoadItemsAsync())
            if (i.IsWorldBible) names[i.Id] = i.Name;
        foreach (var l in await entities.LoadLoreAsync())
            if (l.IsWorldBible) names[l.Id] = l.Name;
        return names;
    }

    /// <summary>
    /// Every entity id this book's scenes point at: the cast the writer
    /// recorded, plus the mentions they confirmed in the prose.
    /// </summary>
    private async Task<HashSet<string>> BookAppearancesAsync(
        IReadOnlyList<ChapterData> chapters, IReadOnlyList<SceneData> scenes)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scene in scenes)
        {
            foreach (var id in scene.Cast ?? []) found.Add(id);
            if (!string.IsNullOrEmpty(scene.FocusEntityId)) found.Add(scene.FocusEntityId);
        }

        foreach (var chapter in chapters)
        {
            foreach (var scene in _workspace.Projects.GetScenesForChapter(chapter.Guid))
            {
                if (scene.ArchivedAt != null) continue;
                var html = await _workspace.Projects.ReadSceneContentAsync(chapter, scene);
                foreach (var id in AppearanceIndexService.ExtractMentionIds(html)) found.Add(id);
            }
        }
        return found;
    }
}

/// <summary>One book in the series, at a glance.</summary>
public sealed record SeriesBookDto(
    string Id, string Name, string Author, int Chapters, int Scenes, int Words, int StagedScenes);

/// <summary>
/// A shared Codex entry and the books it appears in. <c>BookCount</c> is what
/// makes the list worth reading: a character in one book of three is either a
/// walk-on or a thread that was dropped.
/// </summary>
public sealed record SeriesEntityDto(string Id, string Name, string[] BookIds, int BookCount);

public sealed record SeriesOverviewDto(SeriesBookDto[] Books, SeriesEntityDto[] Entities);
