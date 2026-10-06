using System.Text.RegularExpressions;
using System.Text;
using Novalist.Core.Models;
using XBrushes = PdfSharpCore.Drawing.XBrushes;
using XFont = PdfSharpCore.Drawing.XFont;
using XFontStyle = PdfSharpCore.Drawing.XFontStyle;
using XGraphics = PdfSharpCore.Drawing.XGraphics;
using XImage = PdfSharpCore.Drawing.XImage;
using XPoint = PdfSharpCore.Drawing.XPoint;
using XUnit = PdfSharpCore.Drawing.XUnit;
using static Novalist.Core.Services.ExportProse;

namespace Novalist.Core.Services;

public partial class ExportService
{
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

        var content = await CompileCodexAsync(options, _entityService);

        string? ResolveImage(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;
            var abs = _entityService.GetImageFullPath(relativePath);
            return !string.IsNullOrWhiteSpace(abs) && File.Exists(abs) ? abs : null;
        }

        WriteCodexPdf(content, options, outputPath, ResolveImage);
    }

    private static void WriteCodexPdf(
        CodexContent content, ExportOptions options, string outputPath,
        Func<string, string?> resolveImage)
        => new CodexPdfWriter(options, resolveImage).Save(content, outputPath);
    private sealed class CodexPdfWriter
    {
        private const string fontName = "Times New Roman";
        private const double lineHeight = 15.0;
        private static readonly XUnit pageWidth = XUnit.FromInch(8.5);
        private static readonly XUnit pageHeight = XUnit.FromInch(11);
        private static readonly XUnit margin = XUnit.FromInch(1);
        private static readonly double textWidth = pageWidth - 2 * margin;
        private static readonly double pageBottom = pageHeight - margin;
        private static readonly double fieldIndent = XUnit.FromInch(0.2);
        private static readonly double bulletIndent = XUnit.FromInch(0.18);
        private static readonly double maxImageSide = XUnit.FromInch(3);
        private readonly PdfSharpCore.Pdf.PdfDocument doc = new();
        private readonly XFont bodyFont = new(fontName, 11);
        private readonly XFont labelFont = new(fontName, 11, XFontStyle.Bold);
        private readonly XFont blockFont = new(fontName, 12.5, XFontStyle.Bold);
        private readonly XFont entityFont = new(fontName, 15, XFontStyle.Bold);
        private readonly XFont sectionFont = new(fontName, 20, XFontStyle.Bold);
        private readonly ExportOptions options;
        private readonly Func<string, string?> resolveImage;
        private readonly string docTitle;
        private XGraphics? gfx;
        private PdfSharpCore.Pdf.PdfPage? currentPage;
        private double y;

        public CodexPdfWriter(ExportOptions options, Func<string, string?> resolveImage)
        {
            this.options = options;
            this.resolveImage = resolveImage;
            docTitle = string.IsNullOrWhiteSpace(options.Title) ? "Codex" : options.Title;
            doc.Info.Title = docTitle;
            if (!string.IsNullOrWhiteSpace(options.Author)) doc.Info.Author = options.Author;
        }

        [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(gfx), nameof(currentPage))]
        private void NewPage()
        {
            gfx?.Dispose();
            var page = doc.AddPage();
            page.Width = pageWidth;
            page.Height = pageHeight;
            currentPage = page;
            gfx = XGraphics.FromPdfPage(page);
            y = margin + lineHeight;
        }

        [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(gfx))]
        private void Ensure(double needed)
        {
            if (gfx == null || y + needed > pageBottom) NewPage();
        }

        // Lays out styled runs as one flowing line, wrapping at the right
        // margin and continuing at `indent` on every following line.
        private void DrawRuns(IEnumerable<InlineSegment> runs, double indent, XFont regular, XFont strong)
        {
            Ensure(lineHeight);
            var left = margin + indent;
            var right = margin + textWidth;
            var x = left;

            void Place(string word, XFont font)
            {
                Ensure(lineHeight);
                var width = gfx.MeasureString(word, font).Width;
                if (x + width > right && x > left)
                {
                    y += lineHeight;
                    Ensure(lineHeight);
                    x = left;
                }
                gfx.DrawString(word, font, XBrushes.Black, new XPoint(x, y));
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
                    // aislop-ignore-next-line ai-slop/csharp-string-concat-in-loop -- Both operands are numeric PDF coordinates; this advances the drawing position.
                    if (pendingSpace && x > left) x += gfx.MeasureString(" ", font).Width;
                    pendingSpace = false;
                    if (gfx.MeasureString(word, font).Width > right - left)
                        foreach (var chunk in HardBreak(word, font, gfx, right - left)) Place(chunk, font);
                    else
                        Place(word, font);
                }
            }

            y += lineHeight;
        }

        private void DrawHeading(string text, double indent, XFont font)
            => DrawRuns([new InlineSegment { Text = text }], indent, font, font);

        private void DrawProse(string content, double indent)
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
                    gfx.DrawString("•", bodyFont, XBrushes.Black, new XPoint(margin + indent, y));
                }
                DrawRuns(line.Segments, lineIndent, line.Heading ? labelFont : bodyFont, labelFont);
            }
        }

        private void DrawField(KeyValuePair<string, string> field)
        {
            var runs = new List<InlineSegment> { new() { Text = field.Key + ": ", Bold = true } };
            runs.AddRange(ParseInlineMarkdown(WhitespaceRunRegex().Replace(field.Value, " ").Trim()));
            DrawRuns(runs, fieldIndent, bodyFont, labelFont);
        }

        private void DrawImage(string absolutePath)
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
                gfx.DrawImage(image, margin + fieldIndent, y, width, height);
                y += height + lineHeight * 0.5;
            }
        }

        private void DrawImages(List<EntityImage>? images)
        {
            if (images is not { Count: > 0 } || !options.IncludesPart("images")) return;
            foreach (var img in images)
            {
                if (string.IsNullOrWhiteSpace(img.Path)) continue;
                var abs = resolveImage(img.Path);
                if (abs != null) DrawImage(abs);
            }
        }

        private void DrawSections(List<EntitySection>? sections)
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
        private void DrawEntity(
            PdfSharpCore.Pdf.PdfOutline group,
            bool first,
            string name,
            IEnumerable<KeyValuePair<string, string>> fields,
            IEntityData entity)
        {
            if (!first) NewPage();
            group.Outlines.Add(name, currentPage, false);

            DrawHeading(name, 0, entityFont);
            DrawImages(entity.Images);
            if (options.IncludesPart("fields"))
                foreach (var field in fields) DrawField(field);

            if (entity.Relationships is { Count: > 0 } && options.IncludesPart("relationships"))
            {
                y += lineHeight * 0.5;
                DrawHeading(Label(options, "relationships", "Relationships"), fieldIndent, blockFont);
                foreach (var rel in entity.Relationships)
                    DrawHeading($"{rel.Role}: {rel.Target}", fieldIndent * 2, bodyFont);
            }

            DrawSections(entity.Sections);
            y += lineHeight;
        }

        private PdfSharpCore.Pdf.PdfOutline SectionHeading(string title)
        {
            NewPage();
            var outline = doc.Outlines.Add(title, currentPage, true);
            DrawHeading(title, 0, sectionFont);
            y += lineHeight;
            return outline;
        }

        public void Save(CodexContent content, string outputPath)
        {

            if (options.IncludeTitlePage)
            {
                NewPage();
                var titleFont = new XFont(fontName, 26, XFontStyle.Bold);
                var titleWidth = gfx.MeasureString(docTitle, titleFont).Width;
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
                        c);
                }
            }

            if (content.Locations.Count > 0)
            {
                var group = SectionHeading(Label(options, "locations", "Locations"));
                for (var i = 0; i < content.Locations.Count; i++)
                {
                    var l = content.Locations[i];
                    DrawEntity(group, i == 0, l.Name, GenericFields(l.Type, l.Description, l.CustomProperties, options),
                        l);
                }
            }

            if (content.Items.Count > 0)
            {
                var group = SectionHeading(Label(options, "items", "Items"));
                for (var i = 0; i < content.Items.Count; i++)
                {
                    var it = content.Items[i];
                    DrawEntity(group, i == 0, it.Name, GenericFields(it.Type, it.Description, it.CustomProperties, options),
                        it);
                }
            }

            if (content.Lore.Count > 0)
            {
                var group = SectionHeading(Label(options, "lore", "Lore"));
                for (var i = 0; i < content.Lore.Count; i++)
                {
                    var lo = content.Lore[i];
                    DrawEntity(group, i == 0, lo.Name, GenericFields(lo.Category, lo.Description, lo.CustomProperties, options),
                        lo);
                }
            }

            // An empty selection still has to produce a readable file.
            if (gfx == null) NewPage();
            gfx.Dispose();
            doc.Save(outputPath);

        }
    }
}
