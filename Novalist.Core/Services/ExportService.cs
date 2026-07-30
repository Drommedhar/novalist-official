using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Novalist.Core.Models;
using Novalist.Core.Utilities;
using XBrushes = PdfSharpCore.Drawing.XBrushes;
using XFont = PdfSharpCore.Drawing.XFont;
using XFontStyle = PdfSharpCore.Drawing.XFontStyle;
using XGraphics = PdfSharpCore.Drawing.XGraphics;
using XImage = PdfSharpCore.Drawing.XImage;
using XPoint = PdfSharpCore.Drawing.XPoint;
using XUnit = PdfSharpCore.Drawing.XUnit;

namespace Novalist.Core.Services;

public enum ExportFormat
{
    Epub,
    Docx,
    Pdf,
    Markdown,
    FinalDraft,
    LaTeX,
    Codex,
    CodexPdf
}

/// <summary>What an export would contain, reported before it is written.</summary>
public sealed class ExportPreview
{
    public int Chapters { get; init; }
    public int Scenes { get; init; }
    public int Words { get; init; }
    public int Characters { get; init; }
    public int Pages { get; init; }

    /// <summary>
    /// Pictures in the prose with nothing written about what they show. A
    /// reader who cannot see them gets nothing at all, and an EPUB that
    /// carries one cannot honestly claim to be accessible - so the count is
    /// reported before the export runs rather than discovered afterwards.
    /// </summary>
    public int UndescribedImages { get; init; }

    /// <summary>
    /// True only on the Normseite grid, where the layout fixes the columns and
    /// the lines so the count is arithmetic rather than a guess. Everywhere
    /// else the number is an estimate and has to be shown as one.
    /// </summary>
    public bool PagesAreExact { get; init; }
}

public class ExportOptions
{
    public ExportFormat Format { get; set; } = ExportFormat.Epub;
    public bool IncludeTitlePage { get; set; } = true;
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
   /// <summary>Optional preset id from <see cref="ExportPresets.All"/>.</summary>
    public string? PresetId { get; set; }
    public List<string> SelectedChapterGuids { get; set; } = [];

    /// <summary>
    /// Scene stages this export includes, by key. Null or empty means every
    /// stage - the common case, and the one an export that names no filter
    /// has to keep doing.
    /// </summary>
    public List<string>? IncludedStages { get; set; }

    /// <summary>ISBN, publisher, series and the rest. Never null; an empty one
    /// simply writes nothing extra.</summary>
    public Models.PublishingMetadata Publishing { get; set; } = new();

    /// <summary>
    /// Absolute path of the book's cover image. When set and readable, EPUB gets
    /// a real cover (manifest item with <c>properties="cover-image"</c>, the
    /// EPUB 2 <c>meta name="cover"</c> retailers still read, and a cover page
    /// first in the spine) and PDF gets a full-bleed cover page. Empty or
    /// missing means no cover, which is what every export did before.
    /// </summary>
    public string CoverImagePath { get; set; } = string.Empty;

    /// <summary>
    /// BCP-47 language tag written to EPUB's <c>dc:language</c>. Defaults to
    /// English only when nothing is supplied; a German or Chinese book that
    /// ships as <c>en</c> is mis-shelved at retailer ingestion.
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// Codex export filter: qualified entity keys of the form <c>type:id</c>
    /// (<c>character:</c>, <c>location:</c>, <c>item:</c>, <c>lore:</c>).
    /// <c>null</c> exports every entity; an empty list exports none.
    /// </summary>
    public List<string>? SelectedEntityKeys { get; set; }

    /// <summary>
    /// Translations for the codex export's fixed labels, keyed by
    /// <see cref="ExportService"/>'s label keys ("role", "characters", …).
    /// Supplied by the UI in the user's language; missing keys fall back to
    /// English.
    /// </summary>
    public Dictionary<string, string>? Labels { get; set; }

    /// <summary>
    /// Front- and back-matter pages to write around the story. Compiled from the
    /// book so the exporter does not need the project service.
    /// </summary>
    public List<MatterExportContent> Matter { get; set; } = [];

    /// <summary>
    /// Layouts the writer authored, so a custom preset id resolves to theirs
    /// rather than silently falling back to the default.
    /// </summary>
    public List<ExportPreset> CustomPresets { get; set; } = [];

    /// <summary>
    /// Substitutions applied to the compiled output only. Replace All writes to
    /// the source scenes; these never do, so a rule can be turned off without
    /// anything to undo.
    /// </summary>
    public List<Models.ExportReplacement> Replacements { get; set; } = [];

    /// <summary>
    /// How deep the table of contents goes. 1 lists the chapters, which is what
    /// every export did before; 2 also lists the scenes inside them. Values
    /// outside that range are clamped rather than rejected, because a contents
    /// list is not worth failing an export over.
    /// </summary>
    public int TocDepth { get; set; } = 1;

    /// <summary>
    /// Heading printed above the contents. Empty means "Table of Contents" -
    /// which is wrong in every language but English, and wrong in English for
    /// anyone who wanted "Contents".
    /// </summary>
    public string TocTitle { get; set; } = string.Empty;

    /// <summary>
    /// Absolute path of a DOCX whose styles this export should adopt. When set
    /// and readable, its <c>word/styles.xml</c> replaces the one Novalist
    /// generates, so an agent's or publisher's house style survives an export
    /// instead of being reapplied by hand afterwards. A path that is missing,
    /// locked, or not a DOCX falls back to Novalist's own styles: a bad
    /// reference document is a reason to ignore it, never to fail the export.
    /// </summary>
    public string ReferenceDocPath { get; set; } = string.Empty;

    /// <summary>The contents depth, clamped to what the writers can render.</summary>
    public int EffectiveTocDepth => Math.Clamp(TocDepth, 1, 2);

    /// <summary>
    /// The store this build is for, by <see cref="Models.RetailerLink.Key"/>, or
    /// empty for a neutral build that names no shop.
    ///
    /// One format, one path, one file was the whole model, so every copy of a
    /// book sold in five shops carried the same back-matter link - and Amazon
    /// refuses a book whose back matter links to a rival store.
    /// </summary>
    public string RetailerKey { get; set; } = string.Empty;

