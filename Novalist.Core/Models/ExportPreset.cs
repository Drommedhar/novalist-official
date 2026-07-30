namespace Novalist.Core.Models;

/// <summary>
/// Named export preset bundling font / spacing / margin / heading conventions.
/// Built-ins live in <see cref="ExportPresets"/>; users can future-extend
/// with custom presets stored alongside <see cref="ProjectMetadata"/>.
/// </summary>
public sealed record ExportPreset
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    public string BodyFontFamily { get; init; } = "Georgia";
    public double BodyFontSizePt { get; init; } = 12;
    public double LineSpacingMultiplier { get; init; } = 1.5;
    public double MarginInches { get; init; } = 1.0;
    public double FirstLineIndentInches { get; init; } = 0.35;
    public double ChapterTopMarginInches { get; init; } = 2.0;
    public string SceneSeparator { get; init; } = "* * *";

    /// <summary>
    /// The line printed at the top of every page, with placeholders resolved.
    ///
    /// Empty keeps the submission default - surname and short title - which is
    /// what every manuscript export printed when the running head could not be
    /// authored at all. A layout that wants "The Salt Road / Chapter Four"
    /// says so with placeholders rather than being told it cannot.
    /// </summary>
    public string RunningHead { get; init; } = string.Empty;
    public bool DoubleSpaced { get; init; }
    public bool ShunnHeader { get; init; }

    /// <summary>
    /// Lays the text out on the German Normseite grid: every line hard-wrapped
    /// to <see cref="GridColumns"/> monospace columns and a page break forced
    /// every <see cref="GridLines"/> lines, so the page count is exact.
    /// Honoured by the DOCX writer only.
    /// </summary>
    public bool NormseitenGrid { get; init; }
    /// <summary>Monospace columns per line on the Normseite grid.</summary>
    public int GridColumns { get; init; } = 60;
    /// <summary>Lines per page on the Normseite grid.</summary>
    public int GridLines { get; init; } = 30;
    /// <summary>Exact line height in points. 0 keeps the multiplier-based spacing.</summary>
    public double LineHeightPt { get; init; }

    /// <summary>Page geometry in centimetres, used by the Normseite grid.</summary>
    public double PageWidthCm { get; init; } = 21.0;
    public double PageHeightCm { get; init; } = 29.7;
    public double MarginTopCm { get; init; } = 2.5;
    public double MarginBottomCm { get; init; } = 2.5;
    public double MarginLeftCm { get; init; } = 2.5;
    public double MarginRightCm { get; init; } = 2.5;
    public double HeaderDistanceCm { get; init; } = 1.5;

    /// <summary>Usable text width in centimetres (page width less both margins).</summary>
    public double TextWidthCm => PageWidthCm - MarginLeftCm - MarginRightCm;

    /// <summary>
    /// Print each scene's title above it. Off for a novel, where scenes are
    /// separated by an ornament and nothing else; on for a collection or a
    /// working draft where the titles are how the writer navigates.
    /// </summary>
    public bool ShowSceneTitles { get; init; }

    /// <summary>
    /// How a chapter heading reads. <c>{number}</c> and <c>{title}</c> are
    /// substituted; the default is the title alone, which is what a novel
    /// whose chapters are named wants. Leaving <c>{title}</c> out of the
    /// format is how a chapter ships numbered and untitled.
    /// </summary>
    public string ChapterTitleFormat { get; init; } = "{title}";

    /// <summary>
    /// Which numerals <c>{number}</c> writes. A print edition that reads
    /// "Chapter Seven" and an ebook that reads "7" are the same book in two
    /// layouts, which is the whole reason this sits on the layout.
    /// </summary>
    public ChapterNumberStyle ChapterNumberStyle { get; init; } = ChapterNumberStyle.Arabic;

    /// <summary>Sets the finished heading in capitals, as print editions often do.</summary>
    public bool ChapterHeadingUppercase { get; init; }

    /// <summary>
    /// Sets the first letter of each chapter as a drop cap. Honoured where a
    /// format has a real one - EPUB, DOCX, PDF and LaTeX; Markdown has no
    /// typography to carry it and prints the letter normally.
    /// </summary>
    public bool DropCap { get; init; }

    /// <summary>
    /// How many words of the first sentence are set in small capitals after a
    /// drop cap. Zero is off. The convention is two or three - enough to carry
    /// the eye out of the initial and back into the line.
    /// </summary>
    public int LeadInSmallCapsWords { get; init; }

    /// <summary>
    /// Extra CSS appended to the EPUB stylesheet. The one place a writer can
    /// reach the look of the ebook itself rather than of the page.
    /// </summary>
    public string EbookCss { get; init; } = string.Empty;

    /// <summary>
    /// The page as a print shop describes it: trim size, inside and outside
    /// margins, gutter, bleed. Null keeps the manuscript page - US Letter with
    /// one symmetric margin - which is right for a submission and wrong for
    /// anything going to a printer.
    /// </summary>
    public PrintSpec? Print { get; init; }

    /// <summary>
    /// False for a built-in. User presets are stored on the book and can be
    /// edited or deleted; built-ins can only be copied.
    /// </summary>
    public bool IsCustom { get; init; }

    /// <summary>The chapter heading for one chapter, with the format applied.</summary>
    public string ChapterHeading(int number, string title)
    {
        var format = string.IsNullOrWhiteSpace(ChapterTitleFormat) ? "{title}" : ChapterTitleFormat;
        var heading = format
            .Replace("{number}", FormatNumber(number, ChapterNumberStyle))
            .Replace("{title}", title ?? string.Empty)
            .Trim();
        return ChapterHeadingUppercase ? heading.ToUpperInvariant() : heading;
    }

    /// <summary>One chapter number, in the layout's numerals.</summary>
    public static string FormatNumber(int number, ChapterNumberStyle style) => style switch
    {
        ChapterNumberStyle.RomanUpper => ToRoman(number),
        ChapterNumberStyle.RomanLower => ToRoman(number).ToLowerInvariant(),
        ChapterNumberStyle.Words => ToWords(number),
        _ => number.ToString()
    };

    private static readonly (int Value, string Numeral)[] RomanNumerals =
    [
        (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"),
        (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
    ];

    private static string ToRoman(int number)
    {
        // Below one there is no numeral to write, so the digit stands in rather
        // than the heading silently losing its number.
        if (number <= 0) return number.ToString();
        var sb = new System.Text.StringBuilder();
        foreach (var (value, numeral) in RomanNumerals)
        {
            while (number >= value)
            {
                sb.Append(numeral);
                number -= value;
            }
        }
        return sb.ToString();
    }

    private static readonly string[] Ones =
    [
        "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine",
        "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen",
        "Seventeen", "Eighteen", "Nineteen"
    ];

    private static readonly string[] Tens =
    [
        "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"
    ];

    /// <summary>
    /// English words for a chapter number. Deliberately English only: this is
    /// the numeral on a heading a writer chose to spell out, and getting it
    /// wrong in another language is worse than not offering it.
    /// </summary>
    private static string ToWords(int number)
    {
        if (number < 0 || number > 999) return number.ToString();
        if (number < 20) return Ones[number];
        if (number < 100)
        {
            var tens = Tens[number / 10];
            return number % 10 == 0 ? tens : $"{tens}-{Ones[number % 10].ToLowerInvariant()}";
        }
        var hundreds = $"{Ones[number / 100]} Hundred";
        return number % 100 == 0 ? hundreds : $"{hundreds} {ToWords(number % 100).ToLowerInvariant()}";
    }
}

/// <summary>Which numerals a chapter heading writes its number in.</summary>
public enum ChapterNumberStyle
{
    Arabic,
    RomanUpper,
    RomanLower,
    Words
}

public static class ExportPresets
{
    public const string DefaultId = "default";
    public const string ShunnId = "shunn-manuscript";
    public const string EbookFlowId = "ebook-flow";
    public const string NormseitenId = "normseiten";
    public const string LargePrintId = "large-print";

    public static IReadOnlyList<ExportPreset> All { get; } =
    [
        new ExportPreset
        {
            Id = DefaultId,
            DisplayName = "Default",
            Description = "Georgia 12pt, 1.5 line spacing — readable PDF/EPUB.",
            BodyFontFamily = "Georgia",
            BodyFontSizePt = 12,
            LineSpacingMultiplier = 1.5,
            MarginInches = 1.0,
            FirstLineIndentInches = 0.35,
            ChapterTopMarginInches = 2.0,
            SceneSeparator = "* * *"
        },
        new ExportPreset
        {
            Id = ShunnId,
            DisplayName = "Shunn Manuscript Format",
            Description = "Industry-standard submission format: Courier 12pt, double-spaced, Shunn header.",
            BodyFontFamily = "Courier New",
            BodyFontSizePt = 12,
            LineSpacingMultiplier = 2.0,
            MarginInches = 1.0,
            FirstLineIndentInches = 0.5,
            ChapterTopMarginInches = 3.0,
            SceneSeparator = "#",
            DoubleSpaced = true,
            ShunnHeader = true
        },
        new ExportPreset
        {
            Id = EbookFlowId,
            DisplayName = "Ebook Flow",
            Description = "Tighter spacing for digital reading: Georgia 11pt, 1.4 line spacing, narrower margins.",
            BodyFontFamily = "Georgia",
            BodyFontSizePt = 11,
            LineSpacingMultiplier = 1.4,
            MarginInches = 0.6,
            FirstLineIndentInches = 0.25,
            ChapterTopMarginInches = 1.4,
            SceneSeparator = "* * *"
        },
        new ExportPreset
        {
            // A large-print edition is not a PDF somebody zoomed. It is set at
            // 16pt with generous leading and narrower outside margins so the
            // longer lines still fit the page, and it is the one format a
            // partially sighted reader can actually read - which no amount of
            // "you can change the font size in the reader" replaces for print.
            Id = LargePrintId,
            DisplayName = "Large Print",
            Description = "16pt with generous leading and narrow margins, for a large-print edition.",
            BodyFontFamily = "Verdana",
            BodyFontSizePt = 16,
            LineSpacingMultiplier = 1.6,
            // Narrower than the default: at 16pt a one-inch margin either side
            // leaves too little measure and the text breaks badly.
            MarginInches = 0.6,
            // No first-line indent. A large-print edition marks paragraphs with
            // space rather than indentation, which is easier to follow.
            FirstLineIndentInches = 0,
            ChapterTopMarginInches = 1.5,
            SceneSeparator = "* * *"
        },
        new ExportPreset
        {
            Id = NormseitenId,
            DisplayName = "Normseiten",
            Description = "German standard pages: Courier New 12pt, 60 characters per line, 30 lines per page (DOCX only).",
            BodyFontFamily = "Courier New",
            BodyFontSizePt = 12,
            LineSpacingMultiplier = 2.0,
            FirstLineIndentInches = 0,
            ChapterTopMarginInches = 0,
            SceneSeparator = "* * *",
            NormseitenGrid = true,
            GridColumns = 60,
            GridLines = 30,
            LineHeightPt = 20,
            PageWidthCm = 21.0,
            PageHeightCm = 29.7,
            MarginTopCm = 3.0,
            MarginBottomCm = 4.5,
            MarginLeftCm = 2.5,
            MarginRightCm = 3.2,
            HeaderDistanceCm = 1.5
        }
    ];

    public static ExportPreset GetById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return All[0];
        return All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];
    }
}
