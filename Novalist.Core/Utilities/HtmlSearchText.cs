using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Novalist.Core.Utilities;

internal sealed class HtmlSearchText
{
    private sealed record Part(string Raw, string Text, int Start, bool Markup);
    private readonly List<Part> _parts = [];
    public string Text { get; }

    public HtmlSearchText(string html)
    {
        var visible = new StringBuilder();
        foreach (Match token in Regex.Matches(html, """<(?:"[^"]*"|'[^']*'|[^'">])*>|[^<]+|<""",
            RegexOptions.NonBacktracking, TimeSpan.FromSeconds(2)))
        {
            var raw = token.Value;
            var markup = raw.StartsWith('<') && raw.Length > 1;
            var text = markup
                ? Regex.IsMatch(raw, @"^</p\s*>$|^<br\s*/?>$",
                    RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(2)) ? "\n" : string.Empty
                : WebUtility.HtmlDecode(raw);
            _parts.Add(new Part(raw, text, visible.Length, markup));
            visible.Append(text);
        }
        Text = visible.ToString();
    }

    public (string Html, int Count) Replace(Regex regex, string replacement)
    {
        var matches = regex.Matches(Text);
        if (matches.Count == 0) return (string.Concat(_parts.Select(part => part.Raw)), 0);
        var output = new StringBuilder();
        var matchIndex = 0;
        var lastInserted = -1;
        foreach (var part in _parts)
        {
            if (part.Text.Length == 0)
            {
                output.Append(part.Raw);
                continue;
            }
            while (matchIndex < matches.Count &&
                matches[matchIndex].Index + matches[matchIndex].Length < part.Start)
                matchIndex++;
            var changed = ReplacePart(part, matches, matchIndex, replacement, ref lastInserted);
            output.Append(changed);
        }
        if (lastInserted < matches.Count - 1)
            output.Append(WebUtility.HtmlEncode(replacement));
        return (output.ToString(), matches.Count);
    }

    private static string ReplacePart(Part part, MatchCollection matches, int firstMatch,
        string replacement, ref int lastInserted)
    {
        var text = new StringBuilder();
        var cursor = 0;
        var changed = false;
        var end = part.Start + part.Text.Length;
        for (var i = firstMatch; i < matches.Count && matches[i].Index <= end; i++)
        {
            var match = matches[i];
            if (match.Index == end && match.Length > 0) break;
            var matchEnd = match.Index + match.Length;
            if (matchEnd <= part.Start && match.Length > 0) continue;
            var start = Math.Clamp(match.Index - part.Start, 0, part.Text.Length);
            var stop = Math.Clamp(matchEnd - part.Start, start, part.Text.Length);
            text.Append(part.Text, cursor, start - cursor);
            if (i > lastInserted)
            {
                text.Append(replacement);
                lastInserted = i;
            }
            cursor = stop;
            changed = true;
        }
        if (!changed) return part.Raw;
        text.Append(part.Text, cursor, part.Text.Length - cursor);
        return part.Markup
            ? WebUtility.HtmlEncode(text.ToString().Replace("\n", string.Empty)) + part.Raw
            : WebUtility.HtmlEncode(text.ToString());
    }
}
