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
        p.SceneSeparator, p.DoubleSpaced, p.ShowSceneTitles,
        p.ChapterTitleFormat, p.EbookCss);

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
        DoubleSpaced = d.DoubleSpaced,
        ShowSceneTitles = d.ShowSceneTitles,
        ChapterTitleFormat = string.IsNullOrWhiteSpace(d.ChapterTitleFormat)
            ? "{title}"
            : d.ChapterTitleFormat,
        EbookCss = d.EbookCss ?? string.Empty
    };

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
    bool DoubleSpaced,
    bool ShowSceneTitles,
    string ChapterTitleFormat,
    string EbookCss);