    /// <summary>The store this build is for, or null when it names none.</summary>
    public Models.RetailerLink? ResolveRetailer()
        => string.IsNullOrWhiteSpace(RetailerKey)
            ? null
            : Publishing.Retailers.FirstOrDefault(
                r => string.Equals(r.Key, RetailerKey.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Which parts of a Codex entry the export carries:
    /// <c>images</c>, <c>fields</c>, <c>relationships</c>, <c>sections</c>.
    ///
    /// Null means all of them - what every codex export did before. A series
    /// bible that has to leave the portraits out, or a submission packet that
    /// wants the names and nothing else, was an all-or-nothing choice per entry
    /// until this existed.
    /// </summary>
    public List<string>? CodexParts { get; set; }

    /// <summary>
    /// Section titles to carry, when sections are included at all. Null means
    /// every section; naming some is how "Appearance but not Secrets" is said.
    /// </summary>
    public List<string>? SelectedSectionTitles { get; set; }

    /// <summary>True when this export carries the named part of an entry.</summary>
    public bool IncludesPart(string part)
        => CodexParts == null || CodexParts.Contains(part, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True when a section with this title belongs in the export. False for
    /// every section when sections are off, whatever the title list says.
    /// </summary>
    public bool IncludesSection(string? title)
        => IncludesPart("sections")
           && (SelectedSectionTitles == null
               || SelectedSectionTitles.Contains(title ?? string.Empty, StringComparer.OrdinalIgnoreCase));

    /// <summary>Resolves to the configured preset (or default).</summary>
    public ExportPreset ResolvePreset()
    {
        if (!string.IsNullOrWhiteSpace(PresetId))
        {
            var custom = CustomPresets.FirstOrDefault(p => p.Id == PresetId);
            if (custom != null) return custom;
            return ExportPresets.GetById(PresetId);
        }
        return ExportPresets.GetById(ExportPresets.DefaultId);
    }
}

/// <summary>One front- or back-matter page on its way into an export.</summary>
public class MatterExportContent
{
    public string Id { get; set; } = string.Empty;

    /// <summary>The <see cref="Models.BookMatterKind"/> name, so writers can key
    /// per-kind layout off it without referencing the enum.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>"Front" or "Back".</summary>
    public string Placement { get; set; } = string.Empty;

    /// <summary>Heading to print. Empty means print no heading.</summary>
    public string Title { get; set; } = string.Empty;

    public string HtmlContent { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool InTableOfContents { get; set; }
}

public class ChapterExportContent
{
    public string Title { get; set; } = string.Empty;
    public int Order { get; set; }
    public string? Subtitle { get; set; }

    /// <summary>True when this chapter opens straight into its prose.</summary>
    public bool HideHeading { get; set; }

    public List<SceneExportContent> Scenes { get; set; } = [];
}

public class SceneExportContent
{
    public string Title { get; set; } = string.Empty;
    public int Order { get; set; }
    public string HtmlContent { get; set; } = string.Empty;

    /// <summary>
    /// Unresolved inline comments on this scene, carried through so DOCX can
    /// emit them as real Word comments an editor can reply to. Resolved ones are
    /// left behind: they are a record of a finished conversation, not a note the
    /// editor should see.
    /// </summary>
    public List<SceneExportComment> Comments { get; set; } = [];

    /// <summary>
    /// The scene's footnotes, keyed by the id its <c>&lt;sup class="nv-fn"&gt;</c>
    /// anchor carries. Kept beside the prose rather than appended to it, so each
    /// format can render a real note where the anchor sits.
    /// </summary>
    public Dictionary<string, string> Footnotes { get; set; } = [];
}

/// <summary>One comment travelling with a scene into an export.</summary>
public class SceneExportComment
{
    public string Id { get; set; } = string.Empty;

    /// <summary>The prose the comment was attached to, used to place the Word
    /// comment's range.</summary>
    public string AnchorText { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Internal representation of a text segment with formatting metadata.
/// </summary>
internal sealed class InlineSegment
{
    public string Text { get; set; } = string.Empty;
    public bool Bold { get; set; }
    public bool Italic { get; set; }

    /// <summary>
    /// Struck-through prose. Kept because a writer who struck a line meant the
    /// reader to see it struck; a highlight, by contrast, is a working mark and
    /// is dropped on the way out.
    /// </summary>
    public bool Strike { get; set; }

    /// <summary>
    /// When set, this segment is a footnote anchor rather than prose: the text
    /// of the note, which each format renders in its own way. The prose it was
    /// anchored to is the segment before it.
    /// </summary>
    public string? FootnoteText { get; set; }
}

/// <summary>
/// Provides export functionality for Novalist projects.
/// Supports EPUB, DOCX, PDF, and Markdown output formats.
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
    /// Compile chapter and scene data for export.
    /// </summary>
    public async Task<List<ChapterExportContent>> CompileChaptersAsync(ExportOptions options)
    {
        var chapters = _projectService.GetChaptersOrdered()
            .Where(c => options.SelectedChapterGuids.Contains(c.Guid))
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
            StoreLink = store?.Url ?? string.Empty
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

        foreach (var chapter in chapters)
        {
            var scenes = _projectService.GetScenesForChapter(chapter.Guid)
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
                        await _projectService.ReadSceneContentAsync(chapter, scene))),
                    options.Replacements);
                sceneContents.Add(new SceneExportContent
                {
                    Title = scene.Title,
                    Order = scene.Order,
                    HtmlContent = html,
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

            result.Add(new ChapterExportContent
            {
                Title = chapter.Title,
                Order = chapter.Order,
                Subtitle = chapter.Subtitle,
                HideHeading = chapter.HideHeading,
                Scenes = sceneContents
            });
        }

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

    // ─── Final Draft (.fdx) ──────────────────────────────────────────

    private async Task ExportToFinalDraftAsync(List<ChapterExportContent> chapters, ExportOptions options, string outputPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>");
        sb.AppendLine("<FinalDraft DocumentType=\"Script\" Template=\"No\" Version=\"5\">");
        sb.AppendLine("  <Content>");

        if (options.IncludeTitlePage && !string.IsNullOrWhiteSpace(options.Title))
        {
            sb.AppendLine("    <Paragraph Type=\"General\"><Text>" + XmlEscape(options.Title) + "</Text></Paragraph>");
            if (!string.IsNullOrWhiteSpace(options.Author))
                sb.AppendLine("    <Paragraph Type=\"General\"><Text>" + XmlEscape(options.Author) + "</Text></Paragraph>");
        }

        var fdxPreset = options.ResolvePreset();
        for (var ci = 0; ci < chapters.Count; ci++)
        {
            var chapter = chapters[ci];
            var fdxHeading = fdxPreset.ChapterHeading(ci + 1, chapter.Title).ToUpperInvariant();
            sb.AppendLine($"    <Paragraph Type=\"Scene Heading\"><Text>{XmlEscape(fdxHeading)}</Text></Paragraph>");
            foreach (var scene in chapter.Scenes)
            {
                sb.AppendLine($"    <Paragraph Type=\"Scene Heading\"><Text>{XmlEscape(scene.Title.ToUpperInvariant())}</Text></Paragraph>");
                foreach (var para in ParseHtmlToParagraphs(scene.HtmlContent))
                {
                    var text = string.Concat(para.Select(p => p.Text));
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    sb.AppendLine($"    <Paragraph Type=\"Action\"><Text>{XmlEscape(text.Trim())}</Text></Paragraph>");
                }
            }
        }

        sb.AppendLine("  </Content>");
        sb.AppendLine("</FinalDraft>");
        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8);
    }

    private static string XmlEscape(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    // ─── LaTeX ───────────────────────────────────────────────────────

    private async Task ExportToLatexAsync(List<ChapterExportContent> chapters, ExportOptions options, string outputPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\\documentclass[12pt,a4paper]{book}");
        sb.AppendLine("\\usepackage[utf8]{inputenc}");
        sb.AppendLine("\\usepackage{csquotes}");
        sb.AppendLine("\\usepackage{setspace}");
        // \sout comes from ulem; without it a struck line fails to compile.
        sb.AppendLine("\\usepackage[normalem]{ulem}");
        // Real drop caps, for a layout that asks for one.
        sb.AppendLine("\\usepackage{lettrine}");
        sb.AppendLine("\\doublespacing");
        if (!string.IsNullOrWhiteSpace(options.Title)) sb.AppendLine($"\\title{{{LatexEscape(options.Title)}}}");
        if (!string.IsNullOrWhiteSpace(options.Author)) sb.AppendLine($"\\author{{{LatexEscape(options.Author)}}}");
        sb.AppendLine("\\begin{document}");
        if (options.IncludeTitlePage) sb.AppendLine("\\maketitle");

        var latexPreset = options.ResolvePreset();
        for (var ci = 0; ci < chapters.Count; ci++)
        {
            var chapter = chapters[ci];
            // Starred, because the heading already carries whatever numbering
            // the layout asks for and LaTeX's own would print a second one.
            if (!chapter.HideHeading)
            {
                sb.AppendLine($"\\chapter*{{{LatexEscape(latexPreset.ChapterHeading(ci + 1, chapter.Title))}}}");
                if (!string.IsNullOrWhiteSpace(chapter.Subtitle))
                    sb.AppendLine(
                        $"\\begin{{center}}\\textit{{{LatexEscape(chapter.Subtitle)}}}\\end{{center}}");
            }
            for (int si = 0; si < chapter.Scenes.Count; si++)
            {
                if (si > 0) sb.AppendLine("\\begin{center}* * *\\end{center}");
                // A run of list items becomes one itemize/enumerate environment
                // rather than one per item, which LaTeX renders as a stack of
                // single-entry lists.
                var openList = ListKind.None;
                var latexFirst = si == 0;
                foreach (var block in ParseHtmlToBlocks(
                    chapter.Scenes[si].HtmlContent, chapter.Scenes[si].Footnotes))
                {
                    if (block.ImagePath != null)
                    {
                        sb.AppendLine("\\begin{figure}[h]\\centering");
                        sb.AppendLine(
                            $"\\includegraphics[width=\\linewidth]{{{block.ImagePath}}}");
                        if (block.ImageAlt.Length > 0)
                            sb.AppendLine($"\\caption*{{{LatexEscape(block.ImageAlt)}}}");
                        sb.AppendLine("\\end{figure}");
                        continue;
                    }
                    var body = string.Concat(block.Segments.Select(seg =>
                    {
                        // LaTeX has had this all along: a real footnote, set
                        // at the foot of whatever page the anchor lands on.
                        if (seg.FootnoteText != null)
                            return $"\\footnote{{{LatexEscape(seg.FootnoteText)}}}";
                        var t = LatexEscape(seg.Text);
                        if (seg.Strike) t = $"\\sout{{{t}}}";
                        if (seg.Bold && seg.Italic) return $"\\textbf{{\\textit{{{t}}}}}";
                        if (seg.Bold) return $"\\textbf{{{t}}}";
                        if (seg.Italic) return $"\\textit{{{t}}}";
                        return t;
                    }));

                    if (block.List != openList)
                    {
                        if (openList != ListKind.None)
                            sb.AppendLine(openList == ListKind.Number
                                ? "\\end{enumerate}" : "\\end{itemize}");
                        if (block.List != ListKind.None)
                            sb.AppendLine(block.List == ListKind.Number
                                ? "\\begin{enumerate}" : "\\begin{itemize}");
                        openList = block.List;
                    }

                    if (block.List != ListKind.None)
                    {
                        sb.AppendLine($"\\item {body}");
                        continue;
                    }

                    // lettrine sets the initial and the small-caps lead-in in
                    // one command, which is exactly the opener this describes.
                    var opener = latexFirst && latexPreset.DropCap && block.StyleId == null
                        ? SplitOpener(
                            string.Concat(block.Segments.Select(seg => seg.Text)),
                            latexPreset.LeadInSmallCapsWords)
                        : null;
                    latexFirst = false;

                    sb.AppendLine(opener != null
                        ? $"\\lettrine{{{LatexEscape(opener.Value.Initial)}}}"
                            + $"{{{LatexEscape(opener.Value.LeadIn)}}}{LatexEscape(opener.Value.Tail)}"
                        : block.StyleId switch
                        {
                            "heading" => $"\\section*{{{body}}}",
                            "subheading" => $"\\subsection*{{{body}}}",
                            "blockquote" => $"\\begin{{quote}}{body}\\end{{quote}}",
                            "poetry" => $"\\begin{{verse}}{body}\\end{{verse}}",
                            _ => body,
                        });
                    sb.AppendLine();
                }
                // A scene that ends inside a list still has to close it.
                if (openList != ListKind.None)
                    sb.AppendLine(openList == ListKind.Number
                        ? "\\end{enumerate}" : "\\end{itemize}");
            }
        }
        sb.AppendLine("\\end{document}");
        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8);
    }

    // ─── Codex (markdown / PDF with images) ──────────────────────────

    /// <summary>Entity kind prefixes used by <see cref="ExportOptions.SelectedEntityKeys"/>.</summary>
    private const string CodexCharacterKind = "character";
    private const string CodexLocationKind = "location";
    private const string CodexItemKind = "item";
    private const string CodexLoreKind = "lore";

    /// <summary>The codex entities selected for export, already ordered by name.</summary>
    private sealed class CodexContent
    {
        public List<CharacterData> Characters { get; init; } = [];
        public List<LocationData> Locations { get; init; } = [];
        public List<ItemData> Items { get; init; } = [];
        public List<LoreData> Lore { get; init; } = [];
    }

    private async Task<CodexContent> CompileCodexAsync(ExportOptions options)
    {
        var keys = options.SelectedEntityKeys is null
            ? null
            : new HashSet<string>(options.SelectedEntityKeys, StringComparer.OrdinalIgnoreCase);

        bool Included(string kind, string id) => keys is null || keys.Contains($"{kind}:{id}");

        var byName = System.StringComparer.CurrentCultureIgnoreCase;
        var characters = await _entityService!.LoadCharactersAsync();
        var locations = await _entityService.LoadLocationsAsync();
        var items = await _entityService.LoadItemsAsync();
        var lore = await _entityService.LoadLoreAsync();

        return new CodexContent
        {
            Characters = characters.Where(c => Included(CodexCharacterKind, c.Id))
                .OrderBy(c => c.DisplayName, byName).ToList(),
            Locations = locations.Where(l => Included(CodexLocationKind, l.Id))
                .OrderBy(l => l.Name, byName).ToList(),
            Items = items.Where(i => Included(CodexItemKind, i.Id))
                .OrderBy(i => i.Name, byName).ToList(),
            Lore = lore.Where(l => Included(CodexLoreKind, l.Id))
                .OrderBy(l => l.Name, byName).ToList()
        };
    }

    /// <summary>
    /// Resolves a fixed codex label in the user's language, falling back to
    /// English when the caller supplied no translation for it.
    /// </summary>
    private static string Label(ExportOptions options, string key, string fallback)
        => options.Labels is not null && options.Labels.TryGetValue(key, out var text)
           && !string.IsNullOrWhiteSpace(text)
            ? text
            : fallback;

    /// <summary>Field rows rendered for a character, in display order, skipping empty values.</summary>
    private static IEnumerable<KeyValuePair<string, string>> CharacterFields(CharacterData c, ExportOptions options)
    {
        // In date age mode the Age field only restates the birth date, so it is
        // dropped rather than printed as a bare date.
        var age = string.Equals(c.AgeMode, "date", StringComparison.OrdinalIgnoreCase) ? string.Empty : c.Age;

        var fixedFields = new (string Key, string Fallback, string Value)[]
        {
            ("role", "Role", c.Role),
            ("age", "Age", age),
            ("gender", "Gender", c.Gender),
            ("group", "Group", c.Group),
            ("eyes", "Eyes", c.EyeColor),
            ("hair", "Hair", c.HairColor),
            ("height", "Height", c.Height),
            ("build", "Build", c.Build),
            ("skin", "Skin", c.SkinTone),
            ("notable", "Notable", c.DistinguishingFeatures)
        };

        foreach (var (key, fallback, value) in fixedFields)
            if (!string.IsNullOrWhiteSpace(value))
                yield return new KeyValuePair<string, string>(Label(options, key, fallback), value);

        if (c.CustomProperties is { Count: > 0 })
            foreach (var kv in c.CustomProperties)
                if (!string.IsNullOrWhiteSpace(kv.Value))
                    yield return kv;
    }

    /// <summary>Field rows rendered for a location / item / lore entry.</summary>
    private static IEnumerable<KeyValuePair<string, string>> GenericFields(
        string type, string description, Dictionary<string, string>? customProps, ExportOptions options)
    {
        if (!string.IsNullOrWhiteSpace(type))
            yield return new KeyValuePair<string, string>(Label(options, "type", "Type"), type);
        if (!string.IsNullOrWhiteSpace(description))
            yield return new KeyValuePair<string, string>(Label(options, "description", "Description"), description);
        if (customProps is { Count: > 0 })
            foreach (var kv in customProps)
                if (!string.IsNullOrWhiteSpace(kv.Value))
                    yield return kv;
    }

    public async Task ExportCodexAsync(ExportOptions options, string outputPath)
    {
        if (_entityService == null)
        {
            await File.WriteAllTextAsync(outputPath, "Codex export requires entity service.", Encoding.UTF8);
            return;
        }

        var outputDir = Path.GetDirectoryName(outputPath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(outputPath);
        var imagesFolderName = SanitizeFolderName(baseName) + "_images";
        var imagesAbsDir = Path.Combine(outputDir, imagesFolderName);
        Directory.CreateDirectory(imagesAbsDir);

        var copyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? CopyImage(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;
            if (copyMap.TryGetValue(relativePath, out var existing)) return existing;
            var abs = _entityService!.GetImageFullPath(relativePath);
            if (string.IsNullOrWhiteSpace(abs) || !File.Exists(abs)) return null;
            var fileName = Path.GetFileName(abs);
            var dest = Path.Combine(imagesAbsDir, fileName);
            int n = 1;
            while (File.Exists(dest) &&
                   !FilesEqual(abs, dest))
            {
                fileName = $"{Path.GetFileNameWithoutExtension(abs)}_{n}{Path.GetExtension(abs)}";
                dest = Path.Combine(imagesAbsDir, fileName);
                n++;
            }
            if (!File.Exists(dest)) File.Copy(abs, dest, overwrite: false);
            var rel = imagesFolderName + "/" + fileName;
            copyMap[relativePath] = rel;
            return rel;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# {options.Title ?? "Codex"}");
        if (!string.IsNullOrWhiteSpace(options.Author)) sb.AppendLine($"_by {options.Author}_");
        sb.AppendLine();

        var content = await CompileCodexAsync(options);

        if (content.Characters.Count > 0)
        {
            sb.AppendLine($"## {Label(options, "characters", "Characters")}");
            foreach (var c in content.Characters)
                AppendCharacter(sb, c, options, CopyImage);
        }

        if (content.Locations.Count > 0)
        {
            sb.AppendLine($"## {Label(options, "locations", "Locations")}");
            foreach (var l in content.Locations)
                AppendGenericEntity(sb, l.Name, l.Type, l.Description, l.Images, l.CustomProperties, l.Sections, options, CopyImage);
        }

        if (content.Items.Count > 0)
        {
            sb.AppendLine($"## {Label(options, "items", "Items")}");
            foreach (var it in content.Items)
                AppendGenericEntity(sb, it.Name, it.Type, it.Description, it.Images, it.CustomProperties, it.Sections, options, CopyImage);
        }

        if (content.Lore.Count > 0)
        {
            sb.AppendLine($"## {Label(options, "lore", "Lore")}");
            foreach (var lo in content.Lore)
                AppendGenericEntity(sb, lo.Name, lo.Category, lo.Description, lo.Images, lo.CustomProperties, lo.Sections, options, CopyImage);
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8);

        // Remove the images folder if nothing was copied (entities had no images).
        if (copyMap.Count == 0)
        {
            try { Directory.Delete(imagesAbsDir, recursive: false); } catch { }
        }
    }

    // The catch is a TOCTOU safety net: callers verify both files exist before
    // calling, so FileInfo.Length cannot realistically throw — excluded as the
    // catch line is not deterministically reachable.
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static bool FilesEqual(string a, string b)
    {
        try
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            return fa.Length == fb.Length;
        }
        catch { return false; }
    }

    private static string SanitizeFolderName(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var arr = s.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var clean = new string(arr).Trim();
        return string.IsNullOrEmpty(clean) ? "codex" : clean;
    }

    private static void AppendCharacter(StringBuilder sb, CharacterData c, ExportOptions options, Func<string, string?> copyImage)
    {
        sb.AppendLine($"### {c.DisplayName}");
        if (c.Images is { Count: > 0 } && options.IncludesPart("images"))
        {
            foreach (var img in c.Images)
            {
                if (string.IsNullOrWhiteSpace(img.Path)) continue;
                var rel = copyImage(img.Path);
                if (rel != null) sb.AppendLine($"![{img.Name}]({rel})");
            }
        }
        sb.AppendLine();
        if (options.IncludesPart("fields"))
            foreach (var field in CharacterFields(c, options))
                sb.AppendLine($"- **{field.Key}:** {field.Value}");

        if (c.Relationships is { Count: > 0 } && options.IncludesPart("relationships"))
        {
            sb.AppendLine();
            sb.AppendLine($"**{Label(options, "relationships", "Relationships")}**");
            foreach (var r in c.Relationships)
                sb.AppendLine($"- {r.Role}: {r.Target}");
        }

        if (c.Sections is { Count: > 0 })
        {
            foreach (var s in c.Sections)
            {
                if (string.IsNullOrWhiteSpace(s.Content) || !options.IncludesSection(s.Title)) continue;
                sb.AppendLine();
                sb.AppendLine($"**{s.Title}**");
                sb.AppendLine(StripHtml(s.Content));
            }
        }
        sb.AppendLine();
    }

    private static void AppendGenericEntity(StringBuilder sb, string name, string type, string description,
        List<EntityImage>? images, Dictionary<string, string>? customProps, List<EntitySection>? sections,
        ExportOptions options, Func<string, string?> copyImage)
    {
        sb.AppendLine($"### {name}");
        if (images is { Count: > 0 } && options.IncludesPart("images"))
            foreach (var img in images)
            {
                if (string.IsNullOrWhiteSpace(img.Path)) continue;
                var rel = copyImage(img.Path);
                if (rel != null) sb.AppendLine($"![{img.Name}]({rel})");
            }
        sb.AppendLine();
        if (options.IncludesPart("fields"))
            foreach (var field in GenericFields(type, description, customProps, options))
                sb.AppendLine($"- **{field.Key}:** {field.Value}");
        if (sections is { Count: > 0 })
        {
            foreach (var s in sections)
            {
                if (string.IsNullOrWhiteSpace(s.Content) || !options.IncludesSection(s.Title)) continue;
                sb.AppendLine();
                sb.AppendLine($"**{s.Title}**");
                sb.AppendLine(StripHtml(s.Content));
            }
        }
        sb.AppendLine();
    }

    /// <summary>One logical line of codex prose after markdown-ish parsing.</summary>
    internal sealed class CodexProseLine
    {
        public bool Heading { get; init; }
        public bool Bullet { get; init; }
        public List<InlineSegment> Segments { get; init; } = [];
    }

    /// <summary>
    /// Turns stored entity prose — editor HTML, markdown, or plain text — into
    /// lines a PDF page can lay out: block tags and newlines end a line, stray
    /// control characters are dropped (they render as boxes), and leading
    /// <c>#</c> / <c>*</c> markers plus <c>**bold**</c> spans become styling
    /// instead of literal text.
    /// </summary>
    internal static List<CodexProseLine> ParseCodexProse(string content)
    {
        var text = StripHtml(BlockTagRegex().Replace(content, "\n"))
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');
        // Tabs and friends become spaces; every other control character is
        // dropped, since a PDF renders it as a box glyph.
        text = new string(text
            .Select(c => c != '\n' && char.IsWhiteSpace(c) ? ' ' : c)
            .Where(c => c == '\n' || !char.IsControl(c))
            .ToArray());

        var lines = new List<CodexProseLine>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                lines.Add(new CodexProseLine());
                continue;
            }

            var heading = MarkdownHeadingRegex().IsMatch(line);
            if (heading) line = MarkdownHeadingRegex().Replace(line, string.Empty);
            var bullet = !heading && MarkdownBulletRegex().IsMatch(line);
            if (bullet) line = MarkdownBulletRegex().Replace(line, string.Empty);

            lines.Add(new CodexProseLine
            {
                Heading = heading,
                Bullet = bullet,
                Segments = ParseInlineMarkdown(line)
            });
        }
        return lines;
    }

    /// <summary>Splits a line into runs, toggling bold on <c>**</c> markers.</summary>
    internal static List<InlineSegment> ParseInlineMarkdown(string line)
    {
        var segments = new List<InlineSegment>();
        var buffer = new StringBuilder();
        var bold = false;

        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '*' && i + 1 < line.Length && line[i + 1] == '*')
            {
                if (buffer.Length > 0)
                {
                    segments.Add(new InlineSegment { Text = buffer.ToString(), Bold = bold });
                    buffer.Clear();
                }
                bold = !bold;
                i++;
                continue;
            }
            buffer.Append(line[i]);
        }

        if (buffer.Length > 0)
            segments.Add(new InlineSegment { Text = buffer.ToString(), Bold = bold });
        return segments;
    }

    /// <summary>
    /// Splits a word too wide for a whole line (a long URL, or prose written
    /// without spaces) into chunks that fit.
    /// </summary>
    private static IEnumerable<string> HardBreak(string word, XFont font, XGraphics gfx, double maxWidth)
    {
        var start = 0;
        while (start < word.Length)
        {
            var take = 1;
            while (start + take < word.Length &&
                   gfx.MeasureString(word.Substring(start, take + 1), font).Width <= maxWidth)
                take++;
            yield return word.Substring(start, take);
            start += take;
        }
    }

    /// <summary>
    /// Codex export as a self-contained PDF: entity images are drawn into the
    /// document instead of being written next to it in a sidecar folder.
    /// </summary>
    public async Task ExportCodexPdfAsync(ExportOptions options, string outputPath)
    {
        if (_entityService == null)
        {
            await File.WriteAllTextAsync(outputPath, "Codex export requires entity service.", Encoding.UTF8);
            return;
        }

        var content = await CompileCodexAsync(options);

        string? ResolveImage(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;
            var abs = _entityService!.GetImageFullPath(relativePath);
            return !string.IsNullOrWhiteSpace(abs) && File.Exists(abs) ? abs : null;
        }

        WriteCodexPdf(content, options, outputPath, ResolveImage);
    }

    private static void WriteCodexPdf(
        CodexContent content,
        ExportOptions options,
        string outputPath,
        Func<string, string?> resolveImage)
    {
        const string fontName = "Times New Roman";
        var doc = new PdfSharpCore.Pdf.PdfDocument();
        var docTitle = string.IsNullOrWhiteSpace(options.Title) ? "Codex" : options.Title;
        doc.Info.Title = docTitle;
        if (!string.IsNullOrWhiteSpace(options.Author))
            doc.Info.Author = options.Author;

        var pageWidth = XUnit.FromInch(8.5);
        var pageHeight = XUnit.FromInch(11);
        var margin = XUnit.FromInch(1);
        var textWidth = pageWidth - 2 * margin;
        var pageBottom = pageHeight - margin;
        var fieldIndent = (double)XUnit.FromInch(0.2);
        var bulletIndent = (double)XUnit.FromInch(0.18);
        var maxImageSide = (double)XUnit.FromInch(3);
        const double lineHeight = 15.0;

        var bodyFont = new XFont(fontName, 11);
        var labelFont = new XFont(fontName, 11, XFontStyle.Bold);
        var blockFont = new XFont(fontName, 12.5, XFontStyle.Bold);
        var entityFont = new XFont(fontName, 15, XFontStyle.Bold);
        var sectionFont = new XFont(fontName, 20, XFontStyle.Bold);

        XGraphics? gfx = null;
        PdfSharpCore.Pdf.PdfPage? currentPage = null;
        var y = 0.0;

        void NewPage()
        {
            gfx?.Dispose();
            var page = doc.AddPage();
            page.Width = pageWidth;
            page.Height = pageHeight;
            currentPage = page;
            gfx = XGraphics.FromPdfPage(page);
            y = margin + lineHeight;
        }

        void Ensure(double needed)
        {
            if (gfx == null || y + needed > pageBottom) NewPage();
        }

        // Lays out styled runs as one flowing line, wrapping at the right
        // margin and continuing at `indent` on every following line.
        void DrawRuns(IEnumerable<InlineSegment> runs, double indent, XFont regular, XFont strong)
        {
            Ensure(lineHeight);
            var left = margin + indent;
            var right = margin + textWidth;
            var x = left;

            void Place(string word, XFont font)
            {
                var width = gfx!.MeasureString(word, font).Width;
                if (x + width > right && x > left)
                {
                    y += lineHeight;
                    Ensure(lineHeight);
                    x = left;
                }
                gfx!.DrawString(word, font, XBrushes.Black, new XPoint(x, y));
                x += width;
            }

            // A space is drawn only where the source text had one, so a run
            // boundary ("**bold**" followed by ".") does not invent one.
            var pendingSpace = false;
            foreach (var run in runs)
            {
                var font = run.Bold ? strong : regular;
                var parts = run.Text.Split(' ');
                for (var j = 0; j < parts.Length; j++)
                {
                    if (j > 0) pendingSpace = true;
                    var word = parts[j];
                    if (word.Length == 0) continue;
                    if (pendingSpace && x > left) x += gfx!.MeasureString(" ", font).Width;
                    pendingSpace = false;
                    if (gfx!.MeasureString(word, font).Width > right - left)
                        foreach (var chunk in HardBreak(word, font, gfx, right - left)) Place(chunk, font);
                    else
                        Place(word, font);
                }
            }

            y += lineHeight;
        }

        void DrawHeading(string text, double indent, XFont font)
            => DrawRuns([new InlineSegment { Text = text }], indent, font, font);

        void DrawProse(string content, double indent)
        {
            foreach (var line in ParseCodexProse(content))
            {
                if (line.Segments.Count == 0)
                {
                    y += lineHeight * 0.4;   // blank line -> paragraph gap
                    continue;
                }

                var lineIndent = indent;
                if (line.Bullet)
                {
                    lineIndent += bulletIndent;
                    Ensure(lineHeight);
                    gfx!.DrawString("•", bodyFont, XBrushes.Black, new XPoint(margin + indent, y));
                }
                DrawRuns(line.Segments, lineIndent, line.Heading ? labelFont : bodyFont, labelFont);
            }
        }

        void DrawField(KeyValuePair<string, string> field)
        {
            var runs = new List<InlineSegment> { new() { Text = field.Key + ": ", Bold = true } };
            runs.AddRange(ParseInlineMarkdown(WhitespaceRunRegex().Replace(field.Value, " ").Trim()));
            DrawRuns(runs, fieldIndent, bodyFont, labelFont);
        }

        void DrawImage(string absolutePath)
        {
            XImage image;
            // Unreadable or unsupported image files are skipped rather than
            // failing the whole export.
            try { image = XImage.FromFile(absolutePath); }
            catch { return; }

            using (image)
            {
                var scale = Math.Min(1.0, Math.Min(maxImageSide / image.PointWidth, maxImageSide / image.PointHeight));
                var width = image.PointWidth * scale;
                var height = image.PointHeight * scale;
                Ensure(height + lineHeight);
                gfx!.DrawImage(image, margin + fieldIndent, y, width, height);
                y += height + lineHeight * 0.5;
            }
        }

        void DrawImages(List<EntityImage>? images)
        {
            if (images is not { Count: > 0 } || !options.IncludesPart("images")) return;
            foreach (var img in images)
            {
                if (string.IsNullOrWhiteSpace(img.Path)) continue;
                var abs = resolveImage(img.Path);
                if (abs != null) DrawImage(abs);
            }
        }

        void DrawSections(List<EntitySection>? sections)
        {
            if (sections is not { Count: > 0 }) return;
            foreach (var section in sections)
            {
                if (string.IsNullOrWhiteSpace(section.Content)
                    || !options.IncludesSection(section.Title)) continue;
                y += lineHeight * 0.5;
                DrawHeading(section.Title, fieldIndent, blockFont);
                DrawProse(section.Content, fieldIndent);
            }
        }

        // Every entry opens its own page so a reader can flip to one entry, and
        // gets a bookmark nested under its group in the PDF outline.
        void DrawEntity(
            PdfSharpCore.Pdf.PdfOutline group,
            bool first,
            string name,
            IEnumerable<KeyValuePair<string, string>> fields,
            List<EntityImage>? images,
            List<EntitySection>? sections,
            List<EntityRelationship>? relationships)
        {
            if (!first) NewPage();
            group.Outlines.Add(name, currentPage, false);

            DrawHeading(name, 0, entityFont);
            DrawImages(images);
            if (options.IncludesPart("fields"))
                foreach (var field in fields) DrawField(field);

            if (relationships is { Count: > 0 } && options.IncludesPart("relationships"))
            {
                y += lineHeight * 0.5;
                DrawHeading(Label(options, "relationships", "Relationships"), fieldIndent, blockFont);
                foreach (var rel in relationships)
                    DrawHeading($"{rel.Role}: {rel.Target}", fieldIndent * 2, bodyFont);
            }

            DrawSections(sections);
            y += lineHeight;
        }

        PdfSharpCore.Pdf.PdfOutline SectionHeading(string title)
        {
            NewPage();
            var outline = doc.Outlines.Add(title, currentPage, true);
            DrawHeading(title, 0, sectionFont);
            y += lineHeight;
            return outline;
        }

        if (options.IncludeTitlePage)
        {
            NewPage();
            var titleFont = new XFont(fontName, 26, XFontStyle.Bold);
            var titleWidth = gfx!.MeasureString(docTitle, titleFont).Width;
            gfx.DrawString(docTitle, titleFont, XBrushes.Black,
                new XPoint((pageWidth - titleWidth) / 2, pageHeight * 0.45));

            if (!string.IsNullOrWhiteSpace(options.Author))
            {
                var authorFont = new XFont(fontName, 14, XFontStyle.Italic);
                var authorWidth = gfx.MeasureString(options.Author, authorFont).Width;
                gfx.DrawString(options.Author, authorFont, XBrushes.Black,
                    new XPoint((pageWidth - authorWidth) / 2, pageHeight * 0.45 + 30));
            }
        }

        if (content.Characters.Count > 0)
        {
            var group = SectionHeading(Label(options, "characters", "Characters"));
            for (var i = 0; i < content.Characters.Count; i++)
            {
                var c = content.Characters[i];
                DrawEntity(group, i == 0, c.DisplayName, CharacterFields(c, options),
                    c.Images, c.Sections, c.Relationships);
            }
        }

        if (content.Locations.Count > 0)
        {
            var group = SectionHeading(Label(options, "locations", "Locations"));
            for (var i = 0; i < content.Locations.Count; i++)
            {
                var l = content.Locations[i];
                DrawEntity(group, i == 0, l.Name, GenericFields(l.Type, l.Description, l.CustomProperties, options),
                    l.Images, l.Sections, l.Relationships);
            }
        }

        if (content.Items.Count > 0)
        {
            var group = SectionHeading(Label(options, "items", "Items"));
            for (var i = 0; i < content.Items.Count; i++)
            {
                var it = content.Items[i];
                DrawEntity(group, i == 0, it.Name, GenericFields(it.Type, it.Description, it.CustomProperties, options),
                    it.Images, it.Sections, it.Relationships);
            }
        }

        if (content.Lore.Count > 0)
        {
            var group = SectionHeading(Label(options, "lore", "Lore"));
            for (var i = 0; i < content.Lore.Count; i++)
            {
                var lo = content.Lore[i];
                DrawEntity(group, i == 0, lo.Name, GenericFields(lo.Category, lo.Description, lo.CustomProperties, options),
                    lo.Images, lo.Sections, lo.Relationships);
            }
        }

        // An empty selection still has to produce a readable file.
        if (doc.PageCount == 0) NewPage();
        gfx!.Dispose();
        doc.Save(outputPath);
    }

    private static string LatexEscape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\textbackslash{}"); break;
                case '&': sb.Append("\\&"); break;
                case '%': sb.Append("\\%"); break;
                case '$': sb.Append("\\$"); break;
                case '#': sb.Append("\\#"); break;
                case '_': sb.Append("\\_"); break;
                case '{': sb.Append("\\{"); break;
                case '}': sb.Append("\\}"); break;
                case '~': sb.Append("\\textasciitilde{}"); break;
                case '^': sb.Append("\\textasciicircum{}"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Export the project's timeline as a chronological outline (Markdown).
    /// Groups events by their linked chapter when present; otherwise lists them
    /// under "Unscheduled events" in <see cref="TimelineManualEvent.Order"/>.
    /// </summary>
    public async Task ExportTimelineOutlineAsync(string outputPath)
    {
        var timeline = _projectService.ProjectSettings?.Timeline ?? new TimelineData();
        var chapters = _projectService.GetChaptersOrdered().ToList();
        var categories = timeline.Categories.ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("# Story Outline");
        sb.AppendLine();

        var eventsByChapter = timeline.ManualEvents
            .GroupBy(ev => string.IsNullOrWhiteSpace(ev.LinkedChapterGuid) ? string.Empty : ev.LinkedChapterGuid)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Order).ToList());

        foreach (var chapter in chapters)
        {
            sb.Append("## ").Append(chapter.Order).Append(". ").AppendLine(chapter.Title);
            if (!string.IsNullOrWhiteSpace(chapter.Act))
                sb.Append("_Act: ").Append(chapter.Act).AppendLine("_");
            if (!string.IsNullOrWhiteSpace(chapter.Date))
                sb.Append("_Date: ").Append(chapter.Date).AppendLine("_");

            if (eventsByChapter.TryGetValue(chapter.Guid, out var chapterEvents))
            {
                foreach (var ev in chapterEvents)
                    AppendEvent(sb, ev, categories);
            }

            var scenes = _projectService.GetScenesForChapter(chapter.Guid);
            foreach (var scene in scenes)
            {
                sb.Append("- **").Append(scene.Title).Append("**");
                if (!string.IsNullOrWhiteSpace(scene.Date))
                    sb.Append(" — ").Append(scene.Date);
                if (!string.IsNullOrWhiteSpace(scene.Synopsis))
                    sb.Append(" — ").Append(scene.Synopsis.Replace('\n', ' '));
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        if (eventsByChapter.TryGetValue(string.Empty, out var unscheduled) && unscheduled.Count > 0)
        {
            sb.AppendLine("## Unscheduled events");
            foreach (var ev in unscheduled)
                AppendEvent(sb, ev, categories);
            sb.AppendLine();
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8);
    }

    private static void AppendEvent(StringBuilder sb, TimelineManualEvent ev, IReadOnlyDictionary<string, string> categories)
    {
        sb.Append("- ");
        if (!string.IsNullOrWhiteSpace(ev.Date)) sb.Append('[').Append(ev.Date).Append("] ");
        sb.Append(ev.Title);
        if (!string.IsNullOrWhiteSpace(ev.CategoryId) && categories.TryGetValue(ev.CategoryId, out var cat))
            sb.Append(" _(").Append(cat).Append(")_");
        if (!string.IsNullOrWhiteSpace(ev.Description))
            sb.Append(" — ").Append(ev.Description.Replace('\n', ' '));
        sb.AppendLine();
    }

    // ─── HTML Processing ─────────────────────────────────────────────

    /// <summary>
    /// Extract plain-text paragraphs from scene HTML content.
    /// Returns a list of paragraphs with inline formatting preserved as segments.
    /// </summary>
    private static List<List<InlineSegment>> ParseHtmlToParagraphs(
        string html, IReadOnlyDictionary<string, string>? footnotes = null)
        => [.. ParseHtmlToBlocks(html, footnotes).Select(b => b.Segments)];

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
    {
        if (string.IsNullOrWhiteSpace(html)) return [];

        var blocks = new List<ExportBlock>();
        var matches = BlockRegex().Matches(html);

        if (matches.Count == 0)
        {
            // No block markup at all: the whole thing is one paragraph.
            var stripped = StripHtml(html);
            if (!string.IsNullOrWhiteSpace(stripped))
                blocks.Add(new ExportBlock(
                    [new InlineSegment { Text = stripped.Trim() }], null, ListKind.None));
            return blocks;
        }

        // A list item's kind comes from the ul/ol it sits in, which the per-item
        // match cannot see, so the enclosing tag is tracked across the loop.
        var listKind = ListKind.None;
        foreach (Match match in matches)
        {
            var tag = match.Groups["tag"].Value.ToLowerInvariant();
            if (tag is "ul" or "ol")
            {
                listKind = tag == "ul" ? ListKind.Bullet : ListKind.Number;
                continue;
            }
            if (tag is "/ul" or "/ol")
            {
                listKind = ListKind.None;
                continue;
            }

            // An image is a block of its own: the editor only ever puts one in
            // a paragraph by itself, and a writer that tried to mix it with
            // runs would have to invent a layout nobody asked for.
            var image = ImageTagRegex().Match(match.Groups["body"].Value);
            if (image.Success)
            {
                var attrs = image.Groups["attrs"].Value;
                blocks.Add(new ExportBlock([], null, ListKind.None)
                {
                    ImagePath = WebUtility.HtmlDecode(HtmlAttribute(attrs, "src")),
                    ImageAlt = WebUtility.HtmlDecode(HtmlAttribute(attrs, "alt"))
                });
                continue;
            }

            var segments = ParseInlineFormatting(match.Groups["body"].Value, footnotes);
            // A paragraph holding nothing but a footnote anchor is still
            // worth keeping - the note is the content.
            if (segments.Count == 0
                || segments.All(s => string.IsNullOrWhiteSpace(s.Text) && s.FootnoteText == null))
                continue;

            blocks.Add(tag == "li"
                // A stray li outside any list still reads as a bullet rather
                // than silently becoming body text.
                ? new ExportBlock(segments, null, listKind == ListKind.None ? ListKind.Bullet : listKind)
                : new ExportBlock(segments, ExtractStyleClass(match.Groups["attrs"].Value), ListKind.None));
        }

        return blocks;
    }

    /// <summary>
    /// Parse inline formatting (bold, italic, underline) from HTML content.
    /// </summary>
    private static List<InlineSegment> ParseInlineFormatting(
        string html, IReadOnlyDictionary<string, string>? footnotes = null)
    {
        var segments = new List<InlineSegment>();
        ParseInlineRecursive(html, false, false, segments, footnotes);
        return segments;
    }

    private static void ParseInlineRecursive(
        string html, bool bold, bool italic, List<InlineSegment> segments,
        IReadOnlyDictionary<string, string>? footnotes = null, bool strike = false)
    {
        var pos = 0;
        while (pos < html.Length)
        {
            var tagStart = html.IndexOf('<', pos);
            if (tagStart < 0)
            {
                // Remaining text
                var text = WebUtility.HtmlDecode(html[pos..]);
                if (!string.IsNullOrEmpty(text))
                    segments.Add(new InlineSegment
                    {
                        Text = text, Bold = bold, Italic = italic, Strike = strike
                    });
                break;
            }

            // Text before tag
            if (tagStart > pos)
            {
                var text = WebUtility.HtmlDecode(html[pos..tagStart]);
                if (!string.IsNullOrEmpty(text))
                    segments.Add(new InlineSegment
                    {
                        Text = text, Bold = bold, Italic = italic, Strike = strike
                    });
            }

            var tagEnd = html.IndexOf('>', tagStart);
            if (tagEnd < 0) break;

            var tag = html[(tagStart + 1)..tagEnd].Trim().ToLowerInvariant();
            pos = tagEnd + 1;

            // Self-closing tags
            if (tag is "br" or "br/" or "br /")
            {
                segments.Add(new InlineSegment
                {
                    Text = "\n", Bold = bold, Italic = italic, Strike = strike
                });
                continue;
            }

            // Skip closing tags at this level
            if (tag.StartsWith('/'))
                continue;

            // Remove attributes from tag name for matching
            var tagName = tag.Split(' ', '/')[0];

            // Find matching closing tag
            var closingTag = $"</{tagName}>";
            var closeIdx = FindMatchingCloseTag(html, pos, tagName);
            if (closeIdx < 0)
            {
                // No closing tag found, skip
                continue;
            }

            var innerContent = html[pos..closeIdx];
            pos = closeIdx + closingTag.Length;

            // A footnote anchor is not prose. Left to the default branch it
            // became a bare digit sitting in the middle of a sentence.
            if (tagName == "sup" && tag.Contains("nv-fn"))
            {
                var id = FootnoteIdRegex().Match(tag).Groups[1].Value;
                if (footnotes != null && footnotes.TryGetValue(id, out var noteText))
                    segments.Add(new InlineSegment { FootnoteText = noteText });
                continue;
            }

            switch (tagName)
            {
                case "b" or "strong":
                    ParseInlineRecursive(innerContent, true, italic, segments, footnotes, strike);
                    break;
                case "i" or "em":
                    ParseInlineRecursive(innerContent, bold, true, segments, footnotes, strike);
                    break;
                case "s" or "strike" or "del":
                    ParseInlineRecursive(innerContent, bold, italic, segments, footnotes, true);
                    break;
                case "u":
                    // Underline treated as regular text in export (no underline in most book formats)
                    ParseInlineRecursive(innerContent, bold, italic, segments, footnotes, strike);
                    break;
                case "span":
                    // Spans may carry style info but for export we just recurse.
                    // A highlight is one of them: it is a working mark the
                    // writer left themselves, not something to print.
                    ParseInlineRecursive(innerContent, bold, italic, segments, footnotes, strike);
                    break;
                default:
                    // Unknown tag - just extract text
                    ParseInlineRecursive(innerContent, bold, italic, segments, footnotes, strike);
                    break;
            }
        }
    }

    // The trailing `return -1` after the loop is compiler-required but
    // unreachable: the loop only exits by returning (depth hits 0) or via the
    // inner `nextClose < 0` return. Excluded so that dead line doesn't block 100%.
    /// <summary>
    /// The next opening tag of exactly this name.
    ///
    /// A plain IndexOf on "&lt;s" also matches "&lt;span", which made the
    /// nesting count wrong and dropped a struck phrase that happened to share a
    /// paragraph with a span - and the same for b/blockquote and i/img.
    /// </summary>
    private static int NextOpenTag(string html, int from, string openPattern)
    {
        // Unconditional: the only ways out are a match or running off the end,
        // both of which return. A bounded loop would leave an unreachable line
        // after it.
        var pos = from;
        while (true)
        {
            var at = html.IndexOf(openPattern, pos, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return -1;
            var after = at + openPattern.Length;
            if (after >= html.Length || !char.IsAsciiLetterOrDigit(html[after]))
                return at;
            pos = at + 1;
        }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static int FindMatchingCloseTag(string html, int startPos, string tagName)
    {
        var depth = 1;
        var pos = startPos;
        var openPattern = $"<{tagName}";
        var closePattern = $"</{tagName}>";

        while (pos < html.Length && depth > 0)
        {
            var nextOpen = NextOpenTag(html, pos, openPattern);
            var nextClose = html.IndexOf(closePattern, pos, StringComparison.OrdinalIgnoreCase);

            if (nextClose < 0) return -1;

            if (nextOpen >= 0 && nextOpen < nextClose)
            {
                depth++;
                pos = nextOpen + openPattern.Length;
            }
            else
            {
                depth--;
                if (depth == 0) return nextClose;
                pos = nextClose + closePattern.Length;
            }
        }

        return -1;
    }

    /// <summary>
    /// Strip all HTML tags and decode entities.
    /// </summary>
    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        var text = Regex.Replace(html, "<[^>]+>", string.Empty);
        return WebUtility.HtmlDecode(text);
    }

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

    // ─── EPUB Export ─────────────────────────────────────────────────

    private static async Task ExportToEpubAsync(
        List<ChapterExportContent> chapters,
        ExportOptions options,
        string outputPath)
    {
        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        // mimetype - must be first, stored without compression
        var mimetypeEntry = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        await using (var w = new StreamWriter(mimetypeEntry.Open(), Encoding.ASCII))
            await w.WriteAsync("application/epub+zip");

        var bookId = $"urn:uuid:{GenerateUuid()}";
        var modifiedDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        // META-INF/container.xml
        await WriteEntryAsync(zip, "META-INF/container.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles>
                <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
              </rootfiles>
            </container>
            """);

        // Stylesheet
        await WriteEntryAsync(zip, "OEBPS/styles.css", GenerateEpubStylesheet(options));

        // Cover: the image itself plus the XHTML page that displays it. Both are
        // skipped when no cover is set or the file cannot be read, so a missing
        // cover can never fail an export.
        if (CoverMediaType(options.CoverImagePath) != null)
        {
            var ext = Path.GetExtension(options.CoverImagePath);
            await WriteBinaryEntryAsync(zip, $"OEBPS/cover{ext}", options.CoverImagePath);
            await WriteEntryAsync(zip, "OEBPS/cover.xhtml", GenerateCoverXhtml(options, ext));
        }

        // Title page
        if (options.IncludeTitlePage)
            await WriteEntryAsync(zip, "OEBPS/title.xhtml", GenerateTitlePageXhtml(options));

        // Matter pages get their own files, each carrying its kind as an
        // epub:type so a reader can style a copyright page as one.
        for (var i = 0; i < options.Matter.Count; i++)
            await WriteEntryAsync(zip, $"OEBPS/matter-{i + 1}.xhtml", GenerateMatterXhtml(options.Matter[i]));

        // Images used in the prose. Copied once each however many chapters
        // reference them, and named by position rather than by file name so a
        // path with spaces or non-ASCII cannot produce an unopenable package.
        var images = CollectProseImages(chapters);
        foreach (var (absolute, href) in images)
            await WriteBinaryEntryAsync(zip, $"OEBPS/{href}", absolute);

        // Chapter files
        for (var i = 0; i < chapters.Count; i++)
            await WriteEntryAsync(
                zip, $"OEBPS/chapter-{i + 1}.xhtml",
                GenerateChapterXhtml(chapters[i], options, i + 1, images));

        // Navigation
        await WriteEntryAsync(zip, "OEBPS/nav.xhtml", GenerateNavXhtml(chapters, options));
        await WriteEntryAsync(zip, "OEBPS/toc.ncx", GenerateTocNcx(chapters, options, bookId));
        await WriteEntryAsync(
            zip, "OEBPS/content.opf",
            GenerateContentOpf(chapters, options, bookId, modifiedDate, images));
    }

    private static async Task WriteEntryAsync(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        await using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        await writer.WriteAsync(content);
    }

    private static async Task WriteBinaryEntryAsync(ZipArchive zip, string path, string sourceFile)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        await using var target = entry.Open();
        await using var source = File.OpenRead(sourceFile);
        await source.CopyToAsync(target);
    }

    /// <summary>
    /// Reduces a writing-language setting to a BCP-47 primary subtag fit for
    /// <c>dc:language</c>. The quote-style presets carry typographic variants
    /// ("de-low", "de-guillemet") that are not language tags, so only the part
    /// before the first hyphen is kept. Falls back to "en" on anything unusable.
    /// </summary>
    public static string NormalizeLanguageTag(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return "en";

        var primary = language.Trim().Split('-')[0].ToLowerInvariant();
        return primary.Length is >= 2 and <= 3 && primary.All(char.IsAsciiLetter) ? primary : "en";
    }

    /// <summary>
    /// EPUB media type for a cover image, or null when there is no usable cover:
    /// no path set, the file is gone, or the extension is not one readers accept.
    /// Callers use null as "export without a cover" rather than as an error.
    /// </summary>
    internal static string? CoverMediaType(string? coverPath)
    {
        if (string.IsNullOrWhiteSpace(coverPath) || !File.Exists(coverPath))
            return null;

        return Path.GetExtension(coverPath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => null
        };
    }

    /// <summary>
    /// Full-bleed cover page. Uses svg preserveAspectRatio rather than a plain
    /// img so the image scales to the reader's screen without distortion, which
    /// is the shape Kindle and Apple Books both expect.
    /// </summary>
    private static string GenerateCoverXhtml(ExportOptions options, string extension)
    {
        // $$ raw string: interpolation holes are {{...}}, so the CSS braces below
        // stay literal instead of being parsed as format specifiers.
        return $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
            <head>
              <title>{{EscapeXml(options.Title)}}</title>
              <style type="text/css">
                body { margin: 0; padding: 0; text-align: center; }
                svg { height: 100%; width: 100%; }
              </style>
            </head>
            <body epub:type="cover">
              <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"
                   version="1.1" viewBox="0 0 100 160" preserveAspectRatio="xMidYMid meet">
                <image width="100" height="160" xlink:href="cover{{extension}}"/>
              </svg>
            </body>
            </html>
            """;
    }

    private static string GenerateEpubStylesheet(ExportOptions options)
    {
        var preset = options.ResolvePreset();
        // Appended rather than merged, so the writer's rules win by cascade
        // order - which is the only way they can override anything above.
        var extra = string.IsNullOrWhiteSpace(preset.EbookCss)
            ? string.Empty
            : "\n\n/* From your export layout */\n" + preset.EbookCss;
        // Concatenated rather than interpolated: the stylesheet below is full of
        // CSS braces, every one of which an interpolated string reads as a hole.
        return BaseEpubStylesheet + extra;
    }

    private const string BaseEpubStylesheet = """
            @page { margin: 1in; }

            body {
              font-family: Georgia, "Times New Roman", Times, serif;
              line-height: 1.5;
              margin: 1em;
              padding: 0;
            }

            h1.chapter-title {
              font-size: 1.5em;
              text-align: center;
              font-weight: bold;
              margin-top: 3em;
              margin-bottom: 2em;
            }

            p {
              margin-top: 0;
              margin-bottom: 0.8em;
              text-align: justify;
              orphans: 2;
              widows: 2;
            }

            p.scene-break {
              text-align: center;
              margin-top: 1.5em;
              margin-bottom: 1.5em;
            }

            p.chapter-subtitle {
              text-align: center;
              text-indent: 0;
              font-style: italic;
              margin-top: -0.6em;
              margin-bottom: 1.6em;
            }

            span.drop-cap {
              float: left;
              font-size: 3.2em;
              line-height: 0.85;
              padding-right: 0.06em;
            }

            span.lead-in {
              font-variant: small-caps;
            }

            p.prose-image {
              text-align: center;
              text-indent: 0;
              margin: 1.5em 0;
            }

            p.prose-image img {
              max-width: 100%;
            }

            h2, h3 {
              text-align: left;
              margin-top: 1.6em;
              margin-bottom: 0.6em;
            }

            blockquote {
              margin: 1em 2em;
              font-style: italic;
            }

            blockquote p {
              text-align: left;
              text-indent: 0;
            }

            /* Verse keeps its own line breaks and is never justified, which
               would stretch a short line across the page. */
            p.poetry {
              margin: 0 0 0.2em 2em;
              text-align: left;
              text-indent: 0;
              white-space: pre-wrap;
            }

            ul, ol {
              margin: 0.8em 0 0.8em 2em;
              padding: 0;
            }

            li {
              margin-bottom: 0.3em;
              text-align: left;
            }

            p.series, p.publisher {
              text-align: center;
              font-style: italic;
              margin: 0.4em 0;
            }

            div.title-page {
              text-align: center;
              padding-top: 30%;
            }

            div.title-page h1 {
              font-size: 2em;
              font-weight: bold;
              margin-bottom: 1em;
              text-indent: 0;
            }

            div.title-page p.author {
              font-size: 1.2em;
              font-style: italic;
              text-indent: 0;
            }
            """;

    /// <summary>
    /// The chapter's opening paragraph, with the initial set as a drop cap and
    /// the words after it in small capitals. Anything the splitter will not
    /// take - markup first, a number, an opening quotation mark - is returned
    /// untouched rather than wrapped into something odd.
    /// </summary>
    private static string OpenerXhtml(string content, bool isOpener, ExportPreset preset)
    {
        if (!isOpener || !preset.DropCap) return content;
        var split = SplitOpener(content, preset.LeadInSmallCapsWords);
        if (split == null) return content;

        var (initial, leadIn, tail) = split.Value;
        var lead = leadIn.Length > 0 ? $"<span class=\"lead-in\">{leadIn}</span>" : string.Empty;
        return $"<span class=\"drop-cap\">{initial}</span>{lead}{tail}";
    }

    /// <summary>
    /// The chapter's heading block: nothing at all when the chapter hides it,
    /// otherwise the title and, under it, whatever subtitle it carries.
    /// </summary>
    private static string ChapterHeadingXhtml(
        ChapterExportContent chapter, ExportPreset preset, int number)
    {
        if (chapter.HideHeading) return string.Empty;

        var heading = $"    <h1 class=\"chapter-title\">{EscapeXml(preset.ChapterHeading(number, chapter.Title))}</h1>";
        return string.IsNullOrWhiteSpace(chapter.Subtitle)
            ? heading
            : heading + $"\n    <p class=\"chapter-subtitle\">{EscapeXml(chapter.Subtitle)}</p>";
    }

    /// <summary>
    /// Every prose image in the book, mapped to the href it will have inside
    /// the package. Files that are missing are left out rather than producing
    /// a manifest entry pointing at nothing.
    /// </summary>
    private static Dictionary<string, string> CollectProseImages(
        List<ChapterExportContent> chapters)
    {
        var images = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scene in chapters.SelectMany(c => c.Scenes))
        {
            foreach (var block in ParseHtmlToBlocks(scene.HtmlContent, scene.Footnotes))
            {
                if (block.ImagePath == null || images.ContainsKey(block.ImagePath)) continue;
                if (!File.Exists(block.ImagePath)) continue;
                var ext = Path.GetExtension(block.ImagePath).ToLowerInvariant();
                images[block.ImagePath] = $"images/image-{images.Count + 1}{ext}";
            }
        }
        return images;
    }

    private static string GenerateChapterXhtml(
        ChapterExportContent chapter, ExportOptions options, int number,
        IReadOnlyDictionary<string, string>? images = null)
    {
        var preset = options.ResolvePreset();
        var bodyHtml = new StringBuilder();
        // Notes are collected as the chapter is laid out and written as asides
        // at its end, which is where a reading system looks for the target of
        // a noteref it has to pop up.
        var footnoteDefs = new List<string>();
        for (var si = 0; si < chapter.Scenes.Count; si++)
        {
            if (si > 0)
                bodyHtml.AppendLine(
                    $"    <p class=\"scene-break\">{EscapeXml(preset.SceneSeparator)}</p>");

            var scene = chapter.Scenes[si];
            // Off for a novel, where an ornament is the whole separator; on for
            // a collection, where the titles are how a reader navigates.
            if (preset.ShowSceneTitles && !string.IsNullOrWhiteSpace(scene.Title))
                bodyHtml.AppendLine(
                    $"    <h3 class=\"scene-title\" id=\"scene-{si + 1}\">"
                    + $"{EscapeXml(scene.Title)}</h3>");
            else if (options.EffectiveTocDepth >= 2)
                // Somewhere for a contents entry to land when the layout prints
                // no scene heading - which is every built-in layout. Without
                // this, choosing "chapters and scenes" would silently do
                // nothing on a novel.
                bodyHtml.AppendLine($"    <span id=\"scene-{si + 1}\"></span>");

            var isFirst = si == 0;
            var openList = ListKind.None;

            foreach (var block in ParseHtmlToBlocks(scene.HtmlContent, scene.Footnotes))
            {
                if (block.ImagePath != null)
                {
                    // An image whose file has gone is dropped rather than
                    // written as a broken reference the reader has to see.
                    if (images == null || !images.TryGetValue(block.ImagePath, out var href)) continue;
                    bodyHtml.AppendLine(
                        $"    <p class=\"prose-image\"><img src=\"{EscapeXml(href)}\" alt=\"{EscapeXml(block.ImageAlt)}\"/></p>");
                    continue;
                }

                var content = SegmentsToXhtml(block.Segments, footnoteDefs);

                if (block.List != openList)
                {
                    if (openList != ListKind.None)
                        bodyHtml.AppendLine(openList == ListKind.Number ? "    </ol>" : "    </ul>");
                    if (block.List != ListKind.None)
                        bodyHtml.AppendLine(block.List == ListKind.Number ? "    <ol>" : "    <ul>");
                    openList = block.List;
                }

                if (block.List != ListKind.None)
                {
                    bodyHtml.AppendLine($"      <li>{content}</li>");
                    isFirst = false;
                    continue;
                }

                // Real heading and blockquote elements rather than styled
                // paragraphs, so a reading system's navigation and its quote
                // styling both work on them.
                bodyHtml.AppendLine(block.StyleId switch
                {
                    "heading" => $"    <h2>{content}</h2>",
                    "subheading" => $"    <h3>{content}</h3>",
                    "blockquote" => $"    <blockquote><p>{content}</p></blockquote>",
                    "poetry" => $"    <p class=\"poetry\">{content}</p>",
                    _ => $"    <p{(isFirst ? " class=\"no-indent\"" : "")}>{OpenerXhtml(content, isFirst && si == 0, preset)}</p>"
                });
                isFirst = false;
            }

            if (openList != ListKind.None)
                bodyHtml.AppendLine(openList == ListKind.Number ? "    </ol>" : "    </ul>");
        }

        if (footnoteDefs.Count > 0)
        {
            bodyHtml.AppendLine("    <section epub:type=\"footnotes\" class=\"footnotes\">");
            for (var n = 1; n <= footnoteDefs.Count; n++)
                bodyHtml.AppendLine(
                    $"      <aside epub:type=\"footnote\" id=\"fn{n}\"><p>"
                    + $"<a href=\"#fnref{n}\">{n}.</a> {EscapeXml(footnoteDefs[n - 1])}</p></aside>");
            bodyHtml.AppendLine("    </section>");
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="{EscapeXml(options.Language)}">
            <head>
              <meta charset="UTF-8"/>
              <title>{EscapeXml(chapter.Title)}</title>
              <link rel="stylesheet" type="text/css" href="styles.css"/>
            </head>
            <body>
              <section epub:type="chapter">
                {ChapterHeadingXhtml(chapter, preset, number)}
            {bodyHtml}
              </section>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// EPUB 3 notes: the anchor becomes a <c>noteref</c> link, and the note
    /// itself an <c>aside</c> a reader can show as a popup. Numbering runs
    /// across the chapter file the notes are collected into.
    /// </summary>
    private static string SegmentsToXhtml(
        List<InlineSegment> segments, List<string>? footnoteDefs = null)
    {
        var sb = new StringBuilder();
        foreach (var seg in segments)
        {
            if (seg.FootnoteText != null)
            {
                if (footnoteDefs == null) continue;
                footnoteDefs.Add(seg.FootnoteText);
                var n = footnoteDefs.Count;
                sb.Append($"<a class=\"noteref\" epub:type=\"noteref\" id=\"fnref{n}\" href=\"#fn{n}\"><sup>{n}</sup></a>");
                continue;
            }
            var text = EscapeXml(seg.Text);
            if (seg.Strike) text = $"<s>{text}</s>";
            if (seg.Bold && seg.Italic)
                sb.Append($"<strong><em>{text}</em></strong>");
            else if (seg.Bold)
                sb.Append($"<strong>{text}</strong>");
            else if (seg.Italic)
                sb.Append($"<em>{text}</em>");
            else
                sb.Append(text);
        }
        return sb.ToString();
    }

    /// <summary>
    /// One matter page as XHTML. The kind rides along as an <c>epub:type</c> and
    /// a class, which is what lets a reader or a stylesheet set a copyright page
    /// differently from an epigraph without guessing from the heading.
    /// </summary>
    private static string GenerateMatterXhtml(MatterExportContent matter)
    {
        var cssClass = "matter matter-" + matter.Kind.ToLowerInvariant();
        var heading = string.IsNullOrEmpty(matter.Title)
            ? string.Empty
            : $"<h1 class=\"matter-title\">{EscapeXml(matter.Title)}</h1>";

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
            <head>
              <title>{EscapeXml(string.IsNullOrEmpty(matter.Title) ? matter.Kind : matter.Title)}</title>
              <link rel="stylesheet" type="text/css" href="styles.css"/>
            </head>
            <body class="{cssClass}" epub:type="{EpubTypeFor(matter.Kind)}">
              {heading}
              {matter.HtmlContent}
            </body>
            </html>
            """;
    }

    /// <summary>
    /// EPUB structural semantics vocabulary name for a matter kind. Kinds with
    /// no standard term fall back to "frontmatter"/"backmatter", which is always
    /// valid.
    /// </summary>
    private static string EpubTypeFor(string kind) => kind switch
    {
        "HalfTitle" => "halftitlepage",
        "TitlePage" => "titlepage",
        "Copyright" => "copyright-page",
        "Dedication" => "dedication",
        "Epigraph" => "epigraph",
        "TableOfContents" => "toc",
        "Foreword" => "foreword",
        "Preface" => "preface",
        "Prologue" => "prologue",
        "Epilogue" => "epilogue",
        "Afterword" => "afterword",
        "Acknowledgments" => "acknowledgments",
        _ => "frontmatter"
    };

    private static string GenerateTitlePageXhtml(ExportOptions options)
    {
        var authorHtml = !string.IsNullOrWhiteSpace(options.Author)
            ? $"<p class=\"author\">{EscapeXml(options.Author)}</p>"
            : "";

        // "Book Two of The Ravens" under the title, the way a printed series
        // states it. Only when there is a series to state.
        var seriesHtml = string.IsNullOrWhiteSpace(options.Publishing.SeriesName)
            ? ""
            : $"<p class=\"series\">{EscapeXml(SeriesLine(options.Publishing))}</p>";

        var publisherHtml = string.IsNullOrWhiteSpace(options.Publishing.Publisher)
            ? ""
            : $"<p class=\"publisher\">{EscapeXml(options.Publishing.Publisher.Trim())}</p>";

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="{EscapeXml(options.Language)}">
            <head>
              <meta charset="UTF-8"/>
              <title>{EscapeXml(options.Title)}</title>
              <link rel="stylesheet" type="text/css" href="styles.css"/>
            </head>
            <body>
              <div class="title-page" epub:type="titlepage">
                <h1>{EscapeXml(options.Title)}</h1>
                {seriesHtml}
                {authorHtml}
                {publisherHtml}
              </div>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// The series line for a title page. "The Ravens, Book 2" when there is a
    /// position, the series name alone when there is not - a book whose place in
    /// its series the writer has not decided still belongs to the series.
    /// </summary>
    internal static string SeriesLine(Models.PublishingMetadata publishing)
    {
        var name = publishing.SeriesName.Trim();
        var position = publishing.SeriesPosition.Trim();
        return position.Length > 0 ? $"{name}, Book {position}" : name;
    }

    private static string GenerateNavXhtml(List<ChapterExportContent> chapters, ExportOptions options)
    {
        var items = new StringBuilder();
        if (options.IncludeTitlePage)
            items.AppendLine(
                $"      <li><a href=\"title.xhtml\">{EscapeXml(Label(options, "titlePage", "Title Page"))}</a></li>");

        // Only matter the writer marked for the contents is listed. A copyright
        // page in the table of contents is a mistake, not a feature.
        void ListMatter(string placement)
        {
            for (var i = 0; i < options.Matter.Count; i++)
            {
                var matter = options.Matter[i];
                if (!matter.InTableOfContents
                    || !string.Equals(matter.Placement, placement, StringComparison.Ordinal))
                    continue;

                var label = string.IsNullOrEmpty(matter.Title)
                    ? SpaceCamelCase(matter.Kind)
                    : matter.Title;
                items.AppendLine($"      <li><a href=\"matter-{i + 1}.xhtml\">{EscapeXml(label)}</a></li>");
            }
        }

        ListMatter("Front");

        for (var i = 0; i < chapters.Count; i++)
        {
            var href = $"chapter-{i + 1}.xhtml";
            var nested = NavigableScenes(chapters[i], options);
            items.Append($"      <li><a href=\"{href}\">{EscapeXml(chapters[i].Title)}</a>");
            if (nested.Count > 0)
            {
                items.AppendLine();
                items.AppendLine("        <ol>");
                foreach (var (number, title) in nested)
                    items.AppendLine(
                        $"          <li><a href=\"{href}#scene-{number}\">{EscapeXml(title)}</a></li>");
                items.AppendLine("        </ol>");
                items.AppendLine("      </li>");
            }
            else
            {
                items.AppendLine("</li>");
            }
        }

        ListMatter("Back");

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="{EscapeXml(options.Language)}">
            <head>
              <meta charset="UTF-8"/>
              <title>{EscapeXml(TocHeading(options))}</title>
            </head>
            <body>
              <nav epub:type="toc" id="toc">
                <h1>{EscapeXml(TocHeading(options))}</h1>
                <ol>
            {items}
                </ol>
              </nav>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// The heading the contents page carries. English only when the writer said
    /// nothing, because a hardcoded "Table of Contents" on a German book is the
    /// one line of a book nobody can edit.
    /// </summary>
    private static string TocHeading(ExportOptions options)
        => string.IsNullOrWhiteSpace(options.TocTitle)
            ? Label(options, "tableOfContents", "Table of Contents")
            : options.TocTitle.Trim();

    /// <summary>
    /// The scenes of a chapter that belong in the contents, as (number, title).
    ///
    /// A titled scene qualifies whether or not the layout prints that title:
    /// writers name scenes in the binder for themselves, and those names are
    /// exactly what belongs in a contents list. An untitled scene is skipped -
    /// there is nothing to call it, and "Scene 3" is noise, not navigation.
    /// </summary>
    private static List<(int Number, string Title)> NavigableScenes(
        ChapterExportContent chapter, ExportOptions options)
    {
        var listed = new List<(int, string)>();
        if (options.EffectiveTocDepth < 2) return listed;

        for (var i = 0; i < chapter.Scenes.Count; i++)
            if (!string.IsNullOrWhiteSpace(chapter.Scenes[i].Title))
                listed.Add((i + 1, chapter.Scenes[i].Title));
        return listed;
    }

    private static string GenerateTocNcx(List<ChapterExportContent> chapters, ExportOptions options, string bookId)
    {
        var navPoints = new StringBuilder();
        var playOrder = 1;

        if (options.IncludeTitlePage)
        {
            navPoints.AppendLine($"""
                    <navPoint id="title" playOrder="{playOrder}">
                      <navLabel><text>{EscapeXml(Label(options, "titlePage", "Title Page"))}</text></navLabel>
                      <content src="title.xhtml"/>
                    </navPoint>
                """);
            playOrder++;
        }

        var deepest = 1;
        for (var i = 0; i < chapters.Count; i++)
        {
            var nested = NavigableScenes(chapters[i], options);
            var inner = new StringBuilder();
            foreach (var (number, title) in nested)
            {
                playOrder++;
                deepest = 2;
                inner.AppendLine($"""
                        <navPoint id="chapter-{i + 1}-scene-{number}" playOrder="{playOrder}">
                          <navLabel><text>{EscapeXml(title)}</text></navLabel>
                          <content src="chapter-{i + 1}.xhtml#scene-{number}"/>
                        </navPoint>
                    """);
            }

            navPoints.AppendLine($"""
                    <navPoint id="chapter-{i + 1}" playOrder="{playOrder - nested.Count}">
                      <navLabel><text>{EscapeXml(chapters[i].Title)}</text></navLabel>
                      <content src="chapter-{i + 1}.xhtml"/>
                    {inner.ToString().TrimEnd()}
                    </navPoint>
                """);
            playOrder++;
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE ncx PUBLIC "-//NISO//DTD ncx 2005-1//EN" "http://www.daisy.org/z3986/2005/ncx-2005-1.dtd">
            <ncx version="2005-1" xmlns="http://www.daisy.org/z3986/2005/ncx/">
              <head>
                <meta name="dtb:uid" content="{EscapeXml(bookId)}"/>
                <meta name="dtb:depth" content="{deepest}"/>
                <meta name="dtb:totalPageCount" content="0"/>
                <meta name="dtb:maxPageNumber" content="0"/>
              </head>
              <docTitle><text>{EscapeXml(options.Title)}</text></docTitle>
              <navMap>
            {navPoints}
              </navMap>
            </ncx>
            """;
    }

    /// <summary>
    /// What a reading system and a retailer need to know about how accessible
    /// this book is. Declared from what the file actually contains rather than
    /// asserted: a book with an undescribed picture says so, because claiming
    /// alt text that is not there is worse than claiming nothing.
    /// </summary>
    private static string AccessibilityMetadataXml(IReadOnlyDictionary<string, string>? images)
    {
        var hasImages = images is { Count: > 0 };
        var features = new List<string> { "structuralNavigation", "tableOfContents" };
        if (hasImages) features.Add("alternativeText");

        var lines = new List<string>
        {
            "    <meta property=\"schema:accessMode\">textual</meta>"
        };
        if (hasImages) lines.Add("    <meta property=\"schema:accessMode\">visual</meta>");
        lines.Add("    <meta property=\"schema:accessModeSufficient\">textual</meta>");
        foreach (var feature in features)
            lines.Add($"    <meta property=\"schema:accessibilityFeature\">{feature}</meta>");
        lines.Add("    <meta property=\"schema:accessibilityHazard\">none</meta>");
        lines.Add(
            "    <meta property=\"schema:accessibilitySummary\">"
            + (hasImages
                ? "Reflowable text with a table of contents. Images carry the descriptions the author wrote."
                : "Reflowable text with a table of contents, and no images to describe.")
            + "</meta>");
        return string.Join("\n", lines);
    }

    private static string GenerateContentOpf(
        List<ChapterExportContent> chapters,
        ExportOptions options,
        string bookId,
        string modifiedDate,
        IReadOnlyDictionary<string, string>? images = null)
    {
        var manifestItems = new StringBuilder();
        var spineItems = new StringBuilder();

        manifestItems.AppendLine("    <item id=\"css\" href=\"styles.css\" media-type=\"text/css\"/>");
        manifestItems.AppendLine("    <item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>");
        manifestItems.AppendLine("    <item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>");

        // Prose images are manifested but never spined: they are shown inside a
        // chapter, not read as a document of their own.
        var imageIndex = 0;
        foreach (var href in images?.Values ?? [])
        {
            imageIndex++;
            var media = CoverMediaType(href) ?? "image/png";
            manifestItems.AppendLine(
                $"    <item id=\"prose-image-{imageIndex}\" href=\"{href}\" media-type=\"{media}\"/>");
        }

        if (options.IncludeTitlePage)
        {
            manifestItems.AppendLine("    <item id=\"title\" href=\"title.xhtml\" media-type=\"application/xhtml+xml\"/>");
            spineItems.AppendLine("    <itemref idref=\"title\"/>");
        }

        // Front matter precedes the story in the spine; back matter follows it.
        void AppendMatter(string placement)
        {
            for (var i = 0; i < options.Matter.Count; i++)
            {
                if (!string.Equals(options.Matter[i].Placement, placement, StringComparison.Ordinal))
                    continue;

                var matterId = $"matter-{i + 1}";
                manifestItems.AppendLine(
                    $"    <item id=\"{matterId}\" href=\"{matterId}.xhtml\" media-type=\"application/xhtml+xml\"/>");
                spineItems.AppendLine($"    <itemref idref=\"{matterId}\"/>");
            }
        }

        AppendMatter("Front");

        for (var i = 0; i < chapters.Count; i++)
        {
            var id = $"chapter-{i + 1}";
            manifestItems.AppendLine($"    <item id=\"{id}\" href=\"{id}.xhtml\" media-type=\"application/xhtml+xml\"/>");
            spineItems.AppendLine($"    <itemref idref=\"{id}\"/>");
        }

        AppendMatter("Back");

        var authorXml = !string.IsNullOrWhiteSpace(options.Author)
            ? $"<dc:creator>{EscapeXml(options.Author)}</dc:creator>"
            : "";

        var coverMetaXml = "";
        if (CoverMediaType(options.CoverImagePath) is { } coverMedia)
        {
            var ext = Path.GetExtension(options.CoverImagePath);
            manifestItems.AppendLine(
                $"    <item id=\"cover-image\" href=\"cover{ext}\" media-type=\"{coverMedia}\" properties=\"cover-image\"/>");
            manifestItems.AppendLine(
                "    <item id=\"cover\" href=\"cover.xhtml\" media-type=\"application/xhtml+xml\"/>");
            // The cover page goes first in the spine, ahead of the title page.
            spineItems.Insert(0, "    <itemref idref=\"cover\" linear=\"no\"/>" + Environment.NewLine);
            // EPUB 2 style pointer: EPUB 3 uses properties="cover-image", but
            // Kindle and several retailers still key off this meta tag.
            coverMetaXml = "<meta name=\"cover\" content=\"cover-image\"/>";
        }

        var language = string.IsNullOrWhiteSpace(options.Language) ? "en" : options.Language.Trim();

        // An ISBN is the identifier a retailer keys on, so when there is one it
        // becomes the package's unique identifier rather than sitting beside a
        // generated UUID that means nothing outside this file.
        var publishing = options.Publishing;
        var isbn = publishing.NormalizedIsbn();
        var identifierXml = isbn.Length > 0
            ? $"<dc:identifier id=\"BookId\">urn:isbn:{EscapeXml(isbn)}</dc:identifier>"
            : $"<dc:identifier id=\"BookId\">{EscapeXml(bookId)}</dc:identifier>";

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package version="3.0" xmlns="http://www.idpf.org/2007/opf" unique-identifier="BookId">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:schema="http://schema.org/">
                {identifierXml}
                <dc:title>{EscapeXml(options.Title)}</dc:title>
                {authorXml}
                <dc:language>{EscapeXml(language)}</dc:language>
            {PublishingMetadataXml(publishing)}
                {coverMetaXml}
                <meta property="dcterms:modified">{modifiedDate}</meta>
            {AccessibilityMetadataXml(images)}
              </metadata>
              <manifest>
            {manifestItems}
              </manifest>
              <spine toc="ncx">
            {spineItems}
              </spine>
            </package>
            """;
    }

    /// <summary>
    /// The optional Dublin Core elements, plus the EPUB 3 collection markup that
    /// states a book's place in its series.
    ///
    /// Series is the one that is not a plain element: EPUB 3 expresses it as a
    /// <c>belongs-to-collection</c> meta refined by <c>collection-type</c> and
    /// <c>group-position</c>. Retailers that do not read it fall back to the
    /// title, which is why the book still reads correctly without it.
    /// </summary>
    private static string PublishingMetadataXml(Models.PublishingMetadata publishing)
    {
        if (!publishing.HasAny) return string.Empty;

        var sb = new StringBuilder();
        void Element(string name, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                sb.AppendLine($"    <dc:{name}>{EscapeXml(value.Trim())}</dc:{name}>");
        }

        Element("publisher", publishing.Publisher);
        Element("description", publishing.Description);
        Element("rights", publishing.Rights);
        Element("date", publishing.PublicationDate);

        foreach (var subject in publishing.Subjects)
            Element("subject", subject);

        if (!string.IsNullOrWhiteSpace(publishing.SeriesName))
        {
            sb.AppendLine(
                $"    <meta property=\"belongs-to-collection\" id=\"series\">{EscapeXml(publishing.SeriesName.Trim())}</meta>");
            sb.AppendLine(
                "    <meta refines=\"#series\" property=\"collection-type\">series</meta>");
            if (!string.IsNullOrWhiteSpace(publishing.SeriesPosition))
                sb.AppendLine(
                    $"    <meta refines=\"#series\" property=\"group-position\">{EscapeXml(publishing.SeriesPosition.Trim())}</meta>");
        }

        // An ISBN promoted to the package identifier is still worth stating as
        // its own element: some ingestion pipelines look for the scheme-tagged
        // form rather than parsing the urn.
        var isbn = publishing.NormalizedIsbn();
        if (isbn.Length > 0)
            sb.AppendLine(
                $"    <dc:identifier opf:scheme=\"ISBN\" xmlns:opf=\"http://www.idpf.org/2007/opf\">{EscapeXml(isbn)}</dc:identifier>");

        return sb.ToString().TrimEnd();
    }

    // ─── DOCX Export ─────────────────────────────────────────────────

    private static async Task ExportToDocxAsync(
        List<ChapterExportContent> chapters,
        ExportOptions options,
        string outputPath)
    {
        if (options.ResolvePreset().NormseitenGrid)
        {
            await WriteNormseitenDocxAsync(BuildManuscriptBlocks(chapters, options), options, outputPath);
            return;
        }

        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        var smf = options.ResolvePreset().ShunnHeader;

        // Comments travel with the scenes so an editor opening the file sees
        // them as Word comments they can reply to, rather than as inline prose.
        var exportComments = chapters
            .SelectMany(c => c.Scenes)
            .SelectMany(sc => sc.Comments)
            .ToList();
        var hasComments = exportComments.Count > 0;
        // Word keys comment parts by integer id; this maps Novalist's GUIDs onto
        // the position each comment occupies in the document-wide list.
        var commentIds = exportComments
            .Select((c, i) => (c.Id, i))
            .ToDictionary(pair => pair.Id, pair => pair.i, StringComparer.Ordinal);

        // Prose images: one media part each, with a relationship the drawing
        // runs point at. Collected before the package parts so both the content
        // types and the relationships can declare them.
        var proseImages = CollectProseImages(chapters);
        var imageRels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var imageParts = new List<(string Part, string Absolute)>();
        foreach (var (absolute, _) in proseImages)
        {
            var ext = Path.GetExtension(absolute).ToLowerInvariant().TrimStart('.');
            var part = $"media/image-{imageParts.Count + 1}.{ext}";
            imageRels[absolute] = $"rIdImage{imageParts.Count + 1}";
            imageParts.Add((part, absolute));
        }

        var imageExtensions = imageParts
            .Select(p => Path.GetExtension(p.Part).TrimStart('.').ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(ext => $"\n  <Default Extension=\"{ext}\" ContentType=\"{DocxImageContentType(ext)}\"/>")
            .ToList();

        // [Content_Types].xml
        var contentTypesExtra = smf
            ? "\n  <Override PartName=\"/word/header1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml\"/>"
            : "";

        await WriteEntryAsync(zip, "[Content_Types].xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>{string.Concat(imageExtensions)}
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
              <Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>
              <Override PartName="/word/footnotes.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml"/>{contentTypesExtra}{(hasComments ? "\n  <Override PartName=\"/word/comments.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml\"/>" : "")}
            </Types>
            """);

        // _rels/.rels
        await WriteEntryAsync(zip, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);

        // word/_rels/document.xml.rels
        var headerRel = smf
            ? "\n  <Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" Target=\"header1.xml\"/>"
            : "";

        await WriteEntryAsync(zip, "word/_rels/document.xml.rels", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
              <Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>
              <Relationship Id="rId5" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footnotes" Target="footnotes.xml"/>{ImageRelationshipsXml(imageParts)}{headerRel}{(hasComments ? "\n  <Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments\" Target=\"comments.xml\"/>" : "")}
            </Relationships>
            """);

        foreach (var (part, absolute) in imageParts)
            await WriteBinaryEntryAsync(zip, $"word/{part}", absolute);

        // word/styles.xml
        await WriteEntryAsync(zip, "word/styles.xml", GenerateDocxStyles(options));
        // Real Word list numbering rather than a literal bullet character typed
        // into the text, so an editor can renumber and restyle the list.
        await WriteEntryAsync(zip, "word/numbering.xml", GenerateDocxNumbering());

        if (hasComments)
            await WriteEntryAsync(zip, "word/comments.xml", GenerateDocxComments(exportComments));

        // SMF header
        if (smf)
        {
            var surname = !string.IsNullOrWhiteSpace(options.Author)
                ? options.Author.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last()
                : "";
            var shortTitle = options.Title.Length > 30 ? options.Title[..27] + "..." : options.Title;
            var headerText = $"{surname} / {shortTitle.ToUpperInvariant()}";

            await WriteEntryAsync(zip, "word/header1.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:p>
                    <w:pPr><w:jc w:val="right"/></w:pPr>
                    <w:r><w:rPr><w:rFonts w:ascii="Courier New" w:hAnsi="Courier New"/><w:sz w:val="20"/></w:rPr><w:t xml:space="preserve">{EscapeXml(headerText)} / </w:t></w:r>
                    <w:r><w:rPr><w:rFonts w:ascii="Courier New" w:hAnsi="Courier New"/><w:sz w:val="20"/></w:rPr><w:fldChar w:fldCharType="begin"/></w:r>
                    <w:r><w:rPr><w:rFonts w:ascii="Courier New" w:hAnsi="Courier New"/><w:sz w:val="20"/></w:rPr><w:instrText> PAGE </w:instrText></w:r>
                    <w:r><w:rPr><w:rFonts w:ascii="Courier New" w:hAnsi="Courier New"/><w:sz w:val="20"/></w:rPr><w:fldChar w:fldCharType="end"/></w:r>
                  </w:p>
                </w:hdr>
                """);
        }

