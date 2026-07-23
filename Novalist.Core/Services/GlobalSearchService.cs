using Novalist.Core.Models;
using Novalist.Core.Utilities;

namespace Novalist.Core.Services;

/// <summary>Where a global-search hit came from. The renderer uses this to group
/// results and to decide what opening the hit should do.</summary>
public static class GlobalSearchKinds
{
    public const string Scene = "scene";           // scene title
    public const string SceneText = "sceneText";   // scene prose
    public const string SceneNote = "sceneNote";   // synopsis or notes
    public const string Annotation = "annotation"; // comment or footnote
    public const string Entity = "entity";         // Codex entry
    public const string Research = "research";     // research item
    public const string Timeline = "timeline";     // manual timeline event
}

/// <summary>
/// One global-search result. The id fields say how to open it: a chapter/scene
/// pair opens the editor, an entity opens its Wiki article, a research id opens
/// the Research view.
/// </summary>
public sealed record GlobalSearchHit(
    string Kind,
    string Title,
    string? Subtitle,
    string? Snippet,
    string? ChapterGuid = null,
    string? SceneId = null,
    string? EntityTypeKey = null,
    string? EntityId = null,
    string? ResearchId = null);

/// <summary>
/// One case-insensitive substring query across everything the writer has
/// written: scene titles and prose, synopses and notes, comments and footnotes,
/// every Codex entry (names, aliases, field values, sections), research items,
/// and manual timeline events. Find &amp; Replace only ever saw scene prose, so
/// this is the surface that answers "I wrote about this somewhere".
/// Purely deterministic — no index, no AI; a novel-sized project is small enough
/// to scan directly.
/// </summary>
public sealed class GlobalSearchService
{
    private const int SnippetPad = 40;

    private readonly IProjectService _projects;
    private readonly IEntityService _entities;
    private readonly IResearchService _research;

    public GlobalSearchService(
        IProjectService projects, IEntityService entities, IResearchService research)
    {
        _projects = projects;
        _entities = entities;
        _research = research;
    }

    /// <summary>Runs the query. <paramref name="limit"/> caps the number of hits
    /// returned per kind so one noisy source cannot crowd out the others.</summary>
    public async Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(
        string query, int limit = 20, CancellationToken cancellationToken = default)
    {
        var needle = (query ?? string.Empty).Trim();
        if (needle.Length == 0)
            return [];

        var hits = new List<GlobalSearchHit>();
        await AddSceneHitsAsync(needle, limit, hits, cancellationToken).ConfigureAwait(false);
        await AddEntityHitsAsync(needle, limit, hits).ConfigureAwait(false);
        AddResearchHits(needle, limit, hits);
        AddTimelineHits(needle, limit, hits);
        return hits;
    }

    private async Task AddSceneHitsAsync(
        string needle, int limit, List<GlobalSearchHit> hits, CancellationToken cancellationToken)
    {
        var titles = new List<GlobalSearchHit>();
        var texts = new List<GlobalSearchHit>();
        var notes = new List<GlobalSearchHit>();
        var annotations = new List<GlobalSearchHit>();

        foreach (var chapter in _projects.GetChaptersOrdered())
        {
            foreach (var scene in _projects.GetScenesForChapter(chapter.Guid))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (titles.Count < limit && Contains(scene.Title, needle))
                    titles.Add(new GlobalSearchHit(
                        GlobalSearchKinds.Scene, scene.Title, chapter.Title, null,
                        chapter.Guid, scene.Id));

                if (notes.Count < limit && Contains(scene.Synopsis, needle))
                    notes.Add(new GlobalSearchHit(
                        GlobalSearchKinds.SceneNote, scene.Title, chapter.Title,
                        Snippet(scene.Synopsis!, needle), chapter.Guid, scene.Id));

                if (notes.Count < limit && Contains(scene.Notes, needle))
                    notes.Add(new GlobalSearchHit(
                        GlobalSearchKinds.SceneNote, scene.Title, chapter.Title,
                        Snippet(scene.Notes!, needle), chapter.Guid, scene.Id));

                foreach (var comment in scene.Comments ?? [])
                {
                    if (annotations.Count >= limit) break;
                    if (Contains(comment.Text, needle) || Contains(comment.AnchorText, needle))
                        annotations.Add(new GlobalSearchHit(
                            GlobalSearchKinds.Annotation, scene.Title, chapter.Title,
                            Snippet(comment.Text, needle), chapter.Guid, scene.Id));
                }

                foreach (var footnote in scene.Footnotes ?? [])
                {
                    if (annotations.Count >= limit) break;
                    if (Contains(footnote.Text, needle))
                        annotations.Add(new GlobalSearchHit(
                            GlobalSearchKinds.Annotation, scene.Title, chapter.Title,
                            Snippet(footnote.Text, needle), chapter.Guid, scene.Id));
                }

                if (texts.Count < limit)
                {
                    var html = await _projects.ReadSceneContentAsync(chapter, scene).ConfigureAwait(false);
                    var plain = TextDiff.StripHtml(html);
                    if (Contains(plain, needle))
                        texts.Add(new GlobalSearchHit(
                            GlobalSearchKinds.SceneText, scene.Title, chapter.Title,
                            Snippet(plain, needle), chapter.Guid, scene.Id));
                }
            }
        }

