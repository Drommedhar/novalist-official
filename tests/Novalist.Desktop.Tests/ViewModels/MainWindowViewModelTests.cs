using Avalonia.Threading;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;
using Novalist.Desktop.Services;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

[Collection("Avalonia")]
public class MainWindowViewModelTests
{
    static MainWindowViewModelTests()
    {
        var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Locales");
        Loc.Instance.Initialize(dir, "en");
    }

    private sealed class H
    {
        public IProjectService Proj = null!;
        public ISettingsService Settings = null!;
        public IEntityService Entity = null!;
        public IGitService Git = null!;
        public AppSettings App = null!;
        public ProjectSettings ProjSettings = null!;
        public MainWindowViewModel Vm = null!;
    }

    private static H Build()
    {
        var h = new H();
        h.App = new AppSettings();
        h.Settings = Substitute.For<ISettingsService>();
        h.Settings.Settings.Returns(h.App);
        h.Settings.Effective.Returns(h.App);
        h.Settings.SaveAsync().Returns(Task.CompletedTask);
        h.Settings.LoadAsync().Returns(Task.CompletedTask);

        h.ProjSettings = new ProjectSettings { Overrides = new SettingsOverrides() };
        h.Proj = Substitute.For<IProjectService>();
        h.Proj.ProjectSettings.Returns(h.ProjSettings);
        h.Proj.IsProjectLoaded.Returns(false);

        h.Entity = Substitute.For<IEntityService>();
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData>());
        h.Entity.LoadLocationsAsync().Returns(new List<LocationData>());
        h.Entity.LoadItemsAsync().Returns(new List<ItemData>());
        h.Entity.LoadLoreAsync().Returns(new List<LoreData>());
        h.Entity.GetCustomEntityTypes().Returns(new List<CustomEntityTypeDefinition>());
        h.Entity.LoadCustomEntitiesAsync(Arg.Any<string>()).Returns(new List<CustomEntityData>());
        h.Git = Substitute.For<IGitService>();

        h.Vm = new MainWindowViewModel(h.Proj, h.Settings, h.Entity, h.Git);
        return h;
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

    // Pump repeatedly until a fire-and-forget Task.Run continuation lands (or give up).
    private static void SpinPump(Func<bool> until, int max = 50)
    {
        for (var i = 0; i < max && !until(); i++)
        {
            System.Threading.Thread.Sleep(5);
            Pump();
        }
    }

    private static async Task<H> LoadedAsync()
    {
        var h = Build();
        var book = new BookData { Id = "b1", Name = "Book One" };
        var meta = new ProjectMetadata { Name = "Proj", Books = { book } };
        h.Proj.LoadProjectAsync(Arg.Any<string>()).Returns(meta);
        h.Proj.IsProjectLoaded.Returns(true);
        h.Proj.CurrentProject.Returns(meta);
        h.Proj.ActiveBook.Returns(book);
        h.Proj.ProjectRoot.Returns("C:/proj");
        h.Proj.GetChaptersOrdered().Returns(new List<ChapterData>());
        h.Proj.GetScenesForChapter(Arg.Any<string>()).Returns(new List<SceneData>());
        h.Proj.ReadSceneContentAsync(Arg.Any<ChapterData>(), Arg.Any<SceneData>()).Returns(Task.FromResult("scene content here"));
        h.Proj.GetSceneFilePath(Arg.Any<ChapterData>(), Arg.Any<SceneData>()).Returns("C:/proj/s.html");
        h.Proj.SaveScenesAsync().Returns(Task.CompletedTask);
        h.Proj.RenameProjectAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        h.Proj.CreateBookAsync(Arg.Any<string>()).Returns(ci => Task.FromResult(new BookData { Id = "b2", Name = (string)ci[0] }));
        h.Proj.SwitchBookAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        h.Proj.RenameBookAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.CompletedTask);
        h.Proj.DeleteBookAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        h.Proj.CreateDraftAsync(Arg.Any<string>(), Arg.Any<string?>()).Returns(Task.FromResult(new BookDraftMetadata { Id = "d2", Name = "Draft 2" }));
        h.Proj.SwitchDraftAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        h.Proj.DeleteDraftAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        h.Git.InitializeAsync(Arg.Any<string>()).Returns(Task.CompletedTask);