        // Build document body. Notes are collected as it is laid out; the part
        // they live in can only be written once their numbers are known.
        var body = new StringBuilder();
        var footnoteDefs = new List<string>();

        if (options.IncludeTitlePage)
        {
            body.Append($"<w:p><w:pPr><w:pStyle w:val=\"Title\"/></w:pPr><w:r><w:t>{EscapeXml(options.Title)}</w:t></w:r></w:p>");
            if (!string.IsNullOrWhiteSpace(options.Author))
                body.Append($"<w:p><w:pPr><w:pStyle w:val=\"Subtitle\"/></w:pPr><w:r><w:t>{EscapeXml(options.Author)}</w:t></w:r></w:p>");
        }

        // Front matter, each on its own page, before the story.
        foreach (var matter in options.Matter.Where(m => m.Placement == "Front"))
            body.Append(BuildDocxMatter(matter, options.IncludeTitlePage || body.Length > 0));

        var matterPrecedesChapters = options.Matter.Any(m => m.Placement == "Front");

        for (var i = 0; i < chapters.Count; i++)
        {
            var chapter = chapters[i];
            var needsPageBreak = i > 0 || options.IncludeTitlePage || matterPrecedesChapters;

            // Chapter heading
            var docxHeading = options.ResolvePreset().ChapterHeading(i + 1, chapter.Title);
            var breakBefore = needsPageBreak ? "<w:pageBreakBefore/>" : string.Empty;
            if (chapter.HideHeading)
            {
                // The page still turns; only the words are gone.
                if (needsPageBreak)
                    body.Append("<w:p><w:pPr><w:pageBreakBefore/></w:pPr></w:p>");
            }
            else
            {
                body.Append($"<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/>{breakBefore}</w:pPr><w:r><w:t>{EscapeXml(docxHeading)}</w:t></w:r></w:p>");
                if (!string.IsNullOrWhiteSpace(chapter.Subtitle))
                    body.Append($"<w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr><w:r><w:rPr><w:i/></w:rPr><w:t>{EscapeXml(chapter.Subtitle)}</w:t></w:r></w:p>");
            }

            // Scenes
            for (var si = 0; si < chapter.Scenes.Count; si++)
            {
                // Scene break between scenes
                if (si > 0)
                {
                    body.Append($"<w:p><w:pPr><w:pStyle w:val=\"SceneBreak\"/></w:pPr><w:r><w:t>{SceneBreakText}</w:t></w:r></w:p>");
                }

                var scene = chapter.Scenes[si];
                var blocks = ParseHtmlToBlocks(scene.HtmlContent, scene.Footnotes);
                var isFirstPara = si == 0;
                // One anchor per comment: a phrase repeated across paragraphs
                // would otherwise mark every occurrence.
                var anchored = new HashSet<string>(StringComparer.Ordinal);

                foreach (var block in blocks)
                {
                    if (block.ImagePath != null)
                    {
                        if (imageRels.TryGetValue(block.ImagePath, out var relId))
                            body.Append(DocxImageParagraph(block.ImagePath, relId, block.ImageAlt));
                        continue;
                    }

                    var para = block.Segments;

                    // The chapter's opening paragraph, when the layout asks for
                    // a drop cap: Word wants the initial in a framed paragraph
                    // of its own, with the rest following as normal text.
                    var docxPreset = options.ResolvePreset();
                    if (isFirstPara && si == 0 && docxPreset.DropCap && block.StyleId == null
                        && block.List == ListKind.None)
                    {
                        var opener = SplitOpener(
                            string.Concat(para.Select(seg => seg.Text)),
                            docxPreset.LeadInSmallCapsWords);
                        if (opener != null)
                        {
                            body.Append(DocxDropCapParagraphs(opener.Value, docxPreset));
                            isFirstPara = false;
                            continue;
                        }
                    }

                    var runs = SegmentsToDocxRuns(para, footnoteDefs);
                    var paragraphXml =
                        $"<w:p><w:pPr>{DocxParagraphProperties(block, isFirstPara)}</w:pPr>{runs}</w:p>";

                    // Skipped when this scene has none, even if other scenes do.
                    if (scene.Comments.Count > 0)
                    {
                        var plain = string.Concat(para.Select(seg => seg.Text));
                        paragraphXml = WrapDocxCommentRanges(
                            paragraphXml, plain, commentIds, scene, anchored);
                    }

                    body.Append(paragraphXml);
                    isFirstPara = false;
                }
            }
        }

