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
            .Select(f => new ExportExtensionFormatDto(f.FormatKey, f.DisplayName, f.FileExtension))
            .ToArray();

    [JsonRpcMethod("export/timelineOutline")]
    public async Task<ExportResultDto> TimelineOutlineAsync(string outputPath)
    {
        var service = new ExportService(_workspace.Projects, new EntityService(_workspace.Projects));
        await service.ExportTimelineOutlineAsync(outputPath);
        var info = new FileInfo(outputPath);
        return new ExportResultDto(outputPath, info.Exists, info.Exists ? info.Length : 0);
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
        string[]? includedStages = null)
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
                    _workspace.Settings.Effective.AutoReplacementLanguage)
            };
            if (parsedFormat == ExportFormat.Codex)
            {
                await service.ExportCodexAsync(options, outputPath);
            }
            else if (parsedFormat == ExportFormat.CodexPdf)
            {
                await service.ExportCodexPdfAsync(options, outputPath);
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
            await descriptor.Export(new ExportContext
            {
                ProjectRoot = _workspace.Projects.ProjectRoot ?? string.Empty,
                OutputPath = outputPath,
                BookName = string.IsNullOrWhiteSpace(title) ? "Untitled" : title
            });
        }

        var info = new FileInfo(outputPath);
        return new ExportResultDto(outputPath, info.Exists, info.Exists ? info.Length : 0);
    }
}

public sealed record ExportResultDto(string OutputPath, bool Success, long SizeBytes);

public sealed record ExportExtensionFormatDto(string FormatKey, string DisplayName, string FileExtension);
