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
    private string _activeTab = "Edit";

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

    public MainWindowViewModel(IProjectService projectService, ISettingsService settingsService, IEntityService entityService, IGitService gitService)
    {
        _projectService = projectService;
        _settingsService = settingsService;
        _entityService = entityService;
        _gitService = gitService;

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
            new HotkeyDescriptor { ActionId = "app.project.save", DisplayName = Loc.T("hotkeys.project.save"), Category = catProject, DefaultGesture = "Ctrl+S", OnExecute = () => { if (Editor?.IsDirty == true) _ = Editor.SaveAsync(); }, CanExecute = () => Editor?.IsDocumentOpen == true },

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

    /// <summary>Ribbon groups contributed by extensions, keyed by group name.</summary>
    [ObservableProperty]
    private ObservableCollection<ExtensionRibbonGroup> _extensionRibbonGroups = [];

    /// <summary>Ribbon groups contributed by extensions for the View tab.</summary>
    [ObservableProperty]
    private ObservableCollection<ExtensionRibbonGroup> _extensionViewRibbonGroups = [];

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

    private CancellationTokenSource? _notificationCts;
    private DispatcherTimer? _statusBarRefreshTimer;

    /// <summary>
    /// Called by App after extensions are discovered, loaded, and initialized.
    /// </summary>
    public void OnExtensionsLoaded(ExtensionManager manager, Core.Services.IExtensionGalleryService? galleryService = null)
    {
        ExtensionManager = manager;
        Extensions = new ExtensionsViewModel(manager, galleryService);

        // Build ribbon groups from contributed items
        RebuildExtensionRibbonGroups(manager);

        // Expose status bar items
        ExtensionStatusBarItems = new ObservableCollection<ExtensionStatusBarItemVM>(
            manager.StatusBarItems.Select(s => new ExtensionStatusBarItemVM(s)));

        // Build sidebar tabs from contributed panels (left sidebar only)
        ExtensionSidebarTabs = new ObservableCollection<ExtensionSidebarTabVM>(
            manager.SidebarPanels
                .Where(p => !string.Equals(p.Side, "Right", StringComparison.OrdinalIgnoreCase))
                .Select(p => new ExtensionSidebarTabVM(p)));

        // Store right sidebar panels for on-demand creation
        ExtensionRightSidebarPanels = manager.SidebarPanels
            .Where(p => string.Equals(p.Side, "Right", StringComparison.OrdinalIgnoreCase))
            .ToList();



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
        host.ContentViewActivated += viewKey =>
            Dispatcher.UIThread.Post(() =>
            {
                if (string.IsNullOrEmpty(viewKey))
                    SetActiveContentView("Scene");
                else
                    ActiveContentView = $"ext:{viewKey}";
            });

        // Subscribe to right sidebar toggle requests
        host.RightSidebarToggled += panelId =>
            Dispatcher.UIThread.Post(() =>
            {
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

    private void RebuildExtensionRibbonGroups(ExtensionManager manager)
    {
        var extensionGroups = manager.RibbonItems
            .Where(r => string.Equals(r.Tab, "Extensions", StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => r.Group)
            .Select(g => new ExtensionRibbonGroup(g.Key, g.ToList()))
            .ToList();
        ExtensionRibbonGroups = new ObservableCollection<ExtensionRibbonGroup>(extensionGroups);

        var viewGroups = manager.RibbonItems
            .Where(r => string.Equals(r.Tab, "View", StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => r.Group)
            .Select(g => new ExtensionRibbonGroup(g.Key, g.ToList()))
            .ToList();
        ExtensionViewRibbonGroups = new ObservableCollection<ExtensionRibbonGroup>(viewGroups);
    }

    [RelayCommand]
    private void ExecuteExtensionRibbonItem(RibbonItem item)
    {
        item.OnClick?.Invoke();
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

    public async void ShowExtensionNotification(string message)
    {
        _notificationCts?.Cancel();
        _notificationCts = new CancellationTokenSource();
        var token = _notificationCts.Token;

        ExtensionNotification = message;
        try
        {
            await Task.Delay(4000, token);
            ExtensionNotification = null;
        }
        catch (TaskCanceledException)
        {
            // A newer notification replaced this one
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

    public async Task CreateProjectAsync(string parentDirectory, string projectName, string firstBookName)
    {
        var metadata = await _projectService.CreateProjectAsync(parentDirectory, projectName, firstBookName);
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

        Manuscript = new ManuscriptViewModel(_projectService);
        Manuscript.SceneOpenRequested += OnSceneOpenRequested;
        Manuscript.SceneSaved += () => _ = RefreshGitStatusAsync();

        _settingsService.AddRecentProject(metadata.Name, projectPath, GetCoverImageAbsolutePath());
        _ = _settingsService.SaveAsync();
        _ = RefreshStatusBarAsync();
        OnPropertyChanged(nameof(HasOpenEditors));

        // Restore per-project view state
        var viewState = _projectService.ProjectSettings.ViewState;
        IsExplorerVisible = viewState.IsExplorerVisible;
        IsContextSidebarVisible = viewState.IsContextSidebarVisible;
        IsSceneNotesVisible = viewState.IsSceneNotesVisible;

        // Auto-open dashboard
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
            ContextSidebar?.RefreshContext();
            StatusText = Loc.T("status.editing", scene.Title);
            RefreshProjectWordMetrics();
        }
        catch (System.Exception ex)
        {
            StatusText = Loc.T("status.errorOpenScene", ex.Message);
        }
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
            ActiveContentView = Editor?.IsDocumentOpen == true ? "Scene" : "Dashboard";
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
        if (e.PropertyName == nameof(EditorViewModel.IsDocumentOpen))
        {
            if (Editor?.IsDocumentOpen != true
                && ActiveContentView == "Scene")
            {
                ActiveContentView = EntityEditor?.IsOpen == true ? "Entity" : "Dashboard";
            }

            OnPropertyChanged(nameof(HasOpenEditors));
        }

        if (e.PropertyName is nameof(EditorViewModel.WordCount)
            or nameof(EditorViewModel.IsDocumentOpen)
            or nameof(EditorViewModel.CurrentScene)
            or nameof(EditorViewModel.ReadabilityScore))
        {
            RefreshProjectWordMetrics();
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
        if (e.PropertyName != nameof(EntityEditorViewModel.IsOpen))
            return;

        if (EntityEditor?.IsOpen != true && ActiveContentView == "Entity")
            ActiveContentView = Editor?.IsDocumentOpen == true ? "Scene" : "Dashboard";

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

    private void RefreshProjectWordMetrics()
    {
        if (!_projectService.IsProjectLoaded || _projectService.CurrentProject == null)
            return;

        var chapters = _projectService.GetChaptersOrdered();
        var totalWords = 0;
        var totalScenes = 0;
        var breakdown = new StringBuilder();
        breakdown.AppendLine(Loc.T("status.chapterBreakdown"));
        var chapterOverviewSource = new List<(ChapterData Chapter, int WordCount, ReadabilityResult Readability, List<StatusBarSceneOverviewItem> Scenes)>(chapters.Count);
        var maxChapterWords = 1;

        foreach (var chapter in chapters)
        {
            var scenes = _projectService.GetScenesForChapter(chapter.Guid);
            var chapterWords = 0;
            totalScenes += scenes.Count;
            var sceneOverview = new List<StatusBarSceneOverviewItem>(scenes.Count);
            var chapterText = new StringBuilder();

            foreach (var scene in scenes)
            {
                var sceneWords = GetSceneWordCount(scene);
                chapterWords += sceneWords;
                sceneOverview.Add(new StatusBarSceneOverviewItem(scene.Title, sceneWords));

                var sceneContent = GetSceneContentForStats(chapter, scene);
                if (!string.IsNullOrWhiteSpace(sceneContent))
                {
                    if (chapterText.Length > 0)
                        chapterText.AppendLine();

                    chapterText.Append(sceneContent);
                }
            }

            totalWords += chapterWords;
            maxChapterWords = Math.Max(maxChapterWords, chapterWords);
            breakdown.Append(chapter.Title)
                .Append(": ")
                .AppendLine(TextStatistics.FormatCompactCount(chapterWords));

            foreach (var scene in scenes)
            {
                breakdown.Append("  - ")
                    .Append(scene.Title)
                    .Append(": ")
                    .AppendLine(TextStatistics.FormatCompactCount(GetSceneWordCount(scene)));
            }

            var chapterReadability = TextStatistics.Calculate(chapterText.ToString(), _settingsService.Settings.AutoReplacementLanguage).Readability;
            chapterOverviewSource.Add((chapter, chapterWords, chapterReadability, sceneOverview));
        }

        var chapterOverview = chapterOverviewSource
            .Select(entry => new StatusBarChapterOverviewItem(
                entry.Chapter.Title,
                entry.WordCount,
                entry.Readability,
                entry.Scenes,
                CalculatePopupBarWidth(entry.WordCount, maxChapterWords),
                maxChapterWords))
            .ToList();

        var goals = EnsureProjectGoals(totalWords);
        var dailyBaseline = goals.DailyBaselineWords ?? totalWords;
        var dailyWords = Math.Max(0, totalWords - dailyBaseline);

        ProjectTotalWords = totalWords;
        ProjectChapterCount = chapters.Count;
        ProjectSceneCount = totalScenes;
        ProjectReadingTimeMinutes = TextStatistics.EstimateReadingTime(totalWords);
        AverageChapterWords = chapters.Count > 0 ? (int)Math.Round(totalWords / (double)chapters.Count) : 0;
        ProjectBreakdownTooltip = breakdown.ToString().TrimEnd();
        ProjectOverviewChapters = chapterOverview;

        DailyGoalCurrentWords = dailyWords;
        DailyGoalTargetWords = goals.DailyGoal;
        DailyGoalPercent = goals.DailyGoal > 0
            ? Math.Min(100, (int)Math.Round(dailyWords * 100d / goals.DailyGoal))
            : 0;

        ProjectGoalTargetWords = goals.ProjectGoal;
        ProjectGoalPercent = goals.ProjectGoal > 0
            ? Math.Min(100, (int)Math.Round(totalWords * 100d / goals.ProjectGoal))
            : 0;

        GoalTooltip = BuildGoalTooltip(goals, dailyWords, totalWords);
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
        ActiveContentView = "Dashboard";
        IsStartMenuOpen = false;
    }

    [RelayCommand]
    private void ShowTimeline()
    {
        ActiveContentView = "Timeline";
        Timeline?.Refresh();
        IsStartMenuOpen = false;
    }

    [RelayCommand]
    private void ShowExport()
    {
        ActiveContentView = "Export";
        Export?.Refresh();
        IsStartMenuOpen = false;
    }

    [RelayCommand]
    private void ShowImageGallery()
    {
        ActiveContentView = "ImageGallery";
        ImageGallery?.Refresh();
        IsStartMenuOpen = false;
    }

    [RelayCommand]
    private void ShowCodexHub()
    {
        ActiveContentView = "CodexHub";
        CodexHub?.Refresh();
        IsStartMenuOpen = false;
    }

    [RelayCommand]
    private void ShowManuscript()
    {
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
        ActiveContentView = "Git";
        if (Git != null)
            await Git.RefreshAsync();
        IsStartMenuOpen = false;
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
    private void SetActiveTab(string tab)
    {
        IsStartMenuOpen = false;
        ActiveTab = tab;
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
        Title = $"{Loc.T("app.title")} {VersionInfo.Version}";
        StatusText = string.Empty;
    }

    [RelayCommand]
    private void SetActiveContentView(string view)
    {
        if (string.Equals(view, "Dashboard", StringComparison.Ordinal))
        {
            ActiveContentView = "Dashboard";
            return;
        }

        if (string.Equals(view, "Timeline", StringComparison.Ordinal))
        {
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
            ActiveContentView = "CodexHub";
            CodexHub?.Refresh();
            return;
        }

        if (string.Equals(view, "Manuscript", StringComparison.Ordinal))
        {
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

public sealed class ExtensionRibbonGroup
{
    public ExtensionRibbonGroup(string groupName, List<RibbonItem> items)
    {
        GroupName = groupName;
        Items = items;
    }

    public string GroupName { get; }
    public List<RibbonItem> Items { get; }
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