        hits.AddRange(titles);
        hits.AddRange(texts);
        hits.AddRange(notes);
        hits.AddRange(annotations);
    }

    private async Task AddEntityHitsAsync(string needle, int limit, List<GlobalSearchHit> hits)
    {
        var found = new List<GlobalSearchHit>();

        void Consider(
            string typeKey, string id, string name, IReadOnlyList<string> aliases,
            IReadOnlyList<EntitySection> sections, IReadOnlyDictionary<string, string> customProps,
            params string?[] fields)
        {
            if (found.Count >= limit) return;

            if (Contains(name, needle))
            {
                found.Add(new GlobalSearchHit(
                    GlobalSearchKinds.Entity, name, typeKey, null, null, null, typeKey, id));
                return;
            }
            var alias = aliases.FirstOrDefault(a => Contains(a, needle));
            if (alias != null)
            {
                found.Add(new GlobalSearchHit(
                    GlobalSearchKinds.Entity, name, typeKey, alias, null, null, typeKey, id));
                return;
            }
            var field = fields.FirstOrDefault(f => Contains(f, needle));
            if (field != null)
            {
                found.Add(new GlobalSearchHit(
                    GlobalSearchKinds.Entity, name, typeKey, Snippet(field, needle),
                    null, null, typeKey, id));
                return;
            }
            var prop = customProps.FirstOrDefault(p => Contains(p.Value, needle));
            if (prop.Value != null)
            {
                found.Add(new GlobalSearchHit(
                    GlobalSearchKinds.Entity, name, typeKey, Snippet(prop.Value, needle),
                    null, null, typeKey, id));
                return;
            }
            var section = sections.FirstOrDefault(s =>
                Contains(s.Content, needle) || Contains(s.Title, needle));
            if (section != null)
                found.Add(new GlobalSearchHit(
                    GlobalSearchKinds.Entity, name, typeKey,
                    Snippet(TextDiff.StripHtml(section.Content), needle), null, null, typeKey, id));
        }

        foreach (var c in await _entities.LoadCharactersAsync().ConfigureAwait(false))
            Consider("character", c.Id, EntityResolveIndex.Compose(c.Name, c.Surname), c.Aliases,
                c.Sections, c.CustomProperties,
                c.Role, c.Group, c.Gender, c.Age, c.DistinguishingFeatures);

        foreach (var l in await _entities.LoadLocationsAsync().ConfigureAwait(false))
            Consider("location", l.Id, l.Name, l.Aliases, l.Sections, l.CustomProperties,
                l.Type, l.Parent, l.Description);

        foreach (var i in await _entities.LoadItemsAsync().ConfigureAwait(false))
            Consider("item", i.Id, i.Name, i.Aliases, i.Sections, i.CustomProperties,
                i.Type, i.Origin, i.Description);

        foreach (var l in await _entities.LoadLoreAsync().ConfigureAwait(false))
            Consider("lore", l.Id, l.Name, l.Aliases, l.Sections, l.CustomProperties,
                l.Category, l.Description);

        foreach (var typeDef in _entities.GetCustomEntityTypes())
        {
            var entities = await _entities.LoadCustomEntitiesAsync(typeDef.TypeKey).ConfigureAwait(false);
            foreach (var e in entities)
                Consider(typeDef.TypeKey, e.Id, e.Name, e.Aliases, e.Sections, e.CustomProperties,
                    [.. e.Fields.Values]);
        }

        hits.AddRange(found);
    }

    private void AddResearchHits(string needle, int limit, List<GlobalSearchHit> hits)
    {
        var found = 0;
        foreach (var item in _research.GetAll())
        {
            if (found >= limit) break;
            var matchesTag = item.Tags.Any(tag => Contains(tag, needle));
            if (!Contains(item.Title, needle) && !Contains(item.Content, needle) && !matchesTag)
                continue;
            hits.Add(new GlobalSearchHit(
                GlobalSearchKinds.Research, item.Title, item.Type.ToString(),
                Contains(item.Content, needle) ? Snippet(item.Content, needle) : null,
                ResearchId: item.Id));
            found++;
        }
    }

    private void AddTimelineHits(string needle, int limit, List<GlobalSearchHit> hits)
    {
        var events = _projects.ProjectSettings?.Timeline?.ManualEvents;
        if (events == null) return;

        var found = 0;
        foreach (var e in events)
        {
            if (found >= limit) break;
            if (!Contains(e.Title, needle) && !Contains(e.Description, needle)) continue;
            hits.Add(new GlobalSearchHit(
                GlobalSearchKinds.Timeline, e.Title, e.Date,
                Contains(e.Description, needle) ? Snippet(e.Description, needle) : null));
            found++;
        }
    }

    private static bool Contains(string? haystack, string needle)
        => !string.IsNullOrEmpty(haystack)
           && haystack.Contains(needle, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>A short excerpt around the first match, ellipsised at both ends
    /// when it is cut out of a longer text.</summary>
    private static string Snippet(string text, string needle)
    {
        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var at = collapsed.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase);
        if (at < 0) return Truncate(collapsed);

        var start = Math.Max(0, at - SnippetPad);
        var end = Math.Min(collapsed.Length, at + needle.Length + SnippetPad);
        var slice = collapsed[start..end];
        if (start > 0) slice = "..." + slice;
        if (end < collapsed.Length) slice += "...";
        return slice;
    }

    private static string Truncate(string text)
        => text.Length <= SnippetPad * 2 ? text : text[..(SnippetPad * 2)] + "...";
}
