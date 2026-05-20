using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Novalist.Desktop.Services;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;
using Novalist.Sdk.Services;
using Xunit;

namespace Novalist.Desktop.Tests.Services;

[Collection("Avalonia")]
public class HostServicesTests
{
    private static (HostServices Host, IFileService File, IProjectService Proj, IEntityService Ent, AppSettings App) Build()
    {
        var file = Substitute.For<IFileService>();
        var proj = Substitute.For<IProjectService>();
        var ent = Substitute.For<IEntityService>();
        var settings = Substitute.For<ISettingsService>();
        var app = new AppSettings();
        settings.Settings.Returns(app);
        settings.SaveAsync().Returns(Task.CompletedTask);
        return (new HostServices(file, proj, ent, settings), file, proj, ent, app);
    }

    // ── basics ──

    [AvaloniaFact]
    public void Facades_ReturnSelf_AndHostVersion()
    {
        var (h, _, _, _, _) = Build();
        Assert.Same(h, h.FileService);
        Assert.Same(h, h.ProjectService);
        Assert.Same(h, h.EntityService);
        Assert.False(string.IsNullOrEmpty(h.HostVersion));
    }

    [AvaloniaFact]
    public void GetExtensionDataPath_NoProject_Throws()
    {
        var (h, _, proj, _, _) = Build();
        proj.ProjectRoot.Returns((string?)null);
        Assert.Throws<InvalidOperationException>(() => h.GetExtensionDataPath("x"));
    }

    [AvaloniaFact]
    public void GetExtensionDataPath_WithProject_CreatesDir()
    {
        using var dir = new TempDir();
        var (h, _, proj, _, _) = Build();
        proj.ProjectRoot.Returns(dir.Path);
        var path = h.GetExtensionDataPath("ext1");
        Assert.True(Directory.Exists(path));
    }

    [AvaloniaFact]
    public void Localization_RegistersAndCaches()
    {
        using var dir = new TempDir();
        var (h, _, _, _, _) = Build();
        h.RegisterExtensionLocales("ext1", dir.Path);
        var loc1 = h.GetLocalization("ext1");
        var loc2 = h.GetLocalization("ext1");
        Assert.Same(loc1, loc2);
        // Unknown id -> a fresh no-op service (cached thereafter).
        var unknown = h.GetLocalization("other");
        Assert.NotNull(unknown);
        Assert.Same(unknown, h.GetLocalization("other"));
    }

    [AvaloniaFact]
    public void HostData_ReadWrite()
    {
        var (h, _, _, _, app) = Build();
        Assert.Null(h.ReadHostData("k"));
        h.WriteHostDataAsync("k", "{\"a\":1}").GetAwaiter().GetResult();
        Assert.Equal("{\"a\":1}", h.ReadHostData("k"));
        Assert.Equal("{\"a\":1}", app.ExtensionData["k"]);
    }

    // ── events ──

    [AvaloniaFact]
    public void Notifications_ContentView_Sidebar_Editor_Events()
    {
        var (h, _, _, _, _) = Build();

        string? notified = null;
        h.NotificationRequested += m => notified = m;
        h.ShowNotification("hi");
        Assert.Equal("hi", notified);

        string? activated = null;
        h.ContentViewActivated += (k, _) => activated = k;
        h.ActivateContentView("ext.view");
        Assert.Equal("ext.view", activated);

        string? toggled = null;
        h.RightSidebarToggled += p => toggled = p;
        h.ToggleRightSidebar("panel1");
        Assert.Equal("panel1", toggled);

        IEditorExtension? reg = null, unreg = null;
        var ed = Substitute.For<IEditorExtension>();
        h.EditorExtensionRegistered += e => reg = e;
        h.EditorExtensionUnregistered += e => unreg = e;
        h.RegisterEditorExtension(ed);
        h.UnregisterEditorExtension(ed);
        Assert.Same(ed, reg);
        Assert.Same(ed, unreg);
    }

    [AvaloniaFact]
    public void InlineActionContributors_RegisterDedupUnregister()
    {
        var (h, _, _, _, _) = Build();
        var changes = 0;
        h.InlineActionContributorsChanged += () => changes++;
        var c = Substitute.For<IInlineActionContributor>();
        h.RegisterInlineActionContributor(c);
        h.RegisterInlineActionContributor(c); // dedup -> still raises but not added twice
        Assert.Single(h.GetInlineActionContributors());
        h.UnregisterInlineActionContributor(c);
        Assert.Empty(h.GetInlineActionContributors());
        h.UnregisterInlineActionContributor(c); // not present -> no raise
    }

