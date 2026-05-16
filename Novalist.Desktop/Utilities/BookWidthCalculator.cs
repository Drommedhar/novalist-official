using System;
using System.Globalization;
using Avalonia.Media;
using Novalist.Core.Models;

namespace Novalist.Desktop.Utilities;

public static class BookWidthCalculator
{
    // Text block widths in inches for standard book formats.
    private const double USTrade6x9Width = 4.75;
    private const double Digest5_5x8_5Width = 4.3;
    private const double A5Width = 4.63;
    private const double MassMarketWidth = 3.35;

    private const double LogicalDpi = 96.0;
    private const double MinEditorWidth = 300.0;

    // Representative English prose sample (lowercase-heavy, matching real text distribution).
    private const string MeasureSample =
        "abcdefghijklmnopqrstuvwxyz"
        + "abcdefghijklmnopqrstuvwxyz"
        + "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
        + " ,.;:!?'-\"";

    public static double GetTextBlockWidthInches(string pageFormat, double? customWidth)
    {
        return pageFormat switch
        {
            "Digest5_5x8_5" => Digest5_5x8_5Width,
            "A5" => A5Width,
            "MassMarket" => MassMarketWidth,
            "Custom" when customWidth is > 0 => customWidth.Value,
            _ => USTrade6x9Width
        };
    }

    public static double MeasureAverageCharWidth(string fontFamily, double fontSize)
    {
        var typeface = new Typeface(fontFamily);
        var text = new FormattedText(
            MeasureSample,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            null);

        return text.Width / MeasureSample.Length;
    }

    public static double Calculate(AppSettings settings)
    {
        var textBlockInches = GetTextBlockWidthInches(settings.BookPageFormat, settings.BookTextBlockWidth);
        var textBlockPx = textBlockInches * LogicalDpi;

        var bookAvgChar = MeasureAverageCharWidth(settings.BookFontFamily, settings.BookFontSize);
        var editorAvgChar = MeasureAverageCharWidth(settings.EditorFontFamily, settings.EditorFontSize);

        if (bookAvgChar <= 0) return MinEditorWidth;

        var charsPerLine = textBlockPx / bookAvgChar;

        // Editor content width = chars * editor avg char + horizontal padding (24px each side)
        var editorContentWidth = charsPerLine * editorAvgChar;
        var totalWidth = editorContentWidth + 48; // 24px padding on each side

        var result = Math.Max(totalWidth, MinEditorWidth);
        Log.Debug($"[BookWidth] Calculate: format={settings.BookPageFormat}, blockIn={textBlockInches:F2}, blockPx={textBlockPx:F1}, bookFont={settings.BookFontFamily}@{settings.BookFontSize}, editorFont={settings.EditorFontFamily}@{settings.EditorFontSize}, bookAvg={bookAvgChar:F2}, editorAvg={editorAvgChar:F2}, cpl={charsPerLine:F1}, result={result:F1}");
        return result;
    }

    public static int EstimateCharsPerLine(AppSettings settings)
    {
        var textBlockInches = GetTextBlockWidthInches(settings.BookPageFormat, settings.BookTextBlockWidth);
        var textBlockPx = textBlockInches * LogicalDpi;
        var bookAvgChar = MeasureAverageCharWidth(settings.BookFontFamily, settings.BookFontSize);

        return bookAvgChar > 0 ? (int)Math.Round(textBlockPx / bookAvgChar) : 65;
    }

    public static readonly string[] PageFormats = ["USTrade6x9", "Digest5_5x8_5", "A5", "MassMarket", "Custom"];

    public static string GetPageFormatDisplayName(string format)
    {
        return format switch
        {
            "USTrade6x9" => "US Trade (6×9)",
            "Digest5_5x8_5" => "Digest (5.5×8.5)",
            "A5" => "A5 (5.83×8.27)",
            "MassMarket" => "Mass Market (4.25×6.87)",
            "Custom" => "Custom",
            _ => format
        };
    }
}
