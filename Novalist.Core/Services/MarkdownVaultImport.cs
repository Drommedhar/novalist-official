using System.Text.RegularExpressions;

namespace Novalist.Core.Services;

/// <summary>One Markdown file, read as something Novalist could hold.</summary>
public sealed class VaultNote
{
    /// <summary>Path relative to the vault root, with forward slashes.</summary>
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>The note's title: its front matter, its first heading, or its file name.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>The prose, with the front matter taken off.</summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>Tags from the front matter, plus the folders it was filed in.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>
/// Reads a folder of Markdown files.
///
/// Novalist imported one thing: a vault made by the old Obsidian plugin, with
/// its own metadata files. A folder of ordinary notes - which is what a vault
/// is once the plugin is gone, and what every other tool exports - had no way
/// in at all.
///
/// Deliberately does not try to guess entity types. A note about a character
/// and a note about a battle look identical, and an import that files half of
/// them wrongly is worse than one that files all of them as research the writer
/// can move.
/// </summary>
public static partial class MarkdownVaultImport
{
    /// <summary>Folders that are a tool's own state rather than the writer's notes.</summary>
    private static readonly string[] SkipFolders =
        [".obsidian", ".git", ".trash", ".novalist", "node_modules", ".stfolder"];

    [GeneratedRegex(@"^---\r?\n(.*?)\r?\n---\r?\n?", RegexOptions.Singleline)]
    private static partial Regex FrontMatterRegex();

    [GeneratedRegex(@"^#{1,6}\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex FirstHeadingRegex();

    /// <summary>Every note in a folder, in a stable order.</summary>
    public static IReadOnlyList<VaultNote> Scan(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return [];

        var notes = new List<VaultNote>();
        foreach (var file in Directory
                     .EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (IsSkipped(relative)) continue;

            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (IOException)
            {
                // A file being written by something else is skipped rather than
                // failing an import of four hundred others.
                continue;
            }

            notes.Add(Read(relative, text));
        }
        return notes;
    }

    /// <summary>True for a path inside a tool's own state folder.</summary>
    internal static bool IsSkipped(string relativePath)
        => relativePath.Split('/')
            .Any(segment => SkipFolders.Contains(segment, StringComparer.OrdinalIgnoreCase));

    /// <summary>One note, read from its text.</summary>
    internal static VaultNote Read(string relativePath, string? text)
    {
        var content = text ?? string.Empty;
        var tags = new List<string>();
        var frontTitle = string.Empty;

        var matter = FrontMatterRegex().Match(content);
        if (matter.Success)
        {
            content = content[matter.Length..];
            foreach (var line in matter.Groups[1].Value.Split('\n'))
            {
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var key = line[..colon].Trim().ToLowerInvariant();
                var value = line[(colon + 1)..].Trim().Trim('"', '\'');
                if (key == "title") frontTitle = value;
                if (key is "tags" or "tag") tags.AddRange(SplitTags(value));
            }
        }

        // The folders a note was filed in are the only classification the
        // writer actually made, so they survive as tags rather than being lost.
        var folders = relativePath.Split('/');
        tags.AddRange(folders.Take(folders.Length - 1));

        var body = content.Trim();
        var title = frontTitle;
        if (title.Length == 0)
        {
            var heading = FirstHeadingRegex().Match(body);
            title = heading.Success ? heading.Groups[1].Value.Trim() : string.Empty;
        }
        if (title.Length == 0) title = Path.GetFileNameWithoutExtension(relativePath);

        return new VaultNote
        {
            RelativePath = relativePath,
            Title = title,
            Body = body,
            Tags =
            [
                .. tags
                    .Select(t => t.Trim())
                    .Where(t => t.Length > 0)
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
            ]
        };
    }

    /// <summary>Tags as any of the three ways a front matter writes them.</summary>
    private static IEnumerable<string> SplitTags(string value)
        => value
            .Trim('[', ']')
            .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().TrimStart('#', '-'));
}