    [AvaloniaFact]
    public async Task RunWizard_NullLauncher_ReturnsNull_ThenInvokes()
    {
        var (h, _, _, _, _) = Build();
        Assert.Null(await h.RunWizardAsync(new Sdk.Models.Wizards.WizardDefinition()));
        h.WizardLauncher = (_, _) => Task.FromResult<Sdk.Models.Wizards.WizardResult?>(new Sdk.Models.Wizards.WizardResult { DefinitionId = "x" });
        var res = await h.RunWizardAsync(new Sdk.Models.Wizards.WizardDefinition());
        Assert.Equal("x", res!.DefinitionId);
    }

    [AvaloniaFact]
    public void ShowBusyProgress_NoFactory_ReturnsNoop()
    {
        var (h, _, _, _, _) = Build();
        var p = h.ShowBusyProgress(new BusyProgressOptions());
        // NoopBusyProgress: methods are safe no-ops; Dispose sets IsClosed.
        p.SetStatus("x"); p.SetProgress(0.5); p.SetTitle("t"); p.SetIndeterminate(true); p.SetDetails(null);
        Assert.False(p.IsClosed);
        Assert.Equal(System.Threading.CancellationToken.None, p.CancellationToken);
        p.Dispose();
        Assert.True(p.IsClosed);
    }

    [AvaloniaFact]
    public void ShowBusyProgress_WithFactory_OnUIThread_Invokes()
    {
        var (h, _, _, _, _) = Build();
        var fake = Substitute.For<IBusyProgress>();
        h.BusyProgressFactory = _ => fake;
        Assert.Same(fake, h.ShowBusyProgress(new BusyProgressOptions())); // test runs on UI thread
    }

    [AvaloniaFact]
    public void RaiseEvents_FireWithMappedInfo()
    {
        var (h, _, _, _, _) = Build();
        Sdk.Services.ProjectInfo? p = null; h.ProjectLoaded += i => p = i;
        h.RaiseProjectLoaded("Proj", "/root");
        Assert.Equal("Proj", p!.Name);

        Sdk.Services.SceneInfo? opened = null; h.SceneOpened += i => opened = i;
        h.RaiseSceneOpened("s1", "T", "c1", "Ch", 10);
        Assert.Equal("s1", opened!.Id);
        Assert.Equal("s1", ((IExtensionProjectService)h).CurrentScene!.Id); // cached

        Sdk.Services.SceneInfo? saved = null; h.SceneSaved += i => saved = i;
        h.RaiseSceneSaved("s1", "T", "c1", "Ch", 10);
        Assert.NotNull(saved);

        Sdk.Services.BookInfo? book = null; h.BookChanged += i => book = i;
        h.RaiseBookChanged("b1", "Book");
        Assert.Equal("b1", book!.Id);

        string? lang = null; h.LanguageChanged += l => lang = l;
        h.RaiseLanguageChanged("de");
        Assert.Equal("de", lang);
    }

    // ── IExtensionFileService delegation ──

    [AvaloniaFact]
    public async Task FileService_Delegates()
    {
        var (h, file, _, _, _) = Build();
        var efs = (IExtensionFileService)h;
        file.ReadTextAsync("p").Returns("content");
        Assert.Equal("content", await efs.ReadTextAsync("p"));
        await efs.WriteTextAsync("p", "c");
        await file.Received().WriteTextAsync("p", "c");
        file.ExistsAsync("p").Returns(true);
        Assert.True(await efs.ExistsAsync("p"));
        file.CombinePath("a", "b").Returns("a/b");
        Assert.Equal("a/b", efs.CombinePath("a", "b"));
        file.GetFileName("a/b.txt").Returns("b.txt");
        Assert.Equal("b.txt", efs.GetFileName("a/b.txt"));
    }

    // ── IExtensionProjectService ──

