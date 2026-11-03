using Novalist.Core.Models;

namespace Novalist.Core.Services;

public partial class ExportService
{
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
    private static void MarkPageBoxes(PdfSharp.Pdf.PdfPage page, PrintSpec spec)
    {
        if (spec.BleedInches <= 0) return;

        var bleed = PdfSharp.Drawing.XUnit.FromInch(spec.BleedInches).Point;
        var trimWidth = PdfSharp.Drawing.XUnit.FromInch(spec.TrimWidthInches).Point;
        var trimHeight = PdfSharp.Drawing.XUnit.FromInch(spec.TrimHeightInches).Point;

        page.TrimBox = new PdfSharp.Pdf.PdfRectangle(
            new PdfSharp.Drawing.XRect(bleed, bleed, trimWidth, trimHeight));
        page.BleedBox = new PdfSharp.Pdf.PdfRectangle(
            new PdfSharp.Drawing.XRect(
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
    PdfSharp.Drawing.XFont font,
    PdfSharp.Drawing.XGraphics gfx,
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
        PdfSharp.Pdf.PdfDocument doc,
        string coverPath,
        double pageWidth,
        double pageHeight,
        ref int pageNumber,
        PrintSpec spec)
    {
        PdfImage source;
        try
        {
            source = new PdfImage(coverPath);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceWarning($"PDF cover could not be decoded: {exception.GetType().Name}");
            return;
        }

        using (source)
        {
            var image = source.Image;
            var page = doc.AddPage();
            page.Width = PdfSharp.Drawing.XUnit.FromPoint(pageWidth);
            page.Height = PdfSharp.Drawing.XUnit.FromPoint(pageHeight);
            MarkPageBoxes(page, spec);
            pageNumber++;

            using var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
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
