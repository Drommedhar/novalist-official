using Novalist.Core.Models;

namespace Novalist.Core.Services;

public partial class ExportService
{
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
}
