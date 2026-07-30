using Novalist.Core.Models;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// The book's keyword vocabulary, and the operations a free-text tag list could
/// never support.
///
/// Scene tags were a <c>List&lt;string&gt;</c> inside the analysis overrides with
/// nothing behind them: no registry, no colours, no rename. So "flashback",
/// "Flashback" and "flash-back" were three different tags, and fixing that meant
/// opening every scene that used the wrong one.
///
/// Renaming and deleting reach into the scenes, because a vocabulary that only
/// changes the registry leaves the scenes saying the old thing - which is the
/// bug, not the feature. Only tags a writer set are rewritten: the analysis
/// seeds suggestions, and rewriting those would be editing a machine's opinion
/// rather than the writer's.
/// </summary>
public sealed class KeywordsRpc
{
    private readonly Workspace _workspace;

    public KeywordsRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    [JsonRpcMethod("keywords/list")]
    public KeywordDto[] List()
    {
        var book = _workspace.Projects.ActiveBook;
        if (book == null) return [];

        var counts = Counts();
        return [.. book.Keywords
            .OrderBy(k => k.Order)
            .Select(k => new KeywordDto(
                k.Id, k.Name, k.Color, k.ParentId ?? string.Empty,
                counts.TryGetValue(k.Name, out var n) ? n : 0))];
    }

