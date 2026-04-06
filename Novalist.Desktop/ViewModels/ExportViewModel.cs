using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;
using Novalist.Sdk.Models;

namespace Novalist.Desktop.ViewModels;

public partial class ExportChapterItem : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected = true;

    public string Guid { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Order { get; set; }
}

public partial class ExportViewModel : ObservableObject
{
    private readonly IProjectService _projectService;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _author = string.Empty;

    [ObservableProperty]
    private int _selectedFormatIndex;

    [ObservableProperty]
    private bool _includeTitlePage = true;

    [ObservableProperty]
    private bool _smfPreset;

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ExportChapterItem> _chapters = [];

    public bool IsSmfVisible => SelectedFormatIndex is 1 or 2; // DOCX or PDF

    public string ExportButtonText => Loc.T("export.exportButton", FormatLabel);

    public int SelectedCount => Chapters.Count(c => c.IsSelected);

    public string SelectedCountText => Loc.T("export.selectedCount", SelectedCount);

    public Func<string, string, Task<string?>>? ShowSaveFileDialog { get; set; }

    /// <summary>Extension export formats appended after the 4 built-in ones.</summary>
    public List<ExportFormatDescriptor> ExtensionFormats { get; } = [];

    /// <summary>Display names for the ComboBox (built-in + extension).</summary>
    public ObservableCollection<string> FormatNames { get; } = [];

    private const int BuiltInFormatCount = 4;

    private bool IsExtensionFormat => SelectedFormatIndex >= BuiltInFormatCount;

    private ExportFormatDescriptor? SelectedExtensionFormat =>
        IsExtensionFormat && SelectedFormatIndex - BuiltInFormatCount < ExtensionFormats.Count
            ? ExtensionFormats[SelectedFormatIndex - BuiltInFormatCount]
            : null;

    private string FormatLabel => SelectedFormatIndex switch
    {
        0 => "EPUB",
        1 => "DOCX",
        2 => "PDF",
        3 => "Markdown",
        _ => SelectedExtensionFormat?.DisplayName ?? "EPUB"
    };

    private ExportFormat SelectedFormat => SelectedFormatIndex switch
    {
        0 => ExportFormat.Epub,
        1 => ExportFormat.Docx,
        2 => ExportFormat.Pdf,
        3 => ExportFormat.Markdown,
        _ => ExportFormat.Epub
    };

    private string FileExtension => SelectedFormatIndex switch
    {
        0 => ".epub",
        1 => ".docx",
        2 => ".pdf",
        3 => ".md",
        _ => SelectedExtensionFormat?.FileExtension ?? ".epub"
    };

    public ExportViewModel(IProjectService projectService)
    {
        _projectService = projectService;
    }

    public void LoadExtensionFormats(IReadOnlyList<ExportFormatDescriptor> formats)
    {
        System.Diagnostics.Debug.WriteLine($"[Export] LoadExtensionFormats called with {formats.Count} formats");
        ExtensionFormats.Clear();
        ExtensionFormats.AddRange(formats);
        RebuildFormatNames();
        System.Diagnostics.Debug.WriteLine($"[Export] FormatNames count after rebuild: {FormatNames.Count}");
    }

    private void RebuildFormatNames()
    {
        FormatNames.Clear();
        FormatNames.Add(Loc.T("export.formatEpub"));
        FormatNames.Add(Loc.T("export.formatDocx"));
        FormatNames.Add(Loc.T("export.formatPdf"));
        FormatNames.Add(Loc.T("export.formatMarkdown"));
        foreach (var ext in ExtensionFormats)
        {
            System.Diagnostics.Debug.WriteLine($"[Export] Adding extension format: '{ext.DisplayName}'");
            FormatNames.Add(ext.DisplayName);
        }
    }

    public void Refresh()
    {
        if (FormatNames.Count == 0)
            RebuildFormatNames();

        var chapters = _projectService.GetChaptersOrdered();
        var items = chapters.Select(c => new ExportChapterItem
        {
            Guid = c.Guid,
            DisplayName = $"{c.Order}. {c.Title}",
            Order = c.Order,
            IsSelected = true
        }).ToList();

        // Subscribe to selection changes
        foreach (var item in items)
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ExportChapterItem.IsSelected))
                {
                    OnPropertyChanged(nameof(SelectedCount));
                    OnPropertyChanged(nameof(SelectedCountText));
                }
            };

        Chapters = new ObservableCollection<ExportChapterItem>(items);

        // Default title from project name
        if (string.IsNullOrWhiteSpace(Title))
            Title = _projectService.CurrentProject?.Name ?? "My Novel";

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedCountText));
    }

    partial void OnSelectedFormatIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsSmfVisible));
        OnPropertyChanged(nameof(ExportButtonText));

        // Reset SMF when switching away from DOCX/PDF
        if (!IsSmfVisible)
            SmfPreset = false;
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var ch in Chapters)
            ch.IsSelected = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var ch in Chapters)
            ch.IsSelected = false;
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (IsExporting || SelectedCount == 0 || ShowSaveFileDialog == null)
            return;

        var filename = SanitizeFilename(Title) + FileExtension;
        var outputPath = await ShowSaveFileDialog.Invoke(filename, FormatLabel);
        if (string.IsNullOrEmpty(outputPath))
            return;

        IsExporting = true;
        StatusMessage = Loc.T("export.exporting");

        try
        {
            if (IsExtensionFormat && SelectedExtensionFormat is { } extFmt)
            {
                var ctx = new ExportContext
                {
                    ProjectRoot = _projectService.ProjectRoot ?? string.Empty,
                    OutputPath = outputPath,
                    BookName = string.IsNullOrWhiteSpace(Title) ? "Untitled" : Title
                };
                if (extFmt.Export != null)
                    await extFmt.Export(ctx);
            }
            else
            {
                var options = new ExportOptions
                {
                    Format = SelectedFormat,
                    IncludeTitlePage = IncludeTitlePage,
                    Title = string.IsNullOrWhiteSpace(Title) ? "Untitled" : Title,
                    Author = Author,
                    SmfPreset = SmfPreset && IsSmfVisible,
                    SelectedChapterGuids = Chapters
                        .Where(c => c.IsSelected)
                        .Select(c => c.Guid)
                        .ToList()
                };

                var exportService = new ExportService(_projectService);
                await exportService.ExportAsync(options, outputPath);
            }

            StatusMessage = Loc.T("export.exportSuccess");
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.T("export.exportFailed", ex.Message);
        }
        finally
        {
            IsExporting = false;
        }
    }

    private static string SanitizeFilename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "export";
        var sanitized = System.Text.RegularExpressions.Regex.Replace(name, @"[^a-zA-Z0-9\-_\s]", "").Replace(' ', '_');
        return string.IsNullOrEmpty(sanitized) ? "export" : sanitized;
    }
}
