using System.Text;
using static Novalist.Core.Services.ExportProse;

namespace Novalist.Core.Services;

public partial class ExportService
{
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
            var fdxHeading = chapter.Heading.ToUpperInvariant();
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
                sb.AppendLine($"\\chapter*{{{LatexEscape(chapter.Heading)}}}");
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
                sb.AppendLine($"{(chapter.IsVolume ? "#" : "##")} {chapter.Heading}");
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
}
