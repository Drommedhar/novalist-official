using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Export layouts: the four Novalist ships and the ones the writer authored.
/// Built-ins can be copied but not edited, so a preset named after a submission
/// standard keeps meaning that standard.
/// </summary>
public sealed class ExportPresetRpc
{
    private readonly Workspace _workspace;

    public ExportPresetRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private ExportPresetService Service => new(_workspace.Projects);

    [JsonRpcMethod("exportPresets/list")]
    public ExportLayoutDto[] List() => [.. Service.All().Select(ToDto)];

    /// <summary>Copies a preset under a new name. This is how a writer starts
    /// one: an empty layout would be a worse starting point than any of the
    /// four that already work.</summary>
    [JsonRpcMethod("exportPresets/duplicate")]
    public async Task<ExportLayoutDto[]> DuplicateAsync(string sourceId, string displayName)
    {
        await Service.DuplicateAsync(sourceId, displayName);
        return List();
    }

    [JsonRpcMethod("exportPresets/save")]
    public async Task<ExportLayoutDto[]> SaveAsync(ExportLayoutDto preset)
    {
        await Service.SaveAsync(FromDto(preset));
        return List();
    }

    [JsonRpcMethod("exportPresets/delete")]
    public async Task<ExportLayoutDto[]> DeleteAsync(string id)
    {
        await Service.DeleteAsync(id);
        return List();
    }

    private static ExportLayoutDto ToDto(ExportPreset p) => new(
        p.Id, p.DisplayName, p.Description, p.IsCustom,
        p.BodyFontFamily, p.BodyFontSizePt, p.LineSpacingMultiplier,
        p.MarginInches, p.FirstLineIndentInches, p.ChapterTopMarginInches,
        p.SceneSeparator, p.RunningHead, p.DoubleSpaced, p.ShowSceneTitles,
        p.ChapterTitleFormat, p.ChapterNumberStyle.ToString(), p.ChapterHeadingUppercase,
        p.DropCap, p.LeadInSmallCapsWords, p.EbookCss,
        p.Print == null ? null : ToDto(p.Print));

    private static PrintSpecDto ToDto(PrintSpec p) => new(
        p.TrimWidthInches, p.TrimHeightInches,
        p.MarginInsideInches, p.MarginOutsideInches, p.MarginTopInches, p.MarginBottomInches,
        p.MirrorMargins, p.GutterInches, p.GutterFromPageCount, p.BleedInches,
        p.AvoidWidowsAndOrphans, p.MinLinesTogether);

    private static ExportPreset FromDto(ExportLayoutDto d) => new()
    {
        Id = d.Id,
        DisplayName = d.DisplayName ?? string.Empty,
        Description = d.Description ?? string.Empty,
        IsCustom = true,
        BodyFontFamily = string.IsNullOrWhiteSpace(d.BodyFontFamily) ? "Georgia" : d.BodyFontFamily,
        // Clamped so a typo cannot produce a file no reader will open - a zero
        // font size or a margin wider than the page.
        BodyFontSizePt = Clamp(d.BodyFontSizePt, 6, 48, 12),
        LineSpacingMultiplier = Clamp(d.LineSpacingMultiplier, 0.8, 3, 1.5),
        MarginInches = Clamp(d.MarginInches, 0.2, 3, 1),
        FirstLineIndentInches = Clamp(d.FirstLineIndentInches, 0, 2, 0.35),
        ChapterTopMarginInches = Clamp(d.ChapterTopMarginInches, 0, 5, 2),
        SceneSeparator = d.SceneSeparator ?? string.Empty,
        RunningHead = d.RunningHead ?? string.Empty,
        DoubleSpaced = d.DoubleSpaced,
        ShowSceneTitles = d.ShowSceneTitles,
        ChapterTitleFormat = string.IsNullOrWhiteSpace(d.ChapterTitleFormat)
            ? "{title}"
            : d.ChapterTitleFormat,
        ChapterNumberStyle = Enum.TryParse<ChapterNumberStyle>(d.ChapterNumberStyle, out var style)
            ? style
            : ChapterNumberStyle.Arabic,
        ChapterHeadingUppercase = d.ChapterHeadingUppercase,
        DropCap = d.DropCap,
        // Clamped: a lead-in longer than a line is not a lead-in.
        LeadInSmallCapsWords = Math.Clamp(d.LeadInSmallCapsWords, 0, 12),
        EbookCss = d.EbookCss ?? string.Empty,
        Print = d.Print == null ? null : FromDto(d.Print)
    };

