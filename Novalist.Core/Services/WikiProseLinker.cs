using System.Text;
using System.Text.RegularExpressions;

namespace Novalist.Core.Services;

/// <summary>
/// Turns entity references inside authored section prose into cross-links the
/// Wiki renderer can follow. Two forms are recognised:
/// <list type="bullet">
/// <item>an explicit wiki-link <c>[[Name]]</c> (or <c>[[Name|display text]]</c>), and</item>
/// <item>a bare occurrence of an entity's name/alias, matched on word boundaries,</item>
/// </list>
/// both resolved through the shared <see cref="EntityResolveIndex"/> map — so an
/// ambiguous name (already dropped from the map) never links, mirroring the
/// editor's auto-mentions. A reference is emitted as a Markdown link with the
/// custom <c>nventity:{typeKey}/{id}</c> scheme; the renderer intercepts that
/// scheme and opens the article. Fenced/inline code, images, and existing
/// Markdown links are left untouched. Purely deterministic — no AI.
/// </summary>
public static partial class WikiProseLinker
{
    // Spans that must never be linkified inside: fenced code, inline code,
    // images, and existing Markdown links. Explicit [[wiki-links]] are matched
    // by the final alternative and rewritten; everything else between matches is
    // plain text open to bare-name linking.
    [GeneratedRegex(
        @"```[\s\S]*?```|`[^`]*`|!\[[^\]]*\]\([^)]*\)|\[[^\]]*\]\([^)]*\)|\[\[[^\]]+\]\]",
        RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    /// <summary>
    /// Rewrites <paramref name="content"/> so entity references become
    /// <c>nventity:</c> Markdown links. <paramref name="selfId"/> (the article's
    /// own entity, when known) is never linked, to avoid an article linking to
    /// itself. Returns the content unchanged when there is nothing to link.
    /// </summary>
    public static string Linkify(
        string? content,
        IReadOnlyDictionary<string, (string Id, string TypeKey)> resolve,
        string? selfId = null)
    {
        if (string.IsNullOrEmpty(content))
            return content ?? string.Empty;

        var matcher = BuildNameMatcher(resolve);
        var sb = new StringBuilder(content.Length + 32);
        var pos = 0;
        foreach (Match m in TokenRegex().Matches(content))
        {
            if (m.Index > pos)
                sb.Append(LinkifyPlain(content[pos..m.Index], matcher, resolve, selfId));

            if (m.Value.StartsWith("[[", StringComparison.Ordinal))
                sb.Append(ResolveWikiLink(m.Value, resolve, selfId));
            else
                sb.Append(m.Value); // protected span, kept verbatim

            pos = m.Index + m.Length;
        }
        if (pos < content.Length)
            sb.Append(LinkifyPlain(content[pos..], matcher, resolve, selfId));

        return sb.ToString();
    }

    /// <summary>A single case-insensitive matcher for every resolvable name,
    /// longer names first so "Aldric Vane" wins over "Aldric" at the same spot.
    /// Null when there are no names to match.</summary>
    private static Regex? BuildNameMatcher(IReadOnlyDictionary<string, (string Id, string TypeKey)> resolve)
    {
        var names = resolve.Keys
            .Where(k => k.Length > 0)
            .OrderByDescending(k => k.Length)
            .Select(Regex.Escape)
            .ToArray();
        if (names.Length == 0)
            return null;

        // Boundaries reject matches sitting inside a larger word so "Ann" does
        // not light up inside "Announcement".
        var pattern = @"(?<![\p{L}\p{N}_])(?:" + string.Join("|", names) + @")(?![\p{L}\p{N}_])";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string LinkifyPlain(
        string text, Regex? matcher,
        IReadOnlyDictionary<string, (string Id, string TypeKey)> resolve, string? selfId)
    {
        if (matcher == null || text.Length == 0)
            return text;
        return matcher.Replace(text, m =>
        {
            var key = EntityResolveIndex.Normalize(m.Value);
            return resolve.TryGetValue(key, out var hit)
                   && !string.Equals(hit.Id, selfId, StringComparison.Ordinal)
                ? Link(m.Value, hit.TypeKey, hit.Id)
                : m.Value;
        });
    }

    private static string ResolveWikiLink(
        string token, IReadOnlyDictionary<string, (string Id, string TypeKey)> resolve, string? selfId)
    {
        var inner = token[2..^2];
        var pipe = inner.IndexOf('|');
        var target = pipe >= 0 ? inner[..pipe] : inner;
        var display = (pipe >= 0 ? inner[(pipe + 1)..] : inner).Trim();
        if (display.Length == 0)
            display = target.Trim();

        var key = EntityResolveIndex.Normalize(target);
        return resolve.TryGetValue(key, out var hit)
               && !string.Equals(hit.Id, selfId, StringComparison.Ordinal)
            ? Link(display, hit.TypeKey, hit.Id)
            : display; // unresolved or self: plain text, brackets stripped
    }

    private static string Link(string text, string typeKey, string id)
        => $"[{text}](nventity:{typeKey}/{id})";
}
