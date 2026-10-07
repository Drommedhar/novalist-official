using System.Text.RegularExpressions;

namespace Novalist.Core.Utilities;

internal sealed record HtmlMergeBlock(string Html)
{
    public string Text => new HtmlSearchText(Html).Text.Trim().Replace('\n', ' ').Replace('\r', ' ');
    public string Key => Text.Length == 0 ? "\uFFFC" : Text;
}

internal static class HtmlMergeBlocks
{
    private static readonly HashSet<string> VoidTags = new(StringComparer.OrdinalIgnoreCase)
        { "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr" };

    public static List<HtmlMergeBlock> Split(string html)
    {
        if (html.Length == 0) return [];
        var blocks = new List<HtmlMergeBlock>();
        var stack = new Stack<string>();
        var start = 0;
        foreach (Match tag in Regex.Matches(html, """<!--[^>]*-->|<(?:"[^"]*"|'[^']*'|[^'">])*>""",
            RegexOptions.NonBacktracking, TimeSpan.FromSeconds(2)))
        {
            var name = Regex.Match(tag.Value, @"^<(/?)([\w:-]+)", RegexOptions.NonBacktracking);
            if (!name.Success) continue;
            var tagName = name.Groups[2].Value;
            if (name.Groups[1].Length != 0)
            {
                if (!stack.TryPop(out var opened) || !tagName.Equals(opened, StringComparison.OrdinalIgnoreCase))
                    return [new HtmlMergeBlock(html)];
            }
            else if (!VoidTags.Contains(tagName) && !tag.Value.EndsWith("/>", StringComparison.Ordinal))
                stack.Push(tagName);
            if (stack.Count != 0) continue;
            var end = tag.Index + tag.Length;
            blocks.Add(new HtmlMergeBlock(html[start..end]));
            start = end;
        }
        if (stack.Count != 0) return [new HtmlMergeBlock(html)];
        if (start < html.Length)
        {
            if (blocks.Count > 0 && string.IsNullOrWhiteSpace(html[start..]))
                blocks[^1] = new HtmlMergeBlock(blocks[^1].Html + html[start..]);
            else blocks.Add(new HtmlMergeBlock(html[start..]));
        }
        return blocks;
    }
}
