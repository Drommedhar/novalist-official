using System.Text.Json;
using System.Text.RegularExpressions;
using Markdig;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public partial class PluginImportService
{
    // ── Chapter / Scene Parsing ─────────────────────────────────────

    private static async Task<List<ParsedChapter>> ParseChapterFilesAsync(string chaptersDir)
    {
        var result = new List<ParsedChapter>();
        if (!Directory.Exists(chaptersDir)) return result;

        var mdFiles = Directory.GetFiles(chaptersDir, "*.md");
        foreach (var file in mdFiles)
        {
            var content = await File.ReadAllTextAsync(file);
            var normalized = content.Replace("\r\n", "\n");
            var chapter = ParseChapterFile(normalized, Path.GetFileNameWithoutExtension(file));
            result.Add(chapter);
        }

        return result.OrderBy(c => c.Order).ToList();
    }

    private static ParsedChapter ParseChapterFile(string content, string fallbackTitle)
    {
        var chapter = new ParsedChapter { Title = fallbackTitle };

        // Extract frontmatter
        var fmMatch = Regex.Match(content, @"^---\n([\s\S]*?)\n---\n?", RegexOptions.Multiline);
        var bodyContent = content;

        if (fmMatch.Success)
        {
            var fm = fmMatch.Groups[1].Value;
            chapter.Guid = ParseFrontmatterField(fm, "guid");
            var orderStr = ParseFrontmatterField(fm, "order");
            if (int.TryParse(orderStr, out var order)) chapter.Order = order;
            chapter.Status = ParseFrontmatterField(fm, "status") ?? "outline";
            chapter.Act = ParseFrontmatterField(fm, "act");
            chapter.Date = ParseFrontmatterField(fm, "date");

            bodyContent = content[fmMatch.Length..];
        }

        // Extract title from H1
        var h1Match = Regex.Match(bodyContent, @"^#\s+(.+)$", RegexOptions.Multiline);
        if (h1Match.Success)
            chapter.Title = h1Match.Groups[1].Value.Trim();

        // Split scenes by H2 headings
        var h2Pattern = new Regex(@"^##\s+(.+)$", RegexOptions.Multiline);
        var h2Matches = h2Pattern.Matches(bodyContent);

        if (h2Matches.Count > 0)
        {
            for (int i = 0; i < h2Matches.Count; i++)
            {
                var sceneTitle = h2Matches[i].Groups[1].Value.Trim();
                var startIdx = h2Matches[i].Index + h2Matches[i].Length;
                var endIdx = i + 1 < h2Matches.Count ? h2Matches[i + 1].Index : bodyContent.Length;
                var sceneContent = bodyContent[startIdx..endIdx].Trim();

                chapter.Scenes.Add(new ParsedScene { Title = sceneTitle, Content = sceneContent });
            }
        }
        else
        {
            // No H2 headings: treat entire body as one scene
            var body = bodyContent;
            // Strip H1 heading if present
            if (h1Match.Success)
                body = bodyContent[(h1Match.Index + h1Match.Length)..].Trim();

            if (!string.IsNullOrWhiteSpace(body))
            {
                chapter.Scenes.Add(new ParsedScene
                {
                    Title = chapter.Title,
                    Content = body
                });
            }
        }

        return chapter;
    }

    private static string? ParseFrontmatterField(string frontmatter, string key)
    {
        var match = Regex.Match(frontmatter, $@"^{Regex.Escape(key)}:\s*(.*)$", RegexOptions.Multiline);
        if (!match.Success) return null;
        var value = match.Groups[1].Value.Trim();
        // Remove quotes if present
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1];
        return string.IsNullOrEmpty(value) ? null : value;
    }

    // ── Entity Sheet Parsing ────────────────────────────────────────

    private static async Task<List<T>> ParseEntityFilesAsync<T>(
        string directory, string sheetHeading, Func<string, string, T> parser) where T : class
    {
        var result = new List<T>();
        if (!Directory.Exists(directory)) return result;

        var mdFiles = Directory.GetFiles(directory, "*.md");
        foreach (var file in mdFiles)
        {
            var content = await File.ReadAllTextAsync(file);
            var entity = parser(content, sheetHeading);
            if (entity != null)
                result.Add(entity);
        }

        return result;
    }

    private static string? GetSheetSection(string content, string heading)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var startIdx = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() == $"## {heading}")
            {
                startIdx = i + 1;
                break;
            }
        }

        if (startIdx == -1) return null;

        var sectionLines = new List<string>();
        for (int i = startIdx; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("## ")) break;
            sectionLines.Add(lines[i]);
        }

        return string.Join('\n', sectionLines);
    }

    private static string ParseSheetField(string content, string fieldName)
    {
        var match = Regex.Match(content, $@"^[ \t]*{Regex.Escape(fieldName)}:[ \t]*(.*?)$", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private static string ParseMultiLineField(string content, string fieldName, string[] nextSections)
    {
        var startMarker = $"\n{fieldName}:\n";
        var idx = content.IndexOf(startMarker, StringComparison.Ordinal);
        if (idx == -1) return string.Empty;

        var startIdx = idx + startMarker.Length;
        var endIdx = content.Length;

        foreach (var next in nextSections)
        {
            var nextIdx = content.IndexOf($"\n{next}", startIdx, StringComparison.Ordinal);
            if (nextIdx != -1 && nextIdx < endIdx)
                endIdx = nextIdx;
        }

        return content[startIdx..endIdx].Trim();
    }

    private static List<(string key, string value)> ParseListSection(string content, string sectionName, string[] nextSections)
    {
        var result = new List<(string, string)>();
        var text = ParseMultiLineField(content, sectionName, nextSections);
        if (string.IsNullOrEmpty(text)) return result;

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            var match = Regex.Match(trimmed, @"^[-*]\s*(.+?)\s*:\s*(.+)$");
            if (match.Success)
                result.Add((match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim()));
        }

        return result;
    }

    private static List<EntitySection> ParseSections(string content, string[] nextSections)
    {
        var result = new List<EntitySection>();
        var text = ParseMultiLineField(content, "Sections", nextSections);
        if (string.IsNullOrEmpty(text)) return result;

        var blocks = Regex.Split(text, @"^\s*---\s*$", RegexOptions.Multiline);
        foreach (var block in blocks)
        {
            var trimmed = block.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            var lines = trimmed.Split('\n');
            var title = lines[0].Trim();
            var sectionContent = string.Join('\n', lines.Skip(1)).Trim();
            if (!string.IsNullOrEmpty(title))
                result.Add(new EntitySection { Title = title, Content = sectionContent });
        }

        return result;
    }

    private static List<EntityImage> ParseImages(string content, string[] nextSections)
    {
        var items = ParseListSection(content, "Images", nextSections);
        return items.Select(i => new EntityImage
        {
            Name = i.key,
            Path = StripWikilink(i.value)
        }).ToList();
    }

    private static Dictionary<string, string> ParseCustomProperties(string content, string[] nextSections)
    {
        var items = ParseListSection(content, "CustomProperties", nextSections);
        var dict = new Dictionary<string, string>();
        foreach (var (key, value) in items)
            dict[key] = value;
        return dict;
    }

    // ── Internal Types ──────────────────────────────────────────────

    private sealed class ParsedChapter
    {
        public string? Guid { get; set; }
        public string Title { get; set; } = string.Empty;
        public int Order { get; set; }
        public string? Status { get; set; }
        public string? Act { get; set; }
        public string? Date { get; set; }
        public List<ParsedScene> Scenes { get; } = [];
    }

    private sealed class ParsedScene
    {
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }
}
