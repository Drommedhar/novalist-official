using System.Text;
using System.Text.RegularExpressions;

namespace Novalist.Core.Services;

/// <summary>One scene the importer proposes.</summary>
public sealed class ImportedScene
{
    public string Title { get; init; } = string.Empty;

    /// <summary>Scene body as editor HTML, ready to write to a scene file.</summary>
    public string Html { get; init; } = string.Empty;

    public int WordCount { get; init; }
}

/// <summary>One chapter the importer proposes.</summary>
public sealed class ImportedChapter
{
    public string Title { get; init; } = string.Empty;
    public IReadOnlyList<ImportedScene> Scenes { get; init; } = [];
}

/// <summary>What an import would create, before anything is written.</summary>
public sealed class ImportPlan
{
    public IReadOnlyList<ImportedChapter> Chapters { get; init; } = [];
    public string Format { get; init; } = string.Empty;

    public int SceneCount => Chapters.Sum(c => c.Scenes.Count);
    public int WordCount => Chapters.Sum(c => c.Scenes.Sum(s => s.WordCount));
    public bool IsEmpty => Chapters.Count == 0;
}

/// <summary>
/// Turns a flat list of paragraphs into chapters and scenes.
///
/// Two signals are used, in order of trust:
///
/// 1. Heading levels the source actually carried. A Word document styled with
///    Heading 1 per chapter is unambiguous and is believed.
/// 2. Failing that, a "Chapter N" style line. This is guesswork, so it is only
///    consulted when the file carried no headings at all - a manuscript that
///    uses headings properly should never have its body text second-guessed.
///
/// Scene breaks come from ornament lines (asterisks, rules) or a heading one
/// level below the chapter heading.
/// </summary>
public static partial class ManuscriptSplitter
{
    /// <summary>
    /// Lines that look like a chapter opening in a manuscript with no styling.
    /// Deliberately narrow: it matches the word for "chapter" in the bundled
    /// interface languages followed by a number or numeral, and a bare numeral
    /// on its own line, and nothing else.
    /// </summary>
    [GeneratedRegex(
        @"^\s*(chapter|kapitel|第\s*[0-9一二三四五六七八九十百]+\s*章)\b[\s.:\-—]*(\d+|[ivxlcdm]+)?\s*(.{0,60})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ChapterHeadingRegex();

    [GeneratedRegex(@"^\s*(\d{1,3}|[ivxlcdm]{1,7})\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex BareNumberRegex();

    /// <summary>Scenes longer than this are split at the next paragraph, so one
    /// unstructured file does not become a single unusable scene.</summary>
    internal const int MaxWordsPerScene = 4000;

    public static ImportPlan Split(ManuscriptDocument document)
    {
        if (document.IsEmpty)
            return new ImportPlan { Format = document.Format };

        var usesHeadings = document.Paragraphs.Any(p => p.HeadingLevel > 0);
        var chapterLevel = usesHeadings
            ? document.Paragraphs.Where(p => p.HeadingLevel > 0).Min(p => p.HeadingLevel)
            : 0;

        var chapters = new List<ImportedChapter>();
        var scenes = new List<ImportedScene>();
        var body = new List<ImportedParagraph>();
        var chapterTitle = string.Empty;
        var sceneTitle = string.Empty;

        void FlushScene()
        {
            if (body.Count == 0)
                return;
            scenes.Add(BuildScene(sceneTitle, body, scenes.Count));
            body.Clear();
            sceneTitle = string.Empty;
        }

        void FlushChapter()
        {
            FlushScene();
            if (scenes.Count == 0 && chapterTitle.Length == 0)
                return;

            chapters.Add(new ImportedChapter
            {
                Title = chapterTitle.Length > 0 ? chapterTitle : $"Chapter {chapters.Count + 1}",
                Scenes = scenes.ToList()
            });
            scenes.Clear();
            chapterTitle = string.Empty;
        }

        foreach (var paragraph in document.Paragraphs)
        {
            if (paragraph.IsSceneBreak)
            {
                FlushScene();
                continue;
            }

            if (usesHeadings && paragraph.HeadingLevel == chapterLevel)
            {
                FlushChapter();
                chapterTitle = paragraph.Text;
                continue;
            }

            if (usesHeadings && paragraph.HeadingLevel > chapterLevel)
            {
                FlushScene();
                sceneTitle = paragraph.Text;
                continue;
            }

            // Only guess from body text when the file gave us nothing better.
            if (!usesHeadings && LooksLikeChapterHeading(paragraph.Text))
            {
                FlushChapter();
                chapterTitle = paragraph.Text.Trim();
                continue;
            }

            body.Add(paragraph);

            // A very long run with no breaks becomes several scenes rather than
            // one the editor struggles to open.
            if (CountWords(body) >= MaxWordsPerScene)
                FlushScene();
        }

        FlushChapter();

        return new ImportPlan { Chapters = chapters, Format = document.Format };
    }

    /// <summary>
    /// Whether an unstyled line reads as a chapter opening. A bare numeral
    /// counts, since plenty of manuscripts number chapters and nothing else.
    /// </summary>
    internal static bool LooksLikeChapterHeading(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 80)
            return false;

        // A line ending in sentence punctuation is prose, whatever it starts with.
        if (trimmed.EndsWith('.') || trimmed.EndsWith(',') || trimmed.EndsWith('!')
            || trimmed.EndsWith('?') || trimmed.EndsWith('"'))
            return false;

        return ChapterHeadingRegex().IsMatch(trimmed) || BareNumberRegex().IsMatch(trimmed);
    }

    private static ImportedScene BuildScene(string title, List<ImportedParagraph> paragraphs, int index)
    {
        return new ImportedScene
        {
            Title = title.Length > 0 ? title : $"Scene {index + 1}",
            Html = ImportedRichText.ToHtml(paragraphs),
            WordCount = CountWords(paragraphs)
        };
    }

    private static int CountWords(List<ImportedParagraph> paragraphs)
    {
        var total = 0;
        foreach (var paragraph in paragraphs)
            total += paragraph.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return total;
    }
}
