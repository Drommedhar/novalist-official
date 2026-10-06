using System.Text;
using Novalist.Core.Models;
using Novalist.Core.Utilities;
using static Novalist.Core.Services.ExportProse;

namespace Novalist.Core.Services;

public partial class ExportService
{
    /// <summary>
    /// What an export would contain, without writing a file. Runs the same
    /// compile the export runs, so the exclusions and the stage filter are
    /// counted rather than guessed at, and a writer stops finding out what
    /// came through by opening the result somewhere else.
    /// </summary>
    public async Task<ExportPreview> PreviewAsync(ExportOptions options)
    {
        var chapters = await CompileChaptersAsync(options);
        var preset = options.ResolvePreset();

        var words = 0;
        var characters = 0;
        var scenes = 0;
        var undescribed = 0;
        foreach (var scene in chapters.SelectMany(c => c.Scenes))
        {
            scenes++;
            var plain = StripHtml(scene.HtmlContent);
            characters += plain.Length;
            words += CountWordsIn(plain);
            undescribed += ParseHtmlToBlocks(scene.HtmlContent)
                .Count(b => b.ImagePath != null && b.ImageAlt.Length == 0);
        }

        // On the Normseite grid the page count is not an estimate: the layout
        // fixes the columns and the lines, so the grid answers exactly.
        if (preset.NormseitenGrid)
        {
            var metrics = NormseitenRenderer.MeasureBlocks(
                BuildManuscriptBlocks(chapters, options), preset.GridColumns, preset.GridLines);
            return new ExportPreview
            {
                Chapters = chapters.Count,
                Scenes = scenes,
                Words = words,
                Characters = characters,
                Pages = metrics.Pages,
                UndescribedImages = undescribed,
                PagesAreExact = true
            };
        }

        return new ExportPreview
        {
            Chapters = chapters.Count,
            Scenes = scenes,
            Words = words,
            Characters = characters,
            Pages = EstimatePages(characters, chapters.Count, preset),
            UndescribedImages = undescribed,
            PagesAreExact = false
        };
    }

    /// <summary>
    /// Pages this layout would take, from its own geometry: the text block that
    /// fits inside the margins, how many characters of this size fit on a line,
    /// and how many lines fit down the page. An estimate, and reported as one -
    /// the real count depends on the renderer's hyphenation and widow control.
    /// </summary>
    private static int EstimatePages(int characters, int chapterCount, ExportPreset preset)
    {
        if (characters <= 0) return 0;

        // A6 through A4 in inches, less both margins; 8.5x11 is the assumption
        // the rest of the pipeline already makes for a page.
        var textWidthInches = Math.Max(1.0, 8.5 - preset.MarginInches * 2);
        var textHeightInches = Math.Max(1.0, 11.0 - preset.MarginInches * 2);

        // 0.5em per character is the usual average for a serif face at text
        // sizes - narrower than the em, wider than the digits.
        var charWidthInches = preset.BodyFontSizePt * 0.5 / 72.0;
        var lineHeightInches = preset.BodyFontSizePt
            * (preset.DoubleSpaced ? 2.0 : preset.LineSpacingMultiplier) / 72.0;

        var charsPerLine = Math.Max(1, (int)(textWidthInches / charWidthInches));
        var linesPerPage = Math.Max(1, (int)(textHeightInches / lineHeightInches));
        var charsPerPage = charsPerLine * linesPerPage;

        // Each chapter starts a page and drops down before its first line, so a
        // book of many short chapters is longer than its character count says.
        var pages = (characters + charsPerPage - 1) / charsPerPage;
        return Math.Max(chapterCount, pages + chapterCount / 2);
    }

    private static int CountWordsIn(string text)
        => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static List<NormseitenBlock> BuildManuscriptBlocks(
        List<ChapterExportContent> chapters,
        ExportOptions options)
    {
        var blocks = new List<NormseitenBlock>();

        if (options.IncludeTitlePage && !string.IsNullOrWhiteSpace(options.Title))
        {
            blocks.Add(NormseitenBlock.Title(options.Title));
            if (!string.IsNullOrWhiteSpace(options.Author))
                blocks.Add(NormseitenBlock.Body(options.Author));
            blocks.Add(NormseitenBlock.Blank());
        }

        foreach (var chapter in chapters)
        {
            blocks.Add(NormseitenBlock.Heading(chapter.Heading));
            for (var si = 0; si < chapter.Scenes.Count; si++)
            {
                if (si > 0)
                {
                    blocks.Add(NormseitenBlock.Blank());
                    blocks.Add(NormseitenBlock.Body(SceneBreakText));
                    blocks.Add(NormseitenBlock.Blank());
                }
                blocks.AddRange(HtmlToNormseitenBlocks(chapter.Scenes[si].HtmlContent));
            }
        }

        return blocks;
    }
}
