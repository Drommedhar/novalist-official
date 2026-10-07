using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

internal static class SceneAnnotationTransfer
{
    private static readonly Regex Tags = new("""<(?:"[^"]*"|'[^']*'|[^'">])*>""",
        RegexOptions.NonBacktracking, TimeSpan.FromSeconds(2));
    private static readonly Regex Attributes = new("""(?<name>[\w:-]+)\s*=\s*(?:"(?<id>[^"]*)"|'(?<id>[^']*)')""",
        RegexOptions.NonBacktracking, TimeSpan.FromSeconds(2));

    public static void Split(SceneData original, SceneData created, string before, string after)
    {
        (original.Comments, created.Comments) = SplitAnnotations(original.Comments, before, after, "data-comment-id", comment => comment.Id);
        (original.Footnotes, created.Footnotes) = SplitAnnotations(original.Footnotes, before, after, "data-fn-id", note => note.Id);
    }

    private static (List<T>? Before, List<T>? After) SplitAnnotations<T>(List<T>? items,
        string before, string after, string attribute, Func<T, string> id) where T : class
    {
        if (items == null) return (null, null);
        var beforeIds = Ids(before, attribute);
        var afterIds = Ids(after, attribute);
        return (items.Where(item => beforeIds.Contains(id(item)) || !afterIds.Contains(id(item))).ToList(),
            items.Where(item => afterIds.Contains(id(item))).Select(Clone).ToList());
    }

    public static string Merge(SceneData first, SceneData second, string secondHtml)
    {
        first.Comments = MergeAnnotations(first.Comments, second.Comments, ref secondHtml, "data-comment-id",
            comment => comment.Id, (comment, id) => comment.Id = id);
        first.Footnotes = MergeAnnotations(first.Footnotes, second.Footnotes, ref secondHtml, "data-fn-id",
            note => note.Id, (note, id) => note.Id = id);
        return secondHtml;
    }

    private static List<T>? MergeAnnotations<T>(List<T>? first, List<T>? second, ref string html,
        string attribute, Func<T, string> id, Action<T, string> setId) where T : class
    {
        if (second == null || second.Count == 0) return first;
        var result = first == null ? [] : new List<T>(first);
        var used = result.Select(id).ToHashSet(StringComparer.Ordinal);
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in second)
        {
            var copy = Clone(item);
            if (!used.Add(id(copy)))
            {
                var existing = result.First(entry => id(entry) == id(copy));
                if (JsonSerializer.Serialize(existing) == JsonSerializer.Serialize(copy)) continue;
                var newId = Guid.NewGuid().ToString();
                replacements[id(copy)] = newId;
                setId(copy, newId);
                used.Add(newId);
            }
            result.Add(copy);
        }
        html = Tags.Replace(html, tag => Attributes.Replace(tag.Value, match =>
            IsAttribute(match, attribute) && replacements.TryGetValue(WebUtility.HtmlDecode(match.Groups["id"].Value), out var replacement)
                ? attribute + "=\"" + replacement + "\""
                : match.Value));
        return result;
    }

    private static HashSet<string> Ids(string html, string attribute)
        => Tags.Matches(html).SelectMany(tag => Attributes.Matches(tag.Value)).Where(match => IsAttribute(match, attribute))
            .Select(match => WebUtility.HtmlDecode(match.Groups["id"].Value)).ToHashSet(StringComparer.Ordinal);

    private static bool IsAttribute(Match match, string attribute)
        => match.Groups["name"].Value.Equals(attribute, StringComparison.OrdinalIgnoreCase);

    private static T Clone<T>(T value) where T : class
        => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))
            ?? throw new InvalidOperationException("An annotation could not be copied.");
}
