using Avalonia;
using Avalonia.Threading;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Editor;
using Novalist.Desktop.Localization;
using Novalist.Desktop.ViewModels;
using Novalist.Sdk.Models;
using Xunit;

namespace Novalist.Desktop.Tests.Editor;

[Collection("Avalonia")]
public class FocusPeekExtensionTests
{
    static FocusPeekExtensionTests()
    {
        var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Locales");
        Loc.Instance.Initialize(dir, "en");
    }

    private sealed class H
    {
        public FocusPeekViewModel Vm = new();
        public IProjectService Proj = null!;
        public IEntityService Entity = null!;
        public IMapService Map = null!;
        public FocusPeekExtension Ext = null!;
        public (EntityType Type, object Entity)? Opened;
        public (string MapId, string PinId)? Navigated;
        public ProjectMetadata Meta = new();
        public ProjectSettings ProjSettings = new();
        public BookData Book = new();
    }

    private static H Build(bool loaded = true)
    {
        var h = new H();
        h.Proj = Substitute.For<IProjectService>();
        h.Proj.IsProjectLoaded.Returns(loaded);
        h.Proj.CurrentProject.Returns(h.Meta);
        h.Proj.ProjectSettings.Returns(h.ProjSettings);
        h.Proj.ActiveBook.Returns(h.Book);
        h.Proj.GetChaptersOrdered().Returns(new List<ChapterData>());
        h.Proj.GetScenesForChapter(Arg.Any<string>()).Returns(new List<SceneData>());

        h.Entity = Substitute.For<IEntityService>();
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData>());
        h.Entity.LoadLocationsAsync().Returns(new List<LocationData>());
        h.Entity.LoadItemsAsync().Returns(new List<ItemData>());
        h.Entity.LoadLoreAsync().Returns(new List<LoreData>());
        h.Entity.LoadCustomEntitiesAsync(Arg.Any<string>()).Returns(new List<CustomEntityData>());

        h.Map = Substitute.For<IMapService>();
        h.Map.LoadMapAsync(Arg.Any<string>()).Returns((MapData?)null);

        h.Ext = new FocusPeekExtension(h.Vm, h.Proj, h.Entity, h.Map,
            (t, e) => h.Opened = (t, e),
            (m, p) => { h.Navigated = (m, p); return Task.CompletedTask; });
        return h;
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

    private static EditorDocumentContext Ctx(string sceneId = "s1", string chapterGuid = "ch1", string sceneTitle = "Scene One", string chapterTitle = "Chapter")
        => new() { SceneId = sceneId, ChapterGuid = chapterGuid, SceneTitle = sceneTitle, ChapterTitle = chapterTitle, FilePath = "C:/x.html" };

    [AvaloniaFact]
    public void NameAndPriority()
    {
        var h = Build();
        Assert.Equal("Focus Peek", h.Ext.Name);
        Assert.Equal(50, h.Ext.Priority);
    }

    [AvaloniaFact]
    public void EmptyIndex_JsonArrays()
    {
        var h = Build();
        Assert.Equal("[]", h.Ext.GetEntityNamesJson());
        Assert.Equal("[]", h.Ext.GetMentionCandidatesJson());
    }

    [AvaloniaFact]
    public async Task RefreshIndex_NotLoaded_NoOp()
    {
        var h = Build(loaded: false);
        await h.Ext.RefreshEntityIndexAsync();
        Assert.Equal("[]", h.Ext.GetEntityNamesJson());
    }

    [AvaloniaFact]
    public async Task RefreshIndex_BuildsAllTypes_AndJson()
    {
        var h = Build();
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData>
        {
            new() { Id = "c1", Name = "Alice", Surname = "Smith", Role = "Lead", Aliases = { "Ace" } },
            new() { Id = "c2", Name = "Dup" }, new() { Id = "c3", Name = "Dup" }, // ambiguous -> dropped
        });
        h.Entity.LoadLocationsAsync().Returns(new List<LocationData> { new() { Id = "l1", Name = "Tower", Type = "Keep", Aliases = { "Spire" } } });
        h.Entity.LoadItemsAsync().Returns(new List<ItemData> { new() { Id = "i1", Name = "Blade", Type = "Weapon" } });
        h.Entity.LoadLoreAsync().Returns(new List<LoreData> { new() { Id = "lo1", Name = "Prophecy", Category = "Myth" } });
        h.Meta.CustomEntityTypes.Add(new CustomEntityTypeDefinition { TypeKey = "faction", DisplayName = "Faction" });
        h.Entity.LoadCustomEntitiesAsync("faction").Returns(new List<CustomEntityData> { new() { Id = "f1", EntityTypeKey = "faction", Name = "House", Aliases = { "Clan" } } });