        // Back matter, after the story.
        foreach (var matter in options.Matter.Where(m => m.Placement == "Back"))
            body.Append(BuildDocxMatter(matter, true));

        // Section properties
        var sectPrHeader = smf
            ? "<w:headerReference w:type=\"default\" r:id=\"rId2\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"/>"
            : "";

        // Written after the body: the notes are only known once it is laid out.
        await WriteEntryAsync(zip, "word/footnotes.xml", GenerateDocxFootnotes(footnoteDefs));

        await WriteEntryAsync(zip, "word/document.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                {body}
                <w:sectPr>
                  {sectPrHeader}
                  <w:pgSz w:w="12240" w:h="15840"/>
                  <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/>
                </w:sectPr>
              </w:body>
            </w:document>
            """);
    }

    /// <summary>
    /// One matter page as DOCX paragraphs. Kinds without a heading render as
    /// body text only, which is how a dedication or a copyright page is set.
    /// </summary>
    private static string BuildDocxMatter(MatterExportContent matter, bool pageBreakBefore)
    {
        var builder = new StringBuilder();
        var breakBefore = pageBreakBefore ? "<w:pageBreakBefore/>" : string.Empty;

        if (!string.IsNullOrEmpty(matter.Title))
        {
            builder.Append(
                $"<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/>{breakBefore}</w:pPr>"
                + $"<w:r><w:t>{EscapeXml(matter.Title)}</w:t></w:r></w:p>");
            breakBefore = string.Empty;
        }

        var first = true;
        foreach (var para in ParseHtmlToParagraphs(matter.HtmlContent))
        {
            // The break rides on the first paragraph when there is no heading.
            var pPr = first && breakBefore.Length > 0
                ? $"<w:pStyle w:val=\"NoIndent\"/>{breakBefore}"
                : "<w:pStyle w:val=\"BodyText\"/>";
            builder.Append($"<w:p><w:pPr>{pPr}</w:pPr>{SegmentsToDocxRuns(para)}</w:p>");
            first = false;
        }

        return builder.ToString();
    }

    /// <summary>
    /// The <c>word/comments.xml</c> part. Ids are the comment's index rather
    /// than Novalist's GUID: the schema requires an integer, and the id only has
    /// to match the anchors in the same document.
    /// </summary>
    internal static string GenerateDocxComments(IReadOnlyList<SceneExportComment> comments)
    {
        var body = new StringBuilder();
        for (var i = 0; i < comments.Count; i++)
        {
            var comment = comments[i];
            body.Append($"<w:comment w:id=\"{i}\" w:author=\"{EscapeXml(DocxCommentAuthor)}\" ");
            body.Append($"w:initials=\"N\" w:date=\"{comment.CreatedAt.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}\">");
            foreach (var line in comment.Text.Replace("\r\n", "\n").Split('\n'))
                body.Append($"<w:p><w:r><w:t xml:space=\"preserve\">{EscapeXml(line)}</w:t></w:r></w:p>");
            body.Append("</w:comment>");
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:comments xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              {body}
            </w:comments>
            """;
    }