    [AvaloniaFact]
    public async Task ProjectService_ReadSceneContent_Branches()
    {
        var (h, _, proj, _, _) = Build();
        var eps = (IExtensionProjectService)h;
        proj.ScenesManifest.Returns((ScenesManifest?)null);
        Assert.Equal("", await eps.ReadSceneContentAsync("c", "s")); // no manifest

        var manifest = new ScenesManifest();
        var scene = new SceneData { Id = "s1" };
        manifest.Chapters["c1"] = new() { scene };
        proj.ScenesManifest.Returns(manifest);
        var ch = new ChapterData { Guid = "c1", Title = "Ch" };
        proj.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        proj.ReadSceneContentAsync(ch, scene).Returns("body");

        Assert.Equal("", await eps.ReadSceneContentAsync("missing", "s1")); // chapter not in manifest
        Assert.Equal("", await eps.ReadSceneContentAsync("c1", "missing")); // scene missing
        Assert.Equal("body", await eps.ReadSceneContentAsync("c1", "s1"));  // found
    }

    [AvaloniaFact]
    public async Task ProjectService_SynopsisAndChaptersAndScenes()
    {
        var (h, _, proj, _, _) = Build();
        var eps = (IExtensionProjectService)h;
        var manifest = new ScenesManifest();
        var scene = new SceneData { Id = "s1", Title = "S", WordCount = 5, Synopsis = "syn" };
        manifest.Chapters["c1"] = new() { scene };
        proj.ScenesManifest.Returns(manifest);
        proj.GetChaptersOrdered().Returns(new List<ChapterData> { new() { Guid = "c1", Title = "Ch", Order = 1 } });

        Assert.Equal("syn", await eps.GetSceneSynopsisAsync("c1", "s1"));
        Assert.Equal("", await eps.GetSceneSynopsisAsync("nope", "s1"));
        await eps.SetSceneSynopsisAsync("c1", "s1", " new ");
        Assert.Equal("new", scene.Synopsis);
        await eps.SetSceneSynopsisAsync("nope", "s1", "x"); // no-op

        Assert.Single(eps.GetChaptersOrdered());
        Assert.Single(eps.GetScenesForChapter("c1"));
        Assert.Empty(eps.GetScenesForChapter("missing"));
    }

    [AvaloniaFact]
    public void ProjectService_Props_Delegate()
    {
        var (h, _, proj, _, _) = Build();
        var eps = (IExtensionProjectService)h;
        proj.ProjectRoot.Returns("/r");
        proj.IsProjectLoaded.Returns(true);
        Assert.Equal("/r", eps.ProjectRoot);
        Assert.True(eps.IsProjectLoaded);
    }

    // ── IExtensionEntityService ──

    [AvaloniaFact]
    public async Task EntityService_LoadsAndMaps()
    {
        var (h, _, _, ent, _) = Build();
        var ees = (IExtensionEntityService)h;
        ent.LoadCharactersAsync().Returns(new List<CharacterData> { new() { Id = "c", Name = "Alice", Role = "Hero" } });
        ent.LoadLocationsAsync().Returns(new List<LocationData> { new() { Id = "l", Name = "City", Type = "Urban" } });
        ent.LoadItemsAsync().Returns(new List<ItemData> { new() { Id = "i", Name = "Sword", Type = "Weapon" } });
        ent.LoadLoreAsync().Returns(new List<LoreData> { new() { Id = "o", Name = "Magic", Category = "Sys" } });
        ent.LoadCustomEntitiesAsync("faction").Returns(new List<CustomEntityData> { new() { Id = "f", Name = "Order", EntityTypeKey = "faction" } });
        ent.GetCustomEntityTypes().Returns(new List<CustomEntityTypeDefinition> { new() { TypeKey = "faction", DisplayName = "Faction" } });

        Assert.Equal("Alice", (await ees.LoadCharactersAsync())[0].DisplayName);
        Assert.Equal("City", (await ees.LoadLocationsAsync())[0].Name);
        Assert.Equal("Sword", (await ees.LoadItemsAsync())[0].Name);
        Assert.Equal("Magic", (await ees.LoadLoreAsync())[0].Name);
        Assert.Equal("Order", (await ees.LoadCustomEntitiesAsync("faction"))[0].Name);
        Assert.Equal("faction", ees.GetCustomEntityTypes()[0].TypeKey);
    }

