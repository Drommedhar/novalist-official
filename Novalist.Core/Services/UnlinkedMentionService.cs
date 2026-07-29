using System.Text.RegularExpressions;
using Novalist.Core.Models;
using Novalist.Core.Utilities;

namespace Novalist.Core.Services;

/// <summary>One place a Codex name appears in prose without being a mention.</summary>
public sealed record UnlinkedMention(
    string ChapterGuid,
    string ChapterTitle,
    string SceneId,
    string SceneTitle,
    string EntityId,
    string EntityName,
    string TypeKey,
    /// <summary>How many times it occurs unlinked in that scene.</summary>
    int Count,
    /// <summary>A line of prose around the first occurrence.</summary>
    string Context);

/// <summary>
/// Codex names sitting in prose as plain text.
///
/// Novalist recognises a bare name for the Wiki and the hover card, but nothing
/// ever turned one into a real mention - so an imported or hand-typed
/// manuscript under-reports every appearance figure the Codex derives, and the
/// only fix was to retype each name through the @-picker.
/// </summary>
public sealed partial class UnlinkedMentionService
{
    private readonly IProjectService _projects;
    private readonly IEntityService _entities;

    public UnlinkedMentionService(IProjectService projects, IEntityService entities)
    {
        _projects = projects;
        _entities = entities;
    }

    /// <summary>
    /// Every unlinked occurrence in the book, scene by scene. Names already
    /// wrapped in a mention span are skipped, which is the whole point.
    /// </summary>
    public async Task<IReadOnlyList<UnlinkedMention>> FindAsync()
    {
        var resolve = await EntityResolveIndex.BuildAsync(_entities);
        if (resolve.Count == 0) return [];

        var names = await NamesByIdAsync();
        var results = new List<UnlinkedMention>();

        foreach (var chapter in _projects.GetChaptersOrdered())
            foreach (var scene in _projects.GetScenesForChapter(chapter.Guid))
            {
                var html = await _projects.ReadSceneContentAsync(chapter, scene);
                if (string.IsNullOrWhiteSpace(html)) continue;

                // Everything outside an existing mention span. A name already
                // linked is not an unlinked mention, and matching inside one
                // would offer to link what is already linked.
                var searchable = MentionSpanRegex().Replace(html, " ");
                var plain = TextDiff.StripHtml(searchable);

                foreach (var (name, target) in resolve)
                {
                    if (name.Length < 3) continue;
                    var matches = Regex.Matches(
                        plain, $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(name)}(?![\p{{L}}\p{{N}}_])",
                        RegexOptions.IgnoreCase);
                    if (matches.Count == 0) continue;

                    results.Add(new UnlinkedMention(
                        chapter.Guid, chapter.Title, scene.Id, scene.Title,
                        target.Id, names.GetValueOrDefault(target.Id, name), target.TypeKey,
                        matches.Count, Snippet(plain, matches[0].Index, matches[0].Length)));
                }
            }

