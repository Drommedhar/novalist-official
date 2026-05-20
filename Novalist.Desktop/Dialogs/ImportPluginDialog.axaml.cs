using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Novalist.Core.Services;

namespace Novalist.Desktop.Dialogs;

public partial class ImportPluginDialog : UserControl
{
    private PluginDetectionResult? _detectionResult;
    private bool _importing;

    /// <summary>
    /// The path to the successfully imported project, or null if cancelled/failed.
    /// </summary>
    public string? ImportedProjectPath { get; private set; }

    /// <summary>
    /// The full import result including app-level settings to merge.
    /// </summary>
    public PluginImportResult? ImportResult { get; private set; }

    public TaskCompletionSource DialogClosed { get; } = new();

    /// <summary>
    /// Delegate for showing a folder picker. Provided by MainWindow.
    /// </summary>
    public Func<Task<string?>>? BrowseFolder { get; set; }

    public ImportPluginDialog()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && !_importing)
        {
            ImportedProjectPath = null;
            DialogClosed.TrySetResult();
        }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] // folder picker + real PluginImportService vault detection (file IO); async-void UI continuation
    private async void OnBrowseVault(object? sender, RoutedEventArgs e)
    {
        if (BrowseFolder == null) return;
        var folder = await BrowseFolder();
        if (string.IsNullOrEmpty(folder)) return;

        VaultPathBox.Text = folder;
        ErrorText.IsVisible = false;

        // Detect plugin projects
        try
        {
            _detectionResult = await PluginImportService.DetectPluginProjectAsync(folder);

            if (_detectionResult.Projects.Count == 0)
            {
                ShowError(Localization.Loc.T("import.noProjectsFound"));
                return;
            }

            if (_detectionResult.Projects.Count > 1)
            {
                ProjectComboBox.ItemsSource = _detectionResult.Projects;
                ProjectComboBox.DisplayMemberBinding = new Avalonia.Data.ReflectionBinding("Name");
                ProjectComboBox.SelectedIndex = 0;
                ProjectSelectorPanel.IsVisible = true;
            }
            else
            {
                ProjectSelectorPanel.IsVisible = false;
            }

            // Pre-fill project name from the detected project
            var selectedProject = _detectionResult.Projects[0];
            if (string.IsNullOrEmpty(ProjectNameBox.Text))
                ProjectNameBox.Text = selectedProject.Name;
            if (string.IsNullOrEmpty(BookNameBox.Text))
                BookNameBox.Text = selectedProject.Name;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void OnProjectSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ProjectComboBox.SelectedItem is PluginProjectInfo project)
        {
            if (string.IsNullOrEmpty(ProjectNameBox.Text) || (_detectionResult != null &&
                _detectionResult.Projects.Exists(p => p.Name == ProjectNameBox.Text)))
            {
                ProjectNameBox.Text = project.Name;
            }
            if (string.IsNullOrEmpty(BookNameBox.Text) || (_detectionResult != null &&
                _detectionResult.Projects.Exists(p => p.Name == BookNameBox.Text)))
            {
                BookNameBox.Text = project.Name;
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] // folder picker (file dialog); async-void UI continuation
    private async void OnBrowseOutput(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (BrowseFolder == null) return;
            var folder = await BrowseFolder();
            if (!string.IsNullOrEmpty(folder))
                OutputPathBox.Text = folder;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] // thin wiring: validates then hands off to the real import (interop); logic lives in TryBuildImportRequest
    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (_importing) return;
        if (!TryBuildImportRequest(out var vaultPath, out var projectName, out var bookName, out var outputPath))
            return;
        await RunImportAsync(vaultPath!, projectName!, bookName!, outputPath!);
    }

    /// <summary>
    /// Reads + validates the import form. Returns false (after surfacing an error)
    /// when a required field is missing; on success the out values are the trimmed
    /// inputs with <paramref name="bookName"/> defaulted to the project name.
    /// </summary>
    private bool TryBuildImportRequest(out string? vaultPath, out string? projectName, out string? bookName, out string? outputPath)
    {
        vaultPath = VaultPathBox.Text?.Trim();
        projectName = ProjectNameBox.Text?.Trim();
        bookName = BookNameBox.Text?.Trim();
        outputPath = OutputPathBox.Text?.Trim();

        if (string.IsNullOrEmpty(vaultPath) || _detectionResult == null || _detectionResult.Projects.Count == 0)
        {
            ShowError(Localization.Loc.T("import.selectVaultFirst"));
            return false;
        }

        if (string.IsNullOrEmpty(projectName))
        {
            ShowError(Localization.Loc.T("import.projectNameRequired"));
            return false;
        }

        if (string.IsNullOrEmpty(outputPath))
        {
            ShowError(Localization.Loc.T("import.outputRequired"));
            return false;
        }

        if (string.IsNullOrEmpty(bookName))
            bookName = projectName;

        return true;
    }

    // Post-validation import execution. Excluded: instantiates the real
    // PluginImportService and imports an Obsidian vault to disk (file IO).
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private async Task RunImportAsync(string vaultPath, string projectName, string bookName, string outputPath)
    {
        var selectedProject = _detectionResult!.Projects.Count > 1
            ? ProjectComboBox.SelectedItem as PluginProjectInfo ?? _detectionResult.Projects[0]
            : _detectionResult.Projects[0];

        // Switch to progress mode
        _importing = true;
        ConfigPanel.IsVisible = false;
        ProgressPanel.IsVisible = true;
        ImportButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        ErrorText.IsVisible = false;

        try
        {
            var service = new PluginImportService();
            service.ProgressChanged = (step, current, total) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    ProgressText.Text = total > 0 ? $"{step} ({current}/{total})" : step;
                });
            };

            var result = await service.ImportAsync(
                vaultPath,
                selectedProject.Path,
                outputPath,
                projectName,
                bookName);

            ImportResult = result;
            ImportedProjectPath = result.ProjectPath;

            // Write import log for debugging
            if (service.Log.Count > 0 && !string.IsNullOrEmpty(result.ProjectPath))
            {
                try
                {
                    var logPath = Path.Combine(result.ProjectPath, "import-log.txt");
                    await File.WriteAllLinesAsync(logPath, service.Log);
                }
                catch { /* ignore log write errors */ }
            }

            DialogClosed.TrySetResult();
        }
        catch (Exception ex)
        {
            // Show error and return to config
            _importing = false;
            ConfigPanel.IsVisible = true;
            ProgressPanel.IsVisible = false;
            ImportButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
            ShowError(ex.Message);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        if (_importing) return;
        ImportedProjectPath = null;
        DialogClosed.TrySetResult();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