    /// <summary>
    /// Replaces the vocabulary. Entries without a name are dropped and a
    /// duplicate name is folded away, because two keywords spelt the same are
    /// the exact problem a registry exists to prevent.
    /// </summary>
    [JsonRpcMethod("keywords/save")]
    public async Task<KeywordDto[]> SaveAsync(KeywordDto[] keywords)
    {
        var book = _workspace.Projects.ActiveBook
            ?? throw new InvalidOperationException("No active book.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = 0;
        book.Keywords = [.. (keywords ?? [])
            .Where(k => !string.IsNullOrWhiteSpace(k.Name))
            .Where(k => seen.Add(k.Name!.Trim()))
            .Select(k => new Keyword
            {
                Id = string.IsNullOrWhiteSpace(k.Id) ? Guid.NewGuid().ToString() : k.Id!,
                Name = k.Name!.Trim(),
                Color = string.IsNullOrWhiteSpace(k.Color) ? "#8b8b8b" : k.Color!.Trim(),
                ParentId = string.IsNullOrWhiteSpace(k.ParentId) ? null : k.ParentId,
                Order = order++
            })];

        // A parent that is no longer in the list would hide its children
        // behind a heading that does not exist.
        var ids = book.Keywords.Select(k => k.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var keyword in book.Keywords)
            if (keyword.ParentId != null
                && (!ids.Contains(keyword.ParentId) || keyword.ParentId == keyword.Id))
                keyword.ParentId = null;

        await _workspace.Projects.SaveProjectAsync();
        return List();
    }

    /// <summary>
    /// Renames a keyword everywhere at once: the registry entry and every scene
    /// tagged with it. This is the whole reason the registry exists.
    /// </summary>
    [JsonRpcMethod("keywords/rename")]
    public async Task<KeywordDto[]> RenameAsync(string keywordId, string name)
    {
        var book = _workspace.Projects.ActiveBook;
        var keyword = book?.Keywords.FirstOrDefault(k => k.Id == keywordId);
        var trimmed = (name ?? string.Empty).Trim();
        if (book == null || keyword == null || trimmed.Length == 0) return List();

        // Renaming onto a name already in use would make two entries the same
        // keyword, which is what this was built to stop.
        if (book.Keywords.Any(k => k.Id != keywordId
                                   && string.Equals(k.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            return List();

        var old = keyword.Name;
        keyword.Name = trimmed;
        await _workspace.Projects.SaveProjectAsync();
        await RewriteAsync(old, trimmed);
        return List();
    }

    /// <summary>
    /// Removes a keyword from the vocabulary, and from every scene unless the
    /// writer says otherwise. Retiring a word from the list while leaving it
    /// written on forty scenes is how a vocabulary drifts back out of control.
    /// </summary>
    [JsonRpcMethod("keywords/delete")]
    public async Task<KeywordDto[]> DeleteAsync(string keywordId, bool clearFromScenes = true)
    {
        var book = _workspace.Projects.ActiveBook;
        var keyword = book?.Keywords.FirstOrDefault(k => k.Id == keywordId);
        if (book == null || keyword == null) return List();

        book.Keywords.Remove(keyword);
        // Children come back to the top rather than disappearing with the
        // heading they happened to be under.
        foreach (var child in book.Keywords.Where(k => k.ParentId == keywordId))
            child.ParentId = null;

        await _workspace.Projects.SaveProjectAsync();
        if (clearFromScenes) await RewriteAsync(keyword.Name, null);
        return List();
    }

    /// <summary>
    /// Adds every tag already written on a scene to the vocabulary.
    ///
    /// Without this a project with two hundred tags starts with an empty
    /// registry, which makes the feature useless to exactly the writers who
    /// need it. Spelling variants fold together here, which is the first
    /// clean-up the registry buys.
    /// </summary>
    [JsonRpcMethod("keywords/harvest")]
    public async Task<KeywordDto[]> HarvestAsync()
    {
        var book = _workspace.Projects.ActiveBook
            ?? throw new InvalidOperationException("No active book.");

        var known = book.Keywords.Select(k => k.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var order = book.Keywords.Count == 0 ? 0 : book.Keywords.Max(k => k.Order) + 1;
        var added = false;

        foreach (var name in Counts().Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            if (!known.Add(name)) continue;
            book.Keywords.Add(new Keyword { Name = name, Order = order++ });
            added = true;
        }

        if (added) await _workspace.Projects.SaveProjectAsync();
        return List();
    }

    /// <summary>The scenes carrying a keyword, so the writer can go to them.</summary>
    [JsonRpcMethod("keywords/scenes")]
    public KeywordSceneDto[] Scenes(string keywordId)
    {
        var keyword = _workspace.Projects.ActiveBook?.Keywords.FirstOrDefault(k => k.Id == keywordId);
        if (keyword == null) return [];

        var hits = new List<KeywordSceneDto>();
        foreach (var chapter in _workspace.Projects.GetChaptersOrdered())
            foreach (var scene in _workspace.Projects.GetScenesForChapter(chapter.Guid))
                if (Tags(scene).Any(t => string.Equals(t, keyword.Name, StringComparison.OrdinalIgnoreCase)))
                    hits.Add(new KeywordSceneDto(scene.Id, chapter.Guid, scene.Title));
        return [.. hits];
    }

    /// <summary>How many scenes carry each written tag, by name.</summary>
    private Dictionary<string, int> Counts()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var chapter in _workspace.Projects.GetChaptersOrdered())
            foreach (var scene in _workspace.Projects.GetScenesForChapter(chapter.Guid))
                foreach (var tag in Tags(scene).Distinct(StringComparer.OrdinalIgnoreCase))
                    counts[tag] = counts.TryGetValue(tag, out var n) ? n + 1 : 1;
        return counts;
    }

    /// <summary>
    /// The tags a writer set on a scene. Analysis-derived suggestions are not
    /// included: renaming those would be editing a machine's opinion rather
    /// than the writer's vocabulary.
    /// </summary>
    private static IReadOnlyList<string> Tags(SceneData scene)
        => scene.AnalysisOverrides?.Tags ?? [];

    /// <summary>
    /// Rewrites one tag across every scene. A null replacement removes it.
    /// </summary>
    private async Task RewriteAsync(string from, string? to)
    {
        var touched = false;
        foreach (var chapter in _workspace.Projects.GetChaptersOrdered())
        {
            foreach (var scene in _workspace.Projects.GetScenesForChapter(chapter.Guid))
            {
                var tags = scene.AnalysisOverrides?.Tags;
                if (tags == null) continue;

                var next = new List<string>();
                var changed = false;
                foreach (var tag in tags)
                {
                    if (!string.Equals(tag, from, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!next.Contains(tag, StringComparer.OrdinalIgnoreCase)) next.Add(tag);
                        continue;
                    }
                    changed = true;
                    // A rename onto a tag the scene already carries collapses
                    // to one, rather than leaving the scene tagged twice.
                    if (to != null && !next.Contains(to, StringComparer.OrdinalIgnoreCase))
                        next.Add(to);
                }

                if (!changed) continue;
                scene.AnalysisOverrides!.Tags = next;
                touched = true;
            }
        }

        if (touched) await _workspace.Projects.SaveScenesAsync();
    }
}

/// <summary>One keyword, with how many scenes carry it.</summary>
public sealed record KeywordDto(
    string? Id, string? Name, string? Color, string? ParentId, int SceneCount);

/// <summary>A scene carrying a keyword: enough to draw the row and open it.</summary>
public sealed record KeywordSceneDto(string SceneId, string ChapterGuid, string Title);