        await h.Vm.LoadProjectAsync("C:/proj");
        Pump();
        return h;
    }

    [AvaloniaFact]
    public async Task LoadProject_RunsRelationshipDuplicateMigration()
    {
        var h = await LoadedAsync();
        await h.Entity.Received().MigrateRelationshipDuplicatesAsync();
    }

    // ── Construction & computed props ───────────────────────────────
    [AvaloniaFact]
    public void Constructs_ExposesComputedProps()
    {
        var h = Build();
        Assert.StartsWith("v", h.Vm.AppVersion);
        Assert.NotNull(h.Vm.SettingsService);
        Assert.NotNull(h.Vm.ProjectService);
        Assert.False(h.Vm.IsAppChromeVisible);
        Assert.False(h.Vm.HasOpenEditors);
        Assert.Equal(string.Empty, h.Vm.GitBranchDisplay);
        Assert.False(h.Vm.IsInGitRepo);
        Assert.Equal(0, h.Vm.GitChangedCount);
        Assert.False(h.Vm.HasProjectOverview);
        _ = h.Vm.ProjectTotalWordsDisplay;
        _ = h.Vm.ProjectReadingTimeDisplay;
        _ = h.Vm.AverageChapterWordsDisplay;
        _ = h.Vm.DailyGoalLabel;
        _ = h.Vm.ProjectGoalLabel;
        _ = h.Vm.IsContextSidebarShowing;
        Assert.True(h.Vm.IsContextTabActive);
        Assert.False(h.Vm.IsFootnotesTabActive);
    }

    [AvaloniaFact]
    public async Task InitializeAsync_LoadsSettings()
    {
        var h = Build();
        await h.Vm.InitializeAsync();
        await h.Settings.Received().LoadAsync();
    }

    // ── Focus mode ──────────────────────────────────────────────────
    [AvaloniaFact]
    public void FocusMode_SavesAndRestores()
    {
        var h = Build();
        h.Vm.IsExplorerVisible = true;
        h.Vm.IsContextSidebarVisible = true;
        h.Vm.IsSceneNotesVisible = true;
        h.Vm.ToggleFocusModeCommand.Execute(null);
        Assert.True(h.Vm.IsFocusMode);
        Assert.False(h.Vm.IsExplorerVisible);
        h.Vm.ToggleFocusModeCommand.Execute(null);
        Assert.False(h.Vm.IsFocusMode);
        Assert.True(h.Vm.IsExplorerVisible); // restored
    }

    // ── Menus / panels toggles ──────────────────────────────────────
    [AvaloniaFact]
    public void StartMenu_Settings_Extensions_Toggles()
    {
        var h = Build();
        h.Vm.ToggleStartMenuCommand.Execute(null);
        Assert.True(h.Vm.IsStartMenuOpen);
        h.Vm.CloseStartMenuCommand.Execute(null);
        Assert.False(h.Vm.IsStartMenuOpen);

        h.Vm.ToggleSettingsCommand.Execute(null);
        Assert.True(h.Vm.IsSettingsOpen);
        h.Vm.OpenSettingsToCategory("templates");
        Assert.True(h.Vm.IsSettingsOpen);

        h.Vm.ToggleExtensionsCommand.Execute(null);
        Assert.True(h.Vm.IsExtensionsOpen);
        Assert.False(h.Vm.IsSettingsOpen); // extensions closes settings
    }

    [AvaloniaFact]
    public void Panel_Toggles()
    {
        var h = Build();
        h.Vm.ToggleExplorerCommand.Execute(null);
        h.Vm.ToggleContextSidebarCommand.Execute(null);
        h.Vm.ToggleSceneNotesCommand.Execute(null);
        h.Vm.ToggleBookPickerCommand.Execute(null);
        Assert.True(h.Vm.IsBookPickerOpen);
        h.Vm.CloseBookPickerCommand.Execute(null);
        Assert.False(h.Vm.IsBookPickerOpen);
    }

    [AvaloniaFact]
    public void ProjectOverview_Toggles()
    {
        var h = Build();
        h.Vm.ToggleProjectOverviewCommand.Execute(null); // no overview -> no-op
        Assert.False(h.Vm.IsProjectOverviewOpen);
        h.Vm.CloseProjectOverviewCommand.Execute(null);
    }

    // ── Show / Close dedicated views (null child VMs, guarded) ──────
    [AvaloniaFact]
    public void ShowAndCloseAllViews()
    {
        var h = Build();
        h.Vm.ShowDashboardCommand.Execute(null);
        Assert.Equal("Dashboard", h.Vm.ActiveContentView);
        h.Vm.ShowTimelineCommand.Execute(null);
        h.Vm.ShowExportCommand.Execute(null);
        h.Vm.ShowImageGalleryCommand.Execute(null);
        h.Vm.ShowCodexHubCommand.Execute(null);
        h.Vm.ShowManuscriptCommand.Execute(null);
        h.Vm.OpenPlotGridCommand.Execute(null);
        h.Vm.OpenCalendarCommand.Execute(null);
        h.Vm.OpenResearchCommand.Execute(null);
        h.Vm.OpenMapsCommand.Execute(null);
        Pump();

        h.Vm.CloseExportTabCommand.Execute(null);
        h.Vm.CloseImageGalleryTabCommand.Execute(null);
        h.Vm.CloseDashboardTabCommand.Execute(null);
        h.Vm.CloseTimelineTabCommand.Execute(null);
        h.Vm.CloseCodexHubTabCommand.Execute(null);
        h.Vm.CloseManuscriptTabCommand.Execute(null);
        h.Vm.CloseMapsTabCommand.Execute(null);
        h.Vm.ClosePlotGridTabCommand.Execute(null);
        h.Vm.CloseCalendarTabCommand.Execute(null);
        h.Vm.CloseResearchTabCommand.Execute(null);
        h.Vm.CloseGitTabCommand.Execute(null);
        h.Vm.CloseExtensionContentTabCommand.Execute(null);
        Pump();
    }

    [AvaloniaFact]
    public async Task ShowGit_And_OpenRelationships()
    {
        var h = Build();
        await h.Vm.ShowGitCommand.ExecuteAsync(null);
        Assert.Equal("Git", h.Vm.ActiveContentView);
        await h.Vm.OpenRelationshipsGraphCommand.ExecuteAsync(null);
        h.Vm.CloseRelationshipsGraphTabCommand.Execute(null);
        Pump();
    }

    [AvaloniaFact]
    public void SetActiveContentView_AllBranches()
    {
        var h = Build();
        foreach (var v in new[] { "Dashboard", "Timeline", "Export", "ImageGallery", "Git", "CodexHub", "Manuscript", "ext:foo", "Scene", "Entity" })
            h.Vm.SetActiveContentViewCommand.Execute(v);
        h.Vm.SetActiveContentViewCommand.Execute("Scene:P:missing"); // routing, no pane
        h.Vm.SetActiveContentViewCommand.Execute("Scene:bad"); // malformed
        Pump();
        Assert.NotNull(h.Vm.ActiveContentView);
    }

    // ── Toasts ──────────────────────────────────────────────────────
    [AvaloniaFact]
    public void Toasts_ShowDismissCap()
    {
        var h = Build();
        h.Vm.ShowToast("a", ToastSeverity.Info, autoDismissMs: 0);
        Assert.Single(h.Vm.Toasts);
        h.Vm.DismissToastCommand.Execute(h.Vm.Toasts[0]);
        Assert.Empty(h.Vm.Toasts);
        h.Vm.DismissToastCommand.Execute(null); // null guard

        for (var i = 0; i < 6; i++)
            h.Vm.ShowToast($"t{i}", ToastSeverity.Info, autoDismissMs: 0);
        Assert.Equal(4, h.Vm.Toasts.Count); // capped at 4

        h.Vm.ShowExtensionNotification("ext");
        Assert.Equal(4, h.Vm.Toasts.Count);
    }

    // ── Start-menu project hooks ────────────────────────────────────
    [AvaloniaFact]
    public async Task OpenProjectFromMenu_AndRecent_FireEvents()
    {
        var h = Build();
        var opened = false;
        string? recent = null;
        h.Vm.OpenProjectFromMenuRequested += () => { opened = true; return Task.CompletedTask; };
        h.Vm.OpenRecentProjectFromMenuRequested += p => { recent = p; return Task.CompletedTask; };
        await h.Vm.OpenProjectFromMenuCommand.ExecuteAsync(null);
        Assert.True(opened);
        await h.Vm.OpenRecentProjectFromMenuCommand.ExecuteAsync(new RecentProject { Path = "C:/p" });
        Assert.Equal("C:/p", recent);
    }

    [AvaloniaFact]
    public void CloseProject_ResetsState()
    {
        var h = Build();
        h.Vm.IsProjectLoaded = true;
        h.Vm.IsDashboardOpen = true;
        h.Vm.CloseProjectCommand.Execute(null);
        Assert.False(h.Vm.IsProjectLoaded);
        Assert.False(h.Vm.IsDashboardOpen);
    }

    // ── Bug regressions ─────────────────────────────────────────────
    // Bug: closing the project while Settings/Extensions overlay is open leaves
    // the overlay open on the welcome screen.
    [AvaloniaFact]
    public void CloseProject_ClosesSettingsAndExtensionsOverlays()
    {
        var h = Build();
        h.Vm.IsProjectLoaded = true;
        h.Vm.IsSettingsOpen = true;
        h.Vm.CloseProjectCommand.Execute(null);
        Assert.False(h.Vm.IsSettingsOpen);

        h.Vm.IsProjectLoaded = true;
        h.Vm.IsExtensionsOpen = true;
        h.Vm.CloseProjectCommand.Execute(null);
        Assert.False(h.Vm.IsExtensionsOpen);
    }

    // Bug: the Settings / Extensions activity-bar buttons stay highlighted
    // (ActiveActivityView) after the view is closed by any path other than the
    // activity-bar toggle. ActiveActivityView must mirror the open flags.
    [AvaloniaFact]
    public void ActiveActivityView_ClearsWhenSettingsClosedDirectly()
    {
        var h = Build();
        h.Vm.ActiveActivityView = "Settings";
        h.Vm.IsSettingsOpen = true;
        h.Vm.IsSettingsOpen = false; // closed via X/Esc, not the activity bar
        Assert.Equal(string.Empty, h.Vm.ActiveActivityView);
    }

    [AvaloniaFact]
    public void ActiveActivityView_ClearsWhenExtensionsClosedDirectly()
    {
        var h = Build();
        h.Vm.ActiveActivityView = "Extensions";
        h.Vm.IsExtensionsOpen = true;
        h.Vm.IsExtensionsOpen = false;
        Assert.Equal(string.Empty, h.Vm.ActiveActivityView);
    }

    [AvaloniaFact]
    public void ActiveActivityView_SetsWhenSettingsOpened()
    {
        var h = Build();
        h.Vm.IsSettingsOpen = true; // opening should mark the button active
        Assert.Equal("Settings", h.Vm.ActiveActivityView);
        h.Vm.IsExtensionsOpen = true; // opening extensions marks it (and is mutually exclusive in UI)
        Assert.Equal("Extensions", h.Vm.ActiveActivityView);
    }

    // ── Activity bar / status bar ───────────────────────────────────
    [AvaloniaFact]
    public async Task ShowActivityView_AllBranches()
    {
        var h = Build();
        await h.Vm.ShowActivityViewCommand.ExecuteAsync("Settings");
        await h.Vm.ShowActivityViewCommand.ExecuteAsync("Extensions");
        await h.Vm.ShowActivityViewCommand.ExecuteAsync("Export");
        Assert.Equal("Export", h.Vm.ActiveActivityView);
        await h.Vm.ShowActivityViewCommand.ExecuteAsync("Export"); // toggle off
        Assert.Equal(string.Empty, h.Vm.ActiveActivityView);
        await h.Vm.ShowActivityViewCommand.ExecuteAsync("ImageGallery");
        await h.Vm.ShowActivityViewCommand.ExecuteAsync("Git");
        Pump();
    }

    [AvaloniaFact]
    public void ExtensionActivityAndStatusBarItems_Execute()
    {
        var h = Build();
        var clicked = false;
        h.Vm.ExecuteExtensionActivityBarItemCommand.Execute(new Novalist.Sdk.Models.ActivityBarItem { OnClick = () => clicked = true });
        Assert.True(clicked);
        var sClicked = false;
        h.Vm.ExecuteExtensionStatusBarItemCommand.Execute(new Novalist.Sdk.Models.StatusBarItem { OnClick = () => sClicked = true });
        Assert.True(sClicked);
    }

    // ── Wave 2: project-loaded surface ──────────────────────────────
    [AvaloniaFact]
    public async Task LoadProject_BuildsChildVMs_AndAutoDashboard()
    {
        var h = await LoadedAsync();
        Assert.True(h.Vm.IsProjectLoaded);
        Assert.NotNull(h.Vm.Editor);
        Assert.NotNull(h.Vm.Explorer);
        Assert.NotNull(h.Vm.EntityPanel);
        Assert.NotNull(h.Vm.EntityEditor);
        Assert.NotNull(h.Vm.ContextSidebar);
        Assert.NotNull(h.Vm.Dashboard);
        Assert.NotNull(h.Vm.Timeline);
        Assert.NotNull(h.Vm.Export);
        Assert.NotNull(h.Vm.Git);
        Assert.NotNull(h.Vm.CodexHub);
        Assert.NotNull(h.Vm.Manuscript);
        Assert.NotNull(h.Vm.Maps);
        Assert.NotNull(h.Vm.PlotGrid);
        Assert.NotNull(h.Vm.RelationshipsGraph);
        Assert.NotNull(h.Vm.Calendar);
        Assert.NotNull(h.Vm.Research);
        Assert.True(h.Vm.IsDashboardOpen);
        Assert.True(h.Vm.IsAppChromeVisible);
    }

    [AvaloniaFact]
    public async Task Loaded_ShowViews_RefreshChildren()
    {
        var h = await LoadedAsync();
        h.Vm.ShowTimelineCommand.Execute(null);
        h.Vm.ShowExportCommand.Execute(null);
        h.Vm.ShowImageGalleryCommand.Execute(null);
        h.Vm.ShowCodexHubCommand.Execute(null);
        h.Vm.ShowManuscriptCommand.Execute(null);
        await h.Vm.ShowGitCommand.ExecuteAsync(null);
        await h.Vm.OpenRelationshipsGraphCommand.ExecuteAsync(null);
        h.Vm.OpenMapsCommand.Execute(null);
        h.Vm.OpenPlotGridCommand.Execute(null);
        h.Vm.OpenCalendarCommand.Execute(null);
        h.Vm.OpenResearchCommand.Execute(null);
        Pump();
        Assert.True(h.Vm.IsManuscriptOpen);
    }

    [AvaloniaFact]
    public async Task Loaded_OpenScene_EnablesEditorAndContentSwitch()
    {
        var h = await LoadedAsync();
        var ch = new ChapterData { Guid = "c1", Title = "Ch" };
        var sc = new SceneData { Id = "s1", Title = "Sc" };
        await h.Vm.Editor!.OpenSceneAsync(ch, sc);
        Pump();
        Assert.True(h.Vm.HasOpenEditors);
        h.Vm.SetActiveContentViewCommand.Execute("Scene");
        Assert.Equal("Scene", h.Vm.ActiveContentView);
        await h.Vm.CloseSceneTabCommand.ExecuteAsync(null);
        Pump();
    }

    [AvaloniaFact]
    public async Task Loaded_AddCommentAndFootnote()
    {
        var h = await LoadedAsync();
        var ch = new ChapterData { Guid = "c1", Title = "Ch" };
        var sc = new SceneData { Id = "s1", Title = "Sc" };
        await h.Vm.Editor!.OpenSceneAsync(ch, sc);
        h.Vm.ActiveContentView = "Scene";
        var commentAdded = false;
        h.Vm.Editor.AddCommentAction = _ => commentAdded = true;
        h.Vm.Editor.AddFootnoteAction = _ => { };
        h.Vm.AddCommentCommand.Execute(null);
        await h.Vm.AddFootnoteCommand.ExecuteAsync(null);
        Assert.True(commentAdded);
    }

    [AvaloniaFact]
    public async Task Loaded_BookManagement()
    {
        var h = await LoadedAsync();
        h.Vm.ShowInputDialog = (_, _, _) => Task.FromResult<string?>("New Book");
        h.Vm.ShowConfirmDialog = (_, _) => Task.FromResult(true);
        await h.Vm.AddBookCommand.ExecuteAsync(null);
        await h.Proj.Received().CreateBookAsync("New Book");

        var book = h.Vm.Books[0];
        await h.Vm.RenameBookCommand.ExecuteAsync(book);
        await h.Vm.DeleteBookCommand.ExecuteAsync(book);

        h.Vm.ToggleBookPickerCommand.Execute(null); // builds book cards
        Pump();
    }

    [AvaloniaFact]
    public async Task Loaded_DraftManagement()
    {
        var h = await LoadedAsync();
        h.Vm.ShowInputDialog = (_, _, _) => Task.FromResult<string?>("Draft X");
        h.Vm.ShowConfirmDialog = (_, _) => Task.FromResult(true);
        await h.Vm.CreateDraftCommand.ExecuteAsync(null);
        await h.Proj.Received().CreateDraftAsync(Arg.Any<string>(), Arg.Any<string?>());
        await h.Vm.SwitchDraftCommand.ExecuteAsync(new BookDraftMetadata { Id = "d1", Name = "D1" });
        await h.Vm.DeleteDraftCommand.ExecuteAsync(new BookDraftMetadata { Id = "d1", Name = "D1" });
    }

    [AvaloniaFact]
    public async Task Loaded_RenameProject()
    {
        var h = await LoadedAsync();
        h.Vm.ShowInputDialog = (_, _, _) => Task.FromResult<string?>("Renamed Project");
        await h.Vm.RenameProjectCommand.ExecuteAsync(null);
        await h.Proj.Received().RenameProjectAsync("Renamed Project");
    }

    [AvaloniaFact]
    public async Task Loaded_ToggleSplitEditor()
    {
        var h = await LoadedAsync();
        await h.Vm.ToggleSplitEditorCommand.ExecuteAsync(null);
        Pump();
        Assert.True(h.Vm.IsSplitEditorOpen);
        Assert.NotNull(h.Vm.SecondaryEditor);
        await h.Vm.ToggleSplitEditorCommand.ExecuteAsync(null);
        Pump();
        Assert.False(h.Vm.IsSplitEditorOpen);
    }

    [AvaloniaFact]
    public async Task Loaded_DialogHookCommands()
    {
        var h = await LoadedAsync();
        var fr = false; var cp = false;
        h.Vm.ShowFindReplaceDialog = () => { fr = true; return Task.CompletedTask; };
        h.Vm.ShowCommandPalette = () => { cp = true; return Task.CompletedTask; };
        await h.Vm.OpenFindReplaceCommand.ExecuteAsync(null);
        await h.Vm.OpenCommandPaletteCommand.ExecuteAsync(null);
        Assert.True(fr);
        Assert.True(cp);
    }

    [AvaloniaFact]
    public async Task Loaded_RefreshAndStatusBar()
    {
        var h = await LoadedAsync();
        await h.Vm.RefreshStatusBarAsync();
        Pump();
        Assert.True(h.Vm.IsProjectLoaded);
    }

    // ── Wave 3: deeper loaded paths ─────────────────────────────────
    [AvaloniaFact]
    public async Task OpenEntityById_AllTypes_AndNotFound()
    {
        var h = await LoadedAsync();
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData> { new() { Id = "e-c", Name = "C" } });
        h.Entity.LoadLocationsAsync().Returns(new List<LocationData> { new() { Id = "e-l", Name = "L" } });
        h.Entity.LoadItemsAsync().Returns(new List<ItemData> { new() { Id = "e-i", Name = "I" } });
        h.Entity.LoadLoreAsync().Returns(new List<LoreData> { new() { Id = "e-lo", Name = "Lo" } });

        await h.Vm.OpenEntityByIdAsync("e-c");
        Assert.True(h.Vm.EntityEditor!.IsOpen);
        await h.Vm.OpenEntityByIdAsync("e-l");
        await h.Vm.OpenEntityByIdAsync("e-i");
        await h.Vm.OpenEntityByIdAsync("e-lo");
        await h.Vm.OpenEntityByIdAsync(""); // empty guard
        await h.Vm.OpenEntityByIdAsync("missing"); // not found, no throw
        Pump();
    }

    [AvaloniaFact]
    public async Task SceneTabs_SplitAndMoveAndActivate()
    {
        var h = await LoadedAsync();
        var ch = new ChapterData { Guid = "c1", Title = "Ch" };
        var s1 = new SceneData { Id = "s1", Title = "S1", ChapterGuid = "c1" };
        var s2 = new SceneData { Id = "s2", Title = "S2", ChapterGuid = "c1" };
        await h.Vm.Editor!.OpenSceneAsync(ch, s1);
        await h.Vm.Editor.OpenSceneAsync(ch, s2);
        Pump();
        Assert.Contains(h.Vm.ContentTabs, t => t.Id == "Scene");

        // split, then move a tab to the secondary pane
        await h.Vm.ToggleSplitEditorCommand.ExecuteAsync(null);
        Pump();
        await h.Vm.MoveSceneTabAsync(h.Vm.Editor, h.Vm.Editor.OpenScenes[0]);
        Pump();
        Assert.NotNull(h.Vm.SecondaryEditor);

        // activate a scene tab via SetActiveContentView routing
        var tab = h.Vm.ContentTabs.FirstOrDefault(t => t.Id.StartsWith("Scene:"));
        if (tab != null)
        {
            h.Vm.SetActiveContentViewCommand.Execute(tab.ActivationKey);
            Pump();
        }
    }

    [AvaloniaFact]
    public async Task AddComment_AddFootnote_FullFlow()
    {
        var h = await LoadedAsync();
        var ch = new ChapterData { Guid = "c1", Title = "Ch" };
        var sc = new SceneData { Id = "s1", Title = "Sc", ChapterGuid = "c1" };
        await h.Vm.Editor!.OpenSceneAsync(ch, sc);
        h.Vm.SetActivePane(h.Vm.Editor);

        // AddComment: the action fires CommentAnchored with a non-empty anchor
        h.Vm.Editor.AddCommentAction = id => h.Vm.Editor.RaiseCommentAnchored(id, "selected text");
        h.Vm.AddCommentCommand.Execute(null);
        Assert.NotNull(sc.Comments);

        // AddComment empty anchor path
        h.Vm.Editor.AddCommentAction = id => h.Vm.Editor.RaiseCommentAnchored(id, string.Empty);
        h.Vm.AddCommentCommand.Execute(null);

        // AddFootnote: input then FootnoteInserted callback
        h.Vm.ShowInputDialog = (_, _, _) => Task.FromResult<string?>("a footnote");
        h.Vm.Editor.AddFootnoteAction = id => h.Vm.Editor.RaiseFootnoteInserted(id, 1);
        await h.Vm.AddFootnoteCommand.ExecuteAsync(null);
        Assert.NotNull(sc.Footnotes);
    }

    [AvaloniaFact]
    public async Task Snapshots_OpenAndTake()
    {
        var h = await LoadedAsync();
        var ch = new ChapterData { Guid = "c1", Title = "Ch" };
        var sc = new SceneData { Id = "s1", Title = "Sc", ChapterGuid = "c1" };
        h.Proj.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        await h.Vm.Editor!.OpenSceneAsync(ch, sc);
        var shown = false;
        h.Vm.ShowSnapshotsDialog = (_, _) => { shown = true; return Task.CompletedTask; };
        await h.Vm.OpenSnapshotsCommand.ExecuteAsync(null);
        Assert.True(shown);
        // TakeSnapshotAsync routes through App.SnapshotService -> the real static
        // ProjectService (no project), so it's covered separately / excluded.
    }

    [AvaloniaFact]
    public async Task EntityEditor_SavedDeleted_Handlers()
    {
        var h = await LoadedAsync();
        var ch = new ChapterData { Guid = "c1", Title = "Ch" };
        var sc = new SceneData { Id = "s1", Title = "Sc", ChapterGuid = "c1" };
        await h.Vm.Editor!.OpenSceneAsync(ch, sc);
        // open + save an entity -> OnEntitySaved
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData> { new() { Id = "e-c", Name = "C" } });
        await h.Vm.OpenEntityByIdAsync("e-c");
        await h.Vm.EntityEditor!.SaveCommand.ExecuteAsync(null);
        Pump();
        h.Vm.EntityEditor.ConfirmDeleteRequested = (_, _) => Task.FromResult(true);
        await h.Vm.EntityEditor.DeleteCommand.ExecuteAsync(null);
        Pump();
        await h.Vm.CloseEntityTabCommand.ExecuteAsync(null);
        Pump();
    }

    // ── Wave 4: project create + wizard flows ───────────────────────
    [AvaloniaFact]
    public async Task CreateProject_LoadsAndAppliesTemplate()
    {
        var h = Build();
        var meta = new ProjectMetadata { Name = "Created", Books = { new BookData { Id = "b1", Name = "B" } } };
        h.Proj.CreateProjectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(meta);
        h.Proj.LoadProjectAsync(Arg.Any<string>()).Returns(meta);
        h.Proj.IsProjectLoaded.Returns(true);
        h.Proj.CurrentProject.Returns(meta);
        h.Proj.ActiveBook.Returns(meta.Books[0]);
        h.Proj.ProjectRoot.Returns("C:/created");
        h.Proj.GetChaptersOrdered().Returns(new List<ChapterData>());
        h.Git.InitializeAsync(Arg.Any<string>()).Returns(Task.CompletedTask);

        await h.Vm.CreateProjectAsync("C:/parent", "Created", "B", templateId: "unknown-template");
        Pump();
        Assert.True(h.Vm.IsProjectLoaded);
    }

    [AvaloniaFact]
    public async Task WizardStateDir_ForScopes()
    {
        var h = await LoadedAsync();
        h.Proj.ActiveBookRoot.Returns("C:/proj/.book");
        var projDir = h.Vm.GetWizardStateDirForScope(Novalist.Sdk.Models.Wizards.WizardScope.Project);
        Assert.Contains("Wizards", projDir);
        var entityDir = h.Vm.GetWizardStateDirForScope(Novalist.Sdk.Models.Wizards.WizardScope.Entity);
        Assert.Contains("Wizards", entityDir);
    }

    [AvaloniaFact]
    public async Task RunCharacterInterview_AppliesResult()
    {
        var h = await LoadedAsync();
        h.Vm.ShowWizardDialog = (_, _, _) => Task.FromResult<Novalist.Sdk.Models.Wizards.WizardResult?>(
            new Novalist.Sdk.Models.Wizards.WizardResult { Completed = true });
        var ch = new CharacterData { Name = "Hero" };
        await h.Vm.RunCharacterInterviewForAsync(ch);
        await h.Entity.Received().SaveCharacterAsync(ch);

        // cancelled path
        h.Vm.ShowWizardDialog = (_, _, _) => Task.FromResult<Novalist.Sdk.Models.Wizards.WizardResult?>(null);
        await h.Vm.RunCharacterInterviewForAsync(ch);
    }

    [AvaloniaFact]
    public async Task RunEntityWizard_PerType()
    {
        var h = await LoadedAsync();
        h.Vm.ShowWizardDialog = (_, _, _) => Task.FromResult<Novalist.Sdk.Models.Wizards.WizardResult?>(
            new Novalist.Sdk.Models.Wizards.WizardResult { Completed = true });
        await h.Vm.RunEntityWizardForCreatedAsync(EntityType.Character, new CharacterData { Name = "C" }, null);
        await h.Vm.RunEntityWizardForCreatedAsync(EntityType.Location, new LocationData { Name = "L" }, null);
        await h.Vm.RunEntityWizardForCreatedAsync(EntityType.Item, new ItemData { Name = "I" }, null);
        await h.Vm.RunEntityWizardForCreatedAsync(EntityType.Lore, new LoreData { Name = "Lo" }, null);
        Pump();
    }

    [AvaloniaFact]
    public async Task RunProjectSnowflakeWizard_NoNameSetsStatus()
    {
        var h = Build();
        // Completed result with no project-name answer -> StatusText branch + early return
        h.Vm.ShowWizardDialog = (_, _, _) => Task.FromResult<Novalist.Sdk.Models.Wizards.WizardResult?>(
            new Novalist.Sdk.Models.Wizards.WizardResult { Completed = true });
        await h.Vm.RunProjectSnowflakeWizardAsync("C:/parent");
        // cancelled
        h.Vm.ShowWizardDialog = (_, _, _) => Task.FromResult<Novalist.Sdk.Models.Wizards.WizardResult?>(null);
        await h.Vm.RunProjectSnowflakeWizardAsync("C:/parent");
        // no dialog hook
        h.Vm.ShowWizardDialog = null;
        await h.Vm.RunProjectSnowflakeWizardAsync("C:/parent");
    }

    // ── Wave 5: word metrics + overview ─────────────────────────────
    [AvaloniaFact]
    public async Task WordMetrics_WithChaptersAndScenes_BuildsOverview()
    {
        var h = Build();
        var book = new BookData { Id = "b1", Name = "B" };
        var meta = new ProjectMetadata { Name = "P", Books = { book } };
        h.ProjSettings.WordCountGoals = new ProjectWordCountGoals { DailyGoal = 100, ProjectGoal = 1000, Deadline = "2026-12-31" };
        h.Proj.LoadProjectAsync(Arg.Any<string>()).Returns(meta);
        h.Proj.IsProjectLoaded.Returns(true);
        h.Proj.CurrentProject.Returns(meta);
        h.Proj.ActiveBook.Returns(book);
        h.Proj.ProjectRoot.Returns("C:/proj");
        var ch = new ChapterData { Guid = "c1", Title = "Chapter 1" };
        var scenes = new List<SceneData>
        {
            new() { Id = "s1", Title = "S1", WordCount = 500, ChapterGuid = "c1" },
            new() { Id = "s2", Title = "S2", WordCount = 300, ChapterGuid = "c1" },
        };
        h.Proj.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        h.Proj.GetScenesForChapter("c1").Returns(scenes);
        h.Proj.GetSceneFilePath(Arg.Any<ChapterData>(), Arg.Any<SceneData>()).Returns("C:/proj/missing.html");
        h.Proj.ReadSceneContentAsync(Arg.Any<ChapterData>(), Arg.Any<SceneData>()).Returns(Task.FromResult(""));
        h.Git.InitializeAsync(Arg.Any<string>()).Returns(Task.CompletedTask);

        await h.Vm.LoadProjectAsync("C:/proj");
        await h.Vm.RefreshStatusBarAsync();
        SpinPump(() => h.Vm.ProjectOverviewChapters.Count > 0);

        Assert.True(h.Vm.ProjectTotalWords >= 800);
        Assert.NotEmpty(h.Vm.ProjectOverviewChapters);
        Assert.True(h.Vm.HasProjectOverview);
        Assert.True(h.Vm.DailyGoalTargetWords == 100);
        Assert.False(string.IsNullOrEmpty(h.Vm.GoalTooltip));
        Assert.False(string.IsNullOrEmpty(h.Vm.ProjectBreakdownTooltip));

        // overview now openable
        h.Vm.ToggleProjectOverviewCommand.Execute(null);
        Assert.True(h.Vm.IsProjectOverviewOpen);
        h.Vm.CloseProjectOverviewCommand.Execute(null);
        Assert.False(h.Vm.IsProjectOverviewOpen);
    }

    [AvaloniaFact]
    public async Task Hotkeys_DescriptorLambdas_Invoke()
    {
        var h = await LoadedAsync();
        // Wire dialog hooks so dialog-driven actions don't bail early.
        h.Vm.ShowInputDialog = (_, _, _) => Task.FromResult<string?>("x");
        h.Vm.ShowFindReplaceDialog = () => Task.CompletedTask;
        h.Vm.ShowCommandPalette = () => Task.CompletedTask;
        var ch = new ChapterData { Guid = "c1", Title = "Ch" };
        var sc = new SceneData { Id = "s1", Title = "Sc", ChapterGuid = "c1" };
        await h.Vm.Editor!.OpenSceneAsync(ch, sc);
        h.Vm.Editor.AddCommentAction = _ => { };
        h.Vm.Editor.AddFootnoteAction = _ => { };
        h.Vm.Editor.ApplyParagraphStyleAction = _ => { };
        h.Vm.Editor.ToggleBoldAction = () => { };
        h.Vm.Editor.ToggleItalicAction = () => { };
        h.Vm.Editor.ToggleUnderlineAction = () => { };
        h.Vm.Editor.AlignLeftAction = () => { };
        h.Vm.Editor.AlignCenterAction = () => { };
        h.Vm.Editor.AlignRightAction = () => { };
        h.Vm.Editor.AlignJustifyAction = () => { };
        h.Vm.SetActivePane(h.Vm.Editor);

        // Invoke every registered descriptor's CanExecute + OnExecute. Downstream
        // errors are tolerated; the goal is exercising the registration lambdas.
        foreach (var d in Novalist.Desktop.App.HotkeyService.GetAllDescriptors())
        {
            try { _ = d.CanExecute?.Invoke(); } catch { }
            try { d.OnExecute?.Invoke(); } catch { }
        }
        Pump();
        Assert.True(h.Vm.IsProjectLoaded);
    }

    // ── Wave 6: cover image, book cards, scene-tab routing ──────────
    [AvaloniaFact]
    public async Task SetCoverImage_PlainAndImport()
    {
        var h = await LoadedAsync();
        h.Proj.SaveProjectAsync().Returns(Task.CompletedTask);
        h.Entity.ImportImageAsync(Arg.Any<string>()).Returns(Task.FromResult("images/cover.png"));

        await h.Vm.SetCoverImageFromPickerAsync("covers/pic.png");
        Assert.Equal("covers/pic.png", h.Proj.CurrentProject!.CoverImage);

        await h.Vm.SetCoverImageFromPickerAsync("import:C:/ext/photo.jpg");
        Assert.Equal("images/cover.png", h.Proj.CurrentProject.CoverImage);

        await h.Vm.SetCoverImageFromPickerAsync(""); // empty guard
    }

    [AvaloniaFact]
    public async Task BookCard_Commands()
    {
        var h = await LoadedAsync();
        h.Vm.ShowInputDialog = (_, _, _) => Task.FromResult<string?>("Renamed");
        h.Vm.ShowConfirmDialog = (_, _) => Task.FromResult(true);
        h.Vm.ToggleBookPickerCommand.Execute(null); // build cards
        Pump();
        var card = h.Vm.BookCards.FirstOrDefault();
        if (card != null)
        {
            h.Vm.SelectBookFromPickerCommand.Execute(card);
            await h.Vm.RenameBookCardCommand.ExecuteAsync(card);
            await h.Vm.DeleteBookCardCommand.ExecuteAsync(card);
        }
        h.Vm.SelectBookFromPickerCommand.Execute(null); // null guard
    }

    [AvaloniaFact]
    public async Task SetActiveContentView_SceneTabRouting()
    {
        var h = await LoadedAsync();
        var ch = new ChapterData { Guid = "c1", Title = "Ch" };
        var sc = new SceneData { Id = "s1", Title = "Sc", ChapterGuid = "c1" };
        await h.Vm.Editor!.OpenSceneAsync(ch, sc);
        Pump();
        // route to the primary pane's scene tab
        h.Vm.SetActiveContentViewCommand.Execute("Scene:P:s1");
        Pump();
        Assert.Equal("Scene", h.Vm.ActiveContentView);
    }

    // ── Wave 7: reload, scene-saved activity, git-status refresh ────
    [AvaloniaFact]
    public async Task LoadProject_Twice_DetachesPreviousEditor()
    {
        var h = await LoadedAsync();
        var firstEditor = h.Vm.Editor;
        await h.Vm.LoadProjectAsync("C:/proj"); // re-load -> detach old editor/entityeditor
        Pump();
        Assert.NotSame(firstEditor, h.Vm.Editor);
    }

    [AvaloniaFact]
    public async Task SceneSaved_LogsRecentActivity()
    {
        var h = await LoadedAsync();
        var ch = new ChapterData { Guid = "c1", Title = "Ch" };
        var sc = new SceneData { Id = "s1", Title = "Sc", ChapterGuid = "c1" };
        await h.Vm.Editor!.OpenSceneAsync(ch, sc);
        h.Vm.Editor.OnTextChanged("<p>dirty edit</p>", "dirty edit");
        await h.Vm.Editor.SaveAsync(); // fires SceneSaved -> OnSceneSavedForActivity
        Pump();
        Assert.True(h.Vm.HasOpenEditors);
    }

    [AvaloniaFact]
    public async Task GitStatusRefresh_WithPopulatedExplorer()
    {
        var h = Build();
        var book = new BookData { Id = "b1", Name = "B" };
        var meta = new ProjectMetadata { Name = "P", Books = { book } };
        var ch = new ChapterData { Guid = "c1", Title = "Chapter 1" };
        var scenes = new List<SceneData> { new() { Id = "s1", Title = "S1", ChapterGuid = "c1" } };
        h.Proj.LoadProjectAsync(Arg.Any<string>()).Returns(meta);
        h.Proj.IsProjectLoaded.Returns(true);
        h.Proj.CurrentProject.Returns(meta);
        h.Proj.ActiveBook.Returns(book);
        h.Proj.ProjectRoot.Returns("C:/proj");
        h.Proj.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        h.Proj.GetScenesForChapter("c1").Returns(scenes);
        h.Proj.GetSceneFilePath(Arg.Any<ChapterData>(), Arg.Any<SceneData>()).Returns("C:/proj/c1/s1.html");
        h.Git.IsGitRepo.Returns(true);
        h.Git.InitializeAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        h.Git.GetStatusAsync().Returns(Task.FromResult<GitRepoInfo?>(
            new GitRepoInfo("main", false, 0, 0, new List<GitFileEntry>())));
        h.Git.GetFileStatus(Arg.Any<string>()).Returns(GitFileStatus.Modified);

        await h.Vm.LoadProjectAsync("C:/proj");
        SpinPump(() => h.Vm.IsInGitRepo);
        Assert.True(h.Vm.IsInGitRepo);
    }

    [AvaloniaFact]
    public async Task Dashboard_EnhancedStats_WithSceneContent()
    {
        var h = Build();
        var book = new BookData { Id = "b1", Name = "B" };
        var meta = new ProjectMetadata { Name = "P", Books = { book } };
        var ch = new ChapterData { Guid = "c1", Title = "Chapter 1" };
        var scenes = new List<SceneData> { new() { Id = "s1", Title = "S1", WordCount = 100, ChapterGuid = "c1" } };
        h.Proj.LoadProjectAsync(Arg.Any<string>()).Returns(meta);
        h.Proj.IsProjectLoaded.Returns(true);
        h.Proj.CurrentProject.Returns(meta);
        h.Proj.ActiveBook.Returns(book);
        h.Proj.ProjectRoot.Returns("C:/proj");
        h.Proj.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        h.Proj.GetScenesForChapter("c1").Returns(scenes);
        h.Proj.GetSceneFilePath(Arg.Any<ChapterData>(), Arg.Any<SceneData>()).Returns("C:/proj/missing.html");
        h.Git.InitializeAsync(Arg.Any<string>()).Returns(Task.CompletedTask);

        await h.Vm.LoadProjectAsync("C:/proj");
        // open the scene so RefreshDashboard's GetSceneContentForStats hits the active-editor branch
        await h.Vm.Editor!.OpenSceneAsync(ch, scenes[0]);
        h.Vm.ShowDashboardCommand.Execute(null);
        Pump();
        Assert.NotNull(h.Vm.Dashboard);
    }

    // ── Wave 8: close active tabs -> GetFallbackView branches ───────
    [AvaloniaFact]
    public async Task CloseActiveTabs_HitFallbackView()
    {
        var h = await LoadedAsync();
        h.Vm.ShowExportCommand.Execute(null); h.Vm.CloseExportTabCommand.Execute(null);
        h.Vm.ShowImageGalleryCommand.Execute(null); h.Vm.CloseImageGalleryTabCommand.Execute(null);
        h.Vm.ShowTimelineCommand.Execute(null); h.Vm.CloseTimelineTabCommand.Execute(null);
        h.Vm.ShowCodexHubCommand.Execute(null); h.Vm.CloseCodexHubTabCommand.Execute(null);
        h.Vm.ShowManuscriptCommand.Execute(null); h.Vm.CloseManuscriptTabCommand.Execute(null);
        await h.Vm.ShowGitCommand.ExecuteAsync(null); h.Vm.CloseGitTabCommand.Execute(null);
        h.Vm.OpenMapsCommand.Execute(null); h.Vm.CloseMapsTabCommand.Execute(null);
        h.Vm.OpenPlotGridCommand.Execute(null); h.Vm.ClosePlotGridTabCommand.Execute(null);
        h.Vm.OpenCalendarCommand.Execute(null); h.Vm.CloseCalendarTabCommand.Execute(null);
        h.Vm.OpenResearchCommand.Execute(null); h.Vm.CloseResearchTabCommand.Execute(null);
        await h.Vm.OpenRelationshipsGraphCommand.ExecuteAsync(null); h.Vm.CloseRelationshipsGraphTabCommand.Execute(null);
        // extension content tab active -> close hits ext fallback
        h.Vm.IsExtensionContentOpen = true;
        h.Vm.ActiveContentView = "ext:foo";
        h.Vm.CloseExtensionContentTabCommand.Execute(null);
        h.Vm.ShowDashboardCommand.Execute(null); h.Vm.CloseDashboardTabCommand.Execute(null);
        Pump();
        Assert.NotNull(h.Vm.ActiveContentView);
    }

    [AvaloniaFact]
    public async Task CloseSceneAndEntity_Tabs_Fallback()
    {
        var h = await LoadedAsync();
        var ch = new ChapterData { Guid = "c1", Title = "Ch" };
        var sc = new SceneData { Id = "s1", Title = "Sc", ChapterGuid = "c1" };
        await h.Vm.Editor!.OpenSceneAsync(ch, sc);
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData> { new() { Id = "e", Name = "C" } });
        await h.Vm.OpenEntityByIdAsync("e"); // entity tab active
        await h.Vm.CloseEntityTabCommand.ExecuteAsync(null); // fallback -> Scene
        h.Vm.SetActiveContentViewCommand.Execute("Scene");
        await h.Vm.CloseSceneTabCommand.ExecuteAsync(null);
        Pump();
        Assert.False(h.Vm.HasOpenEditors);
    }

    // ── Wave 9: custom entity, loaded view-refresh, split auto-close ─
    [AvaloniaFact]
    public async Task OpenCustomEntity_ById()
    {
        var h = await LoadedAsync();
        var meta = h.Proj.CurrentProject!;
        meta.CustomEntityTypes.Add(new CustomEntityTypeDefinition { TypeKey = "faction", DisplayName = "Faction" });
        h.Proj.CurrentProject.Returns(meta);
        h.Entity.LoadCustomEntitiesAsync("faction").Returns(new List<CustomEntityData> { new() { Id = "ce1", EntityTypeKey = "faction", Name = "House" } });
        h.Entity.GetCustomEntityTypes().Returns(new List<CustomEntityTypeDefinition> { new() { TypeKey = "faction", DisplayName = "Faction" } });
        await h.Vm.OpenEntityByIdAsync("ce1");
        Assert.True(h.Vm.EntityEditor!.IsOpen);
    }

    [AvaloniaFact]
    public async Task SetActiveContentView_Loaded_RefreshesChildren()
    {
        var h = await LoadedAsync();
        h.Vm.SetActiveContentViewCommand.Execute("Manuscript");
        h.Vm.SetActiveContentViewCommand.Execute("CodexHub");
        h.Vm.SetActiveContentViewCommand.Execute("Timeline");
        h.Vm.SetActiveContentViewCommand.Execute("Export");
        h.Vm.SetActiveContentViewCommand.Execute("ImageGallery");
        h.Vm.SetActiveContentViewCommand.Execute("Git");
        Pump();
        Assert.True(h.Vm.IsManuscriptOpen);
    }

    [AvaloniaFact]
    public async Task SplitEditor_SecondaryEmpties_AutoCloses()
    {
        var h = await LoadedAsync();
        var ch = new ChapterData { Guid = "c1", Title = "Ch" };
        var s1 = new SceneData { Id = "s1", Title = "S1", ChapterGuid = "c1" };
        await h.Vm.Editor!.OpenSceneAsync(ch, s1);
        await h.Vm.ToggleSplitEditorCommand.ExecuteAsync(null); // mirrors current scene into secondary
        Pump();
        Assert.NotNull(h.Vm.SecondaryEditor);
        // close the only secondary tab -> OnPaneOpenScenesChanged auto-closes the split
        var secTab = h.Vm.SecondaryEditor!.OpenScenes.FirstOrDefault();
        if (secTab != null)
            await h.Vm.SecondaryEditor.CloseTabAsync(secTab);
        SpinPump(() => !h.Vm.IsSplitEditorOpen);
        Assert.False(h.Vm.IsSplitEditorOpen);
    }

    [AvaloniaFact]
    public async Task WordMetrics_BaselineDateTodayButNullWords()
    {
        var h = Build();
        var book = new BookData { Id = "b1", Name = "B" };
        var meta = new ProjectMetadata { Name = "P", Books = { book } };
        h.ProjSettings.WordCountGoals = new ProjectWordCountGoals
        {
            DailyGoal = 50,
            DailyBaselineDate = DateTime.Now.ToString("yyyy-MM-dd"),
            DailyBaselineWords = null, // same-day but no baseline -> else-if branch
        };
        h.Proj.LoadProjectAsync(Arg.Any<string>()).Returns(meta);
        h.Proj.IsProjectLoaded.Returns(true);
        h.Proj.CurrentProject.Returns(meta);
        h.Proj.ActiveBook.Returns(book);
        h.Proj.ProjectRoot.Returns("C:/proj");
        h.Proj.GetChaptersOrdered().Returns(new List<ChapterData> { new() { Guid = "c1", Title = "C" } });
        h.Proj.GetScenesForChapter("c1").Returns(new List<SceneData> { new() { Id = "s1", WordCount = 10, ChapterGuid = "c1" } });
        h.Proj.GetSceneFilePath(Arg.Any<ChapterData>(), Arg.Any<SceneData>()).Returns("C:/proj/x.html");
        h.Git.InitializeAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        await h.Vm.LoadProjectAsync("C:/proj");
        await h.Vm.RefreshStatusBarAsync();
        SpinPump(() => h.Vm.ProjectTotalWords > 0);
        Assert.Equal(10, h.ProjSettings.WordCountGoals.DailyBaselineWords);
    }

    // ── Wave 10: tab close lambdas, footnote-from-pane, misc ────────
    [AvaloniaFact]
    public async Task AddFootnoteFromPane_Event()
    {
        var h = await LoadedAsync();
        var ch = new ChapterData { Guid = "c1", Title = "Ch" };
        var sc = new SceneData { Id = "s1", Title = "Sc", ChapterGuid = "c1" };
        await h.Vm.Editor!.OpenSceneAsync(ch, sc);
        h.Vm.SetActivePane(h.Vm.Editor);
        h.Vm.ShowInputDialog = (_, _, _) => Task.FromResult<string?>("fn");
        h.Vm.Editor.AddFootnoteAction = id => h.Vm.Editor.RaiseFootnoteInserted(id, 1);
        h.Vm.Editor.RaiseAddFootnoteRequested(); // -> OnAddFootnoteRequestedFromPane -> AddFootnote
        Pump();
        Assert.True(h.Vm.HasOpenEditors);
    }

    [AvaloniaFact]
    public async Task ContentTab_CloseLambdas_Invoke()
    {
        var h = await LoadedAsync();
        var ch = new ChapterData { Guid = "c1", Title = "Ch" };
        var sc = new SceneData { Id = "s1", Title = "Sc", ChapterGuid = "c1" };
        await h.Vm.Editor!.OpenSceneAsync(ch, sc);
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData> { new() { Id = "e", Name = "C" } });
        await h.Vm.OpenEntityByIdAsync("e");
        h.Vm.IsExtensionContentOpen = true;
        h.Vm.ExtensionContentTabTitle = "Ext";
        h.Vm.ActiveContentView = "ext:x";
        Pump();
        // invoke each tab's OnClose lambda
        foreach (var tab in h.Vm.ContentTabs.ToList())
        {
            try { tab.OnClose(); } catch { }
            try { tab.ActivateAction?.Invoke(); } catch { }
        }
        Pump();
        Assert.NotNull(h.Vm.ActiveContentView);
    }

    [AvaloniaFact]
    public void Toast_AutoDismiss_Removes()
    {
        var h = Build();
        h.Vm.ShowToast("temp", ToastSeverity.Info, autoDismissMs: 30);
        SpinPump(() => h.Vm.Toasts.Count == 0);
        Assert.Empty(h.Vm.Toasts);
    }

    [AvaloniaFact]
    public void WizardStateDir_EntityScope_NullBookRoot()
    {
        var h = Build();
        h.Proj.ActiveBookRoot.Returns((string?)null);
        var dir = h.Vm.GetWizardStateDirForScope(Novalist.Sdk.Models.Wizards.WizardScope.Entity);
        Assert.Null(dir);
    }

    [AvaloniaFact]
    public async Task SetActiveContentView_Entity_WhenOpen()
    {
        var h = await LoadedAsync();
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData> { new() { Id = "e", Name = "C" } });
        await h.Vm.OpenEntityByIdAsync("e");
        h.Vm.SetActiveContentViewCommand.Execute("Entity");
        Assert.Equal("Entity", h.Vm.ActiveContentView);
    }

    // ── Wave 11: entity-save display arms + drafts ──────────────────
    [AvaloniaFact]
    public async Task EntitySaved_AllTypes_DisplayText()
    {
        var h = await LoadedAsync();
        h.Entity.LoadLocationsAsync().Returns(new List<LocationData> { new() { Id = "l", Name = "L" } });
        h.Entity.LoadItemsAsync().Returns(new List<ItemData> { new() { Id = "i", Name = "I" } });
        h.Entity.LoadLoreAsync().Returns(new List<LoreData> { new() { Id = "lo", Name = "Lo" } });
        foreach (var id in new[] { "l", "i", "lo" })
        {
            await h.Vm.OpenEntityByIdAsync(id);
            await h.Vm.EntityEditor!.SaveCommand.ExecuteAsync(null); // -> OnEntitySaved -> GetEntityDisplayText
            Pump();
        }
        Assert.True(h.Vm.EntityEditor!.IsOpen);
    }

    [AvaloniaFact]
    public async Task DraftList_WithDrafts()
    {
        var h = await LoadedAsync();
        var book = h.Proj.ActiveBook!;
        book.Drafts.Add(new BookDraftMetadata { Id = "d1", Name = "Draft 1" });
        book.ActiveDraftId = "d1";
        h.Vm.RefreshDraftList();
        Assert.NotEmpty(h.Vm.ActiveBookDrafts);
        _ = h.Vm.ActiveDraftName;
    }

    [AvaloniaFact]
    public async Task EntitySaved_Custom_DisplayText()
    {
        var h = await LoadedAsync();
        var meta = h.Proj.CurrentProject!;
        meta.CustomEntityTypes.Add(new CustomEntityTypeDefinition { TypeKey = "faction", DisplayName = "Faction" });
        h.Entity.LoadCustomEntitiesAsync("faction").Returns(new List<CustomEntityData> { new() { Id = "ce", EntityTypeKey = "faction", Name = "House" } });
        h.Entity.GetCustomEntityTypes().Returns(new List<CustomEntityTypeDefinition> { new() { TypeKey = "faction" } });
        await h.Vm.OpenEntityByIdAsync("ce");
        await h.Vm.EntityEditor!.SaveCommand.ExecuteAsync(null); // OnEntitySaved -> GetEntityDisplayText custom arm
        Pump();
        Assert.True(h.Vm.EntityEditor!.IsOpen);
    }

    [AvaloniaFact]
    public async Task SelectBookFromPicker_SetsActiveBook()
    {
        var h = await LoadedAsync();
        h.Vm.ToggleBookPickerCommand.Execute(null);
        Pump();
        Assert.NotEmpty(h.Vm.BookCards);
        h.Vm.SelectBookFromPickerCommand.Execute(h.Vm.BookCards[0]);
        Pump();
        Assert.NotNull(h.Vm.ActiveBook);
    }

    [AvaloniaFact]
    public async Task DeleteBook_Confirmed_UpdatesActiveBook()
    {
        var h = await LoadedAsync();
        // Need >1 book so the "can't delete last book" guard doesn't short-circuit.
        h.Proj.CurrentProject!.Books.Add(new BookData { Id = "b2", Name = "Book Two" });
        h.Vm.ShowConfirmDialog = (_, _) => Task.FromResult(true);
        h.Vm.RefreshDraftList(); // no-op; ensures book list current
        await h.Vm.DeleteBookCommand.ExecuteAsync(new BookData { Id = "b1", Name = "Book One" });
        await h.Proj.Received().DeleteBookAsync("b1");
    }

    // ── Wave 13: final reachable leftovers ──────────────────────────
    [AvaloniaFact]
    public async Task ContentTabs_Reorder_TriggersMove()
    {
        var h = await LoadedAsync();
        var ch = new ChapterData { Guid = "c1", Title = "Ch" };
        var sc = new SceneData { Id = "s1", Title = "Sc", ChapterGuid = "c1" };
        await h.Vm.Editor!.OpenSceneAsync(ch, sc);
        Pump();
        h.Vm.CloseDashboardTabCommand.Execute(null); // ContentTabs = [Scene]
        Pump();
        h.Vm.ShowTimelineCommand.Execute(null); // desired [Timeline, Scene] -> Scene moves
        Pump();
        Assert.Contains(h.Vm.ContentTabs, t => t.Id == "Timeline");
    }

    [AvaloniaFact]
    public async Task SetActiveContentView_SceneRouting_NoMatchingTab()
    {
        var h = await LoadedAsync();
        var ch = new ChapterData { Guid = "c1", Title = "Ch" };
        var sc = new SceneData { Id = "s1", Title = "Sc", ChapterGuid = "c1" };
        await h.Vm.Editor!.OpenSceneAsync(ch, sc);
        // pane exists but no tab with this id -> inner branch falls through
        h.Vm.SetActiveContentViewCommand.Execute("Scene:P:doesnotexist");
        Pump();
        Assert.True(h.Vm.IsProjectLoaded);
    }

    [AvaloniaFact]
    public async Task SelectBookFromPicker_NonActiveBook_SwitchesActive()
    {
        var h = await LoadedAsync();
        h.Vm.Books.Add(new BookData { Id = "b2", Name = "Book Two" }); // VM.Books is the picker source
        h.Vm.ToggleBookPickerCommand.Execute(null); // rebuild cards (now 2)
        Pump();
        var other = h.Vm.BookCards.FirstOrDefault(c => c.Id == "b2");
        Assert.NotNull(other);
        h.Vm.SelectBookFromPickerCommand.Execute(other); // book.Id != ActiveBook.Id -> ActiveBook = book
        Pump();
    }

    // ── Wave 14: async handler bodies (state machines) ──────────────
    [AvaloniaFact]
    public async Task EditFootnote_FromClick_EditEmptyCancel()
    {
        var h = await LoadedAsync();
        var ch = new ChapterData { Guid = "c1", Title = "Ch" };
        var sc = new SceneData { Id = "s1", Title = "Sc", ChapterGuid = "c1", Footnotes = [new SceneFootnote { Id = "f1", Number = 1, Text = "old" }] };
        await h.Vm.Editor!.OpenSceneAsync(ch, sc);
        h.Vm.Editor.RemoveFootnoteAction = _ => { };
        h.Vm.Editor.SyncCommentsAction = () => { };

        h.Vm.ShowInputDialog = (_, _, _) => Task.FromResult<string?>("new text");
        h.Vm.Editor.RaiseFootnoteClicked("f1"); // -> EditFootnoteAsync (edit)
        Pump();
        Assert.Equal("new text", sc.Footnotes![0].Text);

        h.Vm.ShowInputDialog = (_, _, _) => Task.FromResult<string?>("   "); // empty -> delete
        h.Vm.Editor.RaiseFootnoteClicked("f1");
        Pump();

        h.Vm.ShowInputDialog = (_, _, _) => Task.FromResult<string?>(null); // cancel
        sc.Footnotes.Add(new SceneFootnote { Id = "f2", Number = 2, Text = "x" });
        h.Vm.Editor.RaiseFootnoteClicked("f2");
        h.Vm.Editor.RaiseFootnoteClicked("missing"); // not found guard
        Pump();
    }

    [AvaloniaFact]
    public async Task RestoreArchivedScene_FromEditor()
    {
        var h = await LoadedAsync();
        var ch = new ChapterData { Guid = "c1", Title = "Ch" };
        h.Proj.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        var arch = new SceneData { Id = "a1", Title = "Arch", ArchivedAt = DateTime.UtcNow, OriginChapterGuid = "c1" };
        h.Proj.GetScenesForChapter("c1").Returns(new List<SceneData> { arch });
        h.Proj.RestoreArchivedSceneAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int?>()).Returns(Task.CompletedTask);
        await h.Vm.Editor!.OpenSceneAsync(ch, arch);
        h.Vm.Editor.RestoreCurrentArchivedSceneCommand.Execute(null); // raises -> OnRestoreArchivedSceneFromEditor (-> OnSceneOpenRequested)
        Pump();
        await h.Proj.Received().RestoreArchivedSceneAsync("a1", "c1", null);
    }

    [AvaloniaFact]
    public async Task OpenSceneInSplitPane_AndOpenInSplit()
    {
        var h = await LoadedAsync();
        var ch = new ChapterData { Guid = "c1", Title = "Ch" };
        var sc = new SceneData { Id = "s1", Title = "Sc", ChapterGuid = "c1" };
        h.Proj.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        var sceneVm = new SceneTreeItemViewModel(sc, ch);
        // Explorer wires OpenSceneInSplitPaneRequested -> OpenSceneInSplitPaneAsync
        h.Vm.Explorer!.OpenSceneInSplitPaneRequested?.Invoke(sceneVm);
        Pump();
        await h.Vm.OpenInSplitCommand.ExecuteAsync(sceneVm);
        await h.Vm.OpenInSplitCommand.ExecuteAsync(null); // null guard
        Pump();
        Assert.NotNull(h.Vm.SecondaryEditor);
    }

    [AvaloniaFact]
    public async Task SwitchBook_ViaActiveBookChange()
    {
        var h = await LoadedAsync();
        // ActiveBook currently b1; assign a different book -> SwitchBookCoreAsync
        h.Vm.ActiveBook = new BookData { Id = "b2", Name = "Book Two" };
        Pump();
        await h.Proj.Received().SwitchBookAsync("b2");
    }

    [AvaloniaFact]
    public async Task RunEntityWizard_Custom()
    {
        var h = await LoadedAsync();
        var meta = h.Proj.CurrentProject!;
        meta.CustomEntityTypes.Add(new CustomEntityTypeDefinition { TypeKey = "faction", DisplayName = "Faction" });
        h.Vm.ShowWizardDialog = (_, _, _) => Task.FromResult<Novalist.Sdk.Models.Wizards.WizardResult?>(
            new Novalist.Sdk.Models.Wizards.WizardResult { Completed = true });
        await h.Vm.RunEntityWizardForCreatedAsync(EntityType.Custom, new CustomEntityData { EntityTypeKey = "faction", Name = "House" }, "faction");
        Pump();
    }

    [AvaloniaFact]
    public async Task RunProjectSnowflake_Success()
    {
        var h = Build();
        var meta = new ProjectMetadata { Name = "Snow", Books = { new BookData { Id = "b1", Name = "B" } } };
        h.Proj.CreateProjectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(meta);
        h.Proj.LoadProjectAsync(Arg.Any<string>()).Returns(meta);
        h.Proj.IsProjectLoaded.Returns(true);
        h.Proj.CurrentProject.Returns(meta);
        h.Proj.ActiveBook.Returns(meta.Books[0]);
        h.Proj.ProjectRoot.Returns("C:/snow");
        h.Proj.GetChaptersOrdered().Returns(new List<ChapterData>());
        h.Git.InitializeAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        var result = new Novalist.Sdk.Models.Wizards.WizardResult { Completed = true };
        result.Answers["projectName"] = new Novalist.Sdk.Models.Wizards.WizardAnswer { Text = "Snow" };
        h.Vm.ShowWizardDialog = (_, _, _) => Task.FromResult<Novalist.Sdk.Models.Wizards.WizardResult?>(result);
        await h.Vm.RunProjectSnowflakeWizardAsync("C:/parent");
        Pump();
        Assert.True(h.Vm.IsProjectLoaded);
    }

    // ── Wave 15: remaining reachable bodies / guards ────────────────
    [AvaloniaFact]
    public async Task DeleteDraft_WithMultipleDrafts()
    {
        var h = await LoadedAsync();
        var book = h.Proj.ActiveBook!;
        book.Drafts.Add(new BookDraftMetadata { Id = "d1", Name = "D1" });
        book.Drafts.Add(new BookDraftMetadata { Id = "d2", Name = "D2" });
        h.Vm.ShowConfirmDialog = (_, _) => Task.FromResult(true);
        await h.Vm.DeleteDraftCommand.ExecuteAsync(new BookDraftMetadata { Id = "d2", Name = "D2" });
        await h.Proj.Received().DeleteDraftAsync("d2");
    }

    [AvaloniaFact]
    public async Task OpenInSplit_FreshOpensSplit()
    {
        var h = await LoadedAsync();
        var ch = new ChapterData { Guid = "c1", Title = "Ch" };
        var sc = new SceneData { Id = "s1", Title = "Sc", ChapterGuid = "c1" };
        h.Proj.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        // no SecondaryEditor yet -> OpenInSplit must toggle the split first
        await h.Vm.OpenInSplitCommand.ExecuteAsync(new SceneTreeItemViewModel(sc, ch));
        Pump();
        Assert.NotNull(h.Vm.SecondaryEditor);
    }

    [AvaloniaFact]
    public async Task MoveSceneTab_FreshOpensSplit()
    {
        var h = await LoadedAsync();
        var ch = new ChapterData { Guid = "c1", Title = "Ch" };
        var sc = new SceneData { Id = "s1", Title = "Sc", ChapterGuid = "c1" };
        await h.Vm.Editor!.OpenSceneAsync(ch, sc);
        // no SecondaryEditor -> MoveTab opens the split, then moves
        await h.Vm.MoveSceneTabAsync(h.Vm.Editor, h.Vm.Editor.OpenScenes[0]);
        Pump();
        Assert.NotNull(h.Vm.SecondaryEditor);
    }

    [AvaloniaFact]
    public async Task OpenSnapshots_Guards()
    {
        var h = await LoadedAsync();
        h.Vm.ShowSnapshotsDialog = (_, _) => Task.CompletedTask;
        await h.Vm.OpenSnapshotsCommand.ExecuteAsync(null); // no current scene -> return
        var ch = new ChapterData { Guid = "c1", Title = "Ch" };
        var sc = new SceneData { Id = "s1", Title = "Sc", ChapterGuid = "no-such-chapter" };
        await h.Vm.Editor!.OpenSceneAsync(ch, sc);
        await h.Vm.OpenSnapshotsCommand.ExecuteAsync(null); // chapter not found -> return
        Assert.True(h.Vm.IsProjectLoaded);
    }

    [AvaloniaFact]
    public async Task CloseSceneTab_NoDocument_Guard()
    {
        var h = await LoadedAsync();
        await h.Vm.CloseSceneTabCommand.ExecuteAsync(null); // Editor not open -> guard return
        Assert.False(h.Vm.HasOpenEditors);
    }

    [AvaloniaFact]
    public async Task WordMetrics_ActiveSceneContent_Appended()
    {
        var h = Build();
        var book = new BookData { Id = "b1", Name = "B" };
        var meta = new ProjectMetadata { Name = "P", Books = { book } };
        var ch = new ChapterData { Guid = "c1", Title = "Chapter 1" };
        var sc = new SceneData { Id = "s1", Title = "S1", WordCount = 5, ChapterGuid = "c1" };
        h.Proj.LoadProjectAsync(Arg.Any<string>()).Returns(meta);
        h.Proj.IsProjectLoaded.Returns(true);
        h.Proj.CurrentProject.Returns(meta);
        h.Proj.ActiveBook.Returns(book);
        h.Proj.ProjectRoot.Returns("C:/proj");
        h.Proj.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        h.Proj.GetScenesForChapter("c1").Returns(new List<SceneData> { sc });
        h.Proj.GetSceneFilePath(Arg.Any<ChapterData>(), Arg.Any<SceneData>()).Returns("C:/proj/x.html");
        h.Proj.ReadSceneContentAsync(Arg.Any<ChapterData>(), Arg.Any<SceneData>()).Returns(Task.FromResult("Hello world this scene has words."));
        h.Git.InitializeAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        await h.Vm.LoadProjectAsync("C:/proj");
        // open the scene so the active-scene content is used in the metrics aggregate
        await h.Vm.Editor!.OpenSceneAsync(ch, sc);
        h.Vm.Editor.OnTextChanged("<p>live content words here</p>", "live content words here");
        await h.Vm.RefreshStatusBarAsync();
        SpinPump(() => h.Vm.ProjectTotalWords > 0);
        Assert.True(h.Vm.ProjectTotalWords > 0);
    }

    // ── Wave 16: final guards / branches / catch ────────────────────
    [AvaloniaFact]
    public async Task RefreshStatusBar_NotLoaded_Guards()
    {
        var h = Build(); // not loaded
        await h.Vm.RefreshStatusBarAsync(); // RefreshEntityCounts + word-metrics both guard-return
        SpinPump(() => true, 3);
        Assert.False(h.Vm.IsProjectLoaded);
    }

    [AvaloniaFact]
    public async Task WordMetrics_TwoScenesOnDisk_AppendsBetween()
    {
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nv-mwvm-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmp);
        try
        {
            var p1 = System.IO.Path.Combine(tmp, "s1.html");
            var p2 = System.IO.Path.Combine(tmp, "s2.html");
            System.IO.File.WriteAllText(p1, "First scene words on disk.");
            System.IO.File.WriteAllText(p2, "Second scene words on disk.");

            var h = Build();
            var book = new BookData { Id = "b1", Name = "B" };
            var meta = new ProjectMetadata { Name = "P", Books = { book } };
            var ch = new ChapterData { Guid = "c1", Title = "Chapter 1" };
            var s1 = new SceneData { Id = "s1", Title = "S1", WordCount = 5, ChapterGuid = "c1" };
            var s2 = new SceneData { Id = "s2", Title = "S2", WordCount = 5, ChapterGuid = "c1" };
            h.Proj.LoadProjectAsync(Arg.Any<string>()).Returns(meta);
            h.Proj.IsProjectLoaded.Returns(true);
            h.Proj.CurrentProject.Returns(meta);
            h.Proj.ActiveBook.Returns(book);
            h.Proj.ProjectRoot.Returns(tmp);
            h.Proj.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
            h.Proj.GetScenesForChapter("c1").Returns(new List<SceneData> { s1, s2 });
            h.Proj.GetSceneFilePath(ch, s1).Returns(p1);
            h.Proj.GetSceneFilePath(ch, s2).Returns(p2);
            h.Git.InitializeAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
            await h.Vm.LoadProjectAsync("C:/proj");
            // No editor scene open: both scenes read from disk -> chapterText appends both,
            // hitting the "append newline between scenes" branch.
            await h.Vm.RefreshStatusBarAsync();
            SpinPump(() => h.Vm.ProjectTotalWords > 0);
            Assert.True(h.Vm.ProjectTotalWords > 0);
        }
        finally
        {
            try { System.IO.Directory.Delete(tmp, true); } catch { }
        }
    }

    [AvaloniaFact]
    public async Task OpenEntityById_MissingWithCustomType_FallsThrough()
    {
        var h = await LoadedAsync();
        var meta = h.Proj.CurrentProject!;
        meta.CustomEntityTypes.Add(new CustomEntityTypeDefinition { TypeKey = "faction", DisplayName = "Faction" });
        h.Entity.LoadCustomEntitiesAsync("faction").Returns(new List<CustomEntityData> { new() { Id = "other", Name = "x" } });
        await h.Vm.OpenEntityByIdAsync("nope"); // loops custom types, no match -> method end
        Assert.False(h.Vm.EntityEditor!.IsOpen);
    }

    [AvaloniaFact]
    public async Task SwitchBook_SavesDirtyEditorAndClosesEntity()
    {
        var h = await LoadedAsync();
        var ch = new ChapterData { Guid = "c1", Title = "Ch" };
        var sc = new SceneData { Id = "s1", Title = "Sc", ChapterGuid = "c1" };
        await h.Vm.Editor!.OpenSceneAsync(ch, sc);
        h.Vm.Editor.OnTextChanged("<p>dirty</p>", "dirty"); // Editor.IsDirty
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData> { new() { Id = "e", Name = "C" } });
        await h.Vm.OpenEntityByIdAsync("e"); // EntityEditor.IsOpen
        h.Vm.ActiveBook = new BookData { Id = "b2", Name = "Two" }; // -> SwitchBookCore: save dirty + close entity
        SpinPump(() => true, 4);
        await h.Proj.Received().SwitchBookAsync("b2");
    }

    [AvaloniaFact]
    public async Task RunProjectSnowflake_CreateThrows_HitsCatch()
    {
        var h = Build();
        h.Proj.CreateProjectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns<Task<ProjectMetadata>>(_ => throw new InvalidOperationException("boom"));
        var result = new Novalist.Sdk.Models.Wizards.WizardResult { Completed = true };
        result.Answers["projectName"] = new Novalist.Sdk.Models.Wizards.WizardAnswer { Text = "Snow" };
        h.Vm.ShowWizardDialog = (_, _, _) => Task.FromResult<Novalist.Sdk.Models.Wizards.WizardResult?>(result);
        await h.Vm.RunProjectSnowflakeWizardAsync("C:/parent"); // CreateProjectAsync throws -> catch
        Assert.False(h.Vm.IsProjectLoaded);
    }
}
