using System.Text.RegularExpressions;

namespace Novalist.Core.Utilities;

public enum DiffOp
{
    Equal,
    Added,
    Removed
}

public sealed class DiffLine
{
    public DiffOp Op { get; init; }
    /// <summary>Line number on the left (snapshot) side, or null when inserted on the right.</summary>
    public int? LeftIndex { get; init; }
    /// <summary>Line number on the right (current) side, or null when removed.</summary>
    public int? RightIndex { get; init; }
    public string Text { get; init; } = string.Empty;
}

/// <summary>One paired diff row showing snapshot text on the left and current on the right.</summary>
public sealed class PairedDiffRow
{
    public string? LeftText { get; init; }
    public string? RightText { get; init; }
    public int? LeftIndex { get; init; }
    public int? RightIndex { get; init; }
    /// <summary>True when both sides have text but differ — eligible for word-level diff.</summary>
    public bool IsChanged { get; init; }
    /// <summary>True when only one side has text.</summary>
    public bool IsLeftOnly { get; init; }
    public bool IsRightOnly { get; init; }
    /// <summary>True for unchanged context lines.</summary>
    public bool IsEqual { get; init; }
}

public enum WordDiffOp { Equal, Removed, Added }

public sealed class WordDiffSpan
{
    public WordDiffOp Op { get; init; }
    public string Text { get; init; } = string.Empty;
}

/// <summary>
/// Line-level Longest Common Subsequence diff. Suitable for short scenes; not
/// optimised for novels with thousands of lines.
/// </summary>
public static class TextDiff
{
    public static List<DiffLine> Compute(string left, string right)
    {
        var leftLines = SplitLines(left);
        var rightLines = SplitLines(right);

        var lcs = BuildLcsTable(leftLines, rightLines);
        var stack = new List<DiffLine>();
        int i = leftLines.Length;
        int j = rightLines.Length;

        while (i > 0 || j > 0)
        {
            if (i > 0 && j > 0 && leftLines[i - 1] == rightLines[j - 1])
            {
                stack.Add(new DiffLine { Op = DiffOp.Equal, LeftIndex = i - 1, RightIndex = j - 1, Text = leftLines[i - 1] });
                i--; j--;
            }
            else if (j > 0 && (i == 0 || lcs[i, j - 1] >= lcs[i - 1, j]))
            {
                stack.Add(new DiffLine { Op = DiffOp.Added, RightIndex = j - 1, Text = rightLines[j - 1] });
                j--;
            }
            else
            {
                stack.Add(new DiffLine { Op = DiffOp.Removed, LeftIndex = i - 1, Text = leftLines[i - 1] });
                i--;
            }
        }

        stack.Reverse();
        return stack;
    }

    /// <summary>
    /// Pairs up adjacent Removed+Added line groups so each output row places the
    /// snapshot line on the left and the current line on the right (or blank on
    /// one side for pure inserts/deletes). This makes a side-by-side diff.
    /// </summary>
    public static List<PairedDiffRow> ComputePaired(string left, string right)
    {
        var rawDiff = Compute(left, right);
        var rows = new List<PairedDiffRow>();
        int idx = 0;
        while (idx < rawDiff.Count)
        {
            var line = rawDiff[idx];

            if (line.Op == DiffOp.Equal)
            {
                rows.Add(new PairedDiffRow
                {
                    LeftText = line.Text,
                    RightText = line.Text,
                    LeftIndex = line.LeftIndex,
                    RightIndex = line.RightIndex,
                    IsEqual = true
                });
                idx++;
                continue;
            }

            // Collect a contiguous block of removed+added.
            var removed = new List<DiffLine>();
            var added = new List<DiffLine>();
            while (idx < rawDiff.Count && rawDiff[idx].Op != DiffOp.Equal)
            {
                if (rawDiff[idx].Op == DiffOp.Removed) removed.Add(rawDiff[idx]);
                else added.Add(rawDiff[idx]);
                idx++;
            }

            int max = Math.Max(removed.Count, added.Count);
            for (int k = 0; k < max; k++)
            {
                var l = k < removed.Count ? removed[k] : null;
                var r = k < added.Count ? added[k] : null;

                if (l != null && r != null)
                {
                    rows.Add(new PairedDiffRow
                    {
                        LeftText = l.Text,
                        RightText = r.Text,
                        LeftIndex = l.LeftIndex,
                        RightIndex = r.RightIndex,
                        IsChanged = true
                    });
                }
                else if (l != null)
                {
                    rows.Add(new PairedDiffRow
                    {
                        LeftText = l.Text,
                        LeftIndex = l.LeftIndex,
                        IsLeftOnly = true
                    });
                }
                else if (r != null)
                {
                    rows.Add(new PairedDiffRow
                    {
                        RightText = r.Text,
                        RightIndex = r.RightIndex,
                        IsRightOnly = true
                    });
                }
            }
        }
        return rows;
    }

