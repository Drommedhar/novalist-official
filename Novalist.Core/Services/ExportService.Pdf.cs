using Novalist.Core.Models;

namespace Novalist.Core.Services;

public partial class ExportService
{
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
        => new PdfBookRenderer(options, spec, pageCountForGutter).Render(chapters);

    private sealed class PdfBookRenderer
    {
        private readonly ExportOptions options;
        private readonly PrintSpec spec;
        private readonly int pageCountForGutter;
        private readonly bool smf;
        private readonly PdfSharpCore.Pdf.PdfDocument doc;
        private readonly PdfSharpCore.Drawing.XUnit bleed;
        private readonly PdfSharpCore.Drawing.XUnit pageWidth;
        private readonly PdfSharpCore.Drawing.XUnit pageHeight;
        private readonly PdfSharpCore.Drawing.XUnit indent;
        private readonly PdfSharpCore.Drawing.XUnit chapterTopMargin;
        private readonly double topMargin;
        private readonly double bottomMargin;
        private readonly double headerY;
        private readonly string bodyFontName;
        private const double fontSize = 12.0;
        private readonly double lineSpacing;
        private readonly double paragraphGap;
        private readonly PdfSharpCore.Drawing.XFont bodyFont;
        private double margin;
        private double rightMargin;
        private double textWidth;
        private int pageNumber;

        public PdfBookRenderer(ExportOptions options, PrintSpec spec, int pageCountForGutter)
        {
            this.options = options;
            this.spec = spec;
            this.pageCountForGutter = pageCountForGutter;
            smf = options.ResolvePreset().ShunnHeader;
            doc = new PdfSharpCore.Pdf.PdfDocument();
            doc.Info.Title = options.Title;
            if (!string.IsNullOrWhiteSpace(options.Author))
                doc.Info.Author = options.Author;

            // The sheet is the trim plus bleed on every edge; the trim sits inside
            // it, offset by the bleed. A printer cuts the sheet down to the trim,
            // so anything that has to reach the edge is drawn into the bleed and
            // then cut off.
            bleed = PdfSharpCore.Drawing.XUnit.FromInch(spec.BleedInches);
            pageWidth = PdfSharpCore.Drawing.XUnit.FromInch(spec.MediaWidthInches);
            pageHeight = PdfSharpCore.Drawing.XUnit.FromInch(spec.MediaHeightInches);

            // Per page, because inside and outside swap on facing pages. Seeded
            // with page one's values so the first draw before any NewPage - the
            // cover and the title page - has a measure to use.
            margin = PdfSharpCore.Drawing.XUnit.FromInch(
                spec.LeftMarginInches(1, pageCountForGutter)) + bleed;
            rightMargin = PdfSharpCore.Drawing.XUnit.FromInch(
                spec.RightMarginInches(1, pageCountForGutter)) + bleed;
            topMargin = PdfSharpCore.Drawing.XUnit.FromInch(spec.MarginTopInches) + bleed;
            bottomMargin = PdfSharpCore.Drawing.XUnit.FromInch(spec.MarginBottomInches) + bleed;
            textWidth = pageWidth - margin - rightMargin;

            bodyFontName = smf ? "Courier New" : "Times New Roman";

            lineSpacing = smf ? fontSize * 2 : fontSize * 1.5;
            paragraphGap = smf ? 0.0 : fontSize * 0.8;
            indent = smf ? PdfSharpCore.Drawing.XUnit.FromInch(0.5) : PdfSharpCore.Drawing.XUnit.FromInch(0.35);
            chapterTopMargin = smf ? PdfSharpCore.Drawing.XUnit.FromInch(3) : PdfSharpCore.Drawing.XUnit.FromInch(2);

            bodyFont = new PdfSharpCore.Drawing.XFont(bodyFontName, fontSize);


            headerY = (topMargin + bleed) / 2;
        }

        public PdfSharpCore.Pdf.PdfDocument Render(List<ChapterExportContent> chapters)
        {
            if (CoverMediaType(options.CoverImagePath) != null)
                DrawPdfCoverPage(doc, options.CoverImagePath, pageWidth, pageHeight, ref pageNumber, spec);
            DrawTitlePage();
            foreach (var chapter in chapters) DrawChapter(chapter);
            return doc;
        }

        private PdfSharpCore.Drawing.XGraphics NewPage(out double y)
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
                var headerText = $"{RunningHead(options)} / {pageNumber}";
                var hw = gfx.MeasureString(headerText, new PdfSharpCore.Drawing.XFont(bodyFontName, 10));
                gfx.DrawString(headerText,
                    new PdfSharpCore.Drawing.XFont(bodyFontName, 10),
                    PdfSharpCore.Drawing.XBrushes.Black,
                    new PdfSharpCore.Drawing.XPoint(pageWidth - rightMargin - hw.Width, headerY));
            }

            y = topMargin + lineSpacing;
            return gfx;
        }

        private void DrawTitlePage()
        {
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
        }

        private void DrawChapter(ChapterExportContent chapter)
        {
            var gfx = NewPage(out var y);
            y = topMargin + chapterTopMargin;
            var chapterNotes = new List<string>();

            // Chapter title
            var chTitleSize = smf ? fontSize : 18;
            var chTitleFontActual = smf ? bodyFont : new PdfSharpCore.Drawing.XFont(bodyFontName, chTitleSize, PdfSharpCore.Drawing.XFontStyle.Bold);
            var pdfHeading = chapter.Heading;
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
            for (var si = 0; si < chapter.Scenes.Count; si++)
                DrawScene(chapter.Scenes[si], si, chapterNotes, ref gfx, ref y);
            DrawNotes(chapterNotes, ref gfx, ref y);
            gfx.Dispose();
        }

        private void DrawScene(SceneExportContent scene, int si, List<string> chapterNotes,
            ref PdfSharpCore.Drawing.XGraphics gfx, ref double y)
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

            var sceneBlocks = ParseHtmlToBlocks(scene.HtmlContent, scene.Footnotes);
            var isFirstPara = si == 0;
            foreach (var block in sceneBlocks)
            {
                if (block.ImagePath != null)
                {
                    if (DrawImage(block.ImagePath, ref gfx, ref y)) isFirstPara = false;
                    continue;
                }
                DrawParagraph(block, chapterNotes, isFirstPara, ref gfx, ref y);
                isFirstPara = false;
                if (paragraphGap > 0) y += paragraphGap;
            }
        }

        private bool DrawImage(string path, ref PdfSharpCore.Drawing.XGraphics gfx, ref double y)
        {
            if (!File.Exists(path)) return false;
            PdfSharpCore.Drawing.XImage image;
            try
            {
                image = PdfSharpCore.Drawing.XImage.FromFile(path);
            }
            catch (Exception exception)
            {
                // Deliberately broad: the decoders behind this throw
                // their own exception types, and a picture the
                // library will not read must not take the export
                // down with it. The image is left out instead.
                System.Diagnostics.Trace.TraceWarning($"PDF image could not be decoded: {exception.GetType().Name}");
                return false;
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
            return true;
        }

        private void DrawParagraph(ExportBlock block, List<string> chapterNotes, bool isFirstPara,
            ref PdfSharpCore.Drawing.XGraphics gfx, ref double y)
        {
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
        }

        private void DrawNotes(List<string> chapterNotes, ref PdfSharpCore.Drawing.XGraphics gfx, ref double y)
        {
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
        }
    }
}
