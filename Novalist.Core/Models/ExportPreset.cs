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
    /// whose chapters are named wants.
    /// </summary>
    public string ChapterTitleFormat { get; init; } = "{title}";

    /// <summary>
    /// Extra CSS appended to the EPUB stylesheet. The one place a writer can
    /// reach the look of the ebook itself rather than of the page.
    /// </summary>
    public string EbookCss { get; init; } = string.Empty;

    /// <summary>
    /// False for a built-in. User presets are stored on the book and can be
    /// edited or deleted; built-ins can only be copied.
    /// </summary>
    public bool IsCustom { get; init; }

    /// <summary>The chapter heading for one chapter, with the format applied.</summary>
    public string ChapterHeading(int number, string title)
    {
        var format = string.IsNullOrWhiteSpace(ChapterTitleFormat) ? "{title}" : ChapterTitleFormat;
        return format
            .Replace("{number}", number.ToString())
            .Replace("{title}", title ?? string.Empty)
            .Trim();
    }
}

public static class ExportPresets
{
    public const string DefaultId = "default";
    public const string ShunnId = "shunn-manuscript";
    public const string EbookFlowId = "ebook-flow";
    public const string NormseitenId = "normseiten";

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
