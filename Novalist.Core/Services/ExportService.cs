using System.Text;
using Novalist.Core.Models;
using Novalist.Core.Utilities;
using static Novalist.Core.Services.ExportProse;

namespace Novalist.Core.Services;

/// <summary>
/// Compiles selected project content and dispatches it to the format writers.
/// </summary>
public partial class ExportService
{
    private const string SceneBreakText = "* * *";

    private readonly IProjectService _projectService;
    private readonly IEntityService? _entityService;

    public ExportService(IProjectService projectService, IEntityService? entityService = null)
    {
        _projectService = projectService;
        _entityService = entityService;
    }

    /// <summary>
    /// Which book a compile pass is reading, and how to reach its scenes.
    ///
    /// Everything below used to go straight to the active book. A box set has
    /// to read a volume nobody has open, and switching the active book mid-run
    /// would mutate state the watcher and the UI are both reading.
    /// </summary>
    /// <param name="SelectAll">
    /// True for a book the writer did not tick chapters for. The chapter list
    /// in the Export view belongs to the open book, so filtering a further
    /// volume against it would drop every chapter it has.
    /// </param>
    private sealed record VolumeSource(
        BookData? Book,
        List<ChapterData> Chapters,
        Func<string, List<SceneData>> ScenesFor,
        Func<ChapterData, SceneData, Task<string>> ReadScene,
        bool SelectAll = false);

