using System.Text.RegularExpressions;

namespace Novalist.Desktop.Utilities;

/// <summary>
/// Defensive backstop for the diagnostic file log. The primary content-safety
/// guarantee is call-site discipline (allowlist: only structured, non-content
/// data is ever passed to <see cref="Log"/>). This redactor is the second line
/// of defence: it strips anything that could leak story content — chiefly
/// filesystem paths, which embed project / book / author / scene names.
///
/// Hard rule (see CLAUDE.md): the log must never contain story or user content.
/// </summary>
public static partial class LogRedactor
{
    // Single contiguous blob longer than this is treated as suspicious free
    // text / serialized content and dropped. Set high so normal stack-trace
    // frames and namespaces (which are safe and useful) are preserved.
    private const int MaxTokenLength = 120;

    // Windows drive paths (C:\foo\bar), UNC paths (\\server\share\...), and
    // POSIX paths with at least two segments (/home/user/foo). We collapse the
    // whole path to its extension only — the directory chain and filename can
    // contain the author's project, book, or scene titles.
    //
    // Path segments may contain spaces (e.g. "C:\Users\jane\My Novel\scene.json"),
    // so the character classes stop only at hard delimiters (quote, angle
    // bracket, pipe, newline) and end-of-line — NOT at spaces. This deliberately
    // over-matches trailing prose on the same line rather than risk leaking the
    // tail of a space-containing path (book / scene titles). Content-safety wins.
    [GeneratedRegex(@"(?:[A-Za-z]:\\|\\\\)[^\r\n""'<>|]+", RegexOptions.Compiled)]
    private static partial Regex WindowsPathRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9])/(?:[^/\r\n""'<>|]+/)+[^\r\n""'<>|]*", RegexOptions.Compiled)]
    private static partial Regex PosixPathRegex();

    [GeneratedRegex(@"file://[^\r\n""'<>|]+", RegexOptions.Compiled)]
    private static partial Regex FileUrlRegex();

    /// <summary>
    /// Scrubs a single log line of path-shaped content and over-long tokens.
    /// Never throws; on any failure returns a fully-redacted placeholder rather
    /// than risk leaking the original.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] // thin defensive wrapper; ScrubCore holds the tested logic
    public static string Scrub(string line)
    {
        if (string.IsNullOrEmpty(line))
            return line;

        try
        {
            return ScrubCore(line);
        }
        catch
        {
            // Must never leak the original on failure.
            return "<redacted-line>";
        }
    }

    internal static string ScrubCore(string line)
    {
        line = FileUrlRegex().Replace(line, m => "<path>" + ExtensionOf(m.Value));
        line = WindowsPathRegex().Replace(line, m => "<path>" + ExtensionOf(m.Value));
        line = PosixPathRegex().Replace(line, m => "<path>" + ExtensionOf(m.Value));

        return RedactLongTokens(line);
    }

    private static string ExtensionOf(string path)
    {
        // Keep only the file extension (e.g. ".scene.json" -> ".json"). Bare
        // filenames are intentionally dropped because they can be titles.
        var slash = path.LastIndexOfAny(['/', '\\']);
        var name = slash >= 0 ? path[(slash + 1)..] : path;
        var dot = name.LastIndexOf('.');
        if (dot <= 0) return string.Empty;
        var ext = name[dot..];
        // The match may include over-matched trailing prose after the filename
        // ("scene.json now") — keep only the extension token itself.
        var space = ext.IndexOf(' ');
        return space >= 0 ? ext[..space] : ext;
    }

    private static string RedactLongTokens(string line)
    {
        var tokens = line.Split(' ');
        var changed = false;
        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Length > MaxTokenLength)
            {
                tokens[i] = $"<redacted:{tokens[i].Length}>";
                changed = true;
            }
        }
        return changed ? string.Join(' ', tokens) : line;
    }
}
