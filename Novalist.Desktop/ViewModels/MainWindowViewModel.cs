using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using Novalist.Desktop.Localization;
using Novalist.Desktop.Services;
using Novalist.Sdk.Models;

namespace Novalist.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private readonly ISettingsService _settingsService;
    private readonly IEntityService _entityService;
    private readonly IGitService _gitService;

    [ObservableProperty]
    private string _title = $"{Loc.T("app.title")} {VersionInfo.Version}";

    [ObservableProperty]
    private bool _isProjectLoaded;

    [ObservableProperty]
    private string _projectName = string.Empty;

    [ObservableProperty]
    private ExplorerViewModel? _explorer;

    [ObservableProperty]
    private EditorViewModel? _editor;

    [ObservableProperty]
    private EntityPanelViewModel? _entityPanel;

    [ObservableProperty]
    private EntityEditorViewModel? _entityEditor;

    [ObservableProperty]
    private ContextSidebarViewModel? _contextSidebar;

    [ObservableProperty]
    private SceneNotesViewModel? _sceneNotes;

    [ObservableProperty]
    private DashboardViewModel? _dashboard;

    [ObservableProperty]
    private TimelineViewModel? _timeline;

    [ObservableProperty]
    private ExportViewModel? _export;

    [ObservableProperty]
    private ImageGalleryViewModel? _imageGallery;

    [ObservableProperty]
    private GitViewModel? _git;

    [ObservableProperty]
    private CodexHubViewModel? _codexHub;

    [ObservableProperty]
    private ManuscriptViewModel? _manuscript;

    [ObservableProperty]
    private string _statusText = Loc.T("app.ready");

    [ObservableProperty]
    private string _activeActivityView = string.Empty;

    [ObservableProperty]
    private bool _isStartMenuOpen;

    [ObservableProperty]
    private bool _isSettingsOpen;

    [ObservableProperty]
    private int _projectTotalWords;

    [ObservableProperty]
    private int _projectChapterCount;

    [ObservableProperty]
    private int _projectSceneCount;

    [ObservableProperty]
    private int _projectCharacterCount;

    [ObservableProperty]
    private int _projectLocationCount;

    [ObservableProperty]
    private int _projectReadingTimeMinutes;

    [ObservableProperty]
    private int _averageChapterWords;

    [ObservableProperty]
    private int _dailyGoalCurrentWords;

    [ObservableProperty]
    private int _dailyGoalTargetWords;

    [ObservableProperty]
    private int _dailyGoalPercent;

    [ObservableProperty]
    private int _projectGoalTargetWords;

    [ObservableProperty]
    private int _projectGoalPercent;

    [ObservableProperty]
    private string _projectBreakdownTooltip = string.Empty;

    [ObservableProperty]
    private string _goalTooltip = string.Empty;

    [ObservableProperty]
    private bool _isProjectOverviewOpen;

    [ObservableProperty]
    private List<StatusBarChapterOverviewItem> _projectOverviewChapters = [];

    /// <summary>
    /// Tracks which main content area is active: "Scene" or "Entity".
    /// </summary>
    [ObservableProperty]
    private string _activeContentView = "Scene";

    // ── Open-tab state for all tab-managed views ────────────────────
    [ObservableProperty] private bool _isDashboardOpen;
    [ObservableProperty] private bool _isTimelineOpen;
    [ObservableProperty] private bool _isCodexHubOpen;
    [ObservableProperty] private bool _isManuscriptOpen;
    [ObservableProperty] private bool _isExportOpen;
    [ObservableProperty] private bool _isImageGalleryOpen;
    [ObservableProperty] private bool _isGitOpen;
    [ObservableProperty] private bool _isExtensionContentOpen;
    [ObservableProperty] private string _extensionContentTabTitle = string.Empty;

    /// <summary>Data-driven editor tab strip. Rebuilt on tab open/close/title/dirty changes.</summary>
    public ObservableCollection<EditorTabDescriptor> ContentTabs { get; } = [];

    private bool _tabsSyncPending;

    private void QueueSyncContentTabs()
    {
        if (_tabsSyncPending) return;
        _tabsSyncPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _tabsSyncPending = false;
            SyncContentTabs();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    partial void OnIsDashboardOpenChanged(bool value) => QueueSyncContentTabs();
    partial void OnIsTimelineOpenChanged(bool value) => QueueSyncContentTabs();
    partial void OnIsCodexHubOpenChanged(bool value) => QueueSyncContentTabs();
    partial void OnIsManuscriptOpenChanged(bool value) => QueueSyncContentTabs();
    partial void OnIsExportOpenChanged(bool value) => QueueSyncContentTabs();
    partial void OnIsImageGalleryOpenChanged(bool value) => QueueSyncContentTabs();
    partial void OnIsGitOpenChanged(bool value) => QueueSyncContentTabs();
    partial void OnIsExtensionContentOpenChanged(bool value) => QueueSyncContentTabs();
    partial void OnExtensionContentTabTitleChanged(string value) => QueueSyncContentTabs();

    private void SyncContentTabs()
    {
        // Build desired list in display order
        var desired = new List<EditorTabDescriptor>();

        if (IsDashboardOpen)
            desired.Add(new EditorTabDescriptor("Dashboard", "Dashboard", "Dashboard", () => CloseDashboardTabCommand.Execute(null)));
        if (IsTimelineOpen)
            desired.Add(new EditorTabDescriptor("Timeline", "Timeline", "Timeline", () => CloseTimelineTabCommand.Execute(null)));
        if (IsCodexHubOpen)
            desired.Add(new EditorTabDescriptor("CodexHub", "CodexHub", "Codex", () => CloseCodexHubTabCommand.Execute(null)));
        if (IsManuscriptOpen)
            desired.Add(new EditorTabDescriptor("Manuscript", "Manuscript", "Manuscript", () => CloseManuscriptTabCommand.Execute(null)));
        if (IsExportOpen)
            desired.Add(new EditorTabDescriptor("Export", "Export", Loc.T("ribbon.export"), () => CloseExportTabCommand.Execute(null)));
        if (IsImageGalleryOpen)
            desired.Add(new EditorTabDescriptor("ImageGallery", "ImageGallery", Loc.T("ribbon.gallery"), () => CloseImageGalleryTabCommand.Execute(null)));
        if (IsGitOpen)
            desired.Add(new EditorTabDescriptor("Git", "Git", Loc.T("ribbon.git"), () => CloseGitTabCommand.Execute(null)));
        if (IsExtensionContentOpen)
            desired.Add(new EditorTabDescriptor("ExtensionContent", ActiveContentView, ExtensionContentTabTitle, () => CloseExtensionContentTabCommand.Execute(null)));
        if (Editor?.IsDocumentOpen == true)
        {
            var sceneTab = new EditorTabDescriptor(
                "Scene", "Scene", Editor.SceneTabTitle ?? string.Empty,
                () => _ = CloseSceneTabAsync(),
                badge: "SCN", minWidth: 160, tooltip: Editor.DocumentTitle)
            {
                IsDirty = Editor.IsDirty
            };
            desired.Add(sceneTab);
        }
        if (EntityEditor?.IsOpen == true)
        {
            desired.Add(new EditorTabDescriptor(
                "Entity", "Entity", EntityEditor.Title ?? string.Empty,
                () => _ = CloseEntityTabAsync(),
                badge: "ENT", minWidth: 160, tooltip: EntityEditor.Title));
        }

        // Rebuild collection in-place to preserve ItemsControl identity
        // Match by Id; update existing, remove missing, add new
        var existingById = ContentTabs.ToDictionary(t => t.Id);
        for (int i = ContentTabs.Count - 1; i >= 0; i--)
        {
            if (!desired.Any(d => d.Id == ContentTabs[i].Id))
                ContentTabs.RemoveAt(i);
        }
        for (int i = 0; i < desired.Count; i++)
        {
            var d = desired[i];
            if (existingById.TryGetValue(d.Id, out var existing))
            {
                existing.Title = d.Title;
                existing.IsDirty = d.IsDirty;
                existing.Tooltip = d.Tooltip;
                existing.IsActive = d.ActivationKey == ActiveContentView;
                int curIdx = ContentTabs.IndexOf(existing);
                if (curIdx != i)
                    ContentTabs.Move(curIdx, i);
            }
            else
            {
                d.IsActive = d.ActivationKey == ActiveContentView;
                ContentTabs.Insert(i, d);
            }
        }
    }

    private void UpdateContentTabActive()
    {
        foreach (var t in ContentTabs)
            t.IsActive = t.ActivationKey == ActiveContentView
                || (t.Id == "ExtensionContent" && ActiveContentView.StartsWith("ext:", StringComparison.Ordinal));
    }

    /// <summary>
    /// Tracks which sidebar tab is active: "Chapters" or "Entities".
    /// </summary>
    [ObservableProperty]
    private string _activeSidebarTab = "Chapters";

    /// <summary>
    /// Controls visibility of the left sidebar (explorer/entity panel).
    /// </summary>
    [ObservableProperty]
    private bool _isExplorerVisible = true;

    /// <summary>
    /// Controls visibility of the context sidebar (right panel).
    /// </summary>
    [ObservableProperty]
    private bool _isContextSidebarVisible = true;

    /// <summary>
    /// True when the context sidebar should be shown — sidebar is toggled on AND the active view supports it.
    /// </summary>
    public bool IsContextSidebarShowing =>
        IsContextSidebarVisible &&
        (ActiveContentView == "Scene" || ActiveContentView == "Manuscript" || HasExtensionContextTabs);

    [ObservableProperty]
    private bool _hasExtensionContextTabs;

    [ObservableProperty]
    private string _activeContextTab = "Context";

    public ObservableCollection<ExtensionContextTabVM> ExtensionContextTabs { get; } = [];

    public bool IsContextTabActive => ActiveContextTab == "Context";

    partial void OnActiveContextTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsContextTabActive));
        foreach (var tab in ExtensionContextTabs)
            tab.IsActive = tab.Id == value;
    }

    partial void OnIsContextSidebarVisibleChanged(bool value) =>
        OnPropertyChanged(nameof(IsContextSidebarShowing));

    partial void OnActiveContentViewChanged(string value)
    {
        OnPropertyChanged(nameof(IsContextSidebarShowing));
        UpdateContentTabActive();
        // Sync ActiveActivityView for content-typed activity buttons (Export/ImageGallery/Git).
        // Other views (Dashboard/Scene/Entity/Timeline/CodexHub/Manuscript/ext:*) clear it.
        if (value == "Export" || value == "ImageGallery" || value == "Git")
            ActiveActivityView = value;
        else if (ActiveActivityView == "Export" || ActiveActivityView == "ImageGallery" || ActiveActivityView == "Git")
            ActiveActivityView = string.Empty;
    }

    partial void OnHasExtensionContextTabsChanged(bool value) =>
        OnPropertyChanged(nameof(IsContextSidebarShowing));

    /// <summary>
    /// Controls visibility of the scene notes panel (below editor).
    /// </summary>
    [ObservableProperty]
    private bool _isSceneNotesVisible;

    // ── Book management ─────────────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<BookData> _books = [];

    [ObservableProperty]
    private BookData? _activeBook;

    [ObservableProperty]
    private ObservableCollection<BookCard> _bookCards = [];

    [ObservableProperty]
    private bool _isBookPickerOpen;

    public Func<string, string, string, Task<string?>>? ShowInputDialog { get; set; }
    public Func<string, string, Task<bool>>? ShowConfirmDialog { get; set; }
    public Func<ChapterData, SceneData, Task>? ShowSnapshotsDialog { get; set; }

    [ObservableProperty]
    private bool _isFocusMode;

    private bool _focusModeSavedExplorer;
    private bool _focusModeSavedContextSidebar;
    private bool _focusModeSavedSceneNotes;

    public bool IsAppChromeVisible => IsProjectLoaded && !IsFocusMode;

    partial void OnIsProjectLoadedChanged(bool value) => OnPropertyChanged(nameof(IsAppChromeVisible));
    partial void OnIsFocusModeChanged(bool value) => OnPropertyChanged(nameof(IsAppChromeVisible));

    [RelayCommand]
    private void ToggleFocusMode()
    {
        if (!IsFocusMode)
        {
            _focusModeSavedExplorer = IsExplorerVisible;
            _focusModeSavedContextSidebar = IsContextSidebarVisible;
            _focusModeSavedSceneNotes = IsSceneNotesVisible;
            IsExplorerVisible = false;
            IsContextSidebarVisible = false;
            IsSceneNotesVisible = false;
            IsFocusMode = true;
        }
        else
        {
            IsFocusMode = false;
            IsExplorerVisible = _focusModeSavedExplorer;
            IsContextSidebarVisible = _focusModeSavedContextSidebar;
            IsSceneNotesVisible = _focusModeSavedSceneNotes;
        }
    }

    public MainWindowViewModel(IProjectService projectService, ISettingsService settingsService, IEntityService entityService, IGitService gitService)
    {
        _projectService = projectService;
        _settingsService = settingsService;
        _entityService = entityService;
        _gitService = gitService;

        Toast.Show = (msg, sev) => Dispatcher.UIThread.Post(() => ShowToast(msg, sev));

        RegisterBuiltInHotkeys();
    }

    /// <summary>
    /// Registers all built-in keyboard shortcut actions with the hotkey service.
    /// </summary>
    private void RegisterBuiltInHotkeys()
    {
        var cat = Loc.T("hotkeys.category.navigation");
        var catPanels = Loc.T("hotkeys.category.panels");
        var catScene = Loc.T("hotkeys.category.scenes");
        var catEditor = Loc.T("hotkeys.category.editor");
        var catProject = Loc.T("hotkeys.category.project");
        var catGit = Loc.T("hotkeys.category.git");

        App.HotkeyService.RegisterRange([
            // ── Navigation ──
            new HotkeyDescriptor { ActionId = "app.nav.dashboard", DisplayName = Loc.T("hotkeys.nav.dashboard"), Category = cat, DefaultGesture = "Ctrl+D1", OnExecute = ShowDashboard },
            new HotkeyDescriptor { ActionId = "app.nav.editor", DisplayName = Loc.T("hotkeys.nav.editor"), Category = cat, DefaultGesture = "Ctrl+D2", OnExecute = () => SetActiveContentView("Scene") },
            new HotkeyDescriptor { ActionId = "app.nav.entity", DisplayName = Loc.T("hotkeys.nav.entity"), Category = cat, DefaultGesture = "Ctrl+D3", OnExecute = () => SetActiveContentView("Entity") },
            new HotkeyDescriptor { ActionId = "app.nav.timeline", DisplayName = Loc.T("hotkeys.nav.timeline"), Category = cat, DefaultGesture = "Ctrl+D4", OnExecute = ShowTimeline },
            new HotkeyDescriptor { ActionId = "app.nav.export", DisplayName = Loc.T("hotkeys.nav.export"), Category = cat, DefaultGesture = "Ctrl+D5", OnExecute = ShowExport },
            new HotkeyDescriptor { ActionId = "app.nav.gallery", DisplayName = Loc.T("hotkeys.nav.gallery"), Category = cat, DefaultGesture = "Ctrl+D6", OnExecute = ShowImageGallery },
            new HotkeyDescriptor { ActionId = "app.nav.git", DisplayName = Loc.T("hotkeys.nav.git"), Category = cat, DefaultGesture = "Ctrl+D7", OnExecute = () => _ = ShowGitAsync() },
            new HotkeyDescriptor { ActionId = "app.nav.codexHub", DisplayName = Loc.T("hotkeys.nav.codexHub"), Category = cat, DefaultGesture = "Ctrl+D8", OnExecute = ShowCodexHub },
            new HotkeyDescriptor { ActionId = "app.nav.manuscript", DisplayName = Loc.T("hotkeys.nav.manuscript"), Category = cat, DefaultGesture = "Ctrl+D9", OnExecute = ShowManuscript },
            new HotkeyDescriptor { ActionId = "app.nav.settings", DisplayName = Loc.T("hotkeys.nav.settings"), Category = cat, DefaultGesture = "Ctrl+OemComma", OnExecute = ToggleSettings },
            new HotkeyDescriptor { ActionId = "app.nav.extensions", DisplayName = Loc.T("hotkeys.nav.extensions"), Category = cat, DefaultGesture = "Ctrl+Shift+X", OnExecute = ToggleExtensions },
            new HotkeyDescriptor { ActionId = "app.nav.startMenu", DisplayName = Loc.T("hotkeys.nav.startMenu"), Category = cat, DefaultGesture = "Alt+F", OnExecute = ToggleStartMenu },

            // ── Panels ──
            new HotkeyDescriptor { ActionId = "app.panel.explorer", DisplayName = Loc.T("hotkeys.panel.explorer"), Category = catPanels, DefaultGesture = "Ctrl+B", OnExecute = ToggleExplorer, CanExecute = () => IsProjectLoaded },
            new HotkeyDescriptor { ActionId = "app.panel.contextSidebar", DisplayName = Loc.T("hotkeys.panel.contextSidebar"), Category = catPanels, DefaultGesture = "Ctrl+Shift+B", OnExecute = ToggleContextSidebar, CanExecute = () => IsProjectLoaded },
            new HotkeyDescriptor { ActionId = "app.panel.sidebarChapters", DisplayName = Loc.T("hotkeys.panel.sidebarChapters"), Category = catPanels, DefaultGesture = "Ctrl+Shift+D1", OnExecute = () => ActiveSidebarTab = "Chapters", CanExecute = () => IsProjectLoaded },
            new HotkeyDescriptor { ActionId = "app.panel.sidebarEntities", DisplayName = Loc.T("hotkeys.panel.sidebarEntities"), Category = catPanels, DefaultGesture = "Ctrl+Shift+D2", OnExecute = () => ActiveSidebarTab = "Entities", CanExecute = () => IsProjectLoaded },
            new HotkeyDescriptor { ActionId = "app.panel.projectOverview", DisplayName = Loc.T("hotkeys.panel.projectOverview"), Category = catPanels, DefaultGesture = "Ctrl+Shift+O", OnExecute = ToggleProjectOverview, CanExecute = () => IsProjectLoaded },
            new HotkeyDescriptor { ActionId = "app.panel.sceneNotes", DisplayName = Loc.T("hotkeys.panel.sceneNotes"), Category = catPanels, DefaultGesture = "Ctrl+Shift+N", OnExecute = ToggleSceneNotes, CanExecute = () => IsProjectLoaded },
            new HotkeyDescriptor { ActionId = "app.panel.focusMode", DisplayName = Loc.T("hotkeys.panel.focusMode"), Category = catPanels, DefaultGesture = "F11", OnExecute = ToggleFocusMode, CanExecute = () => IsProjectLoaded },

            // ── Scene / Tab management ──
            new HotkeyDescriptor { ActionId = "app.scene.closeTab", DisplayName = Loc.T("hotkeys.scene.closeTab"), Category = catScene, DefaultGesture = "Ctrl+W", OnExecute = () => _ = CloseSceneTabAsync(), CanExecute = () => Editor?.IsDocumentOpen == true },
            new HotkeyDescriptor { ActionId = "app.scene.create", DisplayName = Loc.T("hotkeys.scene.create"), Category = catScene, DefaultGesture = "Ctrl+N", OnExecute = () => Explorer?.CreateSceneCommand.Execute(null), CanExecute = () => Explorer != null },
            new HotkeyDescriptor { ActionId = "app.scene.next", DisplayName = Loc.T("hotkeys.scene.next"), Category = catScene, DefaultGesture = "Ctrl+OemCloseBrackets", OnExecute = () => Explorer?.NavigateScene(1), CanExecute = () => Explorer != null },
            new HotkeyDescriptor { ActionId = "app.scene.prev", DisplayName = Loc.T("hotkeys.scene.prev"), Category = catScene, DefaultGesture = "Ctrl+OemOpenBrackets", OnExecute = () => Explorer?.NavigateScene(-1), CanExecute = () => Explorer != null },
            new HotkeyDescriptor { ActionId = "app.chapter.create", DisplayName = Loc.T("hotkeys.chapter.create"), Category = catScene, DefaultGesture = "Ctrl+Shift+M", OnExecute = () => Explorer?.CreateChapterCommand.Execute(null), CanExecute = () => Explorer != null },

            // ── Editor formatting ──
            new HotkeyDescriptor { ActionId = "app.editor.bold", DisplayName = Loc.T("hotkeys.editor.bold"), Category = catEditor, DefaultGesture = "Ctrl+B", OnExecute = () => Editor?.ToggleBoldAction?.Invoke(), CanExecute = () => Editor?.IsDocumentOpen == true && ActiveContentView == "Scene" },
            new HotkeyDescriptor { ActionId = "app.editor.italic", DisplayName = Loc.T("hotkeys.editor.italic"), Category = catEditor, DefaultGesture = "Ctrl+I", OnExecute = () => Editor?.ToggleItalicAction?.Invoke(), CanExecute = () => Editor?.IsDocumentOpen == true && ActiveContentView == "Scene" },
            new HotkeyDescriptor { ActionId = "app.editor.underline", DisplayName = Loc.T("hotkeys.editor.underline"), Category = catEditor, DefaultGesture = "Ctrl+U", OnExecute = () => Editor?.ToggleUnderlineAction?.Invoke(), CanExecute = () => Editor?.IsDocumentOpen == true && ActiveContentView == "Scene" },
            new HotkeyDescriptor { ActionId = "app.editor.alignLeft", DisplayName = Loc.T("hotkeys.editor.alignLeft"), Category = catEditor, DefaultGesture = "Ctrl+L", OnExecute = () => Editor?.AlignLeftAction?.Invoke(), CanExecute = () => Editor?.IsDocumentOpen == true && ActiveContentView == "Scene" },
            new HotkeyDescriptor { ActionId = "app.editor.alignCenter", DisplayName = Loc.T("hotkeys.editor.alignCenter"), Category = catEditor, DefaultGesture = "Ctrl+E", OnExecute = () => Editor?.AlignCenterAction?.Invoke(), CanExecute = () => Editor?.IsDocumentOpen == true && ActiveContentView == "Scene" },
            new HotkeyDescriptor { ActionId = "app.editor.alignRight", DisplayName = Loc.T("hotkeys.editor.alignRight"), Category = catEditor, DefaultGesture = "Ctrl+R", OnExecute = () => Editor?.AlignRightAction?.Invoke(), CanExecute = () => Editor?.IsDocumentOpen == true && ActiveContentView == "Scene" },
            new HotkeyDescriptor { ActionId = "app.editor.alignJustify", DisplayName = Loc.T("hotkeys.editor.alignJustify"), Category = catEditor, DefaultGesture = "Ctrl+J", OnExecute = () => Editor?.AlignJustifyAction?.Invoke(), CanExecute = () => Editor?.IsDocumentOpen == true && ActiveContentView == "Scene" },

            // ── Project ──
            new HotkeyDescriptor { ActionId = "app.project.save", DisplayName = Loc.T("hotkeys.project.save"), Category = catProject, DefaultGesture = "Ctrl+S", OnExecute = () => { if (Editor?.IsDirty == true) _ = Editor.SaveAsync().ContinueWith(t => { if (t.IsCompletedSuccessfully) Toast.Show?.Invoke(Loc.T("toast.saved"), ToastSeverity.Success); else if (t.Exception != null) Toast.Show?.Invoke(Loc.T("toast.saveFailed", t.Exception.GetBaseException().Message), ToastSeverity.Error); }); }, CanExecute = () => Editor?.IsDocumentOpen == true },

            // ── Git ──
            new HotkeyDescriptor { ActionId = "app.git.commitAll", DisplayName = Loc.T("hotkeys.git.commitAll"), Category = catGit, DefaultGesture = "Ctrl+Shift+K", OnExecute = () => Git?.CommitAllCommand.Execute(null), CanExecute = () => Git?.IsGitRepo == true },
            new HotkeyDescriptor { ActionId = "app.git.push", DisplayName = Loc.T("hotkeys.git.push"), Category = catGit, DefaultGesture = "Ctrl+Shift+P", OnExecute = () => Git?.PushCommand.Execute(null), CanExecute = () => Git?.HasRemote == true },
            new HotkeyDescriptor { ActionId = "app.git.pull", DisplayName = Loc.T("hotkeys.git.pull"), Category = catGit, DefaultGesture = "Ctrl+Shift+L", OnExecute = () => Git?.PullCommand.Execute(null), CanExecute = () => Git?.HasRemote == true },
        ]);
    }

    public ISettingsService SettingsService => _settingsService;
    public IProjectService ProjectService => _projectService;
    public string AppVersion => $"v{Novalist.Core.VersionInfo.Version}";
    public string ProjectTotalWordsDisplay => TextStatistics.FormatCompactCount(ProjectTotalWords);
    public string ProjectReadingTimeDisplay => LocFormatters.ReadingTime(ProjectReadingTimeMinutes);
    public string AverageChapterWordsDisplay => TextStatistics.FormatCompactCount(AverageChapterWords);
    public string DailyGoalLabel => Loc.T("statusBar.dailyPercent", DailyGoalPercent);
    public string ProjectGoalLabel => Loc.T("statusBar.projectPercent", ProjectGoalPercent);
    public bool HasProjectOverview => ProjectOverviewChapters.Count > 0;
    public bool HasOpenEditors => Editor?.IsDocumentOpen == true || EntityEditor?.IsOpen == true;
    public string GitBranchDisplay => Git?.IsGitRepo == true ? $"⎇ {Git.BranchName}" : string.Empty;
    public int GitChangedCount => Git?.ChangedFileCount ?? 0;
    public bool IsInGitRepo => Git?.IsGitRepo == true;

    // ── Extension system ────────────────────────────────────────────

    [ObservableProperty]
    private bool _isExtensionsOpen;

    [ObservableProperty]
    private ExtensionsViewModel? _extensions;

    public ExtensionManager? ExtensionManager { get; private set; }

    /// <summary>Activity bar buttons contributed by extensions.</summary>
    public ObservableCollection<ActivityBarItem> ExtensionActivityBarItems { get; } = [];

    /// <summary>Status bar items contributed by extensions.</summary>
    [ObservableProperty]
    private ObservableCollection<ExtensionStatusBarItemVM> _extensionStatusBarItems = [];

    /// <summary>Sidebar tabs contributed by extensions (left sidebar only).</summary>
    [ObservableProperty]
    private ObservableCollection<ExtensionSidebarTabVM> _extensionSidebarTabs = [];

    /// <summary>Whether an extension right sidebar panel is visible.</summary>
    [ObservableProperty]
    private bool _isExtensionRightSidebarVisible;

    /// <summary>Active right sidebar panel ID, or empty if none.</summary>
    internal string _activeRightSidebarPanelId = string.Empty;

    /// <summary>Right sidebar panels contributed by extensions.</summary>
    internal IReadOnlyList<SidebarPanel> ExtensionRightSidebarPanels { get; private set; } = [];

    /// <summary>Toast notification message shown temporarily.</summary>
    [ObservableProperty]
    private string? _extensionNotification;

    /// <summary>Active toast notifications, newest at top. Auto-dismiss or click-dismiss.</summary>
    public ObservableCollection<ToastNotification> Toasts { get; } = [];

    [RelayCommand]
    private void DismissToast(ToastNotification? toast)
    {
        if (toast != null)
            Toasts.Remove(toast);
    }

    private CancellationTokenSource? _notificationCts;
    private DispatcherTimer? _statusBarRefreshTimer;

    /// <summary>
    /// Called by App after extensions are discovered, loaded, and initialized.
    /// </summary>
    public void OnExtensionsLoaded(ExtensionManager manager, Core.Services.IExtensionGalleryService? galleryService = null)
    {
        ExtensionManager = manager;
        Extensions = new ExtensionsViewModel(manager, galleryService);

        // Build activity bar items from contributed ribbon items
        RebuildExtensionActivityBarItems(manager);

        // Expose status bar items
        ExtensionStatusBarItems = new ObservableCollection<ExtensionStatusBarItemVM>(
            manager.StatusBarItems.Select(s => new ExtensionStatusBarItemVM(s)));

        // Build sidebar tabs from contributed panels (left sidebar only)
        ExtensionSidebarTabs = new ObservableCollection<ExtensionSidebarTabVM>(
            manager.SidebarPanels
                .Where(p => !string.Equals(p.Side, "Right", StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(p.Side, "Context", StringComparison.OrdinalIgnoreCase))
                .Select(p => new ExtensionSidebarTabVM(p)));

        // Store right sidebar panels for on-demand creation
        ExtensionRightSidebarPanels = manager.SidebarPanels
            .Where(p => string.Equals(p.Side, "Right", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Context panels integrate as tabs inside the context sidebar
        foreach (var p in manager.SidebarPanels
            .Where(p => string.Equals(p.Side, "Context", StringComparison.OrdinalIgnoreCase)))
        {
            ExtensionContextTabs.Add(new ExtensionContextTabVM(p));
        }
        HasExtensionContextTabs = ExtensionContextTabs.Count > 0;



        // Pass grammar check contributors to the editor
        Editor?.SetGrammarCheckContributors(manager.GrammarCheckContributors);

        // Bridge SDK editor extensions into the Desktop EditorExtensionManager
        var host = manager.Host;
        host.EditorExtensionRegistered += sdkExt =>
        {
            var bridge = new Editor.SdkEditorExtensionBridge(sdkExt);
            Editor?.ExtensionManager.Register(bridge);
        };
        host.EditorExtensionUnregistered += sdkExt =>
        {
            var existing = Editor?.ExtensionManager.Extensions
                .OfType<Editor.SdkEditorExtensionBridge>()
                .FirstOrDefault(b => b.Name == sdkExt.Name);
            if (existing != null)
                Editor?.ExtensionManager.Unregister(existing);
        };

        // Subscribe to extension toast notifications
        host.NotificationRequested += msg =>
            Dispatcher.UIThread.Post(() => ShowExtensionNotification(msg));

        // Subscribe to extension entity refresh requests
        host.EntityRefreshRequested += () =>
            Dispatcher.UIThread.Post(async () => await EntityPanel.LoadAllAsync());

        // Subscribe to content view activation requests
        host.ContentViewActivated += (viewKey, displayName) =>
            Dispatcher.UIThread.Post(() =>
            {
                if (string.IsNullOrEmpty(viewKey))
                {
                    SetActiveContentView("Scene");
                }
                else
                {
                    IsExtensionContentOpen = true;
                    ExtensionContentTabTitle = !string.IsNullOrEmpty(displayName) ? displayName : viewKey;
                    ActiveContentView = $"ext:{viewKey}";
                }
            });

        // Subscribe to right sidebar toggle requests
        host.RightSidebarToggled += panelId =>
            Dispatcher.UIThread.Post(() =>
            {
                // Context panels open as tabs inside the context sidebar
                if (ExtensionContextTabs.Any(t => t.Id == panelId))
                {
                    if (ActiveContextTab == panelId)
                    {
                        ActiveContextTab = "Context";
                    }
                    else
                    {
                        IsContextSidebarVisible = true;
                        ActiveContextTab = panelId;
                    }
                    return;
                }

                if (_activeRightSidebarPanelId == panelId && IsExtensionRightSidebarVisible)
                {
                    IsExtensionRightSidebarVisible = false;
                    _activeRightSidebarPanelId = string.Empty;
                }
                else
                {
                    _activeRightSidebarPanelId = panelId;
                    IsExtensionRightSidebarVisible = true;
                }
            });

        // Start a 1-second timer to refresh status bar items
        _statusBarRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusBarRefreshTimer.Tick += (_, _) =>
        {
            foreach (var item in ExtensionStatusBarItems)
                item.Refresh();
        };
        _statusBarRefreshTimer.Start();
    }

    private void RebuildExtensionActivityBarItems(ExtensionManager manager)
    {
        ExtensionActivityBarItems.Clear();
        foreach (var r in manager.RibbonItems)
        {
            ExtensionActivityBarItems.Add(new ActivityBarItem
            {
                Label = r.Label,
                Tooltip = string.IsNullOrEmpty(r.Tooltip) ? r.Label : r.Tooltip,
                Icon = r.Icon,
                IconPath = r.IconPath,
                OnClick = r.OnClick
            });
        }
    }

    [RelayCommand]
    private void ExecuteExtensionActivityBarItem(ActivityBarItem item)
    {
        item.OnClick?.Invoke();
    }

    [RelayCommand]
    private async Task ShowActivityViewAsync(string view)
    {
        switch (view)
        {
            case "Settings":
                ToggleSettings();
                ActiveActivityView = IsSettingsOpen ? view : string.Empty;
                return;
            case "Extensions":
                ToggleExtensions();
                ActiveActivityView = IsExtensionsOpen ? view : string.Empty;
                return;
        }

        if (ActiveActivityView == view)
        {
            ActiveActivityView = string.Empty;
            return;
        }
        ActiveActivityView = view;
        switch (view)
        {
            case "Export": ShowExport(); break;
            case "ImageGallery": ShowImageGallery(); break;
            case "Git": await ShowGitAsync(); break;
        }
    }

    [RelayCommand]
    private void ExecuteExtensionStatusBarItem(StatusBarItem item)
    {
        item.OnClick?.Invoke();
    }

    [RelayCommand]
    private void ToggleExtensions()
    {
        IsExtensionsOpen = !IsExtensionsOpen;
        if (IsExtensionsOpen)
        {
            IsStartMenuOpen = false;
            IsSettingsOpen = false;
        }
    }

    public void ShowExtensionNotification(string message) =>
        ShowToast(message, ToastSeverity.Info);

    public void ShowToast(string message, ToastSeverity severity = ToastSeverity.Info, int autoDismissMs = 8000)
    {
        var toast = new ToastNotification(message, severity);
        // Cap stack at 4; drop oldest
        while (Toasts.Count >= 4)
            Toasts.RemoveAt(Toasts.Count - 1);
        Toasts.Insert(0, toast);

        if (autoDismissMs > 0)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(autoDismissMs);
                Dispatcher.UIThread.Post(() => Toasts.Remove(toast));
            });
        }
    }

    public async Task InitializeAsync()
    {
        await _settingsService.LoadAsync();
    }

    public async Task LoadProjectAsync(string projectPath)
    {
        var metadata = await _projectService.LoadProjectAsync(projectPath);
        OnProjectLoaded(metadata, projectPath);
    }

    public async Task CreateProjectAsync(string parentDirectory, string projectName, string firstBookName, string? templateId = null)
    {
        var metadata = await _projectService.CreateProjectAsync(parentDirectory, projectName, firstBookName);

        if (!string.IsNullOrWhiteSpace(templateId))
        {
            var template = App.ProjectTemplateService.GetById(templateId);
            if (template != null)
                await App.ProjectTemplateService.ApplyAsync(_projectService, template);
        }

        var projectPath = _projectService.ProjectRoot!;
        OnProjectLoaded(metadata, projectPath);
    }

    private void OnProjectLoaded(ProjectMetadata metadata, string projectPath)
    {
        IsProjectLoaded = true;
        ProjectName = metadata.Name;

        var activeBook = _projectService.ActiveBook;
        Books = new ObservableCollection<BookData>(metadata.Books);
        ActiveBook = Books.FirstOrDefault(b => b.Id == activeBook?.Id);
        Title = $"Novalist {VersionInfo.Version} — {metadata.Name} — {activeBook?.Name}";
        StatusText = $"Project loaded: {metadata.Name}";

        if (Editor != null)
        {
            Editor.PropertyChanged -= OnEditorPropertyChanged;
            Editor.FocusPeekEntityOpenRequested -= OnFocusPeekEntityOpenRequested;
        }

        if (EntityEditor != null)
        {
            EntityEditor.PropertyChanged -= OnEntityEditorPropertyChanged;
            EntityEditor.Saved -= OnEntitySaved;
        }

        Editor = new EditorViewModel(_projectService, _settingsService, _entityService);
        Editor.PropertyChanged += OnEditorPropertyChanged;
        Editor.FocusPeekEntityOpenRequested += OnFocusPeekEntityOpenRequested;

        // Pass grammar check contributors to the newly created editor
        Editor.SetGrammarCheckContributors(ExtensionManager?.GrammarCheckContributors ?? []);

        ContextSidebar = new ContextSidebarViewModel(_projectService, _entityService);
        ContextSidebar.EntityOpenRequested += OnEntityOpenRequested;
        ContextSidebar.AttachEditor(Editor);
        _ = ContextSidebar.RefreshEntityDataAsync();
        // Preload chapter snapshots in background so first scene open avoids inline forceReload.
        _ = ContextSidebar.PreloadSnapshotsAsync();

        SceneNotes = new SceneNotesViewModel(_projectService);
        SceneNotes.AttachEditor(Editor);

        Explorer = new ExplorerViewModel(_projectService);
        Explorer.SceneOpenRequested += OnSceneOpenRequested;
        Explorer.ProjectChanged += OnProjectChanged;
        Explorer.Refresh();

        EntityEditor = new EntityEditorViewModel(_entityService, _settingsService, _projectService);
        EntityEditor.PropertyChanged += OnEntityEditorPropertyChanged;
        EntityEditor.Saved += OnEntitySaved;
        EntityEditor.Deleted += OnEntityDeleted;
        EntityPanel = new EntityPanelViewModel(_entityService, _projectService);
        EntityPanel.ExtensionEntityTypes = ExtensionManager?.EntityTypes ?? [];
        EntityPanel.EntityOpenRequested += OnEntityOpenRequested;
        EntityPanel.EntityDeleted += OnEntityDeleted;
        EntityPanel.LocationParentChanged += OnLocationParentChanged;
        _ = EntityPanel.LoadAllAsync();

        Dashboard = new DashboardViewModel();

        Timeline = new TimelineViewModel(_projectService);
        Timeline.SceneOpenRequested += OnSceneOpenRequested;

        Export = new ExportViewModel(_projectService);
        if (ExtensionManager?.ExportFormats is { Count: > 0 } exportFormats)
            Export.LoadExtensionFormats(exportFormats);

        ImageGallery = new ImageGalleryViewModel(_entityService);

        Git = new GitViewModel(_gitService);

        CodexHub = new CodexHubViewModel(_entityService, _projectService);
        CodexHub.ExtensionEntityTypes = ExtensionManager?.EntityTypes ?? [];
        CodexHub.EntityOpenRequested += OnEntityOpenRequested;

        Manuscript = new ManuscriptViewModel(_projectService, _entityService);
        Manuscript.SceneOpenRequested += OnSceneOpenRequested;
        Manuscript.SceneFocusChanged += OnManuscriptSceneFocused;
        Manuscript.SceneSaved += () => _ = RefreshGitStatusAsync();

        _settingsService.AddRecentProject(metadata.Name, projectPath, GetCoverImageAbsolutePath());
        _ = _settingsService.SaveAsync();
        _ = RefreshStatusBarAsync();
        // Preload word metrics so first scene open doesn't trigger heavy first-time compute.
        _ = RefreshProjectWordMetricsAsync();
        OnPropertyChanged(nameof(HasOpenEditors));

        // Restore per-project view state
        var viewState = _projectService.ProjectSettings.ViewState;
        IsExplorerVisible = viewState.IsExplorerVisible;
        IsContextSidebarVisible = viewState.IsContextSidebarVisible;
        IsSceneNotesVisible = viewState.IsSceneNotesVisible;

        // Auto-open dashboard
        IsDashboardOpen = true;
        ActiveContentView = "Dashboard";

        // Initialize Git integration asynchronously
        _ = InitializeGitAsync(projectPath);

        // Notify extensions
        ExtensionManager?.Host.RaiseProjectLoaded(metadata.Name, projectPath);
    }

    private async Task InitializeGitAsync(string projectPath)
    {
        await _gitService.InitializeAsync(projectPath);
        if (Git != null)
        {
            Git.StatusRefreshed += OnGitStatusRefreshed;
            await Git.InitializeAsync();
        }
        RefreshExplorerGitStatus();
        OnPropertyChanged(nameof(GitBranchDisplay));
        OnPropertyChanged(nameof(GitChangedCount));
        OnPropertyChanged(nameof(IsInGitRepo));
    }

    private void OnGitStatusRefreshed()
    {
        RefreshExplorerGitStatus();
        OnPropertyChanged(nameof(GitBranchDisplay));
        OnPropertyChanged(nameof(GitChangedCount));
        OnPropertyChanged(nameof(IsInGitRepo));
    }

    private void RefreshExplorerGitStatus()
    {
        if (Explorer == null || Git == null || !_gitService.IsGitRepo)
            return;

        foreach (var item in Explorer.ExplorerItems)
        {
            if (item is not ChapterTreeItemViewModel chapterVm)
                continue;

            foreach (var sceneVm in chapterVm.Scenes)
            {
                var scenePath = _projectService.GetSceneFilePath(sceneVm.ParentChapter, sceneVm.Scene);
                if (_projectService.ProjectRoot != null)
                {
                    var relative = System.IO.Path.GetRelativePath(_projectService.ProjectRoot, scenePath);
                    var status = Git.GetFileStatus(relative);
                    var changed = status != GitFileStatus.Unmodified;
                    sceneVm.HasGitChanges = changed;
                    sceneVm.GitStatusLabel = changed ? "●" : string.Empty;
                }
            }

            chapterVm.RefreshGitStatus();
        }
    }

    private async Task RefreshGitStatusAsync()
    {
        if (Git == null || !_gitService.IsGitRepo)
            return;

        await Git.RefreshAsync();
    }

    private async void OnSceneOpenRequested(ChapterData chapter, SceneData scene)
    {
        if (Editor == null) return;
        try
        {
            ActiveContentView = "Scene";
            IsProjectOverviewOpen = false;
            await Editor.OpenSceneAsync(chapter, scene);
            // ContextSidebar already auto-refreshed via Editor PropertyChanged (Content/IsDocumentOpen/DocumentTitle)
            StatusText = Loc.T("status.editing", scene.Title);
            RefreshProjectWordMetrics();
        }
        catch (System.Exception ex)
        {
            StatusText = Loc.T("status.errorOpenScene", ex.Message);
        }
    }

    private void OnManuscriptSceneFocused(ChapterData chapter, SceneData scene, string plainText)
    {
        ContextSidebar?.RefreshContextForScene(chapter, scene, plainText);
    }

    private void OnEntityOpenRequested(EntityType type, object entity)
    {
        if (EntityEditor == null) return;
        IsProjectOverviewOpen = false;
        var openedEntity = false;

        switch (type)
        {
            case EntityType.Character when entity is CharacterData c:
                EntityEditor.OpenCharacter(c);
                StatusText = Loc.T("status.editing", c.DisplayName);
                openedEntity = true;
                break;
            case EntityType.Location when entity is LocationData l:
                EntityEditor.OpenLocation(l);
                StatusText = Loc.T("status.editing", l.Name);
                openedEntity = true;
                break;
            case EntityType.Item when entity is ItemData i:
                EntityEditor.OpenItem(i);
                StatusText = Loc.T("status.editing", i.Name);
                openedEntity = true;
                break;
            case EntityType.Lore when entity is LoreData lr:
                EntityEditor.OpenLore(lr);
                StatusText = Loc.T("status.editing", lr.Name);
                openedEntity = true;
                break;
            case EntityType.Custom when entity is CustomEntityData ce:
                EntityEditor.OpenCustomEntity(ce);
                StatusText = Loc.T("status.editing", ce.Name);
                openedEntity = true;
                break;
        }

        if (openedEntity)
            ActiveContentView = "Entity";
    }

    private void OnFocusPeekEntityOpenRequested(EntityType type, object entity)
    {
        OnEntityOpenRequested(type, entity);
    }

    private async void OnEntitySaved()
    {
        if (EntityPanel == null) return;
        await EntityPanel.LoadAllAsync();
        if (Editor != null)
            await Editor.RefreshFocusPeekAsync();
        if (ContextSidebar != null)
            await ContextSidebar.RefreshEntityDataAsync();
        await RefreshStatusBarAsync();
        _ = RefreshGitStatusAsync();
    }

    private async void OnEntityDeleted()
    {
        if (EntityPanel == null) return;
        await EntityPanel.LoadAllAsync();
        if (EntityEditor?.IsOpen != true)
            ActiveContentView = GetFallbackView("Entity");
        if (Editor != null)
            await Editor.RefreshFocusPeekAsync();
        if (ContextSidebar != null)
            await ContextSidebar.RefreshEntityDataAsync();
        await RefreshStatusBarAsync();
    }

    private async void OnProjectChanged()
    {
        await RefreshStatusBarAsync();
        ContextSidebar?.RefreshContext();
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorViewModel.IsDocumentOpen)
            or nameof(EditorViewModel.SceneTabTitle)
            or nameof(EditorViewModel.IsDirty)
            or nameof(EditorViewModel.DocumentTitle))
        {
            QueueSyncContentTabs();
        }

        if (e.PropertyName == nameof(EditorViewModel.IsDocumentOpen))
        {
            if (Editor?.IsDocumentOpen != true
                && ActiveContentView == "Scene")
            {
                ActiveContentView = GetFallbackView("Scene");
            }

            OnPropertyChanged(nameof(HasOpenEditors));
        }

        if (e.PropertyName is nameof(EditorViewModel.WordCount)
            or nameof(EditorViewModel.IsDocumentOpen)
            or nameof(EditorViewModel.ReadabilityScore))
        {
            _ = RefreshProjectWordMetricsAsync();
        }

        // Refresh git indicators when a save completes (IsDirty transitions to false)
        if (e.PropertyName == nameof(EditorViewModel.IsDirty) && Editor?.IsDirty == false)
        {
            _ = RefreshGitStatusAsync();
        }
    }

    private void OnLocationParentChanged(LocationData location)
    {
        EntityEditor?.UpdateLocationParent(location);
    }

    private void OnEntityEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EntityEditorViewModel.IsOpen)
            or nameof(EntityEditorViewModel.Title))
        {
            QueueSyncContentTabs();
        }

        if (e.PropertyName != nameof(EntityEditorViewModel.IsOpen))
            return;

        if (EntityEditor?.IsOpen != true && ActiveContentView == "Entity")
            ActiveContentView = GetFallbackView("Entity");

        OnPropertyChanged(nameof(HasOpenEditors));
    }

    [RelayCommand]
    private async Task CloseSceneTabAsync()
    {
        if (Editor == null || !Editor.IsDocumentOpen)
            return;

        await Editor.CloseAsync();
        ContextSidebar?.RefreshContext();
        StatusText = EntityEditor?.IsOpen == true ? Loc.T("status.editing", EntityEditor.Title) : Loc.T("app.ready");
    }

    [RelayCommand]
    private async Task CloseEntityTabAsync()
    {
        if (EntityEditor == null || !EntityEditor.IsOpen)
            return;

        await EntityEditor.CloseCommand.ExecuteAsync(null);
        StatusText = Editor?.IsDocumentOpen == true ? Loc.T("status.editing", Editor.SceneTabTitle) : Loc.T("app.ready");
    }

    [RelayCommand]
    private void CloseDashboardTab()
    {
        IsDashboardOpen = false;
        if (ActiveContentView == "Dashboard")
            ActiveContentView = GetFallbackView("Dashboard");
    }

    [RelayCommand]
    private void CloseTimelineTab()
    {
        IsTimelineOpen = false;
        if (ActiveContentView == "Timeline")
            ActiveContentView = GetFallbackView("Timeline");
    }

    [RelayCommand]
    private void CloseCodexHubTab()
    {
        IsCodexHubOpen = false;
        if (ActiveContentView == "CodexHub")
            ActiveContentView = GetFallbackView("CodexHub");
    }

    [RelayCommand]
    private void CloseManuscriptTab()
    {
        IsManuscriptOpen = false;
        if (ActiveContentView == "Manuscript")
            ActiveContentView = GetFallbackView("Manuscript");
    }

    private string GetFallbackView(string excluding = "")
    {
        if (excluding != "Scene" && Editor?.IsDocumentOpen == true) return "Scene";
        if (excluding != "Entity" && EntityEditor?.IsOpen == true) return "Entity";
        if (excluding != "Dashboard" && IsDashboardOpen) return "Dashboard";
        if (excluding != "Timeline" && IsTimelineOpen) return "Timeline";
        if (excluding != "CodexHub" && IsCodexHubOpen) return "CodexHub";
        if (excluding != "Manuscript" && IsManuscriptOpen) return "Manuscript";
        // Nothing open — open dashboard as last resort
        IsDashboardOpen = true;
        return "Dashboard";
    }

    public async Task RefreshStatusBarAsync()
    {
        await RefreshEntityCountsAsync();
        RefreshProjectWordMetrics();
    }

    private async Task RefreshEntityCountsAsync()
    {
        if (!_projectService.IsProjectLoaded)
            return;

        var charactersTask = _entityService.LoadCharactersAsync();
        var locationsTask = _entityService.LoadLocationsAsync();

        await Task.WhenAll(charactersTask, locationsTask);

        ProjectCharacterCount = charactersTask.Result.Count;
        ProjectLocationCount = locationsTask.Result.Count;
    }

    private void RefreshProjectWordMetrics() => _ = RefreshProjectWordMetricsAsync();

    private int _wordMetricsVersion;

    private async Task RefreshProjectWordMetricsAsync()
    {
        if (!_projectService.IsProjectLoaded || _projectService.CurrentProject == null)
            return;

        var version = Interlocked.Increment(ref _wordMetricsVersion);

        // Snapshot data needed off UI
        var chapters = _projectService.GetChaptersOrdered();
        var lang = _settingsService.Settings.AutoReplacementLanguage;
        var activeSceneId = Editor?.IsDocumentOpen == true ? Editor.CurrentScene?.Id : null;
        var activeContent = Editor?.IsDocumentOpen == true ? Editor.Content : null;
        var activeWordCount = Editor?.IsDocumentOpen == true ? Editor.WordCount : 0;
        var projectRoot = _projectService.ProjectRoot;

        var chapterScenes = chapters
            .Select(ch => (Chapter: ch, Scenes: _projectService.GetScenesForChapter(ch.Guid).ToList(),
                           ScenePaths: _projectService.GetScenesForChapter(ch.Guid).Select(s => _projectService.GetSceneFilePath(ch, s)).ToList()))
            .ToList();

        var built = await Task.Run(() =>
        {
            var totalWords = 0;
            var totalScenes = 0;
            var breakdown = new StringBuilder();
            breakdown.AppendLine(Loc.T("status.chapterBreakdown"));
            var chapterOverviewSource = new List<(ChapterData Chapter, int WordCount, ReadabilityResult Readability, List<StatusBarSceneOverviewItem> Scenes)>(chapterScenes.Count);
            var maxChapterWords = 1;

            foreach (var (chapter, scenes, scenePaths) in chapterScenes)
            {
                var chapterWords = 0;
                totalScenes += scenes.Count;
                var sceneOverview = new List<StatusBarSceneOverviewItem>(scenes.Count);
                var chapterText = new StringBuilder();

                for (int i = 0; i < scenes.Count; i++)
                {
                    var scene = scenes[i];
                    var path = scenePaths[i];
                    var isActive = activeSceneId != null && scene.Id == activeSceneId;
                    var sceneWords = isActive ? activeWordCount : scene.WordCount;
                    chapterWords += sceneWords;
                    sceneOverview.Add(new StatusBarSceneOverviewItem(scene.Title, sceneWords));

                    string sceneContent = isActive
                        ? (activeContent ?? string.Empty)
                        : (projectRoot != null && File.Exists(path) ? File.ReadAllText(path) : string.Empty);

                    if (!string.IsNullOrWhiteSpace(sceneContent))
                    {
                        if (chapterText.Length > 0)
                            chapterText.AppendLine();
                        chapterText.Append(sceneContent);
                    }
                }

                totalWords += chapterWords;
                maxChapterWords = Math.Max(maxChapterWords, chapterWords);
                breakdown.Append(chapter.Title).Append(": ").AppendLine(TextStatistics.FormatCompactCount(chapterWords));

                foreach (var scene in scenes)
                {
                    var w = activeSceneId != null && scene.Id == activeSceneId ? activeWordCount : scene.WordCount;
                    breakdown.Append("  - ").Append(scene.Title).Append(": ").AppendLine(TextStatistics.FormatCompactCount(w));
                }

                var chapterReadability = TextStatistics.Calculate(chapterText.ToString(), lang).Readability;
                chapterOverviewSource.Add((chapter, chapterWords, chapterReadability, sceneOverview));
            }

            return (totalWords, totalScenes, breakdown.ToString().TrimEnd(), chapterOverviewSource, maxChapterWords);
        }).ConfigureAwait(true);

        // Stale check — if newer call started, drop result
        if (version != _wordMetricsVersion)
            return;

        var (totalWords2, totalScenes2, breakdownText, chapterOverviewSource2, maxChapterWords2) = built;

        var chapterOverview = chapterOverviewSource2
            .Select(entry => new StatusBarChapterOverviewItem(
                entry.Chapter.Title,
                entry.WordCount,
                entry.Readability,
                entry.Scenes,
                CalculatePopupBarWidth(entry.WordCount, maxChapterWords2),
                maxChapterWords2))
            .ToList();

        var goals = EnsureProjectGoals(totalWords2);
        var dailyBaseline = goals.DailyBaselineWords ?? totalWords2;
        var dailyWords = Math.Max(0, totalWords2 - dailyBaseline);

        ProjectTotalWords = totalWords2;
        ProjectChapterCount = chapters.Count;
        ProjectSceneCount = totalScenes2;
        ProjectReadingTimeMinutes = TextStatistics.EstimateReadingTime(totalWords2);
        AverageChapterWords = chapters.Count > 0 ? (int)Math.Round(totalWords2 / (double)chapters.Count) : 0;
        ProjectBreakdownTooltip = breakdownText;
        ProjectOverviewChapters = chapterOverview;

        DailyGoalCurrentWords = dailyWords;
        DailyGoalTargetWords = goals.DailyGoal;
        DailyGoalPercent = goals.DailyGoal > 0
            ? Math.Min(100, (int)Math.Round(dailyWords * 100d / goals.DailyGoal))
            : 0;

        ProjectGoalTargetWords = goals.ProjectGoal;
        ProjectGoalPercent = goals.ProjectGoal > 0
            ? Math.Min(100, (int)Math.Round(totalWords2 * 100d / goals.ProjectGoal))
            : 0;

        GoalTooltip = BuildGoalTooltip(goals, dailyWords, totalWords2);
        NotifyStatusBarDisplayPropertiesChanged();
        RefreshDashboard();
    }

    private int GetSceneWordCount(SceneData scene)
    {
        if (Editor?.IsDocumentOpen == true && Editor.CurrentScene?.Id == scene.Id)
            return Editor.WordCount;

        return scene.WordCount;
    }

    private string GetSceneContentForStats(ChapterData chapter, SceneData scene)
    {
        if (Editor?.IsDocumentOpen == true && Editor.CurrentScene?.Id == scene.Id)
            return Editor.Content;

        if (_projectService.ProjectRoot == null)
            return string.Empty;

        var scenePath = _projectService.GetSceneFilePath(chapter, scene);
        return File.Exists(scenePath) ? File.ReadAllText(scenePath) : string.Empty;
    }

    private static double CalculatePopupBarWidth(int words, int maxWords)
    {
        if (maxWords <= 0)
            return 0;

        return Math.Round(40d * words / maxWords, 2);
    }

    private ProjectWordCountGoals EnsureProjectGoals(int totalWords)
    {
        var goals = _projectService.ProjectSettings.WordCountGoals;

        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var changed = false;
        if (!string.Equals(goals.DailyBaselineDate, today, StringComparison.Ordinal))
        {
            goals.DailyBaselineDate = today;
            goals.DailyBaselineWords = totalWords;
            changed = true;
        }
        else if (goals.DailyBaselineWords == null)
        {
            goals.DailyBaselineWords = totalWords;
            changed = true;
        }

        if (changed)
            _ = _projectService.SaveProjectSettingsAsync();

        return goals;
    }

    private string BuildGoalTooltip(ProjectWordCountGoals goals, int dailyWords, int totalWords)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Loc.T("goal.dailyGoal", dailyWords.ToString("N0"), goals.DailyGoal.ToString("N0")));
        builder.AppendLine(Loc.T("goal.projectGoal", totalWords.ToString("N0"), goals.ProjectGoal.ToString("N0")));

        if (!string.IsNullOrWhiteSpace(goals.Deadline))
        {
            builder.AppendLine(Loc.T("goal.deadline", goals.Deadline));
        }

        return builder.ToString().TrimEnd();
    }

    private void NotifyStatusBarDisplayPropertiesChanged()
    {
        OnPropertyChanged(nameof(ProjectTotalWordsDisplay));
        OnPropertyChanged(nameof(ProjectReadingTimeDisplay));
        OnPropertyChanged(nameof(AverageChapterWordsDisplay));
        OnPropertyChanged(nameof(DailyGoalLabel));
        OnPropertyChanged(nameof(ProjectGoalLabel));
        OnPropertyChanged(nameof(HasProjectOverview));
    }

    [RelayCommand]
    private void ToggleProjectOverview()
    {
        if (!HasProjectOverview)
            return;

        IsProjectOverviewOpen = !IsProjectOverviewOpen;
    }

    [RelayCommand]
    private void CloseProjectOverview()
    {
        IsProjectOverviewOpen = false;
    }

    [RelayCommand]
    private void ShowDashboard()
    {
        IsDashboardOpen = true;
        ActiveContentView = "Dashboard";
        IsStartMenuOpen = false;
    }

    [RelayCommand]
    private void ShowTimeline()
    {
        IsTimelineOpen = true;
        ActiveContentView = "Timeline";
        Timeline?.Refresh();
        IsStartMenuOpen = false;
    }

    [RelayCommand]
    private void ShowExport()
    {
        IsExportOpen = true;
        ActiveContentView = "Export";
        Export?.Refresh();
        IsStartMenuOpen = false;
    }

    [RelayCommand]
    private void CloseExportTab()
    {
        IsExportOpen = false;
        if (ActiveContentView == "Export")
            ActiveContentView = GetFallbackView("Export");
    }

    [RelayCommand]
    private void ShowImageGallery()
    {
        IsImageGalleryOpen = true;
        ActiveContentView = "ImageGallery";
        ImageGallery?.Refresh();
        IsStartMenuOpen = false;
    }

    [RelayCommand]
    private void CloseImageGalleryTab()
    {
        IsImageGalleryOpen = false;
        if (ActiveContentView == "ImageGallery")
            ActiveContentView = GetFallbackView("ImageGallery");
    }

    [RelayCommand]
    private void ShowCodexHub()
    {
        IsCodexHubOpen = true;
        ActiveContentView = "CodexHub";
        CodexHub?.Refresh();
        IsStartMenuOpen = false;
    }

    [RelayCommand]
    private void ShowManuscript()
    {
        IsManuscriptOpen = true;
        ActiveContentView = "Manuscript";
        if (Manuscript != null)
        {
            Manuscript.Refresh();
            Manuscript.NotifyContentRefresh();
        }
        IsStartMenuOpen = false;
    }

    [RelayCommand]
    private async Task ShowGitAsync()
    {
        IsGitOpen = true;
        ActiveContentView = "Git";
        if (Git != null)
            await Git.RefreshAsync();
        IsStartMenuOpen = false;
    }

    [RelayCommand]
    private void CloseGitTab()
    {
        IsGitOpen = false;
        if (ActiveContentView == "Git")
            ActiveContentView = GetFallbackView("Git");
    }

    [RelayCommand]
    private void CloseExtensionContentTab()
    {
        IsExtensionContentOpen = false;
        ExtensionContentTabTitle = string.Empty;
        if (ActiveContentView.StartsWith("ext:", StringComparison.Ordinal))
            ActiveContentView = GetFallbackView(ActiveContentView);
    }

    private void RefreshDashboard()
    {
        var coverRelative = _projectService.ActiveBook?.CoverImage
            ?? _projectService.CurrentProject?.CoverImage
            ?? string.Empty;
        Dashboard?.Refresh(
            _projectService.ActiveBook?.Name ?? ProjectName,
            ProjectTotalWords,
            ProjectChapterCount,
            ProjectSceneCount,
            ProjectCharacterCount,
            ProjectLocationCount,
            ProjectReadingTimeMinutes,
            AverageChapterWords,
            DailyGoalCurrentWords,
            DailyGoalTargetWords,
            DailyGoalPercent,
            ProjectGoalTargetWords,
            ProjectGoalPercent,
            _projectService.ProjectSettings.WordCountGoals.Deadline,
            GetCoverImageAbsolutePath(),
            coverRelative);

        // Enhanced stats
        if (Dashboard != null && _projectService.IsProjectLoaded)
        {
            var chapters = _projectService.GetChaptersOrdered();
            var scenesByChapter = new Dictionary<string, List<SceneData>>();
            var sceneContents = new Dictionary<string, string>();

            foreach (var chapter in chapters)
            {
                var scenes = _projectService.GetScenesForChapter(chapter.Guid);
                scenesByChapter[chapter.Guid] = scenes;

                foreach (var scene in scenes)
                {
                    sceneContents[scene.Id] = GetSceneContentForStats(chapter, scene);
                }
            }

            Dashboard.RefreshEnhancedStats(chapters, scenesByChapter, sceneContents);
        }
    }

    [RelayCommand]
    private void ToggleStartMenu()
    {
        IsStartMenuOpen = !IsStartMenuOpen;
    }

    [RelayCommand]
    private void CloseStartMenu()
    {
        IsStartMenuOpen = false;
    }

    /// <summary>Raised when the start menu "Open Project" is clicked; MainWindow handles the folder picker.</summary>
    public event Func<Task>? OpenProjectFromMenuRequested;

    /// <summary>Raised when the start menu needs to open a specific recent project path.</summary>
    public event Func<string, Task>? OpenRecentProjectFromMenuRequested;

    [RelayCommand]
    private async Task OpenProjectFromMenu()
    {
        IsStartMenuOpen = false;
        if (OpenProjectFromMenuRequested != null)
            await OpenProjectFromMenuRequested.Invoke();
    }

    [RelayCommand]
    private async Task OpenRecentProjectFromMenu(RecentProject project)
    {
        IsStartMenuOpen = false;
        if (OpenRecentProjectFromMenuRequested != null)
            await OpenRecentProjectFromMenuRequested.Invoke(project.Path);
    }

    [RelayCommand]
    private void CloseProject()
    {
        IsStartMenuOpen = false;
        IsProjectLoaded = false;
        IsDashboardOpen = false;
        IsTimelineOpen = false;
        IsCodexHubOpen = false;
        IsManuscriptOpen = false;
        Title = $"{Loc.T("app.title")} {VersionInfo.Version}";
        StatusText = string.Empty;
    }

    [RelayCommand]
    private void SetActiveContentView(string view)
    {
        if (!string.IsNullOrEmpty(view) && view.StartsWith("ext:", StringComparison.Ordinal))
        {
            ActiveContentView = view;
            return;
        }

        if (string.Equals(view, "Dashboard", StringComparison.Ordinal))
        {
            IsDashboardOpen = true;
            ActiveContentView = "Dashboard";
            return;
        }

        if (string.Equals(view, "Timeline", StringComparison.Ordinal))
        {
            IsTimelineOpen = true;
            ActiveContentView = "Timeline";
            Timeline?.Refresh();
            return;
        }

        if (string.Equals(view, "Export", StringComparison.Ordinal))
        {
            ActiveContentView = "Export";
            Export?.Refresh();
            return;
        }

        if (string.Equals(view, "ImageGallery", StringComparison.Ordinal))
        {
            ActiveContentView = "ImageGallery";
            ImageGallery?.Refresh();
            return;
        }

        if (string.Equals(view, "Git", StringComparison.Ordinal))
        {
            ActiveContentView = "Git";
            _ = Git?.RefreshAsync();
            return;
        }

        if (string.Equals(view, "CodexHub", StringComparison.Ordinal))
        {
            IsCodexHubOpen = true;
            ActiveContentView = "CodexHub";
            CodexHub?.Refresh();
            return;
        }

        if (string.Equals(view, "Manuscript", StringComparison.Ordinal))
        {
            IsManuscriptOpen = true;
            ActiveContentView = "Manuscript";
            if (Manuscript != null)
            {
                Manuscript.Refresh();
                Manuscript.NotifyContentRefresh();
            }
            return;
        }

        if (string.Equals(view, "Scene", StringComparison.Ordinal) && Editor?.IsDocumentOpen == true)
        {
            ActiveContentView = "Scene";
            return;
        }

        if (string.Equals(view, "Entity", StringComparison.Ordinal) && EntityEditor?.IsOpen == true)
            ActiveContentView = "Entity";
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        IsStartMenuOpen = false;
        IsSettingsOpen = !IsSettingsOpen;
    }

    /// <summary>
    /// Opens Settings and scrolls to a specific category key (e.g. "ext_Writing Toolkit").
    /// </summary>
    public void OpenSettingsToCategory(string categoryKey)
    {
        PendingSettingsCategory = categoryKey;
        IsSettingsOpen = true;
    }

    /// <summary>When set, ShowSettings will auto-select this category after opening.</summary>
    internal string? PendingSettingsCategory { get; set; }

    [RelayCommand]
    private void ToggleExplorer()
    {
        IsExplorerVisible = !IsExplorerVisible;
        SaveViewState();
    }

    [RelayCommand]
    private void ToggleContextSidebar()
    {
        IsContextSidebarVisible = !IsContextSidebarVisible;
        SaveViewState();
    }

    [RelayCommand]
    private void ToggleSceneNotes()
    {
        IsSceneNotesVisible = !IsSceneNotesVisible;
        SaveViewState();
    }

    [RelayCommand]
    private async Task OpenSnapshotsAsync()
    {
        if (Editor?.CurrentScene == null || ShowSnapshotsDialog == null)
            return;

        var scene = Editor.CurrentScene;
        var chapter = _projectService.GetChaptersOrdered()
            .FirstOrDefault(c => string.Equals(c.Guid, scene.ChapterGuid, StringComparison.OrdinalIgnoreCase));
        if (chapter == null)
            return;

        await ShowSnapshotsDialog.Invoke(chapter, scene);
    }

    [RelayCommand]
    private async Task TakeSnapshotAsync()
    {
        if (Editor?.CurrentScene == null)
            return;

        var scene = Editor.CurrentScene;
        var chapter = _projectService.GetChaptersOrdered()
            .FirstOrDefault(c => string.Equals(c.Guid, scene.ChapterGuid, StringComparison.OrdinalIgnoreCase));
        if (chapter == null)
            return;

        await App.SnapshotService.TakeAsync(chapter, scene, string.Empty);
        Toast.Show?.Invoke(Loc.T("snapshots.taken"), ToastSeverity.Info);
    }

    private void SaveViewState()
    {
        if (_projectService.CurrentProject == null) return;

        var viewState = _projectService.ProjectSettings.ViewState;
        viewState.IsExplorerVisible = IsExplorerVisible;
        viewState.IsContextSidebarVisible = IsContextSidebarVisible;
        viewState.IsSceneNotesVisible = IsSceneNotesVisible;
        _ = _projectService.SaveProjectSettingsAsync();
    }

    // ── Cover image ─────────────────────────────────────────────────

    public async Task SetCoverImageFromPickerAsync(string? selectedPath)
    {
        if (string.IsNullOrEmpty(selectedPath) || _projectService.CurrentProject == null)
            return;

        // Handle file import (from file browser)
        if (selectedPath.StartsWith("import:", StringComparison.Ordinal))
        {
            var filePath = selectedPath[7..];
            selectedPath = await _entityService.ImportImageAsync(filePath);
        }

        if (_projectService.ActiveBook != null)
            _projectService.ActiveBook.CoverImage = selectedPath;

        _projectService.CurrentProject.CoverImage = selectedPath;
        await _projectService.SaveProjectAsync();

        _settingsService.AddRecentProject(
            _projectService.CurrentProject.Name,
            _projectService.ProjectRoot!,
            GetCoverImageAbsolutePath());
        await _settingsService.SaveAsync();

        RefreshDashboard();
    }

    private string GetCoverImageAbsolutePath()
    {
        var root = _projectService.ProjectRoot;
        if (root == null) return string.Empty;

        // Prefer active book cover, then project cover
        var bookRoot = _projectService.ActiveBookRoot;
        var bookCover = _projectService.ActiveBook?.CoverImage;
        if (!string.IsNullOrEmpty(bookCover) && bookRoot != null)
        {
            var bookPath = Path.Combine(bookRoot, bookCover);
            if (File.Exists(bookPath))
                return bookPath;
        }

        var projectCover = _projectService.CurrentProject?.CoverImage;
        if (!string.IsNullOrEmpty(projectCover))
        {
            // Project-level cover may be stored relative to the active book folder
            if (bookRoot != null)
            {
                var path = Path.Combine(bookRoot, projectCover);
                if (File.Exists(path))
                    return path;
            }
        }

        return string.Empty;
    }

    // ── Book management commands ────────────────────────────────────

    partial void OnActiveBookChanged(BookData? value)
    {
        if (value == null || _projectService.ActiveBook?.Id == value.Id) return;
        _ = SwitchBookCoreAsync(value.Id);
    }

    private async Task SwitchBookCoreAsync(string bookId)
    {
        // Save current work
        if (Editor?.IsDirty == true)
            await Editor.SaveAsync();
        if (EntityEditor?.IsOpen == true)
            await EntityEditor.CloseCommand.ExecuteAsync(null);

        await _projectService.SwitchBookAsync(bookId);

        // Refresh all sub-VMs
        Explorer?.Refresh();
        if (EntityPanel != null)
            await EntityPanel.LoadAllAsync();
        if (ContextSidebar != null)
            await ContextSidebar.RefreshEntityDataAsync();
        await RefreshStatusBarAsync();
        RefreshDashboard();

        Title = $"Novalist {VersionInfo.Version} — {_projectService.CurrentProject?.Name} — {_projectService.ActiveBook?.Name}";
        StatusText = Loc.T("status.editing", _projectService.ActiveBook?.Name ?? "");
        OnPropertyChanged(nameof(HasOpenEditors));

        // Notify extensions
        if (_projectService.ActiveBook is { } ab)
            ExtensionManager?.Host.RaiseBookChanged(ab.Id, ab.Name);
    }

    [RelayCommand]
    private async Task AddBookAsync()
    {
        if (ShowInputDialog == null) return;

        var name = await ShowInputDialog.Invoke(
            Loc.T("book.addBookTitle"),
            Loc.T("book.addBookPrompt"),
            string.Empty);
        if (string.IsNullOrWhiteSpace(name)) return;

        var book = await _projectService.CreateBookAsync(name.Trim());
        RefreshBookList();
        ActiveBook = Books.FirstOrDefault(b => b.Id == book.Id);
    }

    [RelayCommand]
    private async Task RenameBookAsync(BookData? book)
    {
        if (book == null || ShowInputDialog == null) return;

        var newName = await ShowInputDialog.Invoke(
            Loc.T("book.renameBookTitle"),
            Loc.T("book.renameBookPrompt"),
            book.Name);
        if (string.IsNullOrWhiteSpace(newName)) return;

        await _projectService.RenameBookAsync(book.Id, newName.Trim());
        RefreshBookList();

        Title = $"Novalist {VersionInfo.Version} — {_projectService.CurrentProject?.Name} — {_projectService.ActiveBook?.Name}";
    }

    [RelayCommand]
    private async Task DeleteBookAsync(BookData? book)
    {
        if (book == null || ShowConfirmDialog == null) return;

        if (_projectService.CurrentProject?.Books.Count <= 1)
        {
            StatusText = Loc.T("book.cannotDeleteLast");
            return;
        }

        var confirmed = await ShowConfirmDialog.Invoke(
            Loc.T("book.deleteBookTitle"),
            Loc.T("book.deleteBookMessage", book.Name));
        if (!confirmed) return;

        await _projectService.DeleteBookAsync(book.Id);
        RefreshBookList();
        ActiveBook = Books.FirstOrDefault(b => b.Id == _projectService.ActiveBook?.Id);

        Explorer?.Refresh();
        if (EntityPanel != null)
            await EntityPanel.LoadAllAsync();
        await RefreshStatusBarAsync();
        Title = $"Novalist {VersionInfo.Version} — {_projectService.CurrentProject?.Name} — {_projectService.ActiveBook?.Name}";
    }

    private void RefreshBookList()
    {
        var project = _projectService.CurrentProject;
        if (project == null) return;

        Books = new ObservableCollection<BookData>(project.Books);
        RefreshBookCards();
    }

    private void RefreshBookCards()
    {
        var projectRoot = _projectService.ProjectRoot;
        var activeId = _projectService.ActiveBook?.Id;
        var cards = new ObservableCollection<BookCard>();
        foreach (var book in Books)
        {
            cards.Add(new BookCard(book, projectRoot, book.Id == activeId));
        }
        BookCards = cards;
    }

    [RelayCommand]
    private void ToggleBookPicker()
    {
        if (!IsBookPickerOpen)
            RefreshBookCards();
        IsBookPickerOpen = !IsBookPickerOpen;
    }

    [RelayCommand]
    private void CloseBookPicker()
    {
        IsBookPickerOpen = false;
    }

    [RelayCommand]
    private void SelectBookFromPicker(BookCard? card)
    {
        IsBookPickerOpen = false;
        if (card == null) return;
        var book = Books.FirstOrDefault(b => b.Id == card.Id);
        if (book != null && book.Id != ActiveBook?.Id)
            ActiveBook = book;
    }

    [RelayCommand]
    private Task RenameBookCardAsync(BookCard? card)
        => card == null ? Task.CompletedTask : RenameBookAsync(card.Book);

    [RelayCommand]
    private Task DeleteBookCardAsync(BookCard? card)
        => card == null ? Task.CompletedTask : DeleteBookAsync(card.Book);

    [RelayCommand]
    private async Task RenameProjectAsync()
    {
        if (ShowInputDialog == null) return;
        var project = _projectService.CurrentProject;
        if (project == null) return;

        var newName = await ShowInputDialog.Invoke(
            Loc.T("project.renameProjectTitle"),
            Loc.T("project.renameProjectPrompt"),
            project.Name);
        if (string.IsNullOrWhiteSpace(newName)) return;

        await _projectService.RenameProjectAsync(newName.Trim());
        ProjectName = _projectService.CurrentProject?.Name ?? string.Empty;
        Title = $"Novalist {VersionInfo.Version} \u2014 {_projectService.CurrentProject?.Name} \u2014 {_projectService.ActiveBook?.Name}";
    }
}