    /// <summary>
    /// Author shown on exported comments. Novalist does not model an author
    /// identity, and inventing one from the OS user would put a real name into a
    /// file the writer may be sending to a stranger.
    /// </summary>
    private const string DocxCommentAuthor = "Novalist";

    /// <summary>
    /// Wraps a scene's paragraph in comment range markers when a comment's
    /// anchor text appears in it. Anchoring by text rather than by offset is
    /// deliberate: the export pipeline reflows paragraphs, so a stored offset
    /// would land in the wrong place.
    /// </summary>
    private static string WrapDocxCommentRanges(
        string paragraphXml,
        string paragraphText,
        IReadOnlyDictionary<string, int> commentIds,
        SceneExportContent scene,
        HashSet<string> alreadyAnchored)
    {
        var prefix = new StringBuilder();
        var suffix = new StringBuilder();

        foreach (var comment in scene.Comments)
        {
            if (alreadyAnchored.Contains(comment.Id))
                continue;
            if (string.IsNullOrWhiteSpace(comment.AnchorText)
                || !paragraphText.Contains(comment.AnchorText, StringComparison.Ordinal))
                continue;

            // Every scene comment is in the map: it is built from the same
            // scenes, so there is no not-found case to handle.
            var id = commentIds[comment.Id];
            alreadyAnchored.Add(comment.Id);
            prefix.Append($"<w:commentRangeStart w:id=\"{id}\"/>");
            suffix.Append($"<w:commentRangeEnd w:id=\"{id}\"/>");
            suffix.Append($"<w:r><w:commentReference w:id=\"{id}\"/></w:r>");
        }

        if (prefix.Length == 0)
            return paragraphXml;

        // The markers sit inside the paragraph, after its properties. The caller
        // always builds the paragraph with a w:pPr, so the marker is present.
        var insertAt = paragraphXml.IndexOf("</w:pPr>", StringComparison.Ordinal)
            + "</w:pPr>".Length;

        return paragraphXml[..insertAt]
            + prefix
            + paragraphXml[insertAt..].Replace("</w:p>", suffix + "</w:p>");
    }