    /// <summary>
    /// Word-level diff between two strings. Words are split on whitespace; the
    /// surrounding whitespace is preserved and emitted as Equal spans.
    /// </summary>
    public static List<WordDiffSpan> WordDiff(string left, string right)
    {
        var leftTokens = Tokenize(left);
        var rightTokens = Tokenize(right);

        var lcs = new int[leftTokens.Count + 1, rightTokens.Count + 1];
        for (int i = 1; i <= leftTokens.Count; i++)
        {
            for (int j = 1; j <= rightTokens.Count; j++)
            {
                lcs[i, j] = leftTokens[i - 1] == rightTokens[j - 1]
                    ? lcs[i - 1, j - 1] + 1
                    : Math.Max(lcs[i - 1, j], lcs[i, j - 1]);
            }
        }

        var spans = new List<WordDiffSpan>();
        int a = leftTokens.Count, b = rightTokens.Count;
        var rev = new List<WordDiffSpan>();
        while (a > 0 || b > 0)
        {
            if (a > 0 && b > 0 && leftTokens[a - 1] == rightTokens[b - 1])
            {
                rev.Add(new WordDiffSpan { Op = WordDiffOp.Equal, Text = leftTokens[a - 1] });
                a--; b--;
            }
            else if (b > 0 && (a == 0 || lcs[a, b - 1] >= lcs[a - 1, b]))
            {
                rev.Add(new WordDiffSpan { Op = WordDiffOp.Added, Text = rightTokens[b - 1] });
                b--;
            }
            else
            {
                rev.Add(new WordDiffSpan { Op = WordDiffOp.Removed, Text = leftTokens[a - 1] });
                a--;
            }
        }
        rev.Reverse();
        // Merge adjacent spans of same op for fewer Run elements.
        foreach (var s in rev)
        {
            if (spans.Count > 0 && spans[^1].Op == s.Op)
            {
                spans[^1] = new WordDiffSpan { Op = s.Op, Text = spans[^1].Text + s.Text };
            }
            else
            {
                spans.Add(s);
            }
        }
        return spans;
    }

    private static List<string> Tokenize(string s)
    {
        // Tokens are runs of non-whitespace OR runs of whitespace. Both are
        // emitted so the original spacing is preserved when rejoining.
        var tokens = new List<string>();
        if (string.IsNullOrEmpty(s)) return tokens;
        int i = 0;
        while (i < s.Length)
        {
            int start = i;
            bool isWs = char.IsWhiteSpace(s[i]);
            while (i < s.Length && char.IsWhiteSpace(s[i]) == isWs) i++;
            tokens.Add(s.Substring(start, i - start));
        }
        return tokens;
    }

    private static int[,] BuildLcsTable(string[] left, string[] right)
    {
        var lcs = new int[left.Length + 1, right.Length + 1];
        for (int i = 1; i <= left.Length; i++)
        {
            for (int j = 1; j <= right.Length; j++)
            {
                lcs[i, j] = left[i - 1] == right[j - 1]
                    ? lcs[i - 1, j - 1] + 1
                    : Math.Max(lcs[i - 1, j], lcs[i, j - 1]);
            }
        }
        return lcs;
    }

    private static string[] SplitLines(string s)
    {
        if (string.IsNullOrEmpty(s))
            return [];
        return s.Replace("\r\n", "\n").Split('\n');
    }

    /// <summary>
    /// Strips HTML tags so diffs over scene HTML look like plain prose.
    /// </summary>
    public static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;
        var withBreaks = Regex.Replace(html, "</p>|<br ?/?>", "\n", RegexOptions.IgnoreCase);
        var stripped = Regex.Replace(withBreaks, "<[^>]+>", string.Empty);
        return System.Net.WebUtility.HtmlDecode(stripped);
    }
}
