using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk.Models;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Manuscript and codex export to the built-in formats.</summary>
public sealed class ExportRpc
{
    private readonly Workspace _workspace;

    public ExportRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    [JsonRpcMethod("export/formats")]
    public string[] Formats() => Enum.GetNames<ExportFormat>();

    /// <summary>Named layout presets (font/spacing/margins), e.g. Shunn Manuscript Format.</summary>
    /// <summary>Export formats contributed by loaded extensions (empty when none).</summary>
    [JsonRpcMethod("export/extensionFormats")]
    public ExportExtensionFormatDto[] ExtensionFormats() =>
        _workspace.ExtensionsHost.ExportFormats
            .Select(f => new ExportExtensionFormatDto(
                f.FormatKey, f.DisplayName, f.FileExtension, f.SupportsCover))
            .ToArray();

    /// <summary>
    /// The book's compile-time replacements, in the order they run.
    /// </summary>
    [JsonRpcMethod("export/replacements")]
    public ExportReplacementDto[] Replacements()
        => [.. (_workspace.Projects.ActiveBook?.ExportReplacements ?? [])
            .OrderBy(r => r.Order)
            .Select(r => new ExportReplacementDto(
                r.Id, r.Find, r.Replace, r.IsRegex, r.MatchCase, r.Enabled, r.Order))];