    /// <summary>
    /// Word's own drop cap: the initial sits in a framed paragraph the text
    /// wraps around, and the rest of the opener follows as ordinary prose with
    /// its lead-in words in small capitals.
    /// </summary>
    private static string DocxDropCapParagraphs(
        (string Initial, string LeadIn, string Tail) opener, ExportPreset preset)
    {
        var size = (int)Math.Round(preset.BodyFontSizePt * 2 * 3);  // half-points, three lines
        var initial =
            "<w:p><w:pPr><w:framePr w:dropCap=\"drop\" w:lines=\"3\" w:wrap=\"around\""
            + " w:vAnchor=\"text\" w:hAnchor=\"text\"/><w:spacing w:line=\"640\" w:lineRule=\"exact\"/>"
            + $"</w:pPr><w:r><w:rPr><w:sz w:val=\"{size}\"/></w:rPr>"
            + $"<w:t xml:space=\"preserve\">{EscapeXml(opener.Initial)}</w:t></w:r></w:p>";

        var lead = opener.LeadIn.Length == 0
            ? string.Empty
            : $"<w:r><w:rPr><w:smallCaps/></w:rPr><w:t xml:space=\"preserve\">{EscapeXml(opener.LeadIn)}</w:t></w:r>";

        return initial
            + $"<w:p>{lead}<w:r><w:t xml:space=\"preserve\">{EscapeXml(opener.Tail)}</w:t></w:r></w:p>";
    }

    /// <summary>The content type Word expects for an image part.</summary>
    private static string DocxImageContentType(string extension) => extension switch
    {
        "jpg" or "jpeg" => "image/jpeg",
        "gif" => "image/gif",
        "bmp" => "image/bmp",
        "webp" => "image/webp",
        _ => "image/png"
    };

    /// <summary>One relationship per image part, for the document's rels file.</summary>
    private static string ImageRelationshipsXml(List<(string Part, string Absolute)> parts)
        => string.Concat(parts.Select((p, i) =>
            "\n  <Relationship Id=\"rIdImage" + (i + 1) + "\" Type=\""
            + "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"
            + "\" Target=\"" + p.Part + "\"/>"));

    /// <summary>
    /// One centred paragraph holding an inline drawing. Word measures in EMUs
    /// (914400 to the inch), so the pixel size is converted at 96dpi and capped
    /// at six inches - an image wider than the page is a broken document rather
    /// than a large picture.
    /// </summary>
    private static string DocxImageParagraph(string path, string relId, string alt)
    {
        var (pixelWidth, pixelHeight) = ImageSize(path);
        var naturalWidth = Math.Max(0.1, pixelWidth / 96.0);
        var widthInches = Math.Min(6.0, naturalWidth);
        var heightInches = pixelHeight / 96.0 * (widthInches / naturalWidth);
        var cx = (long)(widthInches * 914400);
        var cy = (long)(heightInches * 914400);
        var description = EscapeXml(alt);
        var id = Math.Abs(relId.GetHashCode()) % 100000 + 1;

        return "<w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr><w:r><w:drawing>"
            + "<wp:inline xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\""
            + " distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">"
            + $"<wp:extent cx=\"{cx}\" cy=\"{cy}\"/>"
            + $"<wp:docPr id=\"{id}\" name=\"Image {id}\" descr=\"{description}\"/>"
            + "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">"
            + "<a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">"
            + "<pic:pic xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">"
            + $"<pic:nvPicPr><pic:cNvPr id=\"0\" name=\"Image {id}\" descr=\"{description}\"/>"
            + "<pic:cNvPicPr/></pic:nvPicPr>"
            + "<pic:blipFill><a:blip xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\""
            + $" r:embed=\"{relId}\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>"
            + $"<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm>"
            + "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>"
            + "</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>";
    }

    /// <summary>
    /// An image's pixel size, read from the file's own header. PNG and JPEG
    /// cover what the editor accepts; anything else falls back to a square,
    /// which lays out sensibly even when it is not exact.
    /// </summary>
    internal static (int Width, int Height) ImageSize(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            var signature = reader.ReadBytes(8);

            // PNG: width and height are big-endian ints at byte 16.
            if (signature.Length == 8 && signature[0] == 0x89 && signature[1] == 0x50)
            {
                stream.Position = 16;
                var w = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(reader.ReadBytes(4));
                var h = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(reader.ReadBytes(4));
                return (w, h);
            }

            // JPEG: walk the segments to the start-of-frame, which carries the size.
            if (signature.Length >= 2 && signature[0] == 0xFF && signature[1] == 0xD8)
            {
                stream.Position = 2;
                while (stream.Position < stream.Length - 8)
                {
                    if (reader.ReadByte() != 0xFF) continue;
                    var marker = reader.ReadByte();
                    var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
                    if (marker is >= 0xC0 and <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                    {
                        reader.ReadByte();
                        var h = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
                        var w = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
                        return (w, h);
                    }
                    stream.Position += length - 2;
                }
            }
        }
        catch (Exception e) when (e is IOException or EndOfStreamException
            or ArgumentException or UnauthorizedAccessException)
        {
            // Unreadable is not fatal: the fallback below still lays out.
        }
        return (600, 600);
    }