public sealed class BookCard
{
    public BookData Book { get; }
    public string Id { get; }
    public string Name { get; }
    public Bitmap? CoverImage { get; }
    public bool HasCoverImage => CoverImage != null;
    public bool IsActive { get; }

    public BookCard(BookData book, string? projectRoot, bool isActive)
    {
        Book = book;
        Id = book.Id;
        Name = book.Name;
        IsActive = isActive;
        CoverImage = LoadCover(book, projectRoot);
    }

    private static Bitmap? LoadCover(BookData book, string? projectRoot)
    {
        if (string.IsNullOrEmpty(book.CoverImage) || string.IsNullOrEmpty(projectRoot))
            return null;

        var bookRoot = Path.Combine(projectRoot, book.FolderName);
        var coverPath = Path.Combine(bookRoot, book.CoverImage);
        if (!File.Exists(coverPath))
            return null;

        try
        {
            using var stream = File.OpenRead(coverPath);
            return Bitmap.DecodeToWidth(stream, 240);
        }
        catch
        {
            return null;
        }
    }
}

public sealed class StatusBarChapterOverviewItem
{
    public StatusBarChapterOverviewItem(
        string name,
        int wordCount,
        ReadabilityResult readability,
        IReadOnlyList<StatusBarSceneOverviewItem> scenes,
        double barWidth,
        int maxWords)
    {
        Name = name;
        WordCount = wordCount;
        Readability = readability;
        Scenes = scenes;
        BarWidth = barWidth;

        foreach (var scene in Scenes)
        {
            scene.BarWidth = Math.Round(40d * scene.WordCount / Math.Max(1, maxWords), 2);
        }
    }