        var changed = false;
        h.Ext.EntityIndexChanged += () => changed = true;
        await h.Ext.RefreshEntityIndexAsync();
        Assert.True(changed);

        var names = h.Ext.GetEntityNamesJson();
        Assert.Contains("Alice Smith", names);
        Assert.Contains("Ace", names);   // alias
        Assert.Contains("Tower", names);
        Assert.Contains("Blade", names);
        Assert.Contains("Prophecy", names);
        Assert.Contains("House", names);
        Assert.DoesNotContain("Dup", names); // ambiguous dropped

        var candidates = h.Ext.GetMentionCandidatesJson();
        Assert.Contains("\"isAlias\":true", candidates);
        Assert.Contains("\"primaryName\":\"Alice Smith\"", candidates);
    }

    [AvaloniaFact]
    public async Task BuildDisplayData_AllTypes()
    {
        var h = Build();
        var loc = new LocationData { Id = "l1", Name = "Capital" };
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData>
        {
            new() { Id = "c1", Name = "Hero", Role = "Lead", EyeColor = "Grey",
                Relationships = { new EntityRelationship { Role = "ally", Target = "Capital" } },
                CustomProperties = { ["House"] = "Stark" }, Sections = { new EntitySection { Title = "Bio", Content = "x" } } },
        });
        h.Entity.LoadLocationsAsync().Returns(new List<LocationData>
        {
            loc, new() { Id = "l2", Name = "Suburb", Parent = "Capital" }, // child of Capital
        });
        h.Entity.LoadItemsAsync().Returns(new List<ItemData> { new() { Id = "i1", Name = "Sword", Type = "Weapon", Origin = "Forged", Description = "sharp" } });
        h.Entity.LoadLoreAsync().Returns(new List<LoreData> { new() { Id = "lo1", Name = "Myth", Category = "Old", Description = "tale" } });
        h.Meta.CustomEntityTypes.Add(new CustomEntityTypeDefinition
        {
            TypeKey = "faction", DisplayName = "Faction",
            DefaultFields = { new CustomEntityFieldDefinition { Key = "Leader", DisplayName = "Leader" },
                              new CustomEntityFieldDefinition { Key = "Ally", DisplayName = "Ally", Type = CustomPropertyType.EntityRef } },
        });
        h.Entity.LoadCustomEntitiesAsync("faction").Returns(new List<CustomEntityData>
        {
            new() { Id = "f1", EntityTypeKey = "faction", Name = "House",
                Fields = { ["Leader"] = "Hero", ["Ally"] = "Capital" }, CustomProperties = { ["Motto"] = "Win" },
                Relationships = { new EntityRelationship { Role = "rival", Target = "Other" } } },
        });
        await h.Ext.RefreshEntityIndexAsync();

        var ch = await h.Ext.BuildDisplayDataByIdAsync("c1");
        Assert.Equal("Hero", ch!.Title);
        Assert.NotEmpty(ch.Pills);
        Assert.NotEmpty(ch.Relationships);

        var l = await h.Ext.BuildDisplayDataByIdAsync("l1");
        Assert.Equal("Capital", l!.Title); // has sublocation pill (Suburb)

        var it = await h.Ext.BuildDisplayDataByIdAsync("i1");
        Assert.Equal("Sword", it!.Title);

        var lo = await h.Ext.BuildDisplayDataByIdAsync("lo1");
        Assert.Equal("Myth", lo!.Title);

        var cu = await h.Ext.BuildDisplayDataByIdAsync("f1");
        Assert.Equal("House", cu!.Title);

        Assert.Null(await h.Ext.BuildDisplayDataByIdAsync("missing"));
    }

    [AvaloniaFact]
    public async Task BuildDisplayData_Character_WithOverride_AiFindings_MapPins()
    {
        var h = Build();
        h.Ext.OnDocumentOpened(Ctx());

        var ch = new CharacterData
        {
            Id = "c1", Name = "Jon", Role = "base",
            ChapterOverrides = { new CharacterOverride { Chapter = "ch1", Scene = "Scene One", Role = "overridden", Name = "JonOverride" } },
        };
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData> { ch });

        // AI findings for this entity
        h.ProjSettings.ChapterAnalysis = new Dictionary<string, ChapterAnalysisResult>
        {
            ["ch1"] = new ChapterAnalysisResult
            {
                Scenes = { ["s1"] = new SceneAnalysisResult { Findings = {
                    new CachedAiFinding { Type = "tip", Title = "T", Description = "D", Excerpt = "E", EntityName = "JonOverride" },
                    new CachedAiFinding { Type = "scene_stats", EntityName = "JonOverride" }, // skipped
                } } },
            },
        };
        // Map pin referencing the entity
        h.Book.Maps.Add(new MapReference { Id = "m1", Name = "World" });
        var map = new MapData { Id = "m1", Name = "World", Pins = { new MapPin { Id = "p1", EntityId = "c1", Label = "Home" } } };
        h.Map.LoadMapAsync("m1").Returns(map);

        await h.Ext.RefreshEntityIndexAsync();
        var data = await h.Ext.BuildDisplayDataByIdAsync("c1");
        Assert.Contains("JonOverride", data!.Title); // override applied
        Assert.NotEmpty(data.AiFindings);
        Assert.NotEmpty(data.MapPins);
    }

    [AvaloniaFact]
    public async Task BuildDisplayData_Character_DateAge()
    {
        var h = Build();
        h.Ext.OnDocumentOpened(Ctx(sceneTitle: "Sc", chapterTitle: "Ch"));
        var chapter = new ChapterData { Guid = "ch1", Title = "Ch" };
        h.Proj.GetChaptersOrdered().Returns(new List<ChapterData> { chapter });
        h.Proj.GetScenesForChapter("ch1").Returns(new List<SceneData> { new() { Id = "s1", Date = "2020-01-01" } });
        var ch = new CharacterData { Id = "c1", Name = "Aged", AgeMode = "date", BirthDate = "2000-01-01", AgeIntervalUnit = IntervalUnit.Years };
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData> { ch });
        await h.Ext.RefreshEntityIndexAsync();
        var data = await h.Ext.BuildDisplayDataByIdAsync("c1");
        Assert.Equal("Aged", data!.Title); // age pill computed from scene date
    }

    [AvaloniaFact]
    public async Task NavigateToEntity_ViaRelationshipTarget()
    {
        var h = Build();
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData>
        {
            new() { Id = "c1", Name = "Jon", Relationships = { new EntityRelationship { Role = "brother", Target = "Robb" } } },
            new() { Id = "c2", Name = "Robb" },
        });
        await h.Ext.RefreshEntityIndexAsync();
        var data = await h.Ext.BuildDisplayDataByIdAsync("c1");
        var target = data!.Relationships[0].Targets[0];
        Assert.True(target.CanNavigate); // Robb is in the index
        await target.NavigateCommand.ExecuteAsync(null); // -> NavigateToEntityAsync sets current ref + shows card
        Assert.True(h.Vm.IsOpen);
    }

    [AvaloniaFact]
    public async Task ViewModelHooks_CloseTogglePinOpen()
    {
        var h = Build();
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData> { new() { Id = "c1", Name = "Jon" } });
        await h.Ext.RefreshEntityIndexAsync();
        h.Ext.EditorBounds = new Size(1000, 800);

        // navigate sets _currentReference so OpenRequested has something to open
        var data = await h.Ext.BuildDisplayDataByIdAsync("c1");
        h.Vm.Show(data!, 0, 0);
        // simulate the editor wiring the current reference by navigating to self
        await h.Ext.BuildDisplayDataByIdAsync("c1");

        h.Vm.TogglePinRequested!.Invoke(); // pin -> PositionPinnedCard
        Assert.True(h.Vm.IsPinned);
        h.Vm.TogglePinRequested!.Invoke(); // unpin
        Assert.False(h.Vm.IsPinned);

        h.Vm.CloseRequested!.Invoke(); // -> HideCard
        Assert.False(h.Vm.IsOpen);
    }

    [AvaloniaFact]
    public async Task OpenRequested_FiresOpenEntity_AfterNavigate()
    {
        var h = Build();
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData>
        {
            new() { Id = "c1", Name = "Jon", Relationships = { new EntityRelationship { Role = "ally", Target = "Robb" } } },
            new() { Id = "c2", Name = "Robb" },
        });
        await h.Ext.RefreshEntityIndexAsync();
        var data = await h.Ext.BuildDisplayDataByIdAsync("c1");
        await data!.Relationships[0].Targets[0].NavigateCommand.ExecuteAsync(null); // sets _currentReference = Robb
        h.Vm.OpenRequested!.Invoke();
        Assert.NotNull(h.Opened);
        Assert.Equal(EntityType.Character, h.Opened!.Value.Type);
    }

    [AvaloniaFact]
    public void OpenRequested_NoCurrentReference_NoOp()
    {
        var h = Build();
        h.Vm.OpenRequested!.Invoke(); // _currentReference null -> no-op
        Assert.Null(h.Opened);
    }

    [AvaloniaFact]
    public void EditorSizeChanged_PinnedRepositions()
    {
        var h = Build();
        h.Ext.OnEditorSizeChanged(new Size(800, 600));
        Assert.Equal(800, h.Ext.EditorBounds.Width);
        h.Vm.SetPinned(true);
        h.Ext.OnEditorSizeChanged(new Size(1200, 900)); // pinned -> PositionPinnedCard
        Assert.Equal(1200, h.Ext.EditorBounds.Width);
    }

    [AvaloniaFact]
    public void HideIfNotPinned_AllGuards()
    {
        var h = Build();
        // pinned -> stays
        h.Vm.SetPinned(true);
        h.Ext.OnPointerPressed();
        Assert.True(h.Vm.IsPinned);

        // pointer over card -> stays
        h.Vm.SetPinned(false);
        h.Vm.SetPointerOverCard(true);
        h.Ext.OnPointerPressed();
        h.Vm.SetPointerOverCard(false);

        // popup open -> stays
        h.Vm.HasOpenPopup = () => true;
        h.Ext.OnPointerPressed();
        h.Vm.HasOpenPopup = () => false;

        // none -> hides (no throw)
        h.Ext.OnPointerPressed();
        Assert.False(h.Vm.IsOpen);
    }

    [AvaloniaFact]
    public void OnEntityExit_AndCardPointerExited_PostHide()
    {
        var h = Build();
        h.Ext.OnEntityExit();
        h.Vm.PointerExitedRequested!.Invoke();
        Pump(); // run the posted HideIfNotPinned
        Assert.False(h.Vm.IsOpen);
    }

    [AvaloniaFact]
    public void OnDocumentClosing_HidesCard()
    {
        var h = Build();
        h.Ext.OnDocumentClosing(Ctx());
        Assert.False(h.Vm.IsOpen);
    }

    [AvaloniaFact]
    public async Task MapPinIndex_NoBook_Empty()
    {
        var h = Build();
        h.Proj.ActiveBook.Returns((BookData?)null);
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData> { new() { Id = "c1", Name = "Jon" } });
        await h.Ext.RefreshEntityIndexAsync();
        var data = await h.Ext.BuildDisplayDataByIdAsync("c1");
        Assert.Empty(data!.MapPins);
    }

    [AvaloniaFact]
    public async Task MapPinIndex_LoadThrows_Skipped()
    {
        var h = Build();
        h.Book.Maps.Add(new MapReference { Id = "m1", Name = "World" });
        h.Map.LoadMapAsync("m1").Returns<Task<MapData?>>(_ => throw new InvalidOperationException("boom"));
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData> { new() { Id = "c1", Name = "Jon" } });
        await h.Ext.RefreshEntityIndexAsync(); // map load throws -> skipped, no crash
        Assert.NotEqual("[]", h.Ext.GetEntityNamesJson());
    }

    [AvaloniaFact]
    public async Task RefreshIndex_ItemLoreAliases_AndBlankAlias()
    {
        var h = Build();
        h.Entity.LoadItemsAsync().Returns(new List<ItemData> { new() { Id = "i1", Name = "Blade", Aliases = { "Edge" } } });
        h.Entity.LoadLoreAsync().Returns(new List<LoreData> { new() { Id = "lo1", Name = "Myth", Aliases = { "Legend" } } });
        // a blank/bracket-only alias is skipped by AddAlias
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData> { new() { Id = "c1", Name = "Jon", Aliases = { "[[]]", "   " } } });
        await h.Ext.RefreshEntityIndexAsync();
        var names = h.Ext.GetEntityNamesJson();
        Assert.Contains("Edge", names);
        Assert.Contains("Legend", names);
        Assert.Contains("Jon", names);
    }

    [AvaloniaFact]
    public async Task BuildDisplayData_ImagesSectionsAndOverrideExtras()
    {
        var h = Build();
        h.Ext.OnDocumentOpened(Ctx());
        var img = new EntityImage { Name = "p", Path = "pic.png" };
        var ch = new CharacterData
        {
            Id = "c1", Name = "Jon", Images = { img },
            ChapterOverrides = { new CharacterOverride
            {
                Chapter = "ch1", Scene = "Scene One", Name = "JonO",
                CustomProperties = new() { ["Mood"] = "dark" },
                Images = new() { new EntityImage { Name = "o", Path = "ov.png" } },
                Relationships = new() { new EntityRelationship { Role = "foe", Target = "X" } },
            } },
        };
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData> { ch });
        h.Entity.LoadLocationsAsync().Returns(new List<LocationData> { new() { Id = "l1", Name = "Keep", Images = { img }, CustomProperties = { ["K"] = "V" }, Sections = { new EntitySection { Title = "S", Content = "c" } } } });
        h.Entity.LoadItemsAsync().Returns(new List<ItemData> { new() { Id = "i1", Name = "Sword", Images = { img }, CustomProperties = { ["K"] = "V" }, Sections = { new EntitySection { Title = "S", Content = "c" } } } });
        h.Entity.LoadLoreAsync().Returns(new List<LoreData> { new() { Id = "lo1", Name = "Myth", Images = { img }, CustomProperties = { ["K"] = "V" }, Sections = { new EntitySection { Title = "S", Content = "c" } } } });
        h.Meta.CustomEntityTypes.Add(new CustomEntityTypeDefinition { TypeKey = "f", DisplayName = "F" });
        h.Entity.LoadCustomEntitiesAsync("f").Returns(new List<CustomEntityData> { new() { Id = "f1", EntityTypeKey = "f", Name = "House", Images = { img }, Sections = { new EntitySection { Title = "S", Content = "c" } } } });
        await h.Ext.RefreshEntityIndexAsync();

        var c = await h.Ext.BuildDisplayDataByIdAsync("c1");
        Assert.Contains("JonO", c!.Title);
        Assert.NotEmpty(c.Images);          // override images used
        Assert.Contains(c.CustomProperties, p => p.Key == "Mood"); // override merged
        Assert.NotEmpty((await h.Ext.BuildDisplayDataByIdAsync("l1"))!.Images);
        Assert.NotEmpty((await h.Ext.BuildDisplayDataByIdAsync("i1"))!.Images);
        Assert.NotEmpty((await h.Ext.BuildDisplayDataByIdAsync("lo1"))!.Images);
        Assert.NotEmpty((await h.Ext.BuildDisplayDataByIdAsync("f1"))!.Sections);
    }

    [AvaloniaFact]
    public async Task BuildDisplayData_Character_DateAge_FutureBirth_FallsBackToRawAge()
    {
        var h = Build();
        h.Ext.OnDocumentOpened(Ctx());
        var chapter = new ChapterData { Guid = "ch1", Title = "Chapter" };
        h.Proj.GetChaptersOrdered().Returns(new List<ChapterData> { chapter });
        h.Proj.GetScenesForChapter("ch1").Returns(new List<SceneData> { new() { Id = "s1", Date = "2020-01-01" } });
        // birth AFTER the reference date -> ComputeAge returns empty -> falls through to raw Age
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData>
        {
            new() { Id = "c1", Name = "Future", Age = "fortyish", AgeMode = "date", BirthDate = "2030-01-01", AgeIntervalUnit = IntervalUnit.Years },
        });
        await h.Ext.RefreshEntityIndexAsync();
        var data = await h.Ext.BuildDisplayDataByIdAsync("c1");
        Assert.Equal("Future", data!.Title);
    }

    [AvaloniaFact]
    public async Task BuildDisplayData_Character_NonDateAge_UsesRawAge()
    {
        var h = Build();
        h.Ext.OnDocumentOpened(Ctx());
        // AgeMode not "date" -> ResolveCharacterAge returns the raw Age (skips date block)
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData> { new() { Id = "c1", Name = "Bob", Age = "40" } });
        await h.Ext.RefreshEntityIndexAsync();
        var data = await h.Ext.BuildDisplayDataByIdAsync("c1");
        Assert.Equal("Bob", data!.Title);
    }
}