    /// <summary>
    /// The styles part of a reference DOCX, or null when there is not a usable
    /// one. A publisher's house style arrives as a styled Word file, not as a
    /// list of settings, and reapplying it by hand after every export is how a
    /// submission goes out in the wrong font.
    /// </summary>
    internal static string? ReadReferenceStyles(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            using var archive = ZipFile.OpenRead(path);
            var entry = archive.GetEntry("word/styles.xml");
            if (entry == null) return null;

            using var reader = new StreamReader(entry.Open());
            var xml = reader.ReadToEnd();
            // A part without the wordprocessing root is not a styles part, and
            // writing it would produce a file Word refuses to open at all.
            return xml.Contains("<w:styles", StringComparison.Ordinal) ? xml : null;
        }
        catch (Exception ex) when (
            ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // A reference document that cannot be read is a reason to fall back
            // to ours, never a reason to fail the export the writer asked for.
            return null;
        }
    }

    private static string GenerateDocxStyles(ExportOptions options)
    {
        if (ReadReferenceStyles(options.ReferenceDocPath) is { } borrowed) return borrowed;

        var smf = options.ResolvePreset().ShunnHeader;
        var fontFamily = smf ? "Courier New" : "Georgia";
        var fontSize = "24";
        var lineSpacing = smf ? "480" : "360";
        var bodyIndent = smf ? "<w:ind w:firstLine=\"720\"/>" : "<w:spacing w:after=\"160\"/>";
        var noIndentSpacing = smf ? "" : "<w:spacing w:after=\"160\"/>";

        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:docDefaults>
                <w:rPrDefault>
                  <w:rPr>
                    <w:rFonts w:ascii="{fontFamily}" w:hAnsi="{fontFamily}" w:eastAsia="{fontFamily}" w:cs="{fontFamily}"/>
                    <w:sz w:val="{fontSize}"/>
                    <w:szCs w:val="{fontSize}"/>
                    <w:lang w:val="en-US"/>
                  </w:rPr>
                </w:rPrDefault>
                <w:pPrDefault>
                  <w:pPr>
                    <w:spacing w:line="{lineSpacing}" w:lineRule="auto"/>
                  </w:pPr>
                </w:pPrDefault>
              </w:docDefaults>

              <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
                <w:name w:val="Normal"/>
              </w:style>

              <w:style w:type="paragraph" w:styleId="Title">
                <w:name w:val="Title"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:jc w:val="center"/>
                  <w:spacing w:before="4800" w:after="240"/>
                </w:pPr>
                <w:rPr>
                  <w:sz w:val="52"/>
                  <w:szCs w:val="52"/>
                  <w:b/>
                  <w:bCs/>
                </w:rPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="Subtitle">
                <w:name w:val="Subtitle"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:jc w:val="center"/>
                  <w:spacing w:before="240"/>
                </w:pPr>
                <w:rPr>
                  <w:sz w:val="32"/>
                  <w:szCs w:val="32"/>
                  <w:i/>
                  <w:iCs/>
                </w:rPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="Heading1">
                <w:name w:val="heading 1"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:jc w:val="center"/>
                  <w:spacing w:before="1440" w:after="720"/>
                </w:pPr>
                <w:rPr>
                  <w:sz w:val="36"/>
                  <w:szCs w:val="36"/>
                  <w:b/>
                  <w:bCs/>
                </w:rPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="BodyText">
                <w:name w:val="Body Text"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  {bodyIndent}
                  <w:jc w:val="both"/>
                </w:pPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="NoIndent">
                <w:name w:val="No Indent"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:ind w:firstLine="0"/>
                  {noIndentSpacing}
                  <w:jc w:val="both"/>
                </w:pPr>
              </w:style>

              <w:style w:type="character" w:styleId="FootnoteReference">
                <w:name w:val="footnote reference"/>
                <w:rPr><w:vertAlign w:val="superscript"/></w:rPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="FootnoteText">
                <w:name w:val="footnote text"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr><w:spacing w:line="240" w:lineRule="auto" w:after="0"/></w:pPr>
                <w:rPr><w:sz w:val="20"/></w:rPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="SceneBreak">
                <w:name w:val="Scene Break"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:jc w:val="center"/>
                  <w:spacing w:before="360" w:after="360"/>
                </w:pPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="Heading2">
                <w:name w:val="heading 2"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:outlineLvl w:val="1"/>
                  <w:ind w:firstLine="0"/>
                  <w:spacing w:before="360" w:after="180"/>
                  <w:keepNext/>
                </w:pPr>
                <w:rPr><w:b/><w:sz w:val="30"/><w:szCs w:val="30"/></w:rPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="Heading3">
                <w:name w:val="heading 3"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:outlineLvl w:val="2"/>
                  <w:ind w:firstLine="0"/>
                  <w:spacing w:before="280" w:after="140"/>
                  <w:keepNext/>
                </w:pPr>
                <w:rPr><w:b/><w:sz w:val="26"/><w:szCs w:val="26"/></w:rPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="Quote">
                <w:name w:val="Quote"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:ind w:left="720" w:right="720" w:firstLine="0"/>
                  <w:spacing w:before="180" w:after="180"/>
                </w:pPr>
                <w:rPr><w:i/></w:rPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="Verse">
                <w:name w:val="Verse"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:ind w:left="720" w:firstLine="0"/>
                  <w:jc w:val="left"/>
                </w:pPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="ListParagraph">
                <w:name w:val="List Paragraph"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:ind w:left="720" w:firstLine="0"/>
                  <w:contextualSpacing/>
                  <w:jc w:val="left"/>
                </w:pPr>
              </w:style>
            </w:styles>
            """;
    }

    /// <summary>
    /// Two list definitions, a bullet and a decimal, each with the nine levels
    /// Word expects. Novalist only ever emits level zero, but a definition
    /// missing its deeper levels makes Word treat the whole part as corrupt.
    /// </summary>
    /// <summary>
    /// The <c>word/footnotes.xml</c> part. The first two entries are the
    /// separator and continuation notes Word expects to exist; the writer's own
    /// notes follow from id 2.
    /// </summary>
    private static string GenerateDocxFootnotes(List<string> notes)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < notes.Count; i++)
        {
            sb.Append($"<w:footnote w:id=\"{i + 2}\"><w:p><w:pPr><w:pStyle w:val=\"FootnoteText\"/></w:pPr>")
              .Append("<w:r><w:rPr><w:rStyle w:val=\"FootnoteReference\"/></w:rPr><w:footnoteRef/></w:r>")
              .Append($"<w:r><w:t xml:space=\"preserve\"> {EscapeXml(notes[i])}</w:t></w:r></w:p></w:footnote>");
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:footnotes xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:footnote w:id="0" w:type="separator"><w:p><w:r><w:separator/></w:r></w:p></w:footnote>
              <w:footnote w:id="1" w:type="continuationSeparator"><w:p><w:r><w:continuationSeparator/></w:r></w:p></w:footnote>
              {sb}
            </w:footnotes>
            """;
    }

    private static string GenerateDocxNumbering()
    {
        var bulletLevels = new StringBuilder();
        var decimalLevels = new StringBuilder();
        for (var level = 0; level < 9; level++)
        {
            var indent = 720 * (level + 1);
            bulletLevels.Append($"""
                  <w:lvl w:ilvl="{level}">
                    <w:start w:val="1"/>
                    <w:numFmt w:val="bullet"/>
                    <w:lvlText w:val="&#8226;"/>
                    <w:lvlJc w:val="left"/>
                    <w:pPr><w:ind w:left="{indent}" w:hanging="360"/></w:pPr>
                    <w:rPr><w:rFonts w:ascii="Symbol" w:hAnsi="Symbol" w:hint="default"/></w:rPr>
                  </w:lvl>
                """);
            decimalLevels.Append($"""
                  <w:lvl w:ilvl="{level}">
                    <w:start w:val="1"/>
                    <w:numFmt w:val="decimal"/>
                    <w:lvlText w:val="%{level + 1}."/>
                    <w:lvlJc w:val="left"/>
                    <w:pPr><w:ind w:left="{indent}" w:hanging="360"/></w:pPr>
                  </w:lvl>
                """);
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:abstractNum w:abstractNumId="0">
                <w:multiLevelType w:val="hybridMultilevel"/>
            {bulletLevels}
              </w:abstractNum>
              <w:abstractNum w:abstractNumId="1">
                <w:multiLevelType w:val="hybridMultilevel"/>
            {decimalLevels}
              </w:abstractNum>
              <w:num w:numId="{DocxBulletNumId}"><w:abstractNumId w:val="0"/></w:num>
              <w:num w:numId="{DocxNumberNumId}"><w:abstractNumId w:val="1"/></w:num>
            </w:numbering>
            """;
    }

    /// <summary>The numId a bulleted list paragraph points at.</summary>
    private const int DocxBulletNumId = 1;

    /// <summary>The numId a numbered list paragraph points at.</summary>
    private const int DocxNumberNumId = 2;

    /// <summary>The paragraph properties for one exported block: its Word style
    /// and, for a list item, the numbering it belongs to.</summary>
    private static string DocxParagraphProperties(ExportBlock block, bool firstInScene)
    {
        if (block.List != ListKind.None)
        {
            var numId = block.List == ListKind.Number ? DocxNumberNumId : DocxBulletNumId;
            return "<w:pStyle w:val=\"ListParagraph\"/>"
                   + $"<w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"{numId}\"/></w:numPr>";
        }

        var style = block.StyleId switch
        {
            "heading" => "Heading2",
            "subheading" => "Heading3",
            "blockquote" => "Quote",
            "poetry" => "Verse",
            // The first paragraph of a scene is not indented; typographic
            // convention, and what the exporter already did.
            _ => firstInScene ? "NoIndent" : "BodyText"
        };
        return $"<w:pStyle w:val=\"{style}\"/>";
    }

    /// <summary>
    /// Word runs. A footnote segment becomes a real <c>w:footnoteReference</c>,
    /// which is what lets an editor see it at the bottom of the page and Word
    /// renumber it - the manual has promised this for a long time while the
    /// exporter appended a paragraph of plain text instead.
    /// </summary>
    private static string SegmentsToDocxRuns(
        List<InlineSegment> segments, List<string>? footnoteDefs = null)
    {
        var sb = new StringBuilder();
        foreach (var seg in segments)
        {
            if (seg.FootnoteText != null)
            {
                if (footnoteDefs == null) continue;
                footnoteDefs.Add(seg.FootnoteText);
                // Ids 0 and 1 are the separator and continuation notes Word
                // requires, so the writer's notes start at 2.
                var id = footnoteDefs.Count + 1;
                sb.Append("<w:r><w:rPr><w:rStyle w:val=\"FootnoteReference\"/></w:rPr>")
                  .Append($"<w:footnoteReference w:id=\"{id}\"/></w:r>");
                continue;
            }
            var rPr = "";
            if (seg.Bold || seg.Italic || seg.Strike)
            {
                var b = seg.Bold ? "<w:b/><w:bCs/>" : "";
                var i = seg.Italic ? "<w:i/><w:iCs/>" : "";
                var s = seg.Strike ? "<w:strike/>" : "";
                rPr = $"<w:rPr>{b}{i}{s}</w:rPr>";
            }
            sb.Append($"<w:r>{rPr}<w:t xml:space=\"preserve\">{EscapeXml(seg.Text)}</w:t></w:r>");
        }
        return sb.ToString();
    }

    // ─── Normseiten (German standard pages) ──────────────────────────

    /// <summary>
    /// Turns editor HTML into Normseite blocks. Paragraphs carrying the
    /// editor's heading / subheading style become headings; everything else is
    /// body text, with a blank line between paragraphs.
    /// </summary>
    public static List<NormseitenBlock> HtmlToNormseitenBlocks(string html)
    {
        var blocks = new List<NormseitenBlock>();
        if (string.IsNullOrWhiteSpace(html)) return blocks;

        var matches = ParagraphAnyRegex().Matches(html);
        if (matches.Count == 0)
        {
            var stripped = StripHtml(html);
            if (!string.IsNullOrWhiteSpace(stripped))
                blocks.Add(NormseitenBlock.Body(stripped));
            return blocks;
        }

        foreach (Match match in matches)
        {
            var styleId = ExtractStyleClass(match.Groups[1].Value);
            var text = string.Concat(ParseInlineFormatting(match.Groups[2].Value).Select(s => s.Text));
            if (string.IsNullOrWhiteSpace(text))
            {
                blocks.Add(NormseitenBlock.Blank());
                continue;
            }
            blocks.Add(styleId is "heading" or "subheading"
                ? NormseitenBlock.Heading(text)
                : NormseitenBlock.Body(text));
            blocks.Add(NormseitenBlock.Blank());
        }

        return blocks;
    }

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
    /// One attribute's value out of a tag's attribute text, whichever order the
    /// attributes are written in. Empty when the attribute is absent, which for
    /// alt text means decorative rather than undescribed.
    /// </summary>
    private static string HtmlAttribute(string attrs, string name)
    {
        foreach (Match attr in HtmlAttributeRegex().Matches(attrs))
        {
            if (string.Equals(attr.Groups["name"].Value, name, StringComparison.OrdinalIgnoreCase))
                return attr.Groups["value"].Value;
        }
        return string.Empty;
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
            blocks.Add(NormseitenBlock.Heading(normseitenPreset.ChapterHeading(ci + 1, chapter.Title)));
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

    private static int CmToTwips(double cm) => (int)Math.Round(cm / 2.54 * 1440);

    /// <summary>
    /// Writes a DOCX laid out on the Normseite grid: every line hard-wrapped to
    /// the preset's column count, a forced page break every N lines, and a
    /// running header carrying the title and "Seite x von y".
    /// </summary>
    public static async Task WriteNormseitenDocxAsync(
        IReadOnlyList<NormseitenBlock> blocks,
        ExportOptions options,
        string outputPath)
    {
        var preset = options.ResolvePreset();
        var lines = NormseitenRenderer.RenderLines(blocks, preset.GridColumns);
        var metrics = NormseitenRenderer.Measure(lines, preset.GridLines);
        var pages = Math.Max(1, metrics.Pages);

        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        await WriteEntryAsync(zip, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
              <Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
            </Types>
            """);

        await WriteEntryAsync(zip, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);

        await WriteEntryAsync(zip, "word/_rels/document.xml.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>
            </Relationships>
            """);

        var font = preset.BodyFontFamily;
        var halfPoints = (int)Math.Round(preset.BodyFontSizePt * 2);
        // Word measures exact line spacing in twentieths of a point.
        var exactLine = (int)Math.Round((preset.LineHeightPt > 0 ? preset.LineHeightPt : preset.BodyFontSizePt * 2) * 20);

        await WriteEntryAsync(zip, "word/styles.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:docDefaults>
                <w:rPrDefault>
                  <w:rPr>
                    <w:rFonts w:ascii="{font}" w:hAnsi="{font}" w:eastAsia="{font}" w:cs="{font}"/>
                    <w:sz w:val="{halfPoints}"/>
                    <w:szCs w:val="{halfPoints}"/>
                  </w:rPr>
                </w:rPrDefault>
                <w:pPrDefault>
                  <w:pPr>
                    <w:spacing w:before="0" w:after="0" w:line="{exactLine}" w:lineRule="exact"/>
                  </w:pPr>
                </w:pPrDefault>
              </w:docDefaults>
              <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
                <w:name w:val="Normal"/>
              </w:style>
              <w:style w:type="paragraph" w:styleId="Header">
                <w:name w:val="header"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:spacing w:before="0" w:after="0" w:line="240" w:lineRule="auto"/>
                </w:pPr>
                <w:rPr>
                  <w:sz w:val="20"/>
                  <w:szCs w:val="20"/>
                </w:rPr>
              </w:style>
            </w:styles>
            """);

        // Header: title flush left, page counter flush right against the text edge.
        var textWidthTwips = CmToTwips(preset.TextWidthCm);
        var headerTitle = string.IsNullOrWhiteSpace(options.Title) ? string.Empty : EscapeXml(options.Title);
        await WriteEntryAsync(zip, "word/header1.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:p>
                <w:pPr>
                  <w:pStyle w:val="Header"/>
                  <w:tabs><w:tab w:val="right" w:pos="{textWidthTwips}"/></w:tabs>
                </w:pPr>
                <w:r><w:t xml:space="preserve">{headerTitle}</w:t></w:r>
                <w:r><w:tab/><w:t xml:space="preserve">Seite </w:t></w:r>
                <w:r><w:fldChar w:fldCharType="begin"/></w:r>
                <w:r><w:instrText xml:space="preserve"> PAGE </w:instrText></w:r>
                <w:r><w:fldChar w:fldCharType="end"/></w:r>
                <w:r><w:t xml:space="preserve"> von {pages}</w:t></w:r>
              </w:p>
            </w:hdr>
            """);

        var body = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            var pageBreak = i > 0 && i % preset.GridLines == 0
                ? "<w:r><w:br w:type=\"page\"/></w:r>"
                : string.Empty;
            var content = lines[i].Length == 0
                ? string.Empty
                : $"<w:r><w:t xml:space=\"preserve\">{EscapeXml(lines[i])}</w:t></w:r>";
            body.Append($"<w:p>{pageBreak}{content}</w:p>");
        }

        await WriteEntryAsync(zip, "word/document.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <w:body>
                {body}
                <w:sectPr>
                  <w:headerReference w:type="default" r:id="rId2"/>
                  <w:pgSz w:w="{CmToTwips(preset.PageWidthCm)}" w:h="{CmToTwips(preset.PageHeightCm)}"/>
                  <w:pgMar w:top="{CmToTwips(preset.MarginTopCm)}" w:right="{CmToTwips(preset.MarginRightCm)}" w:bottom="{CmToTwips(preset.MarginBottomCm)}" w:left="{CmToTwips(preset.MarginLeftCm)}" w:header="{CmToTwips(preset.HeaderDistanceCm)}" w:footer="{CmToTwips(preset.HeaderDistanceCm)}" w:gutter="0"/>
                </w:sectPr>
              </w:body>
            </w:document>
            """);
    }

    // ─── PDF Export ──────────────────────────────────────────────────

    /// <summary>
    /// Lays the book out as a PDF.
    ///
    /// Rendered twice when the gutter is sized from the page count: the gutter
    /// changes the measure, the measure changes the page count, and the page
    /// count changes the gutter. One pass to find out how long the book is,
    /// one to set it with the right gutter. The layout is deterministic, so the
    /// second pass is exact rather than an approximation of the first.
    /// </summary>
    private static void ExportToPdf(
        List<ChapterExportContent> chapters,
        ExportOptions options,
        string outputPath)
    {
        var spec = options.ResolvePreset().Print ?? new PrintSpec();

        var first = RenderPdf(chapters, options, spec, pageCountForGutter: 0);
        var settled = first;
        if (spec.GutterFromPageCount
            && spec.EffectiveGutterInches(first.PageCount) != spec.EffectiveGutterInches(0))
        {
            first.Dispose();
            settled = RenderPdf(chapters, options, spec, first.PageCount);
        }

        settled.Save(outputPath);
        settled.Dispose();
    }

    private static PdfSharpCore.Pdf.PdfDocument RenderPdf(
        List<ChapterExportContent> chapters,
        ExportOptions options,
        PrintSpec spec,
        int pageCountForGutter)
    {
        var smf = options.ResolvePreset().ShunnHeader;
        var doc = new PdfSharpCore.Pdf.PdfDocument();
        doc.Info.Title = options.Title;
        if (!string.IsNullOrWhiteSpace(options.Author))
            doc.Info.Author = options.Author;

        // The sheet is the trim plus bleed on every edge; the trim sits inside
        // it, offset by the bleed. A printer cuts the sheet down to the trim,
        // so anything that has to reach the edge is drawn into the bleed and
        // then cut off.
        var bleed = PdfSharpCore.Drawing.XUnit.FromInch(spec.BleedInches);
        var pageWidth = PdfSharpCore.Drawing.XUnit.FromInch(spec.MediaWidthInches);
        var pageHeight = PdfSharpCore.Drawing.XUnit.FromInch(spec.MediaHeightInches);

        // Per page, because inside and outside swap on facing pages. Seeded
        // with page one's values so the first draw before any NewPage - the
        // cover and the title page - has a measure to use.
        var margin = PdfSharpCore.Drawing.XUnit.FromInch(
            spec.LeftMarginInches(1, pageCountForGutter)) + bleed;
        var rightMargin = PdfSharpCore.Drawing.XUnit.FromInch(
            spec.RightMarginInches(1, pageCountForGutter)) + bleed;
        var topMargin = PdfSharpCore.Drawing.XUnit.FromInch(spec.MarginTopInches) + bleed;
        var bottomMargin = PdfSharpCore.Drawing.XUnit.FromInch(spec.MarginBottomInches) + bleed;
        var textWidth = pageWidth - margin - rightMargin;

        var bodyFontName = smf ? "Courier New" : "Times New Roman";
        var fontSize = 12.0;
        var lineSpacing = smf ? fontSize * 2 : fontSize * 1.5;
        var paragraphGap = smf ? 0.0 : fontSize * 0.8;
        var indent = smf ? PdfSharpCore.Drawing.XUnit.FromInch(0.5) : PdfSharpCore.Drawing.XUnit.FromInch(0.35);
        var chapterTopMargin = smf ? PdfSharpCore.Drawing.XUnit.FromInch(3) : PdfSharpCore.Drawing.XUnit.FromInch(2);

        var bodyFont = new PdfSharpCore.Drawing.XFont(bodyFontName, fontSize);
        var boldFont = new PdfSharpCore.Drawing.XFont(bodyFontName, fontSize, PdfSharpCore.Drawing.XFontStyle.Bold);
        var italicFont = new PdfSharpCore.Drawing.XFont(bodyFontName, fontSize, PdfSharpCore.Drawing.XFontStyle.Italic);
        var boldItalicFont = new PdfSharpCore.Drawing.XFont(bodyFontName, fontSize, PdfSharpCore.Drawing.XFontStyle.BoldItalic);

        var pageNumber = 0;
        var headerY = (topMargin + bleed) / 2;

        var surname = !string.IsNullOrWhiteSpace(options.Author)
            ? options.Author.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last()
            : "";
        var shortTitle = options.Title.Length > 30 ? options.Title[..27] + "..." : options.Title;

        PdfSharpCore.Drawing.XGraphics NewPage(out double y)
        {
            var page = doc.AddPage();
            page.Width = pageWidth;
            page.Height = pageHeight;
            pageNumber++;

            // Which side of the binding this page falls on decides where the
            // wide margin goes, so the measure is recomputed rather than fixed.
            margin = PdfSharpCore.Drawing.XUnit.FromInch(
                spec.LeftMarginInches(pageNumber, pageCountForGutter)) + bleed;
            rightMargin = PdfSharpCore.Drawing.XUnit.FromInch(
                spec.RightMarginInches(pageNumber, pageCountForGutter)) + bleed;
            textWidth = pageWidth - margin - rightMargin;

            MarkPageBoxes(page, spec);
            var gfx = PdfSharpCore.Drawing.XGraphics.FromPdfPage(page);

            if (smf && pageNumber > 1)
            {
                var headerText = $"{surname} / {shortTitle.ToUpperInvariant()} / {pageNumber}";
                var hw = gfx.MeasureString(headerText, new PdfSharpCore.Drawing.XFont(bodyFontName, 10));
                gfx.DrawString(headerText,
                    new PdfSharpCore.Drawing.XFont(bodyFontName, 10),
                    PdfSharpCore.Drawing.XBrushes.Black,
                    new PdfSharpCore.Drawing.XPoint(pageWidth - rightMargin - hw.Width, headerY));
            }

            y = topMargin + lineSpacing;
            return gfx;
        }

        // Cover page: full-bleed, aspect-preserved, ahead of the title page.
        // Skipped silently when there is no usable cover, so a missing or
        // unreadable image can never fail an export.
        if (CoverMediaType(options.CoverImagePath) != null)
            DrawPdfCoverPage(doc, options.CoverImagePath, pageWidth, pageHeight, ref pageNumber, spec);

        // Title page
        if (options.IncludeTitlePage)
        {
            var tp = doc.AddPage();
            tp.Width = pageWidth;
            tp.Height = pageHeight;
            MarkPageBoxes(tp, spec);
            pageNumber++;
            var gfx = PdfSharpCore.Drawing.XGraphics.FromPdfPage(tp);

            if (smf)
            {
                if (!string.IsNullOrWhiteSpace(options.Author))
                {
                    gfx.DrawString(options.Author, bodyFont, PdfSharpCore.Drawing.XBrushes.Black,
                        new PdfSharpCore.Drawing.XPoint(margin, margin));
                }

                var centerY = pageHeight / 2;
                var titleUpper = options.Title.ToUpperInvariant();
                var titleW = gfx.MeasureString(titleUpper, bodyFont);
                gfx.DrawString(titleUpper, bodyFont, PdfSharpCore.Drawing.XBrushes.Black,
                    new PdfSharpCore.Drawing.XPoint((pageWidth - titleW.Width) / 2, centerY + lineSpacing));

                if (!string.IsNullOrWhiteSpace(options.Author))
                {
                    var byLine = $"by {options.Author}";
                    var byW = gfx.MeasureString(byLine, bodyFont);
                    gfx.DrawString(byLine, bodyFont, PdfSharpCore.Drawing.XBrushes.Black,
                        new PdfSharpCore.Drawing.XPoint((pageWidth - byW.Width) / 2, centerY - lineSpacing));
                }
            }
            else
            {
                var titleFont = new PdfSharpCore.Drawing.XFont(bodyFontName, 24, PdfSharpCore.Drawing.XFontStyle.Bold);
                var titleW = gfx.MeasureString(options.Title, titleFont);
                gfx.DrawString(options.Title, titleFont, PdfSharpCore.Drawing.XBrushes.Black,
                    new PdfSharpCore.Drawing.XPoint((pageWidth - titleW.Width) / 2, pageHeight * 0.6));

                if (!string.IsNullOrWhiteSpace(options.Author))
                {
                    var authorFont = new PdfSharpCore.Drawing.XFont(bodyFontName, 16, PdfSharpCore.Drawing.XFontStyle.Italic);
                    var authorW = gfx.MeasureString(options.Author, authorFont);
                    gfx.DrawString(options.Author, authorFont, PdfSharpCore.Drawing.XBrushes.Black,
                        new PdfSharpCore.Drawing.XPoint((pageWidth - authorW.Width) / 2, pageHeight * 0.6 - 36));
                }
            }
        }

        // Chapters
        for (var chapterIndex = 0; chapterIndex < chapters.Count; chapterIndex++)
        {
            var chapter = chapters[chapterIndex];
            var gfx = NewPage(out var y);
            y = topMargin + chapterTopMargin;
            var chapterNotes = new List<string>();

            // Chapter title
            var chTitleFont = smf ? bodyFont : boldFont;
            var chTitleSize = smf ? fontSize : 18;
            var chTitleFontActual = smf ? bodyFont : new PdfSharpCore.Drawing.XFont(bodyFontName, chTitleSize, PdfSharpCore.Drawing.XFontStyle.Bold);
            var pdfHeading = options.ResolvePreset().ChapterHeading(chapterIndex + 1, chapter.Title);
            var chTitleText = smf ? pdfHeading.ToUpperInvariant() : pdfHeading;
            if (!chapter.HideHeading)
            {
                var ctW = gfx.MeasureString(chTitleText, chTitleFontActual);
                gfx.DrawString(chTitleText, chTitleFontActual, PdfSharpCore.Drawing.XBrushes.Black,
                    new PdfSharpCore.Drawing.XPoint((pageWidth - ctW.Width) / 2, y));
                y += lineSpacing * 2;

                if (!string.IsNullOrWhiteSpace(chapter.Subtitle))
                {
                    var subW = gfx.MeasureString(chapter.Subtitle, bodyFont);
                    gfx.DrawString(chapter.Subtitle, bodyFont, PdfSharpCore.Drawing.XBrushes.Black,
                        new PdfSharpCore.Drawing.XPoint((pageWidth - subW.Width) / 2, y));
                    y += lineSpacing * 2;
                }
            }

            // Scenes
            for (var si = 0; si < chapter.Scenes.Count; si++)
            {
                // Scene break
                if (si > 0)
                {
                    y += lineSpacing;
                    if (y > pageHeight - bottomMargin - lineSpacing)
                    {
                        gfx.Dispose();
                        gfx = NewPage(out y);
                    }

                    var sbW = gfx.MeasureString(SceneBreakText, bodyFont);
                    gfx.DrawString(SceneBreakText, bodyFont, PdfSharpCore.Drawing.XBrushes.Black,
                        new PdfSharpCore.Drawing.XPoint((pageWidth - sbW.Width) / 2, y));
                    y += lineSpacing;
                }

                var scene = chapter.Scenes[si];
                var sceneBlocks = ParseHtmlToBlocks(scene.HtmlContent, scene.Footnotes);
                var isFirstPara = si == 0;

                foreach (var block in sceneBlocks)
                {
                    if (block.ImagePath != null)
                    {
                        if (!File.Exists(block.ImagePath)) continue;
                        PdfSharpCore.Drawing.XImage image;
                        try
                        {
                            image = PdfSharpCore.Drawing.XImage.FromFile(block.ImagePath);
                        }
                        catch (Exception)
                        {
                            // Deliberately broad: the decoders behind this throw
                            // their own exception types, and a picture the
                            // library will not read must not take the export
                            // down with it. The image is left out instead.
                            continue;
                        }

                        using (image)
                        {
                            // Scaled to the measure, never enlarged past its own
                            // size: a small image blown up to the text width
                            // prints as a blur.
                            var scale = Math.Min(1.0, textWidth / image.PixelWidth);
                            var width = image.PixelWidth * scale;
                            var height = image.PixelHeight * scale;

                            if (y + height > pageHeight - bottomMargin)
                            {
                                gfx.Dispose();
                                gfx = NewPage(out y);
                            }

                            gfx.DrawImage(image, margin + (textWidth - width) / 2, y, width, height);
                            y += height + lineSpacing;
                        }
                        isFirstPara = false;
                        continue;
                    }

                    var para = block.Segments;
                    // PdfSharpCore lays text out a line at a time, with no way
                    // to reserve the foot of the page mid-paragraph, so notes
                    // are marked here and set at the end of the chapter.
                    var plainText = string.Concat(para.Select(seg =>
                    {
                        if (seg.FootnoteText == null) return seg.Text;
                        chapterNotes.Add(seg.FootnoteText);
                        return $"[{chapterNotes.Count}]";
                    }));
                    var paraIndent = smf && !isFirstPara ? (double)indent : 0.0;
                    var lines = WordWrap(plainText, bodyFont, gfx, textWidth - paraIndent);

                    // Moved whole rather than split badly. One line stranded at
                    // the foot of a page, or carried alone onto the next, is the
                    // mark of a file nobody laid out.
                    if (BreaksBadly(spec, lines.Count, y, lineSpacing, pageHeight - bottomMargin))
                    {
                        gfx.Dispose();
                        gfx = NewPage(out y);
                    }

                    foreach (var line in lines)
                    {
                        if (y > pageHeight - bottomMargin - lineSpacing)
                        {
                            gfx.Dispose();
                            gfx = NewPage(out y);
                        }

                        gfx.DrawString(line, bodyFont, PdfSharpCore.Drawing.XBrushes.Black,
                            new PdfSharpCore.Drawing.XPoint(margin + paraIndent, y));
                        paraIndent = 0; // Only indent first line
                        y += lineSpacing;
                    }

                    isFirstPara = false;
                    if (paragraphGap > 0) y += paragraphGap;
                }
            }

            // The notes, under the chapter they belong to. Numbering restarts
            // per chapter, which is what the markers in the prose say.
            if (chapterNotes.Count > 0)
            {
                y += lineSpacing;
                for (var n = 0; n < chapterNotes.Count; n++)
                {
                    foreach (var line in WordWrap(
                        $"[{n + 1}] {chapterNotes[n]}", bodyFont, gfx, textWidth))
                    {
                        if (y > pageHeight - bottomMargin - lineSpacing)
                        {
                            gfx.Dispose();
                            gfx = NewPage(out y);
                        }
                        gfx.DrawString(line, bodyFont, PdfSharpCore.Drawing.XBrushes.Black,
                            new PdfSharpCore.Drawing.XPoint(margin, y));
                        y += lineSpacing;
                    }
                }
            }

            gfx.Dispose();
        }

        return doc;
    }

    /// <summary>
    /// Marks the trim and bleed boxes on a page.
    ///
    /// The media box is the sheet; the trim box is where the printer cuts. A
    /// file that does not say where the cut goes is the single most common
    /// reason a print job comes back, because the printer has to guess and a
    /// guess an eighth of an inch out is a white sliver down one edge.
    ///
    /// Nothing is written when there is no bleed: with the two boxes equal the
    /// entries carry no information a reader does not already have.
    /// </summary>
    private static void MarkPageBoxes(PdfSharpCore.Pdf.PdfPage page, PrintSpec spec)
    {
        if (spec.BleedInches <= 0) return;

        var bleed = PdfSharpCore.Drawing.XUnit.FromInch(spec.BleedInches).Point;
        var trimWidth = PdfSharpCore.Drawing.XUnit.FromInch(spec.TrimWidthInches).Point;
        var trimHeight = PdfSharpCore.Drawing.XUnit.FromInch(spec.TrimHeightInches).Point;

        page.TrimBox = new PdfSharpCore.Pdf.PdfRectangle(
            new PdfSharpCore.Drawing.XRect(bleed, bleed, trimWidth, trimHeight));
        page.BleedBox = new PdfSharpCore.Pdf.PdfRectangle(
            new PdfSharpCore.Drawing.XRect(
                0, 0, trimWidth + bleed * 2, trimHeight + bleed * 2));
    }

    /// <summary>
    /// Whether a paragraph should start on the next page rather than here.
    ///
    /// A paragraph that leaves one line at the foot of a page (an orphan) or
    /// carries one line onto the next (a widow) is the mark of a file nobody
    /// laid out. Moving the whole paragraph is the cheap fix and the one a
    /// typesetter would make.
    /// </summary>
    internal static bool BreaksBadly(
        PrintSpec spec, int lineCount, double y, double lineSpacing, double pageBottom)
    {
        if (!spec.AvoidWidowsAndOrphans || spec.MinLinesTogether < 2) return false;

        var fits = (int)Math.Floor((pageBottom - y) / Math.Max(1.0, lineSpacing));
        if (fits <= 0) return false;

        // A paragraph shorter than the rule cannot be split badly - wherever it
        // lands, it lands whole.
        if (lineCount <= spec.MinLinesTogether) return false;

        // Nor can one that fits where it is: there is no second page to strand
        // anything on.
        if (lineCount <= fits) return false;

        // Too few lines left here, or too few carried over.
        return fits < spec.MinLinesTogether || lineCount - fits < spec.MinLinesTogether;
    }

        private static List<string> WordWrap(
        string text,
        PdfSharpCore.Drawing.XFont font,
        PdfSharpCore.Drawing.XGraphics gfx,
        double maxWidth)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var currentLine = "";

        foreach (var word in words)
        {
            var testLine = string.IsNullOrEmpty(currentLine) ? word : $"{currentLine} {word}";
            var testWidth = gfx.MeasureString(testLine, font).Width;
            if (testWidth > maxWidth && !string.IsNullOrEmpty(currentLine))
            {
                lines.Add(currentLine);
                currentLine = word;
            }
            else
            {
                currentLine = testLine;
            }
        }

        if (!string.IsNullOrEmpty(currentLine))
            lines.Add(currentLine);

        return lines;
    }

    /// <summary>
    /// Adds a full-page cover, scaled to fit and centred so the image is never
    /// distorted. Any failure to decode the image is swallowed: an export that
    /// produces the book without a cover beats one that produces nothing.
    /// </summary>
    private static void DrawPdfCoverPage(
        PdfSharpCore.Pdf.PdfDocument doc,
        string coverPath,
        double pageWidth,
        double pageHeight,
        ref int pageNumber,
        PrintSpec spec)
    {
        PdfSharpCore.Drawing.XImage? image = null;
        try
        {
            image = PdfSharpCore.Drawing.XImage.FromFile(coverPath);
        }
        catch (Exception)
        {
            return;
        }

        using (image)
        {
            var page = doc.AddPage();
            page.Width = pageWidth;
            page.Height = pageHeight;
            MarkPageBoxes(page, spec);
            pageNumber++;

            using var gfx = PdfSharpCore.Drawing.XGraphics.FromPdfPage(page);
            var scale = Math.Min(pageWidth / image.PixelWidth, pageHeight / image.PixelHeight);
            var drawWidth = image.PixelWidth * scale;
            var drawHeight = image.PixelHeight * scale;
            gfx.DrawImage(
                image,
                (pageWidth - drawWidth) / 2,
                (pageHeight - drawHeight) / 2,
                drawWidth,
                drawHeight);
        }
    }

    // ─── Markdown Export ─────────────────────────────────────────────

    private static async Task ExportToMarkdownAsync(
        List<ChapterExportContent> chapters,
        ExportOptions options,
        string outputPath)
    {
        var sb = new StringBuilder();
        // Numbering runs across the whole file: two scenes each starting at 1
        // would collide in one document.
        var footnoteDefs = new List<string>();

        if (options.IncludeTitlePage)
        {
            sb.AppendLine($"# {options.Title}");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(options.Author))
            {
                sb.AppendLine($"*{options.Author}*");
                sb.AppendLine();
            }
            sb.AppendLine("---");
            sb.AppendLine();
        }

        for (var i = 0; i < chapters.Count; i++)
        {
            var chapter = chapters[i];

            if (i > 0 || options.IncludeTitlePage)
            {
                sb.AppendLine();
                sb.AppendLine("<div style=\"page-break-after: always;\"></div>");
                sb.AppendLine();
            }

            if (!chapter.HideHeading)
            {
                sb.AppendLine($"## {options.ResolvePreset().ChapterHeading(i + 1, chapter.Title)}");
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(chapter.Subtitle))
                {
                    sb.AppendLine($"*{chapter.Subtitle}*");
                    sb.AppendLine();
                }
            }

            for (var si = 0; si < chapter.Scenes.Count; si++)
            {
                if (si > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"<p style=\"text-align: center; margin: 1.5em 0;\">{SceneBreakText}</p>");
                    sb.AppendLine();
                }

                var scene = chapter.Scenes[si];
                // Ordered items number from one per run, so two lists in a scene
                // do not continue each other's count.
                var ordinal = 0;
                foreach (var block in ParseHtmlToBlocks(scene.HtmlContent, scene.Footnotes))
                {
                    if (block.ImagePath != null)
                    {
                        sb.AppendLine($"![{block.ImageAlt}]({block.ImagePath})");
                        sb.AppendLine();
                        continue;
                    }

                    var text = SegmentsToMarkdown(block.Segments, footnoteDefs);
                    if (block.List != ListKind.None)
                    {
                        ordinal = block.List == ListKind.Number ? ordinal + 1 : 0;
                        sb.AppendLine(block.List == ListKind.Number ? $"{ordinal}. {text}" : $"- {text}");
                        sb.AppendLine();
                        continue;
                    }

                    ordinal = 0;
                    sb.AppendLine(block.StyleId switch
                    {
                        "heading" => $"# {text}",
                        "subheading" => $"## {text}",
                        "blockquote" => $"> {text}",
                        "poetry" => $"    {text}",
                        _ => text,
                    });
                    sb.AppendLine();
                }
            }
        }

        // Definitions go at the end of the file, which is where every Markdown
        // renderer expects to find them.
        if (footnoteDefs.Count > 0)
        {
            sb.AppendLine();
            foreach (var (note, index) in footnoteDefs.Select((n, i) => (n, i)))
                sb.AppendLine($"[^{index + 1}]: {note.ReplaceLineEndings(" ")}");
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// Markdown footnote syntax: a <c>[^n]</c> reference where the anchor sat,
    /// with the note itself collected for the definition list at the end of the
    /// document. Numbering runs across the whole file, because two scenes each
    /// starting at 1 would collide in one document.
    /// </summary>
    private static string SegmentsToMarkdown(
        List<InlineSegment> segments, List<string>? footnoteDefs = null)
    {
        var sb = new StringBuilder();
        foreach (var seg in segments)
        {
            if (seg.FootnoteText != null)
            {
                if (footnoteDefs == null) continue;
                footnoteDefs.Add(seg.FootnoteText);
                sb.Append("[^").Append(footnoteDefs.Count).Append(']');
                continue;
            }
            var body = seg.Strike ? $"~~{seg.Text}~~" : seg.Text;
            if (seg.Bold && seg.Italic)
                sb.Append($"***{body}***");
            else if (seg.Bold)
                sb.Append($"**{body}**");
            else if (seg.Italic)
                sb.Append($"*{body}*");
            else
                sb.Append(body);
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"<br\s*/?>|</(?:p|div|li|tr|h[1-6])\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTagRegex();

    /// <summary>The id in a <c>&lt;sup class="nv-fn" data-fn-id="..."&gt;</c> anchor.</summary>
    [GeneratedRegex(@"data-fn-id=""([^""]*)""")]
    private static partial Regex FootnoteIdRegex();

    [GeneratedRegex(@"^#{1,6}\s+")]
    private static partial Regex MarkdownHeadingRegex();

    [GeneratedRegex(@"^[-*+]\s+")]
    private static partial Regex MarkdownBulletRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRunRegex();

    [GeneratedRegex(@"<p[^>]*>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphRegex();

    [GeneratedRegex(@"<p[^>]*\bclass=""([^""]*)""[^>]*>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphWithClassRegex();

    /// <summary>Paragraphs and list items in document order, plus the bare
    /// <c>ul</c>/<c>ol</c> boundaries that say which kind of list an item is in.</summary>
    [GeneratedRegex(
        @"<(?<tag>p|li)(?<attrs>[^>]*)>(?<body>.*?)</\k<tag>>|<(?<tag>ul|ol)[^>]*>|<(?<tag>/ul|/ol)>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex BlockRegex();

    [GeneratedRegex(@"<img\b(?<attrs>[^>]*)>", RegexOptions.IgnoreCase)]
    private static partial Regex ImageTagRegex();

    [GeneratedRegex(
        @"(?<name>[a-zA-Z-]+)\s*=\s*[""'](?<value>[^""']*)[""']",
        RegexOptions.IgnoreCase)]
    private static partial Regex HtmlAttributeRegex();

    [GeneratedRegex(@"<p([^>]*)>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphAnyRegex();

    private static string? ExtractStyleClass(string attrs)
    {
        var m = Regex.Match(attrs, @"class=""([^""]*)""", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        foreach (var token in m.Groups[1].Value.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
            if (token.StartsWith("nv-style-")) return token.Substring("nv-style-".Length);
        return null;
    }
}
