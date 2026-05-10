namespace Novalist.Core.Models;

/// <summary>
/// Named export preset bundling font / spacing / margin / heading conventions.
/// Built-ins live in <see cref="ExportPresets"/>; users can future-extend
/// with custom presets stored alongside <see cref="ProjectMetadata"/>.
/// </summary>
public sealed class ExportPreset
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
}

public static class ExportPresets
{
    public const string DefaultId = "default";
    public const string ShunnId = "shunn-manuscript";
    public const string EbookFlowId = "ebook-flow";

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
        }
    ];

    public static ExportPreset GetById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return All[0];
        return All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];
    }
}