    public string Name { get; }
    public int WordCount { get; }
    public string WordCountDisplay => TextStatistics.FormatCompactCount(WordCount);
    public ReadabilityResult Readability { get; }
    public bool HasReadability => Readability.Score > 0;
    public string ReadabilityDisplay => TextStatistics.FormatReadabilityScore(Readability);
    public string ReadabilityLevelLabel => LocFormatters.ReadabilityLevel(Readability.Level);
    public string ReadabilityColor => TextStatistics.GetReadabilityColor(Readability.Level);
    public IReadOnlyList<StatusBarSceneOverviewItem> Scenes { get; }
    public double BarWidth { get; }
}

public sealed class StatusBarSceneOverviewItem
{
    public StatusBarSceneOverviewItem(string name, int wordCount)
    {
        Name = name;
        WordCount = wordCount;
    }

    public string Name { get; }
    public int WordCount { get; }
    public string WordCountDisplay => TextStatistics.FormatCompactCount(WordCount);
    public double BarWidth { get; set; }
}

public sealed class ExtensionStatusBarItemVM : INotifyPropertyChanged
{
    public ExtensionStatusBarItemVM(StatusBarItem source)
    {
        Source = source;
    }

    public StatusBarItem Source { get; }
    public string DisplayText => Source.GetText();
    public string TooltipText => Source.GetTooltip?.Invoke() ?? string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TooltipText)));
    }
}

public sealed class ExtensionSidebarTabVM
{
    public ExtensionSidebarTabVM(SidebarPanel panel)
    {
        Panel = panel;
    }

    public SidebarPanel Panel { get; }
    public string Id => Panel.Id;
    public string Label => Panel.Label;
    public string Tooltip => Panel.Tooltip;
}

public sealed class ExtensionContextTabVM : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public ExtensionContextTabVM(SidebarPanel panel)
    {
        Panel = panel;
    }

    public SidebarPanel Panel { get; }
    public string Id => Panel.Id;
    public string Label => Panel.Label;
    public string Tooltip => Panel.Tooltip;

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}
