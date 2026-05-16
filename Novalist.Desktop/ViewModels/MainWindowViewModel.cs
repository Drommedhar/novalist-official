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
    private readonly IRecentActivityService _recentActivityService = new RecentActivityService();

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
    private EditorViewModel? _secondaryEditor;

    [ObservableProperty]
    private bool _isSplitEditorOpen;

    /// <summary>
    /// The pane currently focused. Context sidebar re-attaches to follow it.
    /// </summary>
    [ObservableProperty]
    private EditorViewModel? _activeEditor;

    partial void OnActiveEditorChanged(EditorViewModel? value)
    {
        ContextSidebar?.AttachEditor(value);
        FootnotesPanel?.AttachEditor(value);
    }

    public void SetActivePane(EditorViewModel pane)
    {
        if (Editor != null) Editor.IsPaneFocused = pane == Editor;
        if (SecondaryEditor != null) SecondaryEditor.IsPaneFocused = pane == SecondaryEditor;
        ActiveEditor = pane;
    }

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
    private MapViewModel? _maps;

    [ObservableProperty]
    private PlotGridViewModel? _plotGrid;

    [ObservableProperty]
    private bool _isPlotGridOpen;

    partial void OnIsPlotGridOpenChanged(bool value) => QueueSyncContentTabs();

    [ObservableProperty]
    private RelationshipsGraphViewModel? _relationshipsGraph;

    [ObservableProperty]
    private bool _isRelationshipsGraphOpen;

    partial void OnIsRelationshipsGraphOpenChanged(bool value) => QueueSyncContentTabs();

    [ObservableProperty]
    private CalendarViewModel? _calendar;

    [ObservableProperty]
    private bool _isCalendarOpen;

    partial void OnIsCalendarOpenChanged(bool value) => QueueSyncContentTabs();

    [ObservableProperty]
    private ResearchViewModel? _research;

    [ObservableProperty]
    private bool _isResearchOpen;

    partial void OnIsResearchOpenChanged(bool value) => QueueSyncContentTabs();

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
    [ObservableProperty] private bool _isMapsOpen;
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
    partial void OnIsMapsOpenChanged(bool value) => QueueSyncContentTabs();
    partial void OnIsExportOpenChanged(bool value) => QueueSyncContentTabs();
    partial void OnIsImageGalleryOpenChanged(bool value) => QueueSyncContentTabs();
    partial void OnIsGitOpenChanged(bool value) => QueueSyncContentTabs();
    partial void OnIsExtensionContentOpenChanged(bool value) => QueueSyncContentTabs();
    partial void OnExtensionContentTabTitleChanged(string value) => QueueSyncContentTabs();
    partial void OnIsSplitEditorOpenChanged(bool value) => QueueSyncContentTabs();

    partial void OnEditorChanged(EditorViewModel? oldValue, EditorViewModel? newValue)
    {
        if (oldValue != null) DetachPaneListeners(oldValue);
        if (newValue != null) AttachPaneListeners(newValue);
    }

    partial void OnSecondaryEditorChanged(EditorViewModel? oldValue, EditorViewModel? newValue)
    {
        if (oldValue != null) DetachPaneListeners(oldValue);
        if (newValue != null) AttachPaneListeners(newValue);
        QueueSyncContentTabs();
    }

    private readonly Dictionary<EditorViewModel, System.Collections.Specialized.NotifyCollectionChangedEventHandler> _paneCollectionHandlers = new();

    private void AttachPaneListeners(EditorViewModel pane)
    {
        // Capture pane in the handler so we can identify it on event fire.
        // (CollectionChanged.sender is the collection, not the owner.)
        System.Collections.Specialized.NotifyCollectionChangedEventHandler handler =
            (_, e) => OnPaneOpenScenesChanged(pane, e);
        _paneCollectionHandlers[pane] = handler;
        pane.OpenScenes.CollectionChanged += handler;
        pane.PropertyChanged += OnPanePropertyChanged;
        pane.AddCommentRequested += AddComment;
        pane.AddFootnoteRequested += OnAddFootnoteRequestedFromPane;
        Action<string> footnoteClickedHandler = id => _ = EditFootnoteAsync(pane, id);
        _paneFootnoteHandlers[pane] = footnoteClickedHandler;
        pane.FootnoteClicked += footnoteClickedHandler;
    }

    private void DetachPaneListeners(EditorViewModel pane)
    {
        if (_paneCollectionHandlers.TryGetValue(pane, out var handler))
        {
            pane.OpenScenes.CollectionChanged -= handler;
            _paneCollectionHandlers.Remove(pane);
        }
        pane.PropertyChanged -= OnPanePropertyChanged;
        pane.AddCommentRequested -= AddComment;
        pane.AddFootnoteRequested -= OnAddFootnoteRequestedFromPane;
        if (_paneFootnoteHandlers.TryGetValue(pane, out var fnHandler))
        {
            pane.FootnoteClicked -= fnHandler;
            _paneFootnoteHandlers.Remove(pane);
        }
    }

    private readonly Dictionary<EditorViewModel, Action<string>> _paneFootnoteHandlers = new();

    private void OnAddFootnoteRequestedFromPane() => _ = AddFootnote();

    private async Task EditFootnoteAsync(EditorViewModel pane, string footnoteId)
    {
        var scene = pane.CurrentScene;
        if (scene?.Footnotes == null) return;
        var fn = scene.Footnotes.FirstOrDefault(f => f.Id == footnoteId);
        if (fn == null) return;
        if (ShowInputDialog == null) return;

        var result = await ShowInputDialog(
            Loc.T("footnotes.editTitle"),
            Loc.T("footnotes.editPrompt"),
            fn.Text);
        if (result == null) return; // user cancelled

        if (string.IsNullOrWhiteSpace(result))
        {
            // empty = delete
            scene.Footnotes.RemoveAll(f => f.Id == footnoteId);
            pane.RemoveFootnoteAction?.Invoke(footnoteId);
            await _projectService.SaveScenesAsync();
            pane.SyncCommentsAction?.Invoke();
            return;
        }

        fn.Text = result.Trim();
        await _projectService.SaveScenesAsync();
        pane.SyncCommentsAction?.Invoke();
    }

    private void OnPaneOpenScenesChanged(EditorViewModel pane, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        QueueSyncContentTabs();
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove
            && pane == SecondaryEditor
            && pane.OpenScenes.Count == 0
            && IsSplitEditorOpen)
        {
            Dispatcher.UIThread.Post(() => _ = ToggleSplitEditorAsync(),
                Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    private void OnPanePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorViewModel.ActiveOpenScene)
            or nameof(EditorViewModel.IsDirty)
            or nameof(EditorViewModel.SceneTabTitle))
        {
            // Track which scene tab is active per pane focus.
            if (sender is EditorViewModel pane && pane == ActiveEditor && pane.ActiveOpenScene != null)
            {
                var paneKey = pane == Editor ? "P" : "S";
                ActiveSceneTabKey = $"Scene:{paneKey}:{pane.ActiveOpenScene.Scene.Id}";
            }
            QueueSyncContentTabs();
        }
    }

    private void AddSceneTabsForPane(List<EditorTabDescriptor> desired, EditorViewModel? pane, string paneKey)
    {
        if (pane == null || pane.OpenScenes.Count == 0) return;

        foreach (var open in pane.OpenScenes)
        {
            var id = $"Scene:{paneKey}:{open.Scene.Id}";
            var titleBase = string.IsNullOrWhiteSpace(open.Scene.Title) ? open.Chapter.Title : open.Scene.Title;
            var title = SecondaryEditor != null ? $"{titleBase} ({paneKey})" : titleBase;
            var captured = open;
            var tab = new EditorTabDescriptor(
                id, id, title,
                () => _ = pane.CloseTabAsync(captured),
                badge: "SCN", minWidth: 160, tooltip: $"{open.Chapter.Title} — {open.Scene.Title}")
            {
                IsDirty = open == pane.ActiveOpenScene && pane.IsDirty,
                ActivateAction = () => _ = ActivateSceneTabAsync(pane, captured),
                MoveToOtherPaneAction = () => _ = MoveTabAsync(pane, captured)
            };
            desired.Add(tab);
        }
    }

    [ObservableProperty]
    private string _activeSceneTabKey = string.Empty;

    partial void OnActiveSceneTabKeyChanged(string value) => QueueSyncContentTabs();

    private async Task ActivateSceneTabAsync(EditorViewModel pane, EditorOpenScene tab)
    {
        SetActivePane(pane);
        ActiveContentView = "Scene";
        var paneKey = pane == Editor ? "P" : "S";
        ActiveSceneTabKey = $"Scene:{paneKey}:{tab.Scene.Id}";
        await pane.ActivateTabAsync(tab);
    }

    /// <summary>Moves a tab between editor panes. Auto-opens the split if
    /// closed and auto-closes it when the source pane empties.</summary>
    public Task MoveSceneTabAsync(EditorViewModel sourcePane, EditorOpenScene tab)
        => MoveTabAsync(sourcePane, tab);

    private async Task MoveTabAsync(EditorViewModel sourcePane, EditorOpenScene tab)
    {
        var destPane = sourcePane == Editor ? SecondaryEditor : Editor;
        if (destPane == null)
        {
            await ToggleSplitEditorAsync(mirrorCurrentScene: false);
            destPane = sourcePane == Editor ? SecondaryEditor : Editor;
        }
        if (destPane == null || destPane == sourcePane) return;

        var detached = await sourcePane.DetachTabAsync(tab);
        if (detached != null)
        {
            await destPane.AttachTabAsync(detached);
            SetActivePane(destPane);
        }
    }

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
        if (IsMapsOpen)
            desired.Add(new EditorTabDescriptor("Maps", "Maps", Loc.T("ribbon.maps"), () => CloseMapsTabCommand.Execute(null)));
        if (IsPlotGridOpen)
            desired.Add(new EditorTabDescriptor("PlotGrid", "PlotGrid", Loc.T("plotGrid.title"), () => ClosePlotGridTabCommand.Execute(null)));
        if (IsRelationshipsGraphOpen)
            desired.Add(new EditorTabDescriptor("RelationshipsGraph", "RelationshipsGraph", Loc.T("relationships.title"), () => CloseRelationshipsGraphTabCommand.Execute(null)));
        if (IsCalendarOpen)
            desired.Add(new EditorTabDescriptor("Calendar", "Calendar", Loc.T("calendar.title"), () => CloseCalendarTabCommand.Execute(null)));
        if (IsResearchOpen)
            desired.Add(new EditorTabDescriptor("Research", "Research", Loc.T("research.title"), () => CloseResearchTabCommand.Execute(null)));
        if (IsExportOpen)
            desired.Add(new EditorTabDescriptor("Export", "Export", Loc.T("ribbon.export"), () => CloseExportTabCommand.Execute(null)));
        if (IsImageGalleryOpen)
            desired.Add(new EditorTabDescriptor("ImageGallery", "ImageGallery", Loc.T("ribbon.gallery"), () => CloseImageGalleryTabCommand.Execute(null)));
        if (IsGitOpen)
            desired.Add(new EditorTabDescriptor("Git", "Git", Loc.T("ribbon.git"), () => CloseGitTabCommand.Execute(null)));
        if (IsExtensionContentOpen)
            desired.Add(new EditorTabDescriptor("ExtensionContent", ActiveContentView, ExtensionContentTabTitle, () => CloseExtensionContentTabCommand.Execute(null)));
        // Singular Scene tab — top bar only switches scene/content mode; the
        // per-pane tab strips inside the editor handle which scene is active
        // in each pane (mirrors VS Code split editor).
        if ((Editor?.IsDocumentOpen == true) || (SecondaryEditor?.IsDocumentOpen == true))
        {
            var pane = (ActiveEditor == Editor || ActiveEditor == SecondaryEditor) ? ActiveEditor! : Editor!;
            desired.Add(new EditorTabDescriptor(
                "Scene", "Scene", pane.SceneTabTitle ?? string.Empty,
                () => _ = CloseSceneTabAsync(),
                badge: "SCN", minWidth: 160, tooltip: pane.DocumentTitle)
            {
                IsDirty = pane.IsDirty
            });
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
        // Dedup desired and tolerate stale duplicates in ContentTabs.
        desired = desired.GroupBy(d => d.Id).Select(g => g.First()).ToList();
        var existingById = new Dictionary<string, EditorTabDescriptor>();
        foreach (var t in ContentTabs) existingById.TryAdd(t.Id, t);
        for (int i = ContentTabs.Count - 1; i >= 0; i--)
        {
            if (!desired.Any(d => d.Id == ContentTabs[i].Id))
                ContentTabs.RemoveAt(i);
        }
        for (int i = 0; i < desired.Count; i++)
        {
            var d = desired[i];
            bool isActive = d.ActivationKey.StartsWith("Scene:", StringComparison.Ordinal)
                ? d.ActivationKey == ActiveSceneTabKey
                : d.ActivationKey == ActiveContentView;
            if (existingById.TryGetValue(d.Id, out var existing))
            {
                existing.Title = d.Title;
                existing.IsDirty = d.IsDirty;
                existing.Tooltip = d.Tooltip;
                existing.IsActive = isActive;
                int curIdx = ContentTabs.IndexOf(existing);
                if (curIdx != i)
                    ContentTabs.Move(curIdx, i);
            }
            else
            {
                d.IsActive = isActive;
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
    public bool IsFootnotesTabActive => ActiveContextTab == "Footnotes";

    partial void OnActiveContextTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsContextTabActive));
        OnPropertyChanged(nameof(IsFootnotesTabActive));
        foreach (var tab in ExtensionContextTabs)
            tab.IsActive = tab.Id == value;
    }

    [ObservableProperty]
    private FootnotesPanelViewModel? _footnotesPanel;

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
    public Func<Novalist.Sdk.Models.Wizards.WizardDefinition, string?, Novalist.Sdk.Models.Wizards.WizardResult?, Task<Novalist.Sdk.Models.Wizards.WizardResult?>>? ShowWizardDialog { get; set; }
    public Func<ChapterData, SceneData, Task>? ShowSnapshotsDialog { get; set; }
    public Func<Task>? ShowFindReplaceDialog { get; set; }
    public Func<Task>? ShowCommandPalette { get; set; }

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
        // Re-register on language change so descriptor display names re-localise.
        Loc.Instance.LanguageChanged += RegisterBuiltInHotkeys;
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
            new HotkeyDescriptor { ActionId = "app.nav.relationships", DisplayName = Loc.T("hotkeys.nav.relationships"), Category = cat, DefaultGesture = "Ctrl+Shift+R", OnExecute = () => _ = OpenRelationshipsGraphAsync() },
            new HotkeyDescriptor { ActionId = "app.nav.calendar", DisplayName = Loc.T("hotkeys.nav.calendar"), Category = cat, DefaultGesture = "Ctrl+Shift+K", OnExecute = OpenCalendar },
            new HotkeyDescriptor { ActionId = "app.nav.startMenu", DisplayName = Loc.T("hotkeys.nav.startMenu"), Category = cat, DefaultGesture = "Alt+F", OnExecute = ToggleStartMenu },

            // ── Panels ──
            new HotkeyDescriptor { ActionId = "app.panel.explorer", DisplayName = Loc.T("hotkeys.panel.explorer"), Category = catPanels, DefaultGesture = "Ctrl+B", OnExecute = ToggleExplorer, CanExecute = () => IsProjectLoaded && ActiveContentView != "Scene" },
            new HotkeyDescriptor { ActionId = "app.panel.contextSidebar", DisplayName = Loc.T("hotkeys.panel.contextSidebar"), Category = catPanels, DefaultGesture = "Ctrl+Shift+B", OnExecute = ToggleContextSidebar, CanExecute = () => IsProjectLoaded },
            new HotkeyDescriptor { ActionId = "app.panel.sidebarChapters", DisplayName = Loc.T("hotkeys.panel.sidebarChapters"), Category = catPanels, DefaultGesture = "Ctrl+Shift+D1", OnExecute = () => ActiveSidebarTab = "Chapters", CanExecute = () => IsProjectLoaded },
            new HotkeyDescriptor { ActionId = "app.panel.sidebarEntities", DisplayName = Loc.T("hotkeys.panel.sidebarEntities"), Category = catPanels, DefaultGesture = "Ctrl+Shift+D2", OnExecute = () => ActiveSidebarTab = "Entities", CanExecute = () => IsProjectLoaded },
            new HotkeyDescriptor { ActionId = "app.panel.projectOverview", DisplayName = Loc.T("hotkeys.panel.projectOverview"), Category = catPanels, DefaultGesture = "Ctrl+Shift+O", OnExecute = ToggleProjectOverview, CanExecute = () => IsProjectLoaded },
            new HotkeyDescriptor { ActionId = "app.panel.sceneNotes", DisplayName = Loc.T("hotkeys.panel.sceneNotes"), Category = catPanels, DefaultGesture = "Ctrl+Shift+N", OnExecute = ToggleSceneNotes, CanExecute = () => IsProjectLoaded },
            new HotkeyDescriptor { ActionId = "app.panel.focusMode", DisplayName = Loc.T("hotkeys.panel.focusMode"), Category = catPanels, DefaultGesture = "F11", OnExecute = ToggleFocusMode, CanExecute = () => IsProjectLoaded },
            new HotkeyDescriptor { ActionId = "app.editor.findReplace", DisplayName = Loc.T("hotkeys.editor.findReplace"), Category = catEditor, DefaultGesture = "Ctrl+H", OnExecute = () => _ = OpenFindReplaceAsync(), CanExecute = () => IsProjectLoaded },
            new HotkeyDescriptor { ActionId = "app.commandPalette", DisplayName = Loc.T("hotkeys.app.commandPalette"), Category = cat, DefaultGesture = "Ctrl+Shift+P", OnExecute = () => _ = OpenCommandPaletteAsync() },
            new HotkeyDescriptor { ActionId = "app.editor.addComment", DisplayName = Loc.T("hotkeys.editor.addComment"), Category = catEditor, DefaultGesture = "Ctrl+Shift+M", OnExecute = AddComment, CanExecute = () => ActiveContentView == "Scene" && (ActiveEditor ?? Editor)?.IsDocumentOpen == true },
            new HotkeyDescriptor { ActionId = "app.editor.addFootnote", DisplayName = Loc.T("hotkeys.editor.addFootnote"), Category = catEditor, DefaultGesture = "Ctrl+Shift+F", OnExecute = () => _ = AddFootnote(), CanExecute = () => ActiveContentView == "Scene" && (ActiveEditor ?? Editor)?.IsDocumentOpen == true },
            new HotkeyDescriptor { ActionId = "app.editor.styleHeading", DisplayName = Loc.T("hotkeys.editor.styleHeading"), Category = catEditor, DefaultGesture = "Ctrl+Alt+1", OnExecute = () => ApplyParagraphStyle("heading"), CanExecute = () => (ActiveEditor ?? Editor)?.IsDocumentOpen == true },
            new HotkeyDescriptor { ActionId = "app.editor.styleSubheading", DisplayName = Loc.T("hotkeys.editor.styleSubheading"), Category = catEditor, DefaultGesture = "Ctrl+Alt+2", OnExecute = () => ApplyParagraphStyle("subheading"), CanExecute = () => (ActiveEditor ?? Editor)?.IsDocumentOpen == true },
            new HotkeyDescriptor { ActionId = "app.editor.styleBlockquote", DisplayName = Loc.T("hotkeys.editor.styleBlockquote"), Category = catEditor, DefaultGesture = "Ctrl+Alt+3", OnExecute = () => ApplyParagraphStyle("blockquote"), CanExecute = () => (ActiveEditor ?? Editor)?.IsDocumentOpen == true },
            new HotkeyDescriptor { ActionId = "app.editor.stylePoetry", DisplayName = Loc.T("hotkeys.editor.stylePoetry"), Category = catEditor, DefaultGesture = "Ctrl+Alt+4", OnExecute = () => ApplyParagraphStyle("poetry"), CanExecute = () => (ActiveEditor ?? Editor)?.IsDocumentOpen == true },
            new HotkeyDescriptor { ActionId = "app.editor.styleClear", DisplayName = Loc.T("hotkeys.editor.styleClear"), Category = catEditor, DefaultGesture = "Ctrl+Alt+0", OnExecute = () => ApplyParagraphStyle(string.Empty), CanExecute = () => (ActiveEditor ?? Editor)?.IsDocumentOpen == true },

            // ── Scene / Tab management ──
            new HotkeyDescriptor { ActionId = "app.scene.closeTab", DisplayName = Loc.T("hotkeys.scene.closeTab"), Category = catScene, DefaultGesture = "Ctrl+W", OnExecute = () => _ = CloseSceneTabAsync(), CanExecute = () => Editor?.IsDocumentOpen == true },
            new HotkeyDescriptor { ActionId = "app.scene.create", DisplayName = Loc.T("hotkeys.scene.create"), Category = catScene, DefaultGesture = "Ctrl+N", OnExecute = () => Explorer?.CreateSceneCommand.Execute(null), CanExecute = () => Explorer != null },
            new HotkeyDescriptor { ActionId = "app.scene.next", DisplayName = Loc.T("hotkeys.scene.next"), Category = catScene, DefaultGesture = "Ctrl+OemCloseBrackets", OnExecute = () => Explorer?.NavigateScene(1), CanExecute = () => Explorer != null },
            new HotkeyDescriptor { ActionId = "app.scene.prev", DisplayName = Loc.T("hotkeys.scene.prev"), Category = catScene, DefaultGesture = "Ctrl+OemOpenBrackets", OnExecute = () => Explorer?.NavigateScene(-1), CanExecute = () => Explorer != null },
            new HotkeyDescriptor { ActionId = "app.chapter.create", DisplayName = Loc.T("hotkeys.chapter.create"), Category = catScene, DefaultGesture = "Ctrl+Shift+M", OnExecute = () => Explorer?.CreateChapterCommand.Execute(null), CanExecute = () => Explorer != null },
            new HotkeyDescriptor { ActionId = "app.scene.archive", DisplayName = Loc.T("hotkeys.scene.archive"), Category = catScene, DefaultGesture = "", OnExecute = () => Explorer?.ArchiveSelectedSceneCommand.Execute(null), CanExecute = () => Explorer?.SelectedScene != null },
            new HotkeyDescriptor { ActionId = "app.scene.restoreFromArchive", DisplayName = Loc.T("hotkeys.scene.restoreFromArchive"), Category = catScene, DefaultGesture = "", OnExecute = () => { if (Editor?.RestoreCurrentArchivedSceneCommand.CanExecute(null) == true) Editor.RestoreCurrentArchivedSceneCommand.Execute(null); }, CanExecute = () => Editor?.IsCurrentSceneArchived == true },
            new HotkeyDescriptor { ActionId = "app.scene.openArchive", DisplayName = Loc.T("hotkeys.scene.openArchive"), Category = catScene, DefaultGesture = "", OnExecute = () => { if (Explorer != null) Explorer.IsArchiveExpanded = !Explorer.IsArchiveExpanded; }, CanExecute = () => Explorer?.HasArchivedScenes == true },

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

    /// <summary>
    /// Runs the Project Snowflake wizard, then creates the project from its
    /// answers and applies the cast/chapter seed via
    /// <see cref="Services.Wizards.ProjectWizardMapper"/>.
    /// </summary>
    public async Task RunProjectSnowflakeWizardAsync(string parentDirectory)
    {
        if (ShowWizardDialog == null) return;

        var definition = Novalist.Core.Wizards.ProjectSnowflakeWizard.Build(Loc.T);
        var stateDir = GetWizardStateDirForScope(definition.Scope);
        var result = await ShowWizardDialog.Invoke(definition, stateDir, null);
        if (result == null || !result.Completed) return;

        var projectName = Novalist.Desktop.Services.Wizards.ProjectWizardMapper.ExtractProjectName(result);
        var bookName = Novalist.Desktop.Services.Wizards.ProjectWizardMapper.ExtractBookName(result);
        if (string.IsNullOrWhiteSpace(projectName))
        {
            StatusText = Loc.T("toast.projectLoadFailed", "missing project name");
            return;
        }

        try
        {
            await CreateProjectAsync(parentDirectory, projectName, bookName, "blank");
            await Novalist.Desktop.Services.Wizards.ProjectWizardMapper.ApplyAsync(_projectService, _entityService, result);
            // Mapper writes characters via IEntityService.SaveCharacterAsync; the
            // freshly-loaded EntityPanel was populated before those writes
            // landed, so re-load it now.
            if (EntityPanel != null)
                await EntityPanel.LoadAllAsync();
            if (Editor != null)
                await Editor.RefreshFocusPeekAsync();
            Explorer?.Refresh();
            await RefreshStatusBarAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            Toast.Show?.Invoke(Loc.T("toast.projectLoadFailed", ex.Message), ToastSeverity.Error);
        }
    }

    /// <summary>
    /// Runs the generic guided-entity wizard for a freshly-created entity, maps
    /// the result onto it, and saves.
    /// </summary>
    public async Task RunEntityWizardForCreatedAsync(EntityType type, object entity, string? customTypeKey)
    {
        if (ShowWizardDialog == null) return;

        Novalist.Core.Models.CustomEntityTypeDefinition? customDef = null;
        if (type == EntityType.Custom && customTypeKey != null)
        {
            customDef = _projectService.CurrentProject?.CustomEntityTypes
                .FirstOrDefault(t => string.Equals(t.TypeKey, customTypeKey, StringComparison.Ordinal));
        }

        var definition = Novalist.Core.Wizards.EntityGuidedWizard.BuildFor(type, customDef, Loc.T);

        // Entity already has the name from the creation dialog. Hide the wizard's
        // name step and seed its answer so the user is not asked twice.
        var existingName = GetEntityName(entity);
        definition.Steps = definition.Steps
            .Where(s => !string.Equals(s.Id, "name", System.StringComparison.OrdinalIgnoreCase))
            .ToList();
        var seed = new Novalist.Sdk.Models.Wizards.WizardResult { DefinitionId = definition.Id };
        if (!string.IsNullOrEmpty(existingName))
            seed.Answers["name"] = new Novalist.Sdk.Models.Wizards.WizardAnswer { Text = existingName };

        var stateDir = GetWizardStateDirForScope(definition.Scope);
        var result = await ShowWizardDialog.Invoke(definition, stateDir, seed);
        if (result == null || !result.Completed) return;

        switch (type)
        {
            case EntityType.Character when entity is CharacterData c:
                var nc = Novalist.Desktop.Services.Wizards.EntityWizardMapper.BuildCharacter(result);
                c.Surname = nc.Surname;
                c.Gender = nc.Gender;
                c.Age = nc.Age;
                c.Role = nc.Role;
                c.Group = nc.Group;
                foreach (var s in nc.Sections) c.Sections.Add(s);
                await _entityService.SaveCharacterAsync(c);
                break;
            case EntityType.Location when entity is LocationData l:
                var nl = Novalist.Desktop.Services.Wizards.EntityWizardMapper.BuildLocation(result);
                l.Type = nl.Type;
                l.Parent = nl.Parent;
                l.Description = nl.Description;
                await _entityService.SaveLocationAsync(l);
                break;
            case EntityType.Item when entity is ItemData i:
                var ni = Novalist.Desktop.Services.Wizards.EntityWizardMapper.BuildItem(result);
                i.Type = ni.Type;
                i.Origin = ni.Origin;
                i.Description = ni.Description;
                await _entityService.SaveItemAsync(i);
                break;
            case EntityType.Lore when entity is LoreData lo:
                var nlo = Novalist.Desktop.Services.Wizards.EntityWizardMapper.BuildLore(result);
                lo.Category = nlo.Category;
                lo.Description = nlo.Description;
                await _entityService.SaveLoreAsync(lo);
                break;
            case EntityType.Custom when entity is CustomEntityData ce && customDef != null:
                var nce = Novalist.Desktop.Services.Wizards.EntityWizardMapper.BuildCustomEntity(result, customDef);
                foreach (var kv in nce.Fields) ce.Fields[kv.Key] = kv.Value;
                await _entityService.SaveCustomEntityAsync(ce);
                break;
        }

        if (Editor != null)
            await Editor.RefreshFocusPeekAsync();
    }

    /// <summary>
    /// Runs the character-interview wizard for the given character, then maps
    /// its result onto the character and saves.
    /// </summary>
    public async Task RunCharacterInterviewForAsync(CharacterData character)
    {
        if (ShowWizardDialog == null) return;
        var definition = Novalist.Core.Wizards.CharacterInterviewWizard.Build(Loc.T);
        var stateDir = GetWizardStateDirForScope(definition.Scope);

        // Strip the wizard's name step — character already exists. Seed answer
        // so the mapper can still write back if needed.
        definition.Steps = definition.Steps
            .Where(s => !string.Equals(s.Id, "name", System.StringComparison.OrdinalIgnoreCase))
            .ToList();
        var seed = new Novalist.Sdk.Models.Wizards.WizardResult { DefinitionId = definition.Id };
        seed.Answers["name"] = new Novalist.Sdk.Models.Wizards.WizardAnswer { Text = character.Name };

        var result = await ShowWizardDialog.Invoke(definition, stateDir, seed);
        if (result == null || !result.Completed) return;

        Novalist.Desktop.Services.Wizards.CharacterInterviewMapper.Apply(character, result);
        await _entityService.SaveCharacterAsync(character);
        if (Editor != null)
            await Editor.RefreshFocusPeekAsync();
    }

    /// <summary>Returns the directory where wizard state files for the given
    /// scope are persisted. Project-scope: app data; Entity/Reference: active
    /// book's <c>.book/wizards/</c> folder.</summary>
    public string? GetWizardStateDirForScope(Novalist.Sdk.Models.Wizards.WizardScope scope)
    {
        return scope switch
        {
            Novalist.Sdk.Models.Wizards.WizardScope.Project
                => System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Novalist", "Wizards"),
            _ when _projectService.ActiveBookRoot != null
                => System.IO.Path.Combine(_projectService.ActiveBookRoot, ".book", "Wizards"),
            _ => null,
        };
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

        _ = LoadAndMigrateWordHistoryAsync();
        RefreshDraftList();

        var activeBook = _projectService.ActiveBook;
        Books = new ObservableCollection<BookData>(metadata.Books);
        ActiveBook = Books.FirstOrDefault(b => b.Id == activeBook?.Id);
        Title = $"Novalist {VersionInfo.Version} — {metadata.Name} — {activeBook?.Name}";
        StatusText = $"Project loaded: {metadata.Name}";

        if (Editor != null)
        {
            Editor.PropertyChanged -= OnEditorPropertyChanged;
            Editor.FocusPeekEntityOpenRequested -= OnFocusPeekEntityOpenRequested;
            Editor.FocusPeekPinNavigateRequested -= OnFocusPeekPinNavigateRequestedAsync;
        }

        if (EntityEditor != null)
        {
            EntityEditor.PropertyChanged -= OnEntityEditorPropertyChanged;
            EntityEditor.Saved -= OnEntitySaved;
        }

        Editor = new EditorViewModel(_projectService, _settingsService, _entityService);
        Editor.PropertyChanged += OnEditorPropertyChanged;
        Editor.FocusPeekEntityOpenRequested += OnFocusPeekEntityOpenRequested;
        Editor.FocusPeekPinNavigateRequested += OnFocusPeekPinNavigateRequestedAsync;
        Editor.SceneSaved += OnSceneSavedForActivity;
        Editor.RestoreArchivedSceneRequested += OnRestoreArchivedSceneFromEditor;
        ActiveEditor = Editor;
        Editor.IsPaneFocused = true;

        _ = LoadRecentActivityAsync(projectPath);

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

        FootnotesPanel = new FootnotesPanelViewModel(_projectService);
        FootnotesPanel.AttachEditor(Editor);

        Explorer = new ExplorerViewModel(_projectService);
        Explorer.SceneOpenRequested += OnSceneOpenRequested;
        Explorer.ArchivedSceneOpenRequested += OnArchivedSceneOpenRequested;
        Explorer.ProjectChanged += OnProjectChanged;
        Explorer.OpenSceneInSplitPaneRequested = sceneVm => _ = OpenSceneInSplitPaneAsync(sceneVm);
        Explorer.Refresh();

        EntityEditor = new EntityEditorViewModel(_entityService, _settingsService, _projectService);
        EntityEditor.PropertyChanged += OnEntityEditorPropertyChanged;
        EntityEditor.Saved += OnEntitySaved;
        EntityEditor.Deleted += OnEntityDeleted;
        EntityEditor.RunCharacterInterviewRequested = RunCharacterInterviewForAsync;
        EntityPanel = new EntityPanelViewModel(_entityService, _projectService);
        EntityPanel.ExtensionEntityTypes = ExtensionManager?.EntityTypes ?? [];
        EntityPanel.EntityOpenRequested += OnEntityOpenRequested;
        EntityPanel.EntityDeleted += OnEntityDeleted;
        EntityPanel.LocationParentChanged += OnLocationParentChanged;
        _ = EntityPanel.LoadAllAsync();

        Dashboard = new DashboardViewModel();
        Dashboard.AttachWordHistory(App.WordHistoryService);
        Dashboard.SetActiveBookId(_projectService.ActiveBook?.Id ?? string.Empty);

        Timeline = new TimelineViewModel(_projectService);
        Timeline.SceneOpenRequested += OnSceneOpenRequested;

        Export = new ExportViewModel(_projectService, _entityService);
        if (ExtensionManager?.ExportFormats is { Count: > 0 } exportFormats)
            Export.LoadExtensionFormats(exportFormats);

        ImageGallery = new ImageGalleryViewModel(_entityService);

        Git = new GitViewModel(_gitService);

        CodexHub = new CodexHubViewModel(_entityService, _projectService);
        CodexHub.ExtensionEntityTypes = ExtensionManager?.EntityTypes ?? [];
        CodexHub.EntityOpenRequested += OnEntityOpenRequested;
        CodexHub.ManageEntityTypesRequested += () =>
        {
            if (EntityPanel?.CreateEntityTypeCommand.CanExecute(null) == true)
                EntityPanel.CreateEntityTypeCommand.Execute(null);
        };
        CodexHub.OpenTemplatesRequested += () => OpenSettingsToCategory("templates");

        Manuscript = new ManuscriptViewModel(_projectService, _entityService);
        Maps = new MapViewModel(App.MapService, _projectService);
        Maps.RefreshMaps();
        // Note: Maps.ShowInputDialog + Maps.PickImageRequested are wired by
        // MainWindow.WireMapView when the Maps property changes — do NOT set
        // them here (would clobber the host picker with a null fallback).
        PlotGrid = new PlotGridViewModel(_projectService, App.PlotlineService);

        RelationshipsGraph = new RelationshipsGraphViewModel(_entityService);

        Calendar = new CalendarViewModel(_projectService);
        Calendar.SceneOpenRequested += (chapterGuid, sceneId) =>
        {
            var ch = _projectService.GetChaptersOrdered().FirstOrDefault(c => c.Guid == chapterGuid);
            var sc = ch == null ? null : _projectService.GetScenesForChapter(ch.Guid).FirstOrDefault(s => s.Id == sceneId);
            if (ch != null && sc != null) OnSceneOpenRequested(ch, sc);
        };
        Research = new ResearchViewModel(App.ResearchService);
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

    private async Task OpenSceneInSplitPaneAsync(SceneTreeItemViewModel sceneVm)
    {
        var chapter = _projectService.GetChaptersOrdered().FirstOrDefault(c => c.Guid == sceneVm.Scene.ChapterGuid);
        if (chapter == null) return;

        if (SecondaryEditor == null)
            await ToggleSplitEditorAsync(mirrorCurrentScene: false);
        if (SecondaryEditor == null) return;

        ActiveContentView = "Scene";
        IsProjectOverviewOpen = false;
        try
        {
            await SecondaryEditor.OpenSceneAsync(chapter, sceneVm.Scene);
            SetActivePane(SecondaryEditor);
            StatusText = Loc.T("status.editing", sceneVm.Scene.Title);
        }
        catch (System.Exception ex)
        {
            StatusText = Loc.T("status.errorOpenScene", ex.Message);
        }
    }

    private async void OnSceneOpenRequested(ChapterData chapter, SceneData scene)
    {
        // Only honor ActiveEditor when it's still one of the live panes.
        var target = (ActiveEditor == Editor || ActiveEditor == SecondaryEditor)
            ? ActiveEditor
            : Editor;
        if (target == null) return;
        try
        {
            ActiveContentView = "Scene";
            IsProjectOverviewOpen = false;
            await target.OpenSceneAsync(chapter, scene);
            SetActivePane(target);
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

    private async Task LoadRecentActivityAsync(string projectPath)
    {
        try
        {
            await _recentActivityService.LoadAsync(projectPath);
        }
        catch { /* non-critical */ }
        UpdateDashboardRecentActivity();
        _recentActivityService.Changed -= UpdateDashboardRecentActivity;
        _recentActivityService.Changed += UpdateDashboardRecentActivity;
    }

    private void UpdateDashboardRecentActivity()
    {
        if (Dashboard == null) return;
        Dashboard.RecentActivity = new ObservableCollection<ActivityItem>(_recentActivityService.Recent);
    }

    private void OnSceneSavedForActivity(ChapterData chapter, SceneData scene)
    {
        _ = _recentActivityService.LogAsync(new ActivityItem
        {
            Type = ActivityType.Edit,
            ChapterGuid = chapter.Guid,
            ChapterTitle = chapter.Title,
            SceneId = scene.Id,
            SceneTitle = scene.Title,
            Timestamp = DateTime.UtcNow
        });
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

    private async Task OnFocusPeekPinNavigateRequestedAsync(string mapId, string pinId)
    {
        if (string.IsNullOrEmpty(mapId) || string.IsNullOrEmpty(pinId) || Maps == null) return;
        // Switch to the Maps view; open the requested map; flip to view mode;
        // tell the WebView to centre + flash the pin.
        ActiveContentView = "Maps";
        IsMapsOpen = true;
        var mapRef = Maps.Maps?.FirstOrDefault(m => string.Equals(m.Id, mapId, StringComparison.OrdinalIgnoreCase));
        if (mapRef != null) Maps.SelectedMap = mapRef;
        Maps.IsEditMode = false;
        // Wait briefly for the WebView to load + render the map before pushing focus.
        await Task.Delay(150);
        Maps.PushFocusOnPin?.Invoke(pinId);
    }

    private async void OnEntitySaved(IEntityData? entity)
    {
        if (EntityPanel == null) return;

        // Auto-rewrite mention spans whose data-mention-source != "manual"
        // to reflect the entity's new display text after rename.
        if (entity != null)
        {
            var displayText = GetEntityDisplayText(entity);
            if (!string.IsNullOrEmpty(displayText))
                _ = _projectService.SyncMentionDisplayTextAsync(entity.Id, displayText);
        }

        await EntityPanel.LoadAllAsync();
        if (Editor != null)
            await Editor.RefreshFocusPeekAsync();
        if (ContextSidebar != null)
            await ContextSidebar.RefreshEntityDataAsync();
        await RefreshStatusBarAsync();
        _ = RefreshGitStatusAsync();
    }

    private static async Task LoadAndMigrateWordHistoryAsync()
    {
        await App.WordHistoryService.LoadAsync();
        await App.WordHistoryService.MigrateLegacyBaselineAsync();
    }

    /// <summary>Called from MapView when a pin is clicked in view mode.</summary>
    public async Task OpenEntityByIdAsync(string entityId)
    {
        if (string.IsNullOrEmpty(entityId)) return;
        var characters = await _entityService.LoadCharactersAsync();
        var ch = characters.FirstOrDefault(c => string.Equals(c.Id, entityId, StringComparison.OrdinalIgnoreCase));
        if (ch != null) { OnEntityOpenRequested(EntityType.Character, ch); return; }
        var locations = await _entityService.LoadLocationsAsync();
        var loc = locations.FirstOrDefault(l => string.Equals(l.Id, entityId, StringComparison.OrdinalIgnoreCase));
        if (loc != null) { OnEntityOpenRequested(EntityType.Location, loc); return; }
        var items = await _entityService.LoadItemsAsync();
        var it = items.FirstOrDefault(i => string.Equals(i.Id, entityId, StringComparison.OrdinalIgnoreCase));
        if (it != null) { OnEntityOpenRequested(EntityType.Item, it); return; }
        var lore = await _entityService.LoadLoreAsync();
        var lr = lore.FirstOrDefault(l => string.Equals(l.Id, entityId, StringComparison.OrdinalIgnoreCase));
        if (lr != null) { OnEntityOpenRequested(EntityType.Lore, lr); return; }
        // Custom entity types.
        var types = _projectService.CurrentProject?.CustomEntityTypes ?? new();
        foreach (var t in types)
        {
            var customs = await _entityService.LoadCustomEntitiesAsync(t.TypeKey);
            var match = customs.FirstOrDefault(ce => string.Equals(ce.Id, entityId, StringComparison.OrdinalIgnoreCase));
            if (match != null) { OnEntityOpenRequested(EntityType.Custom, match); return; }
        }
    }

    private async Task<(string RelativePath, double Width, double Height)?> PickMapImageAsync()
    {
        // Picker lives on MainWindow; ViewModel asks the View.
        if (PickImageForMapRequested != null)
            return await PickImageForMapRequested.Invoke();
        return null;
    }

    /// <summary>Host-supplied (MainWindow): picks an image, copies it into the
    /// book's Images/ folder if needed, and returns a relative path + dimensions.</summary>
    public Func<Task<(string RelativePath, double Width, double Height)?>>? PickImageForMapRequested { get; set; }

    [RelayCommand]
    private void OpenMaps()
    {
        ActiveContentView = "Maps";
        IsMapsOpen = true;
    }

    private static string GetEntityName(object entity) => entity switch
    {
        CharacterData c => c.Name,
        LocationData l => l.Name,
        ItemData i => i.Name,
        LoreData lo => lo.Name,
        CustomEntityData ce => ce.Name,
        _ => string.Empty
    };

    private static string GetEntityDisplayText(IEntityData entity) => entity switch
    {
        CharacterData c => c.DisplayName,
        LocationData l => l.Name,
        ItemData i => i.Name,
        LoreData lo => lo.Name,
        CustomEntityData ce => ce.Name,
        _ => string.Empty
    };

    private void OnArchivedSceneOpenRequested(SceneData scene)
    {
        if (Editor == null) return;
        // Synthesize a placeholder chapter so the editor can show + load content.
        var chapter = new ChapterData
        {
            Guid = scene.OriginChapterGuid ?? string.Empty,
            Title = string.IsNullOrEmpty(scene.OriginChapterGuid)
                ? Loc.T("explorer.archive")
                : (_projectService.GetChaptersOrdered()
                    .FirstOrDefault(c => string.Equals(c.Guid, scene.OriginChapterGuid, StringComparison.OrdinalIgnoreCase))?.Title
                   ?? Loc.T("explorer.archive"))
        };
        OnSceneOpenRequested(chapter, scene);
        SetActiveContentView("Scene");
    }

    private async void OnRestoreArchivedSceneFromEditor(SceneData scene)
    {
        if (Editor == null) return;
        var origin = scene.OriginChapterGuid;
        var chapters = _projectService.GetChaptersOrdered();
        if (chapters.Count == 0) return;
        var targetGuid = !string.IsNullOrEmpty(origin)
            && chapters.Any(c => string.Equals(c.Guid, origin, StringComparison.OrdinalIgnoreCase))
                ? origin
                : chapters[0].Guid;

        // Close the tab showing the archived scene so the file move is safe.
        var openTab = Editor.OpenScenes.FirstOrDefault(t => t.Scene.Id == scene.Id);
        if (openTab != null)
            await Editor.CloseTabAsync(openTab);

        await _projectService.RestoreArchivedSceneAsync(scene.Id, targetGuid!, null);

        Explorer?.Refresh();
        await RefreshStatusBarAsync();
        _ = RefreshGitStatusAsync();

        // Re-open the restored scene.
        var chapter = chapters.FirstOrDefault(c => string.Equals(c.Guid, targetGuid, StringComparison.OrdinalIgnoreCase));
        var restored = _projectService.GetScenesForChapter(targetGuid!).FirstOrDefault(s => s.Id == scene.Id);
        if (chapter != null && restored != null)
            OnSceneOpenRequested(chapter, restored);
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
        Calendar?.Refresh();
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

    [RelayCommand]
    private void CloseMapsTab()
    {
        IsMapsOpen = false;
        if (ActiveContentView == "Maps")
            ActiveContentView = GetFallbackView("Maps");
    }

    [RelayCommand]
    private void OpenPlotGrid()
    {
        IsPlotGridOpen = true;
        ActiveContentView = "PlotGrid";
        PlotGrid?.Refresh();
    }

    [RelayCommand]
    private void ClosePlotGridTab()
    {
        IsPlotGridOpen = false;
        if (ActiveContentView == "PlotGrid")
            ActiveContentView = GetFallbackView("PlotGrid");
    }

    [RelayCommand]
    private async Task OpenRelationshipsGraphAsync()
    {
        IsRelationshipsGraphOpen = true;
        ActiveContentView = "RelationshipsGraph";
        if (RelationshipsGraph != null) await RelationshipsGraph.ReloadAsync();
    }

    [RelayCommand]
    private void CloseRelationshipsGraphTab()
    {
        IsRelationshipsGraphOpen = false;
        if (ActiveContentView == "RelationshipsGraph")
            ActiveContentView = GetFallbackView("RelationshipsGraph");
    }

    [RelayCommand]
    private void OpenCalendar()
    {
        IsCalendarOpen = true;
        ActiveContentView = "Calendar";
        Calendar?.Refresh();
    }

    [RelayCommand]
    private void CloseCalendarTab()
    {
        IsCalendarOpen = false;
        if (ActiveContentView == "Calendar")
            ActiveContentView = GetFallbackView("Calendar");
    }

    [RelayCommand]
    private void OpenResearch()
    {
        IsResearchOpen = true;
        ActiveContentView = "Research";
        Research?.Refresh();
    }

    [RelayCommand]
    private void CloseResearchTab()
    {
        IsResearchOpen = false;
        if (ActiveContentView == "Research")
            ActiveContentView = GetFallbackView("Research");
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

        if (Dashboard != null)
            Dashboard.Author = _projectService.ProjectSettings.Author ?? string.Empty;

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
        // Scene tabs encoded as "Scene:{paneKey}:{sceneId}" — route to the
        // matching pane and activate the scene there.
        if (!string.IsNullOrEmpty(view) && view.StartsWith("Scene:", StringComparison.Ordinal))
        {
            var parts = view.Split(':', 3);
            if (parts.Length == 3)
            {
                var pane = parts[1] == "P" ? Editor : SecondaryEditor;
                if (pane != null)
                {
                    var tab = pane.OpenScenes.FirstOrDefault(t => t.Scene.Id == parts[2]);
                    if (tab != null)
                    {
                        _ = ActivateSceneTabAsync(pane, tab);
                        return;
                    }
                }
            }
        }

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
    private Task ToggleSplitEditorAsync() => ToggleSplitEditorAsync(mirrorCurrentScene: true);

    private async Task ToggleSplitEditorAsync(bool mirrorCurrentScene)
    {
        if (IsSplitEditorOpen)
        {
            if (SecondaryEditor is { } existing)
            {
                if (existing.IsDirty) await existing.SaveAsync();
            }
            if (Editor != null) SetActivePane(Editor);
            SecondaryEditor = null;
            IsSplitEditorOpen = false;
            return;
        }

        SecondaryEditor = new EditorViewModel(_projectService, _settingsService, _entityService);
        SecondaryEditor.SetGrammarCheckContributors(ExtensionManager?.GrammarCheckContributors ?? []);

        if (mirrorCurrentScene
            && Editor is { CurrentChapter: { } chap, CurrentScene: { } sc })
        {
            await SecondaryEditor.OpenSceneAsync(chap, sc);
        }

        IsSplitEditorOpen = true;
    }

    [RelayCommand]
    private async Task OpenInSplitAsync(SceneTreeItemViewModel? sceneVm)
    {
        if (sceneVm == null) return;
        if (SecondaryEditor == null)
        {
            await ToggleSplitEditorAsync();
            if (SecondaryEditor == null) return;
        }
        await SecondaryEditor.OpenSceneAsync(sceneVm.ParentChapter, sceneVm.Scene);
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
    private void AddComment()
    {
        var pane = (ActiveEditor == Editor || ActiveEditor == SecondaryEditor) ? ActiveEditor : Editor;
        if (pane?.CurrentScene == null || pane.AddCommentAction == null) return;

        var commentId = System.Guid.NewGuid().ToString();
        void OnAnchored(string id, string anchorText)
        {
            if (id != commentId) return;
            pane.CommentAnchored -= OnAnchored;

            if (string.IsNullOrEmpty(anchorText))
            {
                Toast.Show?.Invoke(Loc.T("comments.noSelection"), ToastSeverity.Warning);
                return;
            }

            var scene = pane.CurrentScene;
            if (scene == null) return;
            scene.Comments ??= new List<SceneComment>();
            scene.Comments.Add(new SceneComment
            {
                Id = commentId,
                AnchorText = anchorText,
                Text = string.Empty
            });
            _ = _projectService.SaveScenesAsync();

            IsSceneNotesVisible = true;
            SceneNotes?.SyncCommentsFromScene(scene, commentId);
        }
        pane.CommentAnchored += OnAnchored;
        pane.AddCommentAction.Invoke(commentId);
    }

    private void ApplyParagraphStyle(string styleId)
    {
        var pane = (ActiveEditor == Editor || ActiveEditor == SecondaryEditor) ? ActiveEditor : Editor;
        pane?.ApplyParagraphStyleAction?.Invoke(styleId);
    }

    [RelayCommand]
    private async Task AddFootnote()
    {
        var pane = (ActiveEditor == Editor || ActiveEditor == SecondaryEditor) ? ActiveEditor : Editor;
        if (pane?.CurrentScene == null || pane.AddFootnoteAction == null) return;

        if (ShowInputDialog == null) return;
        var text = await ShowInputDialog(Loc.T("footnotes.addTitle"), Loc.T("footnotes.addPrompt"), string.Empty);
        if (string.IsNullOrWhiteSpace(text)) return;

        var fnId = System.Guid.NewGuid().ToString();
        void OnInserted(string id, int number)
        {
            if (id != fnId) return;
            pane.FootnoteInserted -= OnInserted;

            var scene = pane.CurrentScene;
            if (scene == null) return;
            scene.Footnotes ??= new List<SceneFootnote>();
            scene.Footnotes.Add(new SceneFootnote { Id = fnId, Number = number, Text = text });
            _ = _projectService.SaveScenesAsync();
            pane.SyncCommentsAction?.Invoke();
        }
        pane.FootnoteInserted += OnInserted;
        pane.AddFootnoteAction.Invoke(fnId);
    }

    [RelayCommand]
    private async Task OpenFindReplaceAsync()
    {
        if (ShowFindReplaceDialog != null)
            await ShowFindReplaceDialog.Invoke();
    }

    [RelayCommand]
    private async Task OpenCommandPaletteAsync()
    {
        if (ShowCommandPalette != null)
            await ShowCommandPalette.Invoke();
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

    public string ActiveDraftName
        => _projectService.ActiveBook?.ActiveDraft?.Name ?? string.Empty;

    public ObservableCollection<BookDraftMetadata> ActiveBookDrafts { get; } = new();

    public void RefreshDraftList()
    {
        ActiveBookDrafts.Clear();
        if (_projectService.ActiveBook == null) return;
        foreach (var d in _projectService.ActiveBook.Drafts)
            ActiveBookDrafts.Add(d);
        OnPropertyChanged(nameof(ActiveDraftName));
    }

    [RelayCommand]
    private async Task SwitchDraftAsync(BookDraftMetadata? draft)
    {
        if (draft == null || _projectService.ActiveBook == null) return;
        await _projectService.SwitchDraftAsync(draft.Id);
        Explorer?.Refresh();
        await RefreshStatusBarAsync();
        RefreshDraftList();
        OnPropertyChanged(nameof(ActiveDraftName));
    }

    [RelayCommand]
    private async Task CreateDraftAsync()
    {
        if (_projectService.ActiveBook == null) return;
        if (ShowInputDialog == null) return;
        var name = await ShowInputDialog.Invoke(Loc.T("draft.newTitle"), Loc.T("draft.newPrompt"), Loc.T("draft.defaultName"));
        if (string.IsNullOrWhiteSpace(name)) return;
        // Clone from currently-active draft so user can experiment from current state.
        var fromId = _projectService.ActiveBook.ActiveDraftId;
        var created = await _projectService.CreateDraftAsync(name.Trim(), fromId);
        await _projectService.SwitchDraftAsync(created.Id);
        Explorer?.Refresh();
        await RefreshStatusBarAsync();
        RefreshDraftList();
        OnPropertyChanged(nameof(ActiveDraftName));
    }

    [RelayCommand]
    private async Task DeleteDraftAsync(BookDraftMetadata? draft)
    {
        if (draft == null || _projectService.ActiveBook == null) return;
        if (_projectService.ActiveBook.Drafts.Count <= 1) return;
        if (ShowConfirmDialog != null)
        {
            var ok = await ShowConfirmDialog.Invoke(
                Loc.T("draft.deleteTitle"),
                string.Format(Loc.T("draft.deleteMessage"), draft.Name));
            if (!ok) return;
        }
        await _projectService.DeleteDraftAsync(draft.Id);
        Explorer?.Refresh();
        await RefreshStatusBarAsync();
        RefreshDraftList();
        OnPropertyChanged(nameof(ActiveDraftName));
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