    /// <summary>
    /// The by-line under a volume's heading, or null when the volume is by
    /// whoever wrote the rest of the project - repeating the project's author
    /// over every volume of a series says nothing and reads as a mistake.
    /// </summary>
    private static string? AuthorLine(BookData book, ExportOptions options)
        => !string.IsNullOrWhiteSpace(book.Author)
            && !string.Equals(book.Author.Trim(), (options.Author ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase)
            ? book.Author.Trim()
            : null;

    /// <summary>The open book, which is what every export read before volumes existed.</summary>
    private VolumeSource ActiveSource() => new(
        _projectService.ActiveBook,
        _projectService.GetChaptersOrdered(),
        _projectService.GetScenesForChapter,
        _projectService.ReadSceneContentAsync);

    public async Task<List<ChapterExportContent>> CompileChaptersAsync(ExportOptions options)
    {
        var chapters = await CompileChaptersAsync(options, ActiveSource());

        // A box set: the open book first, then each further volume the writer
        // asked for, each announced by a heading of its own so the contents
        // nests rather than running eighty chapters together.
        var volumes = await OtherVolumesAsync(options);
        if (volumes.Count == 0) return chapters;

        foreach (var volume in volumes)
        {
            chapters.Add(new ChapterExportContent
            {
                Title = volume.Book!.Name,
                // Its own heading and no scenes: a divider announcing the
                // volume, never numbered, because it is not a chapter of one.
                Heading = volume.Book.Name,
                IsVolume = true,
                // An anthology's volumes are by different people. Without this
                // a collection of six writers goes out under one name.
                Subtitle = AuthorLine(volume.Book, options),
                Scenes = []
            });
            chapters.AddRange(await CompileChaptersAsync(options, volume));
        }

        // Each book numbers its chapters from its own start, so appending one
        // to another leaves two chapters claiming the same position - and any
        // writer that sorts by it would interleave the volumes. Renumbered once
        // across the whole set, and only when there is more than one book in
        // it, so a single-book export is untouched.
        for (var i = 0; i < chapters.Count; i++) chapters[i].Order = i;

        return chapters;
    }

    /// <summary>
    /// The further books this export was asked for, in the project's own order,
    /// never including the one already compiled.
    /// </summary>
    private async Task<List<VolumeSource>> OtherVolumesAsync(ExportOptions options)
    {
        var wanted = options.IncludedBookIds;
        if (wanted == null || wanted.Count == 0) return [];

        var sources = new List<VolumeSource>();
        foreach (var book in _projectService.CurrentProject?.Books ?? [])
        {
            if (book.Id == _projectService.ActiveBook?.Id) continue;
            if (!wanted.Contains(book.Id)) continue;

            var manifest = await _projectService.LoadScenesManifestForAsync(book);
            if (manifest == null) continue;

            sources.Add(new VolumeSource(
                book,
                [.. book.Chapters.OrderBy(c => c.Order)],
                guid => manifest.Chapters.TryGetValue(guid, out var scenes)
                    ? [.. scenes.Where(s => s.ArchivedAt == null).OrderBy(s => s.Order)]
                    : [],
                (chapter, scene) => _projectService.ReadSceneContentForAsync(book, chapter, scene),
                SelectAll: true));
        }
        return sources;
    }

    private async Task<List<ChapterExportContent>> CompileChaptersAsync(
        ExportOptions options, VolumeSource source)
    {
        var chapters = source.Chapters
            .Where(c => source.SelectAll || options.SelectedChapterGuids.Contains(c.Guid))
            .OrderBy(c => c.Order)
            .ToList();

        // Publishing metadata belongs to the book, so the caller never has to
        // assemble it - every export path gets it by opening the project.
        options.Publishing = _projectService.ActiveBook?.Publishing ?? new Models.PublishingMetadata();
        options.CustomPresets = [.. _projectService.ActiveBook?.ExportPresets ?? []];

        // Matter pages come from the book, not the chapter selection: they frame
        // the whole book rather than belonging to any chapter.
        options.Replacements = [.. _projectService.ActiveBook?.ExportReplacements ?? []];

        // A title page that says "Book two of the Salt Road" had to be typed
        // out and remembered; a token resolves it from the book every time.
        var store = options.ResolveRetailer();
        var tokens = new TokenContext
        {
            Title = options.Title,
            Author = options.Author,
            Isbn = options.Publishing.NormalizedIsbn() ?? string.Empty,
            Publisher = options.Publishing.Publisher,
            Series = options.Publishing.SeriesName,
            SeriesIndex = options.Publishing.SeriesPosition,
            // The store this build is for, so back matter can point a reader at
            // the shop they bought it in rather than at a competitor.
            StoreName = store?.Name ?? string.Empty,
            StoreLink = store?.Url ?? string.Empty,
            // Both were in the token table and in the manual and neither was
            // ever set, so a title page saying "<$wordcount> words" printed a
            // zero at whoever it was sent to.
            WordCount = chapters
                // The pass's own book, so a volume counts its own words rather
                // than the open book's.
                .SelectMany(c => source.ScenesFor(c.Guid))
                .Where(sc => !sc.Inactive && !sc.ExcludeFromExport)
                .Sum(sc => sc.WordCount),
            PageCount = 0
        };
        // Normseiten when the layout is on that grid, and the usual 250 words
        // to a page otherwise. An estimate either way, and saying so.
        tokens = tokens with
        {
            PageCount = (int)Math.Ceiling(tokens.WordCount / 250.0)
        };

        options.Matter = (_projectService.ActiveBook?.Matter ?? [])
            .Where(m => m.Included && !string.IsNullOrWhiteSpace(m.Content))
            .OrderBy(m => m.Placement)
            .ThenBy(m => m.Order)
            .Select(m => new MatterExportContent
            {
                Id = m.Id,
                Kind = m.Kind.ToString(),
                Placement = m.Placement.ToString(),
                Title = ExportTokens.Resolve(ResolveMatterTitle(m), tokens),
                HtmlContent = Models.ExportReplacements.Apply(
                    ExportTokens.Resolve(m.Content, tokens), options.Replacements),
                Order = m.Order,
                InTableOfContents = m.InTableOfContents
            })
            .ToList();

        var result = new List<ChapterExportContent>();
        var preset = options.ResolvePreset();
        // Counts only the sections that carry a number.
        var numbered = 0;

        foreach (var chapter in chapters)
        {
            // A prologue is not Chapter One, and the chapter after it is. The
            // count walks past the sections standing outside it rather than
            // being the position in the list. Resolved before the scenes are
            // compiled, so a placeholder in the prose reads the same number
            // the heading does.
            var sectionType = SectionTypes.Resolve(
                chapter.SectionTypeKey, source.Book?.SectionTypes);
            if (sectionType.Numbered) numbered++;

            var scenes = source.ScenesFor(chapter.Guid)
                // Three ways a scene stays out of the book: it is not in the
                // book at all, the writer held it back from exports, or it is
                // not at a stage this export asked for.
                .Where(s => !s.Inactive)
                .Where(s => !s.ExcludeFromExport)
                .Where(s => options.IncludedStages == null
                    || options.IncludedStages.Count == 0
                    || options.IncludedStages.Contains(s.Stage ?? string.Empty))
                .ToList();
            var sceneContents = new List<SceneExportContent>();

            foreach (var scene in scenes)
            {
                // Suggested edits resolve here, once, so no writer downstream
                // has to know they exist. An export is a finished book: an
                // insertion nobody rejected is in it, a deletion nobody
                // accepted is not, and the markup itself never reaches a page.
                // Suggested edits resolve, then the book's compile-time rules
                // run - on the way out only. Replace All writes to the source
                // scenes; these never do, which is what makes "the submission
                // copy spells it out and the ebook uses the glyph" possible
                // without keeping two drafts.
                var html = Models.ExportReplacements.Apply(
                    TrackedChanges.Final(ResolveImagePaths(
                        await source.ReadScene(chapter, scene))),
                    options.Replacements);
                // The chapter and scene the writer is in, so <$chaptertitle>
                // in the prose means this chapter rather than nothing. These
                // were documented, in the token table, and populated nowhere.
                var sceneTokens = tokens with
                {
                    ChapterNumber = numbered,
                    ChapterTitle = chapter.Title,
                    SceneTitle = scene.Title,
                    Act = chapter.Act ?? string.Empty
                };
                html = ExportTokens.Resolve(html, sceneTokens);
                sceneContents.Add(new SceneExportContent
                {
                    Title = ExportTokens.Resolve(scene.Title, sceneTokens),
                    Order = scene.Order,
                    HtmlContent = html,
                    Id = scene.Id,
                    // Ids are lowercased because the inline parser lowercases
                    // the tag it finds them in.
                    Footnotes = (scene.Footnotes ?? [])
                        .Where(n => !string.IsNullOrWhiteSpace(n.Text))
                        .GroupBy(n => n.Id.ToLowerInvariant())
                        .ToDictionary(g => g.Key, g => g.First().Text),
                    Comments = (scene.Comments ?? [])
                        .Where(c => !c.Resolved && !string.IsNullOrWhiteSpace(c.Text))
                        .Select(c => new SceneExportComment
                        {
                            Id = c.Id,
                            AnchorText = c.AnchorText ?? string.Empty,
                            Text = c.Text,
                            CreatedAt = c.CreatedAt
                        })
                        .ToList()
                });
            }

            var chapterTokens = tokens with
            {
                ChapterNumber = numbered,
                ChapterTitle = chapter.Title,
                Act = chapter.Act ?? string.Empty
            };
            var title = ExportTokens.Resolve(chapter.Title, chapterTokens);
            result.Add(new ChapterExportContent
            {
                Title = title,
                Order = chapter.Order,
                Guid = chapter.Guid,
                Subtitle = ExportTokens.Resolve(chapter.Subtitle ?? string.Empty, chapterTokens),
                // Built here so every writer prints the same heading and a
                // placeholder in a heading format resolves in all of them.
                Heading = ExportTokens.Resolve(
                    preset.ChapterHeading(numbered, title, sectionType), chapterTokens),
                HideHeading = chapter.HideHeading,
                Scenes = sceneContents
            });
        }

        // The layout's own lines, resolved once against the finished book.
        options.ResolvedSeparator = ExportTokens.Resolve(preset.SceneSeparator, tokens);
        options.ResolvedRunningHead = ExportTokens.Resolve(preset.RunningHead, tokens);

        return result;
    }

    /// <summary>
    /// Heading a matter page should print. An explicit title always wins. With
    /// none, kinds that conventionally carry a heading get their kind name and
    /// the rest get none - a dedication with the word "Dedication" over it is
    /// not how books are set.
    /// </summary>
    internal static string ResolveMatterTitle(BookMatterElement element)
    {
        if (!string.IsNullOrWhiteSpace(element.Title))
            return element.Title.Trim();

        return BookMatterElement.ShowsHeadingByDefault(element.Kind)
            ? SpaceCamelCase(element.Kind.ToString())
            : string.Empty;
    }

    /// <summary>"AboutTheAuthor" to "About The Author", for a default heading.</summary>
    internal static string SpaceCamelCase(string value)
    {
        var builder = new StringBuilder(value.Length + 4);
        for (var i = 0; i < value.Length; i++)
        {
            if (i > 0 && char.IsUpper(value[i]))
                builder.Append(' ');
            builder.Append(value[i]);
        }
        return builder.ToString();
    }

    /// <summary>
    /// Export the project to the specified format and write to a file.
    /// </summary>
    public async Task ExportAsync(ExportOptions options, string outputPath)
    {
        var chapters = await CompileChaptersAsync(options);

        switch (options.Format)
        {
            case ExportFormat.Epub:
                await ExportToEpubAsync(chapters, options, outputPath);
                break;
            case ExportFormat.Docx:
                await ExportToDocxAsync(chapters, options, outputPath);
                break;
            case ExportFormat.Pdf:
                ExportToPdf(chapters, options, outputPath);
                break;
            case ExportFormat.Markdown:
                await ExportToMarkdownAsync(chapters, options, outputPath);
                break;
            case ExportFormat.FinalDraft:
                await ExportToFinalDraftAsync(chapters, options, outputPath);
                break;
            case ExportFormat.LaTeX:
                await ExportToLatexAsync(chapters, options, outputPath);
                break;
            case ExportFormat.Codex:
                await ExportCodexAsync(options, outputPath);
                break;
            case ExportFormat.CodexPdf:
                await ExportCodexPdfAsync(options, outputPath);
                break;
        }
    }

    /// <summary>
    /// The line printed at the top of every page.
    ///
    /// A layout can author it with placeholders. Empty falls back to the
    /// submission convention - surname and short title - which is what every
    /// manuscript export printed when this could not be authored at all.
    /// </summary>
    internal static string RunningHead(ExportOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ResolvedRunningHead))
            return options.ResolvedRunningHead.Trim();

