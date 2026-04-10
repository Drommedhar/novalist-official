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

    private async void OnBrowseOutput(object? sender, RoutedEventArgs e)
    {
        if (BrowseFolder == null) return;
        var folder = await BrowseFolder();
        if (!string.IsNullOrEmpty(folder))
            OutputPathBox.Text = folder;
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (_importing) return;

        // Validate inputs
        var vaultPath = VaultPathBox.Text?.Trim();
        var projectName = ProjectNameBox.Text?.Trim();
        var bookName = BookNameBox.Text?.Trim();
        var outputPath = OutputPathBox.Text?.Trim();

        if (string.IsNullOrEmpty(vaultPath) || _detectionResult == null || _detectionResult.Projects.Count == 0)
        {
            ShowError(Localization.Loc.T("import.selectVaultFirst"));
            return;
        }

        if (string.IsNullOrEmpty(projectName))
        {
            ShowError(Localization.Loc.T("import.projectNameRequired"));
            return;
        }

        if (string.IsNullOrEmpty(outputPath))
        {
            ShowError(Localization.Loc.T("import.outputRequired"));
            return;
        }

        if (string.IsNullOrEmpty(bookName))
            bookName = projectName;

        var selectedProject = _detectionResult.Projects.Count > 1
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