    /// <summary>
    /// Replaces the whole list, in the order given. Whole-list rather than
    /// per-rule because order is what a rule set means: an earlier rule's output
    /// is a later rule's input, and reordering is the common edit.
    /// </summary>
    [JsonRpcMethod("export/saveReplacements")]
    public async Task<ExportReplacementDto[]> SaveReplacementsAsync(ExportReplacementDto[] rules)
    {
        var book = _workspace.Projects.ActiveBook
            ?? throw new InvalidOperationException("No active book.");

        book.ExportReplacements = [.. (rules ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r.Find))
            .Select((r, index) => new Core.Models.ExportReplacement
            {
                Id = string.IsNullOrWhiteSpace(r.Id) ? Guid.NewGuid().ToString() : r.Id,
                Find = r.Find,
                Replace = r.Replace ?? string.Empty,
                IsRegex = r.IsRegex,
                MatchCase = r.MatchCase,
                Enabled = r.Enabled,
                Order = index
            })];
        await _workspace.Projects.SaveProjectAsync();
        return Replacements();
    }

    /// <summary>Every token an export resolves, for the UI to list.</summary>
    /// <summary>
    /// Every section title the Codex uses, so the picker offers what this
    /// project has rather than asking for it to be typed the same way twice.
    /// </summary>
    [JsonRpcMethod("export/codexSections")]
    public async Task<string[]> CodexSectionsAsync()
    {
        var entities = new EntityService(_workspace.Projects);
        var titles = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        // Sections are on each concrete type rather than on IEntityData, so the
        // shapes are matched here rather than widening the interface for one
        // reader.
        void Collect(IEnumerable<Core.Models.IEntityData> all)
        {
            foreach (var entity in all)
            {
                var sections = entity switch
                {
                    Core.Models.CharacterData c => c.Sections,
                    Core.Models.LocationData l => l.Sections,
                    Core.Models.ItemData i => i.Sections,
                    Core.Models.LoreData lo => lo.Sections,
                    _ => ((Core.Models.CustomEntityData)entity).Sections
                };
                foreach (var section in sections)
                    if (!string.IsNullOrWhiteSpace(section.Title))
                        titles.Add(section.Title.Trim());
            }
        }

        Collect(await entities.LoadCharactersAsync());
        Collect(await entities.LoadLocationsAsync());
        Collect(await entities.LoadItemsAsync());
        Collect(await entities.LoadLoreAsync());
        foreach (var typeDef in entities.GetCustomEntityTypes())
            Collect(await entities.LoadCustomEntitiesAsync(typeDef.TypeKey));

        return [.. titles];
    }

    /// <summary>
    /// The stores this book has links for, so an export can be built for one.
    /// </summary>
    [JsonRpcMethod("export/retailers")]
    public RetailerDto[] Retailers()
        => [.. (_workspace.Projects.ActiveBook?.Publishing.Retailers ?? [])
            .Select(r => new RetailerDto(r.Key, r.Name, r.Url, r.ProductId))];

    [JsonRpcMethod("export/tokens")]
    public string[] Tokens() => [.. Core.Services.ExportTokens.Known];

    [JsonRpcMethod("export/timelineOutline")]
    public async Task<ExportResultDto> TimelineOutlineAsync(string outputPath)
    {
        var service = new ExportService(_workspace.Projects, new EntityService(_workspace.Projects));
        await service.ExportTimelineOutlineAsync(outputPath);
        var info = new FileInfo(outputPath);
        return new ExportResultDto(outputPath, info.Exists, info.Exists ? info.Length : 0);
    }

    /// <summary>
    /// What this export would contain, without writing anything. Runs the same
    /// compile the export runs, so held-back scenes and the stage filter are
    /// counted rather than assumed.
    /// </summary>
    [JsonRpcMethod("export/preview")]
    public async Task<ExportPreviewDto> PreviewAsync(
        string[] selectedChapterGuids,
        string? presetId = null,
        string[]? includedStages = null)
    {
        var service = new ExportService(_workspace.Projects);
        var preview = await service.PreviewAsync(new ExportOptions
        {
            PresetId = presetId,
            CustomPresets = [.. _workspace.Projects.ActiveBook?.ExportPresets ?? []],
            SelectedChapterGuids = selectedChapterGuids.ToList(),
            IncludedStages = includedStages?.ToList()
        });
        return new ExportPreviewDto(
            preview.Chapters, preview.Scenes, preview.Words,
            preview.Characters, preview.Pages, preview.PagesAreExact,
            preview.UndescribedImages);
    }

    [JsonRpcMethod("export/run")]
    public async Task<ExportResultDto> RunAsync(
        string format,
        string outputPath,
        string title,
        string author,
        bool includeTitlePage,
        string[] selectedChapterGuids,
        string? presetId = null,
        string[]? selectedEntityKeys = null,
        Dictionary<string, string>? labels = null,
        bool includeCover = true,
        string[]? includedStages = null,
        int tocDepth = 1,
        string? tocTitle = null,
        string? referenceDocPath = null,
        string[]? codexParts = null,
        string[]? sectionTitles = null,
        string? retailerKey = null)
    {
        if (Enum.TryParse<ExportFormat>(format, out var parsedFormat))
        {
            var service = new ExportService(_workspace.Projects, new EntityService(_workspace.Projects));
            var options = new ExportOptions
            {
                Format = parsedFormat,
                Title = title,
                Author = author,
                IncludeTitlePage = includeTitlePage,
                PresetId = presetId,
                // Without these a layout the writer authored resolves to
                // nothing and the export silently comes out in the default.
                CustomPresets = [.. _workspace.Projects.ActiveBook?.ExportPresets ?? []],
                SelectedChapterGuids = selectedChapterGuids.ToList(),
                IncludedStages = includedStages?.ToList(),
                SelectedEntityKeys = selectedEntityKeys?.ToList(),
                Labels = labels,
                // The cover the Dashboard already collects, and the book's
                // writing language rather than a hardcoded "en".
                CoverImagePath = includeCover ? _workspace.ActiveCoverAbsolutePath() ?? string.Empty : string.Empty,
                Language = ExportService.NormalizeLanguageTag(
                    _workspace.Settings.Effective.AutoReplacementLanguage),
                // Contents shape and the publisher's Word template. All three
                // are optional: an older caller passes none and gets exactly
                // the flat chapter list and the built-in styles it always did.
                TocDepth = tocDepth,
                TocTitle = tocTitle ?? string.Empty,
                ReferenceDocPath = referenceDocPath ?? string.Empty,
                // Null means every part, which is what every codex export did
                // before this existed.
                CodexParts = codexParts?.ToList(),
                SelectedSectionTitles = sectionTitles?.ToList(),
                // Which shop this build is for. Empty is a neutral build, which
                // is what every export was before there was anything to say.
                RetailerKey = retailerKey ?? string.Empty
            };
            if (parsedFormat == ExportFormat.Codex)
            {
                await service.ExportCodexAsync(options, outputPath);
            }
            else if (parsedFormat == ExportFormat.CodexPdf)
            {
                await service.ExportCodexPdfAsync(options, outputPath);
            }
            else if (parsedFormat is ExportFormat.Csv or ExportFormat.Json
                     or ExportFormat.CodexCsv or ExportFormat.Opml
                     or ExportFormat.WorldJson or ExportFormat.WorldHtml)
            {
                await service.ExportDataAsync(options, outputPath);
            }
            else if (parsedFormat is ExportFormat.SynopsisReport or ExportFormat.PovReport)
            {
                await service.ExportReportAsync(options, outputPath);
            }
            else
            {
                await service.ExportAsync(options, outputPath);
            }
        }
        else
        {
            var descriptor = _workspace.ExtensionsHost.ExportFormats
                .FirstOrDefault(f => string.Equals(f.FormatKey, format, StringComparison.OrdinalIgnoreCase));
            if (descriptor?.Export == null)
                throw new InvalidOperationException($"Unknown export format: {format}");
            // The same language and cover the built-in formats resolve. Passing
            // a path and a title and nothing else is why every contributed
            // format came out marked English with no cover.
            await descriptor.Export(new ExportContext
            {
                ProjectRoot = _workspace.Projects.ProjectRoot ?? string.Empty,
                OutputPath = outputPath,
                BookName = string.IsNullOrWhiteSpace(title) ? "Untitled" : title,
                Author = author ?? string.Empty,
                Language = ExportService.NormalizeLanguageTag(
                    _workspace.Settings.Effective.AutoReplacementLanguage),
                CoverImagePath = includeCover && descriptor.SupportsCover
                    ? _workspace.ActiveCoverAbsolutePath() ?? string.Empty
                    : string.Empty,
                IncludeTitlePage = includeTitlePage,
                SelectedChapterGuids = [.. selectedChapterGuids]
            });
        }

        var info = new FileInfo(outputPath);
        return new ExportResultDto(outputPath, info.Exists, info.Exists ? info.Length : 0);
    }
}

public sealed record ExportResultDto(string OutputPath, bool Success, long SizeBytes);

/// <summary>
/// Counts for an export that has not run yet. <c>PagesAreExact</c> is true only
/// on the Normseite grid; everywhere else the page count is an estimate and the
/// interface has to say so.
/// </summary>
public sealed record ExportPreviewDto(
    int Chapters, int Scenes, int Words, int Characters, int Pages, bool PagesAreExact,
    /// <summary>Pictures with nothing written about what they show.</summary>
    int UndescribedImages);

/// <summary>One substitution applied to the output and never to the prose.</summary>
public sealed record ExportReplacementDto(
    string Id, string Find, string Replace, bool IsRegex, bool MatchCase, bool Enabled, int Order);

public sealed record ExportExtensionFormatDto(
    string FormatKey, string DisplayName, string FileExtension, bool SupportsCover);