    [AvaloniaFact]
    public async Task EntityService_SaveCustom_RefreshImages()
    {
        var (h, _, proj, ent, _) = Build();
        var ees = (IExtensionEntityService)h;
        await ees.SaveCustomEntityAsync(new Sdk.Services.CustomEntityInfo
        {
            Id = "f", Name = "Order", EntityTypeKey = "faction",
            Fields = new Dictionary<string, string> { ["motto"] = "x" },
            Sections = new List<Sdk.Services.CustomEntitySectionInfo> { new() { Title = "T", Content = "C" } }
        });
        await ent.Received().SaveCustomEntityAsync(Arg.Is<CustomEntityData>(d => d.Id == "f" && d.Sections.Count == 1));

        var refreshed = false;
        h.EntityRefreshRequested += () => refreshed = true;
        ees.RequestEntityRefresh();
        Assert.True(refreshed);

        ent.GetProjectImages().Returns(new List<string> { "a.png" });
        Assert.Single(ees.GetProjectImages());
        ent.GetImageFullPath("a.png").Returns("/abs/a.png");
        Assert.Equal("/abs/a.png", ees.GetImageFullPath("a.png"));
    }

    [AvaloniaFact]
    public async Task GetCharacterImagePath_ResolvesOverridesAndBase()
    {
        var (h, _, proj, ent, _) = Build();
        var ees = (IExtensionEntityService)h;
        var c = new CharacterData
        {
            Id = "c1", Name = "Alice",
            Images = new() { new EntityImage { Name = "base", Path = "base.png" } },
            ChapterOverrides = new()
            {
                new CharacterOverride { Chapter = "Ch1", Scene = "Scene1", Images = new() { new EntityImage { Path = "scene.png" } } }
            }
        };
        ent.LoadCharactersAsync().Returns(new List<CharacterData> { c });
        var ch = new ChapterData { Guid = "g1", Title = "Ch1" };
        proj.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        proj.GetScenesForChapter("g1").Returns(new List<SceneData> { new() { Id = "s1", Title = "Scene1" } });
        ent.GetImageFullPath(Arg.Any<string>()).Returns(ci => "/abs/" + ci.Arg<string>());

        // unknown character -> null
        Assert.Null(await ees.GetCharacterImagePathAsync("nope", null, null));
        // scene override matched
        Assert.Equal("/abs/scene.png", await ees.GetCharacterImagePathAsync("c1", "g1", "s1"));
        // no chapter context -> base image
        Assert.Equal("/abs/base.png", await ees.GetCharacterImagePathAsync("c1", null, null));
    }

    [AvaloniaFact]
    public void GetExtensionSettingsPath_CreatesDir()
    {
        var (h, _, _, _, _) = Build();
        var path = h.GetExtensionSettingsPath("ext.test." + Guid.NewGuid().ToString("N"));
        Assert.True(Directory.Exists(path));
        try { Directory.Delete(path); } catch { }
    }

    [AvaloniaFact]
    public void PostToUI_RunsAction()
    {
        var (h, _, _, _, _) = Build();
        var ran = false;
        h.PostToUI(() => ran = true);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(ran);
    }

    [AvaloniaFact]
    public void NoopBusyProgress_CancelledEvent_AddRemove()
    {
        var (h, _, _, _, _) = Build();
        var p = h.ShowBusyProgress(new BusyProgressOptions());
        Action handler = () => { };
        p.Cancelled += handler;   // no-op add
        p.Cancelled -= handler;   // no-op remove
    }

    [AvaloniaFact]
    public void Hotkey_Register_Unregister_DelegateToApp()
    {
        var (h, _, _, _, _) = Build();
        h.RegisterHotkey(new HotkeyDescriptor { ActionId = "ext.test.hk", DefaultGesture = "Ctrl+J" });
        h.UnregisterHotkey("ext.test.hk"); // no throw
    }

    [AvaloniaFact]
    public void GetAiHooks_NoManager_Empty_AndLanguageDisplay()
    {
        var (h, _, _, _, _) = Build();
        Assert.Empty(h.GetAiHooks());
        Assert.False(string.IsNullOrEmpty(h.CurrentLanguageDisplayName));
        Assert.False(string.IsNullOrEmpty(h.CurrentLanguage));
    }

    [AvaloniaFact]
    public void RaiseLanguageChanged_ReloadsRegisteredLocales()
    {
        using var dir = new TempDir();
        var (h, _, _, _, _) = Build();
        h.RegisterExtensionLocales("ext1", dir.Path); // populates _locServices
        h.RaiseLanguageChanged("de");                  // reload loop runs
    }

