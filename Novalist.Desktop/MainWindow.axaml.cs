using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Novalist.Core.Services;
using Novalist.Desktop.Dialogs;
using Novalist.Desktop.Localization;
using Novalist.Core.Models;
using Novalist.Desktop.ViewModels;
using Novalist.Desktop.Views;
using Novalist.Sdk.Models;

namespace Novalist.Desktop;

public partial class MainWindow : Window
{
    private bool _isDialogOpen;
    private string _webViewLanguage = App.ReadLanguageFromSettings();
    private GridLength _savedExplorerWidth = new(280);
    private GridLength _savedContextSidebarWidth = new(320);
    public MainWindow()
    {
        InitializeComponent();

        // Restore window state from settings before opening
        Opened += OnWindowOpened;
        Closing += OnWindowClosing;

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.PropertyChanged += OnViewModelPropertyChanged;
                vm.ShowInputDialog = ShowInputDialogAsync;
                vm.ShowConfirmDialog = ShowConfirmDialogAsync;
                vm.ShowSnapshotsDialog = ShowSnapshotsDialogAsync;
                vm.PropertyChanged += WireDashboardOnCreation;

                vm.OpenProjectFromMenuRequested += async () =>
                {
                    var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                    {
                        Title = "Select Folder",
                        AllowMultiple = false
                    });
                    if (folders.Count > 0)
                    {
                        try { await vm.LoadProjectAsync(folders[0].Path.LocalPath); }
                        catch (Exception ex) { vm.StatusText = $"Error: {ex.Message}"; Toast.Show?.Invoke(Loc.T("toast.projectLoadFailed", ex.Message), ToastSeverity.Error); }
                    }
                };

