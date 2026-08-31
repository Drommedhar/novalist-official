using System.Text.RegularExpressions;

namespace Novalist.Backend.Extensions;

/// <summary>Defensive backstop that removes path-shaped and suspiciously long
/// content from diagnostic lines before they reach disk.</summary>
internal static partial class LogRedactor
{
    private const int MaxTokenLength = 120;

    [GeneratedRegex(@"(?:[A-Za-z]:\\|\\\\)[^\r\n""'<>|]+", RegexOptions.Compiled)]
    private static partial Regex WindowsPathRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9])/(?:[^/\r\n""'<>|]+/)+[^\r\n""'<>|]*", RegexOptions.Compiled)]
    private static partial Regex PosixPathRegex();

    [GeneratedRegex(@"file://[^\r\n""'<>|]+", RegexOptions.Compiled)]
    private static partial Regex FileUrlRegex();

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    internal static string Scrub(string line)
    {
        if (string.IsNullOrEmpty(line)) return line;
        try { return ScrubCore(line); }
        catch { return "<redacted-line>"; }
    }

    internal static string ScrubCore(string line)
    {
        line = FileUrlRegex().Replace(line, match => "<path>" + ExtensionOf(match.Value));
        line = WindowsPathRegex().Replace(line, match => "<path>" + ExtensionOf(match.Value));
        line = PosixPathRegex().Replace(line, match => "<path>" + ExtensionOf(match.Value));
        return RedactLongTokens(line);
    }

    private static string ExtensionOf(string path)
    {
        var slash = path.LastIndexOfAny(['/', '\\']);
        var name = slash >= 0 ? path[(slash + 1)..] : path;
        var dot = name.LastIndexOf('.');
        if (dot <= 0) return string.Empty;
        var extension = name[dot..];
        var space = extension.IndexOf(' ');
        return space >= 0 ? extension[..space] : extension;
    }

    private static string RedactLongTokens(string line)
    {
        var tokens = line.Split(' ');
        var changed = false;
        for (var index = 0; index < tokens.Length; index++)
        {
            if (tokens[index].Length <= MaxTokenLength) continue;
            tokens[index] = $"<redacted:{tokens[index].Length}>";
            changed = true;
        }
        return changed ? string.Join(' ', tokens) : line;
    }
}