        var surname = !string.IsNullOrWhiteSpace(options.Author)
            ? options.Author.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last()
            : string.Empty;
        // A long title runs into the page number, so it is cut rather than
        // allowed to collide with it.
        var shortTitle = options.Title.Length > 30 ? options.Title[..27] + "..." : options.Title;
        return $"{surname} / {shortTitle.ToUpperInvariant()}";
    }

    /// <summary>
    /// A scene's content as ordered blocks, each carrying its paragraph style
    /// and whether it is a list item.
    ///
    /// Every writer parses from here rather than from raw HTML, so a style added
    /// in the editor reaches DOCX, EPUB, Markdown and LaTeX the same way instead
    /// of being honoured by whichever exporter happened to grow a case for it.
    /// </summary>
    internal static List<ExportBlock> ParseHtmlToBlocks(
        string html, IReadOnlyDictionary<string, string>? footnotes = null)
        => ExportProse.ParseHtmlToBlocks(html, footnotes);

    // ─── XML/HTML Escaping ───────────────────────────────────────────

    private static string EscapeXml(string str)
    {
        return str
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    private static string GenerateUuid()
    {
        return Guid.NewGuid().ToString();
    }

    // ─── Normseiten (German standard pages) ──────────────────────────

    /// <summary>
    /// Turns editor HTML into Normseite blocks. Paragraphs carrying the
    /// editor's heading / subheading style become headings; everything else is
    /// body text, with a blank line between paragraphs.
    /// </summary>
    public static List<NormseitenBlock> HtmlToNormseitenBlocks(string html)
        => ExportProse.HtmlToNormseitenBlocks(html);

    /// <summary>Blocks for a whole-manuscript Normseiten export.</summary>
    /// <summary>
    /// Splits an opening line into its initial letter, the words that follow
    /// in small capitals, and the rest. Returns null when there is nothing to
    /// set - an opener that begins with punctuation or a number is left alone
    /// rather than dropped into a quotation mark.
    /// </summary>
    internal static (string Initial, string LeadIn, string Tail)? SplitOpener(
        string text, int leadInWords)
    {
        if (string.IsNullOrWhiteSpace(text) || !char.IsLetter(text[0])) return null;

        var initial = text[..1];
        var after = text[1..];
        if (leadInWords <= 0) return (initial, string.Empty, after);

        // The lead-in runs to the end of the nth word, counting the initial's
        // own word as the first.
        var index = 0;
        var words = 0;
        while (index < after.Length && words < leadInWords)
        {
            while (index < after.Length && !char.IsWhiteSpace(after[index])) index++;
            words++;
            if (words < leadInWords)
            {
                while (index < after.Length && char.IsWhiteSpace(after[index])) index++;
            }
        }
        return (initial, after[..index], after[index..]);
    }

    /// <summary>
    /// Rewrites the book-relative image paths a scene stores into absolute
    /// ones. The writers each have to open the file; resolving once here means
    /// none of them needs to know where a book keeps its images.
    /// </summary>
    private string ResolveImagePaths(string html)
    {
        if (string.IsNullOrEmpty(html) || !html.Contains("<img", StringComparison.OrdinalIgnoreCase))
            return html;

        var bookRoot = _projectService.ActiveBookRoot;
        if (bookRoot == null) return html;

        return ImageTagRegex().Replace(html, match =>
        {
            var src = HtmlAttribute(match.Groups["attrs"].Value, "src");
            if (src.Length == 0 || src.Contains("://", StringComparison.Ordinal) || Path.IsPathRooted(src))
                return match.Value;
            var absolute = Path.GetFullPath(Path.Combine(bookRoot, src));
            return match.Value.Replace(src, absolute.Replace(Path.DirectorySeparatorChar, '/'));
        });
    }

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

        var normseitenPreset = options.ResolvePreset();
        for (var ci = 0; ci < chapters.Count; ci++)
        {
            var chapter = chapters[ci];
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