    /// <summary>
    /// The page as the writer described it, with every measurement clamped to
    /// something a printer will accept. A trim of zero or a bleed of a foot is
    /// a typo, and a file built from one is rejected rather than printed.
    /// </summary>
    private static PrintSpec FromDto(PrintSpecDto d) => new()
    {
        TrimWidthInches = Clamp(d.TrimWidthInches, 3, 20, 8.5),
        TrimHeightInches = Clamp(d.TrimHeightInches, 3, 20, 11),
        MarginInsideInches = Clamp(d.MarginInsideInches, 0.1, 3, 1),
        MarginOutsideInches = Clamp(d.MarginOutsideInches, 0.1, 3, 1),
        MarginTopInches = Clamp(d.MarginTopInches, 0.1, 3, 1),
        MarginBottomInches = Clamp(d.MarginBottomInches, 0.1, 3, 1),
        MirrorMargins = d.MirrorMargins,
        GutterInches = Clamp(d.GutterInches, 0, 2, 0),
        GutterFromPageCount = d.GutterFromPageCount,
        BleedInches = Clamp(d.BleedInches, 0, 0.5, 0),
        AvoidWidowsAndOrphans = d.AvoidWidowsAndOrphans,
        MinLinesTogether = Math.Clamp(d.MinLinesTogether, 0, 5)
    };

    /// <summary>The trim sizes we know by name, for the picker.</summary>
    [JsonRpcMethod("exportPresets/trims")]
    public TrimDto[] Trims() =>
        [.. PrintSpec.TrimNames.Select(name =>
        {
            var size = PrintSpec.NamedTrim(name)!.Value;
            return new TrimDto(name, size.Width, size.Height);
        })];

    private static double Clamp(double value, double min, double max, double fallback)
        => double.IsFinite(value) && value >= min && value <= max ? value : fallback;
}

/// <summary>
/// One export layout, in full. This is what both the export dropdown and the
/// layout editor read, so the two can never disagree about what exists.
/// <c>IsCustom</c> false means it is built in, and only its copy can be edited.
/// </summary>
public sealed record ExportLayoutDto(
    string Id,
    string DisplayName,
    string Description,
    bool IsCustom,
    string BodyFontFamily,
    double BodyFontSizePt,
    double LineSpacingMultiplier,
    double MarginInches,
    double FirstLineIndentInches,
    double ChapterTopMarginInches,
    string SceneSeparator,
    /// <summary>
    /// The line at the top of every page. Empty keeps the submission default -
    /// surname and short title - which is what every export printed before a
    /// layout could say otherwise. Placeholders resolve here too.
    /// </summary>
    string RunningHead,
    bool DoubleSpaced,
    bool ShowSceneTitles,
    string ChapterTitleFormat,
    /// <summary>Arabic, RomanUpper, RomanLower or Words.</summary>
    string ChapterNumberStyle,
    bool ChapterHeadingUppercase,
    /// <summary>Sets the chapter's first letter as a drop cap.</summary>
    bool DropCap,
    /// <summary>Words after the drop cap set in small capitals; 0 is off.</summary>
    int LeadInSmallCapsWords,
    string EbookCss,
    /// <summary>
    /// The page as a print shop describes it. Null keeps the manuscript page -
    /// US Letter with one symmetric margin - which is right for a submission
    /// and wrong for anything going to a printer.
    /// </summary>
    PrintSpecDto? Print = null);

/// <summary>Trim, margins, gutter and bleed, in inches.</summary>
public sealed record PrintSpecDto(
    double TrimWidthInches,
    double TrimHeightInches,
    double MarginInsideInches,
    double MarginOutsideInches,
    double MarginTopInches,
    double MarginBottomInches,
    bool MirrorMargins,
    double GutterInches,
    bool GutterFromPageCount,
    double BleedInches,
    bool AvoidWidowsAndOrphans,
    int MinLinesTogether);

/// <summary>One trim size we know by name.</summary>
public sealed record TrimDto(string Name, double WidthInches, double HeightInches);