        return results;
    }

    /// <summary>
    /// Wraps every unlinked occurrence of one entity's names in a scene, and
    /// returns how many it converted.
    ///
    /// Only occurrences outside existing spans and outside tags are touched: a
    /// blind string replace over HTML would rewrite attributes and produce a
    /// document the editor cannot read.
    /// </summary>
    public async Task<int> LinkAsync(string chapterGuid, string sceneId, string entityId)
    {
        var chapter = _projects.GetChaptersOrdered().FirstOrDefault(c => c.Guid == chapterGuid);
        var scene = chapter == null
            ? null
            : _projects.GetScenesForChapter(chapterGuid).FirstOrDefault(s => s.Id == sceneId);
        if (chapter == null || scene == null) return 0;

        var resolve = await EntityResolveIndex.BuildAsync(_entities);
        var names = resolve
            .Where(kv => kv.Value.Id == entityId)
            .Select(kv => kv.Key)
            .Where(n => n.Length >= 3)
            // Longest first, so "Mira Vance" is linked as one mention rather
            // than leaving "Vance" dangling inside it.
            .OrderByDescending(n => n.Length)
            .ToList();
        if (names.Count == 0) return 0;

        var html = await _projects.ReadSceneContentAsync(chapter, scene);
        if (string.IsNullOrEmpty(html)) return 0;

        var converted = 0;
        var rebuilt = ForEachTextRun(html, run => WrapNames(run, names, entityId, ref converted));

        if (converted == 0) return 0;
        await _projects.WriteSceneContentAsync(chapter, scene, rebuilt);
        return converted;
    }

    /// <summary>
    /// Wraps every whole-word occurrence of any of the names, longest first.
    ///
    /// One left-to-right pass rather than a replace per name: a second pass
    /// would find the shorter names inside the markup the first one just
    /// inserted, and wrap "Mira" again inside the span holding "Mira Vance".
    /// </summary>
    private static string WrapNames(
        string run, IReadOnlyList<string> names, string entityId, ref int converted)
    {
        var output = new System.Text.StringBuilder();
        var position = 0;

        while (position < run.Length)
        {
            var matched = false;
            foreach (var name in names)
            {
                if (position + name.Length > run.Length) continue;
                if (string.Compare(run, position, name, 0, name.Length,
                        StringComparison.OrdinalIgnoreCase) != 0) continue;
                if (IsWordChar(position - 1 >= 0 ? run[position - 1] : ' ')) continue;
                var after = position + name.Length;
                if (after < run.Length && IsWordChar(run[after])) continue;

                output.Append("<span class=\"nv-entity-mention\" data-entity-id=\"")
                      .Append(entityId)
                      .Append("\">")
                      .Append(run, position, name.Length)
                      .Append("</span>");
                position = after;
                converted++;
                matched = true;
                break;
            }
            if (!matched) output.Append(run[position++]);
        }

        return output.ToString();
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Runs a transform over the text between tags only, leaving markup and the
    /// insides of existing mention spans untouched.
    /// </summary>
    private static string ForEachTextRun(string html, Func<string, string> transform)
    {
        var output = new System.Text.StringBuilder();
        var position = 0;

        foreach (Match span in MentionSpanRegex().Matches(html))
        {
            output.Append(TransformOutsideTags(html[position..span.Index], transform));
            output.Append(span.Value);
            position = span.Index + span.Length;
        }
        output.Append(TransformOutsideTags(html[position..], transform));
        return output.ToString();
    }

    private static string TransformOutsideTags(string fragment, Func<string, string> transform)
    {
        var output = new System.Text.StringBuilder();
        var position = 0;
        while (position < fragment.Length)
        {
            var tagStart = fragment.IndexOf('<', position);
            if (tagStart < 0)
            {
                output.Append(transform(fragment[position..]));
                break;
            }
            output.Append(transform(fragment[position..tagStart]));
            var tagEnd = fragment.IndexOf('>', tagStart);
            if (tagEnd < 0)
            {
                output.Append(fragment[tagStart..]);
                break;
            }
            output.Append(fragment[tagStart..(tagEnd + 1)]);
            position = tagEnd + 1;
        }
        return output.ToString();
    }

    private async Task<Dictionary<string, string>> NamesByIdAsync()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in await _entities.LoadCharactersAsync())
            names[c.Id] = EntityResolveIndex.Compose(c.Name, c.Surname);
        foreach (var l in await _entities.LoadLocationsAsync()) names[l.Id] = l.Name;
        foreach (var i in await _entities.LoadItemsAsync()) names[i.Id] = i.Name;
        foreach (var l in await _entities.LoadLoreAsync()) names[l.Id] = l.Name;
        return names;
    }

    /// <summary>A readable line around an occurrence, for deciding at a glance.</summary>
    private static string Snippet(string text, int index, int length)
    {
        var start = Math.Max(0, index - 40);
        var end = Math.Min(text.Length, index + length + 40);
        var snippet = text[start..end].Replace('\n', ' ').Trim();
        return (start > 0 ? "..." : string.Empty) + snippet + (end < text.Length ? "..." : string.Empty);
    }

    [GeneratedRegex(
        @"<span[^>]*class=""[^""]*nv-entity-mention[^""]*""[^>]*>.*?</span>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex MentionSpanRegex();
}