    [AvaloniaFact]
    public async Task FileService_RemainingDelegates()
    {
        var (h, file, _, _, _) = Build();
        var efs = (IExtensionFileService)h;
        file.DirectoryExistsAsync("d").Returns(true);
        Assert.True(await efs.DirectoryExistsAsync("d"));
        await efs.CreateDirectoryAsync("d");
        await file.Received().CreateDirectoryAsync("d");
        file.GetFilesAsync("d", "*", false).Returns(new List<string> { "f" });
        Assert.Single(await efs.GetFilesAsync("d", "*", false));
        file.GetDirectoriesAsync("d").Returns(new List<string> { "s" });
        Assert.Single(await efs.GetDirectoriesAsync("d"));
        file.GetFileNameWithoutExtension("a/b.txt").Returns("b");
        Assert.Equal("b", efs.GetFileNameWithoutExtension("a/b.txt"));
        file.GetDirectoryName("a/b.txt").Returns("a");
        Assert.Equal("a", efs.GetDirectoryName("a/b.txt"));
    }

    [AvaloniaFact]
    public void ProjectService_RootProps()
    {
        var (h, _, proj, _, _) = Build();
        var eps = (IExtensionProjectService)h;
        proj.ActiveBookRoot.Returns("/book");
        proj.WorldBibleRoot.Returns("/wb");
        Assert.Equal("/book", eps.ActiveBookRoot);
        Assert.Equal("/wb", eps.WorldBibleRoot);
    }

    [AvaloniaFact]
    public async Task ProjectService_SceneAndImage_NullBranches()
    {
        var (h, _, proj, _, _) = Build();
        var eps = (IExtensionProjectService)h;
        // manifest present, scene present, but chapter missing from GetChaptersOrdered -> empty
        var manifest = new ScenesManifest();
        manifest.Chapters["c1"] = new() { new SceneData { Id = "s1" } };
        proj.ScenesManifest.Returns(manifest);
        proj.GetChaptersOrdered().Returns(new List<ChapterData>()); // chapter not found
        Assert.Equal("", await eps.ReadSceneContentAsync("c1", "s1"));

        proj.ScenesManifest.Returns((ScenesManifest?)null);
        Assert.Empty(eps.GetScenesForChapter("c1")); // null manifest
    }

    [AvaloniaFact]
    public async Task GetCharacterImagePath_NoImages_ReturnsNull()
    {
        var (h, _, proj, ent, _) = Build();
        var ees = (IExtensionEntityService)h;
        ent.LoadCharactersAsync().Returns(new List<CharacterData> { new() { Id = "c1", Name = "A" } }); // no images
        Assert.Null(await ees.GetCharacterImagePathAsync("c1", null, null));
    }

    [AvaloniaFact]
    public async Task GetCharacterDetailed_OverridePrecedence()
    {
        var (h, _, proj, ent, _) = Build();
        var ees = (IExtensionEntityService)h;
        var c = new CharacterData
        {
            Id = "c1", Name = "Alice", Role = "BaseRole", Age = "30",
            ChapterOverrides = new()
            {
                new CharacterOverride { Chapter = "Ch1", Scene = "Scene1", Role = "SceneRole" },
                new CharacterOverride { Chapter = "Ch1", Role = "ChapterRole" }
            }
        };
        ent.LoadCharactersAsync().Returns(new List<CharacterData> { c });
        var ch = new ChapterData { Guid = "g1", Title = "Ch1", Act = "I" };
        proj.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        proj.GetScenesForChapter("g1").Returns(new List<SceneData> { new() { Id = "s1", Title = "Scene1" } });

        Assert.Null(await ees.GetCharacterDetailedAsync("nope", null, null));

        var sceneScoped = await ees.GetCharacterDetailedAsync("c1", "g1", "s1");
        Assert.Equal("SceneRole", sceneScoped!.Role); // scene override wins

        var chapterScoped = await ees.GetCharacterDetailedAsync("c1", "g1", null);
        Assert.Equal("ChapterRole", chapterScoped!.Role); // chapter override

        var baseInfo = await ees.GetCharacterDetailedAsync("c1", null, null);
        Assert.Equal("BaseRole", baseInfo!.Role); // base value
        Assert.Equal("30", baseInfo.Age);
    }
}