                vm.OpenRecentProjectFromMenuRequested += async (path) =>
                {
                    try { await vm.LoadProjectAsync(path); }
                    catch (Exception ex) { vm.StatusText = $"Error: {ex.Message}"; Toast.Show?.Invoke(Loc.T("toast.projectLoadFailed", ex.Message), ToastSeverity.Error); }
                };

                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainWindowViewModel.IsProjectLoaded) && !vm.IsProjectLoaded)
                        ShowWelcomeIfNeeded();
                };
            }
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainWindowViewModel vm) return;

        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.Explorer):
                WireExplorerDialog(vm.Explorer);
                break;
            case nameof(MainWindowViewModel.EntityPanel):
                WireEntityPanel(vm.EntityPanel);
                break;
            case nameof(MainWindowViewModel.EntityEditor):
                WireEntityEditor(vm.EntityEditor);
                break;
            case nameof(MainWindowViewModel.Export):
                WireExport(vm.Export);
                break;
            case nameof(MainWindowViewModel.ImageGallery):
                WireImageGallery(vm.ImageGallery);
                break;
            case nameof(MainWindowViewModel.IsSettingsOpen):
                if (vm.IsSettingsOpen)
                    ShowSettings(vm);
                else
                    HideSettings();
                UpdateWebViewVisibility();
                break;
            case nameof(MainWindowViewModel.IsStartMenuOpen):
                if (vm.IsStartMenuOpen)
                    PositionAndFocusStartMenu();
                UpdateWebViewVisibility();
                break;
            case nameof(MainWindowViewModel.IsProjectOverviewOpen):
                if (vm.IsProjectOverviewOpen)
                    FocusOverlay("ProjectOverviewOverlay");
                UpdateWebViewVisibility();
                break;
            case nameof(MainWindowViewModel.IsBookPickerOpen):
                if (vm.IsBookPickerOpen)
                    FocusOverlay("BookPickerOverlay");
                UpdateWebViewVisibility();
                break;
            case nameof(MainWindowViewModel.IsExtensionsOpen):
                UpdateWebViewVisibility();
                break;
            case nameof(MainWindowViewModel.ActiveSidebarTab):
                UpdateSidebarVisibility(vm.ActiveSidebarTab);
                break;
            case nameof(MainWindowViewModel.ActiveContentView):
                UpdateContentVisibility(vm.ActiveContentView);
                ToggleContextSidebarColumn(vm.IsContextSidebarShowing);
                break;
            case nameof(MainWindowViewModel.IsExplorerVisible):
                ToggleExplorerColumn(vm.IsExplorerVisible);
                break;
            case nameof(MainWindowViewModel.IsContextSidebarVisible):
                ToggleContextSidebarColumn(vm.IsContextSidebarShowing);
                break;
            case nameof(MainWindowViewModel.IsExtensionRightSidebarVisible):
                ToggleExtensionRightSidebarColumn(vm);
                break;
            case nameof(MainWindowViewModel.HasExtensionContextTabs):
                ToggleContextSidebarColumn(vm.IsContextSidebarShowing);
                break;
            case nameof(MainWindowViewModel.ActiveContextTab):
                UpdateContextTabContent(vm);
                break;
        }
    }

    private void ToggleExplorerColumn(bool visible)
    {
        var grid = this.FindControl<Grid>("ProjectContentGrid");
        if (grid == null || grid.ColumnDefinitions.Count < 2) return;
        var col = grid.ColumnDefinitions[1];

        if (visible)
        {
            col.Width = _savedExplorerWidth.Value > 0 ? _savedExplorerWidth : new GridLength(280);
            col.MinWidth = 180;
            col.MaxWidth = 500;
        }
        else
        {
            if (col.Width.IsAbsolute && col.Width.Value > 0)
                _savedExplorerWidth = col.Width;
            col.MinWidth = 0;
            col.MaxWidth = 0;
            col.Width = new GridLength(0);
        }
    }

    private void ToggleContextSidebarColumn(bool visible)
    {
        var grid = this.FindControl<Grid>("OuterContentGrid");
        if (grid == null || grid.ColumnDefinitions.Count < 3) return;
        var col = grid.ColumnDefinitions[2];

        if (visible)
        {
            col.Width = _savedContextSidebarWidth;
            col.MinWidth = 280;
            col.MaxWidth = 600;
        }
        else
        {
            _savedContextSidebarWidth = col.Width;
            col.MinWidth = 0;
            col.MaxWidth = 0;
            col.Width = new GridLength(0);
        }
    }

    private GridLength _savedExtRightSidebarWidth = new(320);

    private void ToggleExtensionRightSidebarColumn(MainWindowViewModel vm)
    {
        var grid = this.FindControl<Grid>("SceneContentPanel");
        if (grid == null || grid.ColumnDefinitions.Count < 3) return;

        var splitterCol = grid.ColumnDefinitions[1];
        var sidebarCol = grid.ColumnDefinitions[2];
        var host = this.FindControl<ContentControl>("ExtensionRightSidebarHost");
        var visible = vm.IsExtensionRightSidebarVisible;

        if (visible)
        {
            // Find the matching panel and create the view
            var panel = vm.ExtensionRightSidebarPanels
                .FirstOrDefault(p => p.Id == vm._activeRightSidebarPanelId);
            if (panel != null && host != null)
                host.Content = panel.CreateView();

            splitterCol.Width = GridLength.Auto;
            splitterCol.MinWidth = 0;
            splitterCol.MaxWidth = double.PositiveInfinity;
            sidebarCol.Width = _savedExtRightSidebarWidth;
            sidebarCol.MinWidth = 280;
            sidebarCol.MaxWidth = 600;
        }
        else
        {
            _savedExtRightSidebarWidth = sidebarCol.Width;
            splitterCol.MinWidth = 0;
            splitterCol.MaxWidth = 0;
            splitterCol.Width = new GridLength(0);
            sidebarCol.MinWidth = 0;
            sidebarCol.MaxWidth = 0;
            sidebarCol.Width = new GridLength(0);
            if (host != null) host.Content = null;
        }
    }

    // ── Sidebar tab switching ───────────────────────────────────────

    private void OnSidebarTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tab && DataContext is MainWindowViewModel vm)
        {
            vm.ActiveSidebarTab = tab;
        }
    }

    private void OnExtensionSidebarTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string panelId && DataContext is MainWindowViewModel vm)
        {
            vm.ActiveSidebarTab = $"ext:{panelId}";
        }
    }

    private void UpdateSidebarVisibility(string tab)
    {
        var explorer = this.FindControl<ExplorerView>("ExplorerPanel");
        var entityPanel = this.FindControl<EntityPanelView>("EntityPanelControl");
        var extHost = this.FindControl<ContentControl>("ExtensionSidebarHost");

        if (explorer != null) explorer.IsVisible = tab == "Chapters";
        if (entityPanel != null) entityPanel.IsVisible = tab == "Entities";

        if (extHost != null)
        {
            if (tab.StartsWith("ext:", StringComparison.Ordinal))
            {
                var panelId = tab["ext:".Length..];
                var vm = DataContext as MainWindowViewModel;
                var panel = vm?.ExtensionSidebarTabs.FirstOrDefault(t => t.Id == panelId)?.Panel;
                if (panel != null)
                {
                    extHost.Content = panel.CreateView();
                    extHost.IsVisible = true;
                }
            }
            else
            {
                extHost.IsVisible = false;
            }
        }
    }

    // ── Context sidebar tab switching ────────────────────────────────

    private void OnContextTabClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.ActiveContextTab = "Context";
    }

    private void OnContextExtTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string panelId && DataContext is MainWindowViewModel vm)
            vm.ActiveContextTab = panelId;
    }

    private readonly Dictionary<string, Control> _contextExtTabViews = new();

    private void UpdateContextTabContent(MainWindowViewModel vm)
    {
        var ctxSidebar = this.FindControl<ContextSidebarView>("ContextSidebarPanel");
        var extHost = this.FindControl<ContentControl>("ContextExtTabHost");
        var isNative = string.Equals(vm.ActiveContextTab, "Context", StringComparison.Ordinal);

        if (ctxSidebar != null) ctxSidebar.IsVisible = isNative;

        if (extHost == null) return;

        if (!isNative)
        {
            var panelId = vm.ActiveContextTab;
            if (!_contextExtTabViews.TryGetValue(panelId, out var view))
            {
                var panel = vm.ExtensionContextTabs.FirstOrDefault(t => t.Id == panelId)?.Panel;
                if (panel != null)
                {
                    view = panel.CreateView();
                    _contextExtTabViews[panelId] = view;
                }
            }
            if (view != null)
            {
                extHost.Content = view;
                extHost.IsVisible = true;
            }
        }
        else
        {
            extHost.IsVisible = false;
        }
    }

    private ContentViewDescriptor? _activeExtensionContentView;

    private void UpdateContentVisibility(string view)
    {
        var sceneContent = this.FindControl<Grid>("SceneContentPanel");
        var entityEditor = this.FindControl<EntityEditorView>("EntityEditorPanel");
        var dashboard = this.FindControl<DashboardView>("DashboardPanel");
        var timeline = this.FindControl<TimelineView>("TimelinePanel");
        var exportPanel = this.FindControl<ExportView>("ExportPanel");
        var galleryPanel = this.FindControl<ImageGalleryView>("ImageGalleryPanel");
        var gitPanel = this.FindControl<GitView>("GitPanel");
        var codexHubPanel = this.FindControl<CodexHubView>("CodexHubPanel");
        var manuscriptPanel = this.FindControl<ManuscriptView>("ManuscriptPanel");
        var extHost = this.FindControl<ContentControl>("ExtensionContentHost");

        var isExtView = view.StartsWith("ext:", StringComparison.Ordinal);

        // Deactivate previous extension content view if switching away
        if (_activeExtensionContentView != null && (!isExtView || view["ext:".Length..] != _activeExtensionContentView.ViewKey))
        {
            _activeExtensionContentView.OnDeactivated?.Invoke();
            _activeExtensionContentView = null;
        }

        var editorPanel = this.FindControl<EditorView>("EditorPanel");
        var sceneVisible = view == "Scene";
        var manuscriptVisible = view == "Manuscript";

        // Snapshot+hide WebViews BEFORE flipping parent IsVisible so the
        // native HWND doesn't flash on top of the new view during transition.
        if (!sceneVisible) editorPanel?.SetWebViewVisible(false);
        if (!manuscriptVisible) manuscriptPanel?.SetWebViewVisible(false);

        if (sceneContent != null) sceneContent.IsVisible = sceneVisible;
        if (entityEditor != null) entityEditor.IsVisible = view == "Entity";
        if (dashboard != null) dashboard.IsVisible = view == "Dashboard";
        if (timeline != null) timeline.IsVisible = view == "Timeline";
        if (exportPanel != null) exportPanel.IsVisible = view == "Export";
        if (galleryPanel != null) galleryPanel.IsVisible = view == "ImageGallery";
        if (gitPanel != null) gitPanel.IsVisible = view == "Git";
        if (codexHubPanel != null) codexHubPanel.IsVisible = view == "CodexHub";
        if (manuscriptPanel != null) manuscriptPanel.IsVisible = manuscriptVisible;

        // Restore visibility after parent panel shown — respects overlay state.
        UpdateWebViewVisibility();

        if (extHost != null)
        {
            if (isExtView)
            {
                var viewKey = view["ext:".Length..];
                var vm = DataContext as MainWindowViewModel;
                var desc = vm?.ExtensionManager?.ContentViews
                    .FirstOrDefault(c => c.ViewKey == viewKey);
                if (desc != null)
                {
                    extHost.Content = desc.CreateView();
                    extHost.IsVisible = true;
                    _activeExtensionContentView = desc;
                    desc.OnActivated?.Invoke();
                }
            }
            else
            {
                extHost.IsVisible = false;
                extHost.Content = null;
            }
        }
    }

    private void OnProjectOverviewOverlayPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.CloseProjectOverviewCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnProjectOverviewPopupPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnBookPickerOverlayPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.CloseBookPickerCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnBookPickerPopupPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnBookCardRenameClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: BookCard card } &&
            DataContext is MainWindowViewModel vm)
        {
            vm.RenameBookCardCommand.Execute(card);
        }
    }

    private void OnBookCardDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: BookCard card } &&
            DataContext is MainWindowViewModel vm)
        {
            vm.DeleteBookCardCommand.Execute(card);
        }
    }

    private void OnStartMenuOverlayPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.CloseStartMenuCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnStartMenuPanelPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        var s = App.SettingsService.Settings;
        if (s.WindowWidth > 100 && s.WindowHeight > 100)
        {
            Width = s.WindowWidth;
            Height = s.WindowHeight;
        }
        if (s.WindowX.HasValue && s.WindowY.HasValue)
        {
            // Clamp to current screen bounds (multi-monitor reconnect safety)
            var screens = Screens.All;
            if (screens.Count > 0)
            {
                var px = (int)s.WindowX.Value;
                var py = (int)s.WindowY.Value;
                var inAnyScreen = screens.Any(scr =>
                    px >= scr.Bounds.X - 50 && px < scr.Bounds.X + scr.Bounds.Width - 50 &&
                    py >= scr.Bounds.Y - 50 && py < scr.Bounds.Y + scr.Bounds.Height - 50);
                if (inAnyScreen)
                    Position = new PixelPoint(px, py);
            }
        }
        if (s.IsMaximized)
            WindowState = WindowState.Maximized;
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        var s = App.SettingsService.Settings;
        s.IsMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            s.WindowWidth = Width;
            s.WindowHeight = Height;
            s.WindowX = Position.X;
            s.WindowY = Position.Y;
        }
        _ = App.SettingsService.SaveAsync();
    }

    private void OnEditorTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control)
            return;
        if (!e.GetCurrentPoint(control).Properties.IsMiddleButtonPressed)
            return;
        if (control.DataContext is not EditorTabDescriptor desc)
            return;

        e.Handled = true;
        desc.OnClose?.Invoke();
    }

    private void OnEditorTabCloseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control c && c.DataContext is EditorTabDescriptor desc)
            desc.OnClose?.Invoke();
    }

    // ── Wiring ──────────────────────────────────────────────────────

    private void WireExplorerDialog(ExplorerViewModel? explorer)
    {
        if (explorer == null) return;
        explorer.ShowInputDialog = ShowInputDialogAsync;
        explorer.ShowOptionalInputDialog = ShowOptionalInputDialogAsync;
        explorer.ShowDatePickerDialog = ShowDatePickerDialogAsync;
        explorer.ShowAutoCompleteInputDialog = ShowAutoCompleteInputDialogAsync;
        explorer.ShowChapterDialog = ShowChapterDialogAsync;
        explorer.ShowSceneDialog = ShowSceneDialogAsync;
    }

    private void WireEntityPanel(EntityPanelViewModel? panel)
    {
        if (panel == null) return;
        panel.ShowInputDialog = ShowInputDialogAsync;
        panel.ShowEntityCreationDialog = ShowEntityCreationDialogAsync;
        panel.ShowConfirmDialog = ShowConfirmDialogAsync;
        panel.ShowEntityTypeManagerDialog = ShowEntityTypeManagerDialogAsync;
    }

    private void WireEntityEditor(EntityEditorViewModel? editor)
    {
        if (editor == null) return;
        editor.BrowseImageRequested = BrowseForImageAsync;
        editor.ChooseAddImageSourceRequested = ShowAddImageSourceDialogAsync;
        editor.PickProjectImageRequested = ShowProjectImagePickerAsync;
        editor.ShowInverseRelationshipDialog = ShowInverseRelationshipDialogAsync;
        editor.ConfirmDeleteRequested = ShowConfirmDialogAsync;
    }

    private void WireExport(ExportViewModel? export)
    {
        if (export == null) return;
        export.ShowSaveFileDialog = ShowExportSaveFileDialogAsync;
    }

    private void WireImageGallery(ImageGalleryViewModel? gallery)
    {
        if (gallery == null) return;
        gallery.CopyToClipboard = async text =>
        {
            var clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(text);
        };
        gallery.RevealInExplorer = path =>
        {
            if (System.IO.File.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = System.IO.Path.GetDirectoryName(path)!,
                    UseShellExecute = true
                });
            }
        };
    }

    private async Task<string?> ShowExportSaveFileDialogAsync(string suggestedName, string formatLabel)
    {
        var storageProvider = StorageProvider;
        var result = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedName,
            FileTypeChoices =
            [
                new FilePickerFileType(formatLabel)
                {
                    Patterns = [$"*{System.IO.Path.GetExtension(suggestedName)}"]
                }
            ]
        });

        return result?.Path.LocalPath;
    }

    public async Task CheckForUpdateAsync()
    {
        try
        {
            var updateService = new UpdateService();
            var update = await updateService.CheckForUpdateAsync();
            if (update is null)
                return;

            var dialog = new UpdateDialog(update, updateService);
            await ShowDialogOverlayAsync(dialog, dialog.DialogClosed);
        }
        catch
        {
            // Silently ignore update check failures — network may be unavailable
        }
    }

    private async Task ShowDialogOverlayAsync(UserControl dialog, TaskCompletionSource tcs)
    {
        var dialogOverlay = this.FindControl<Border>("DialogOverlay")!;
        var presenter = this.FindControl<ContentPresenter>("DialogHostPresenter")!;

        _isDialogOpen = true;
        UpdateWebViewVisibility();

        presenter.Content = dialog;
        dialogOverlay.IsVisible = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => dialog.Focus(),
            Avalonia.Threading.DispatcherPriority.Input);

        await tcs.Task;

        dialogOverlay.IsVisible = false;
        presenter.Content = null;

        _isDialogOpen = false;
        UpdateWebViewVisibility();
    }

    private async Task<string?> ShowInputDialogAsync(string title, string prompt, string defaultValue)
    {
        var dialog = new InputDialog(title, prompt, defaultValue);
        await ShowDialogOverlayAsync(dialog, dialog.DialogClosed);
        return dialog.Result;
    }

    private async Task ShowSnapshotsDialogAsync(Novalist.Core.Models.ChapterData chapter, Novalist.Core.Models.SceneData scene)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var dialog = new SnapshotsDialog(
            App.SnapshotService,
            chapter,
            scene,
            onRestored: () =>
            {
                if (vm.Editor != null && string.Equals(vm.Editor.CurrentScene?.Id, scene.Id, StringComparison.OrdinalIgnoreCase))
                {
                    _ = vm.Editor.ReloadCurrentSceneAsync();
                }
            },
            onCompare: async snapshot =>
            {
                var current = await vm.ProjectService.ReadSceneContentAsync(chapter, scene);
                var compare = new SnapshotCompareDialog(snapshot, current, vm.ProjectService, chapter, scene,
                    onPartialApply: () =>
                    {
                        if (vm.Editor != null && string.Equals(vm.Editor.CurrentScene?.Id, scene.Id, StringComparison.OrdinalIgnoreCase))
                        {
                            _ = vm.Editor.ReloadCurrentSceneAsync();
                        }
                    });
                await ShowDialogOverlayAsync(compare, compare.DialogClosed);
            });
        await ShowDialogOverlayAsync(dialog, dialog.DialogClosed);
    }

    private async Task<EntityCreationResult?> ShowEntityCreationDialogAsync(
        string title, string prompt, IReadOnlyList<EntityCreationTemplateOption> templates)
    {
        var options = templates.Select(t => new TemplateOption(t.Id, t.Name)).ToList();
        var dialog = new EntityCreationDialog(title, prompt, options);
        await ShowDialogOverlayAsync(dialog, dialog.DialogClosed);
        return dialog.ResultName != null
            ? new EntityCreationResult(dialog.ResultName, dialog.ResultTemplateId)
            : null;
    }

    private async Task<string?> ShowOptionalInputDialogAsync(string title, string prompt, string defaultValue)
    {
        var dialog = new InputDialog(title, prompt, defaultValue, allowEmpty: true);
        await ShowDialogOverlayAsync(dialog, dialog.DialogClosed);
        return dialog.Result;
    }

    private async Task<string?> ShowDatePickerDialogAsync(string title, string prompt, string currentDate)
    {
        var dialog = new DatePickerDialog(title, prompt, currentDate);
        await ShowDialogOverlayAsync(dialog, dialog.DialogClosed);
        return dialog.Result;
    }

    private async Task<string?> ShowAutoCompleteInputDialogAsync(string prompt, string defaultValue, IReadOnlyList<string> suggestions)
    {
        var dialog = new AutoCompleteInputDialog(prompt, defaultValue, suggestions);
        await ShowDialogOverlayAsync(dialog, dialog.DialogClosed);
        return dialog.Result;
    }

    private async Task<string?> ShowInverseRelationshipDialogAsync(string relationshipRole, string sourceName, string targetName, IReadOnlyList<string> suggestions)
    {
        var dialog = new InverseRelationshipDialog(relationshipRole, sourceName, targetName, suggestions);
        await ShowDialogOverlayAsync(dialog, dialog.DialogClosed);
        return dialog.Result;
    }

    private async Task<ChapterDialogResult?> ShowChapterDialogAsync()
    {
        var dialog = new ChapterDialog();
        await ShowDialogOverlayAsync(dialog, dialog.DialogClosed);
        return dialog.Result;
    }

    private async Task<SceneDialogResult?> ShowSceneDialogAsync(ChapterTreeItemViewModel? preferredChapter)
    {
        if (DataContext is not MainWindowViewModel vm || vm.Explorer == null)
            return null;

        var chapters = vm.Explorer.ExplorerItems
            .OfType<ChapterTreeItemViewModel>()
            .Select(chapter => new SceneChapterOption(chapter.Chapter.Guid, chapter.Chapter.Title))
            .ToList();
        if (chapters.Count == 0)
            return null;

        var dialog = new SceneDialog(chapters, initialChapterGuid: preferredChapter?.Chapter.Guid);
        await ShowDialogOverlayAsync(dialog, dialog.DialogClosed);
        return dialog.Result;
    }

    private async Task<string?> BrowseForImageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Image",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp" } }
            }
        });
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    private void WireDashboardOnCreation(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.Dashboard)) return;
        if (sender is not MainWindowViewModel vm || vm.Dashboard == null) return;
        vm.Dashboard.ChooseAddImageSourceRequested = ShowAddImageSourceDialogAsync;
        vm.Dashboard.PickCoverImageRequested = ShowProjectImagePickerAsync;
        vm.Dashboard.BrowseImageRequested = BrowseForImageAsync;
        vm.Dashboard.CoverImageSelected = path => vm.SetCoverImageFromPickerAsync(path);
    }

    private async Task<AddImageSourceChoice?> ShowAddImageSourceDialogAsync()
    {
        var dialog = new AddImageSourceDialog();
        await ShowDialogOverlayAsync(dialog, dialog.DialogClosed);
        return dialog.Result;
    }

    private async Task<string?> ShowProjectImagePickerAsync(string? currentPath)
    {
        var dialog = new ProjectImagePickerDialog(App.EntityService.GetProjectImages(), currentPath);
        await ShowDialogOverlayAsync(dialog, dialog.DialogClosed);
        return dialog.Result;
    }

    private async Task<bool> ShowConfirmDialogAsync(string title, string message)
    {
        var dialog = new ConfirmDialog(title, message);
        await ShowDialogOverlayAsync(dialog, dialog.DialogClosed);
        return dialog.Confirmed;
    }

    public void ShowWelcomeIfNeeded()
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.IsProjectLoaded) return;

        var welcomeView = this.FindControl<WelcomeView>("WelcomeViewControl");
        if (welcomeView == null) return;

        var welcomeVm = new WelcomeViewModel(vm.SettingsService.Settings.RecentProjects, App.ProjectTemplateService.GetTemplates());

        welcomeVm.BrowseFolderRequested += async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Folder",
                AllowMultiple = false
            });
            return folders.Count > 0 ? folders[0].Path.LocalPath : null;
        };

        welcomeVm.CreateProjectRequested += async (parentDir, name, bookName, templateId) =>
        {
            try
            {
                await vm.CreateProjectAsync(parentDir, name, bookName, templateId);
            }
            catch (Exception ex)
            {
                vm.StatusText = $"Error: {ex.Message}"; Toast.Show?.Invoke(Loc.T("toast.projectLoadFailed", ex.Message), ToastSeverity.Error);
            }
        };

        welcomeVm.OpenProjectRequested += async (projectDir) =>
        {
            try
            {
                await vm.LoadProjectAsync(projectDir);
            }
            catch (Exception ex)
            {
                vm.StatusText = $"Error: {ex.Message}"; Toast.Show?.Invoke(Loc.T("toast.projectLoadFailed", ex.Message), ToastSeverity.Error);
            }
        };

        welcomeVm.ImportPluginProjectRequested += async () =>
        {
            try
            {
                var dialog = new Dialogs.ImportPluginDialog();
                dialog.BrowseFolder = async () =>
                {
                    var folders = await StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
                    {
                        Title = Localization.Loc.T("import.selectFolder"),
                        AllowMultiple = false
                    });
                    return folders.Count > 0 ? folders[0].Path.LocalPath : null;
                };

                await ShowDialogOverlayAsync(dialog, dialog.DialogClosed);

                if (!string.IsNullOrEmpty(dialog.ImportedProjectPath))
                {
                    // Merge imported app-level settings
                    if (dialog.ImportResult != null)
                    {
                        var settings = vm.SettingsService.Settings;
                        var changed = false;

                        foreach (var (role, inverses) in dialog.ImportResult.RelationshipPairs)
                        {
                            foreach (var inverse in inverses)
                                changed |= settings.LearnRelationshipPair(role, inverse);
                        }

                        if (dialog.ImportResult.AutoReplacements.Count > 0 && settings.AutoReplacements.Count == 0)
                        {
                            settings.AutoReplacements = dialog.ImportResult.AutoReplacements;
                            changed = true;
                        }

                        if (!string.IsNullOrEmpty(dialog.ImportResult.AutoReplacementLanguage))
                        {
                            settings.AutoReplacementLanguage = dialog.ImportResult.AutoReplacementLanguage;
                            changed = true;
                        }

                        if (changed)
                            _ = vm.SettingsService.SaveAsync();
                    }

                    await vm.LoadProjectAsync(dialog.ImportedProjectPath);
                }
            }
            catch (Exception ex)
            {
                vm.StatusText = $"Error: {ex.Message}"; Toast.Show?.Invoke(Loc.T("toast.projectLoadFailed", ex.Message), ToastSeverity.Error);
            }
        };

        welcomeView.DataContext = welcomeVm;
    }

    private void ShowSettings(MainWindowViewModel vm)
    {
        var settingsView = this.FindControl<Views.SettingsView>("SettingsViewControl");
        if (settingsView == null) return;

        var settingsVm = new SettingsViewModel(vm.SettingsService, vm.ProjectService);
        settingsVm.ShowTemplateEditor = ShowTemplateEditorAsync;

        // Load extension settings pages
        if (App.ExtensionManager?.SettingsPages is { Count: > 0 } extPages)
            settingsVm.LoadExtensionSettingsPages(extPages);

        settingsVm.CloseRequested += () =>
        {
            vm.IsSettingsOpen = false;
        };
        settingsVm.SettingsChanged += () =>
        {
            // Refresh editor settings (book paragraph spacing, auto-replacements)
            if (vm.Editor != null)
            {
                vm.Editor.ApplySettings();
            }

            _ = vm.RefreshStatusBarAsync();

            // Reinitialize WebView if the UI language changed
            var newLang = App.SettingsService.Settings.Language;
            if (!string.Equals(_webViewLanguage, newLang, StringComparison.Ordinal))
            {
                _webViewLanguage = newLang;
                this.FindControl<EditorView>("EditorPanel")?.ReinitializeWebView(newLang);
            }
        };
        settingsView.DataContext = settingsVm;

        // Auto-select a pending category (e.g. opened from Extensions overlay)
        if (vm.PendingSettingsCategory is { } pendingKey)
        {
            vm.PendingSettingsCategory = null;
            var cat = settingsVm.Categories.FirstOrDefault(c => c.Key == pendingKey);
            if (cat != null)
                settingsVm.SelectedCategory = cat;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() => settingsView.Focus(),
            Avalonia.Threading.DispatcherPriority.Input);
    }

    private void HideSettings()
    {
        var settingsView = this.FindControl<Views.SettingsView>("SettingsViewControl");
        if (settingsView != null)
            settingsView.DataContext = null;
    }

    /// <summary>
    /// Hides the native WebView control when any overlay (dialog, start menu,
    /// settings, project overview) is active. The WebView2 HWND always renders
    /// on top of Avalonia-managed content, so we must hide it to let overlays
    /// be visible.
    /// </summary>
    private void UpdateWebViewVisibility()
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var anyOverlay = _isDialogOpen
                         || vm.IsStartMenuOpen
                         || vm.IsSettingsOpen
                         || vm.IsProjectOverviewOpen
                         || vm.IsExtensionsOpen;
        this.FindControl<EditorView>("EditorPanel")?.SetWebViewVisible(!anyOverlay);
        this.FindControl<ManuscriptView>("ManuscriptPanel")?.SetWebViewVisible(!anyOverlay);
    }

    private void FocusOverlay(string name)
    {
        var control = this.FindControl<Control>(name);
        if (control != null)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => control.Focus(),
                Avalonia.Threading.DispatcherPriority.Input);
    }

    private void PositionAndFocusStartMenu()
    {
        var tabRow = this.FindControl<Control>("RibbonTabRow");
        var menuPanel = this.FindControl<Control>("StartMenuPanel");
        var overlay = this.FindControl<Control>("StartMenuOverlay");

        if (tabRow != null && menuPanel != null)
        {
            // Position the menu panel right below the ribbon tab row
            var bottomLeft = tabRow.TranslatePoint(new Point(0, tabRow.Bounds.Height), this);
            if (bottomLeft.HasValue)
            {
                menuPanel.Margin = new Thickness(0, bottomLeft.Value.Y, 0, 0);
            }
        }

        if (overlay != null)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => overlay.Focus(),
                Avalonia.Threading.DispatcherPriority.Input);
    }

    private async Task<bool> ShowTemplateEditorAsync(TemplateEditorViewModel vm)
    {
        var dialog = new Dialogs.TemplateEditorDialog(vm);
        await ShowDialogOverlayAsync(dialog, dialog.DialogClosed);
        return dialog.Saved;
    }

    private async Task<bool> ShowEntityTypeManagerDialogAsync(EntityTypeManagerViewModel vm)
    {
        var dialog = new Dialogs.EntityTypeManagerDialog(vm);
        await ShowDialogOverlayAsync(dialog, dialog.DialogClosed);
        return dialog.Saved;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!_isDialogOpen)
            App.HotkeyManager.HandleKeyDown(e);

        if (!e.Handled)
            base.OnKeyDown(e);
    }
}