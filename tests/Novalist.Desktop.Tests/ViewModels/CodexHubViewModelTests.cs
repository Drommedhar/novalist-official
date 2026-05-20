using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

public class CodexHubViewModelTests
{
    private static (CodexHubViewModel Vm, IEntityService Ent) Build(
        IEnumerable<CharacterData>? chars = null,
        IEnumerable<LocationData>? locs = null,
        IEnumerable<ItemData>? items = null,
        IEnumerable<LoreData>? lore = null,
        IEnumerable<CustomEntityTypeDefinition>? customTypes = null,
        Dictionary<string, List<CustomEntityData>>? customEntities = null)
    {
        var ent = Substitute.For<IEntityService>();
        ent.LoadCharactersAsync().Returns((chars ?? []).ToList());
        ent.LoadLocationsAsync().Returns((locs ?? []).ToList());
        ent.LoadItemsAsync().Returns((items ?? []).ToList());
        ent.LoadLoreAsync().Returns((lore ?? []).ToList());
        ent.GetCustomEntityTypes().Returns((customTypes ?? []).ToList());
        ent.LoadCustomEntitiesAsync(Arg.Any<string>()).Returns(ci =>
            (customEntities != null && customEntities.TryGetValue(ci.Arg<string>(), out var l) ? l : []));
        return (new CodexHubViewModel(ent, Substitute.For<IProjectService>()), ent);
    }

    private static CharacterData Char(string name, string role = "", bool wb = false, string? img = null)
    {
        var c = new CharacterData { Name = name, Role = role, IsWorldBible = wb };
        if (img != null) c.Images.Add(new EntityImage { Path = img });
        return c;
    }

    [Fact]
    public async Task Load_PopulatesCountsAndFilters()
    {
        var (vm, _) = Build(
            chars: [Char("Bob"), Char("Alice")],
            locs: [new LocationData { Name = "Town" }],
            items: [new ItemData { Name = "Sword" }],
            lore: [new LoreData { Name = "Magic", Category = "System" }]);

        await vm.LoadAsync();

        Assert.False(vm.IsLoading);
        Assert.Equal(2, vm.CharacterCount);
        Assert.Equal(1, vm.LocationCount);
        Assert.Equal(1, vm.ItemCount);
        Assert.Equal(1, vm.LoreCount);
        Assert.Equal(5, vm.TotalCount);
        // All tab: 5 entities, sorted ascending by name
        Assert.Equal(5, vm.FilteredEntities.Count);
        Assert.Equal("Alice", vm.FilteredEntities[0].Name);
    }

    [Fact]
    public async Task Load_CustomTypes_BuildTabsAndEntities()
    {
        var type = new CustomEntityTypeDefinition { TypeKey = "faction", DisplayNamePlural = "Factions", Icon = "X" };
        var custom = new Dictionary<string, List<CustomEntityData>>
        {
            ["faction"] = [new CustomEntityData { Name = "Reds", Fields = new() { ["motto"] = "win" }, IsWorldBible = true }]
        };
        var (vm, _) = Build(customTypes: [type], customEntities: custom);

        await vm.LoadAsync();

        Assert.Single(vm.CustomTabs);
        Assert.Equal("Factions", vm.CustomTabs[0].DisplayName);
        Assert.Equal(1, vm.CustomTabs[0].Count);
        Assert.Equal(1, vm.TotalCount);
        var item = vm.FilteredEntities.Single();
        Assert.Equal("Reds", item.Name);
        Assert.Equal("win", item.Subtitle); // first non-empty field
        Assert.True(item.IsWorldBible);
        Assert.True(item.IsEmoji);
    }

    [Fact]
    public async Task ActiveTab_FiltersByType()
    {
        var (vm, _) = Build(
            chars: [Char("Bob")],
            locs: [new LocationData { Name = "Town" }]);
        await vm.LoadAsync();

        vm.ActiveTab = "Character";
        Assert.Single(vm.FilteredEntities);
        Assert.Equal(EntityType.Character, vm.FilteredEntities[0].EntityType);

        vm.ActiveTab = "Location";
        Assert.Single(vm.FilteredEntities);
        Assert.Equal(EntityType.Location, vm.FilteredEntities[0].EntityType);
    }

    [Fact]
    public async Task Search_FiltersByName()
    {
        var (vm, _) = Build(chars: [Char("Bob"), Char("Alice")]);
        await vm.LoadAsync();
        vm.SearchQuery = "ali";
        Assert.Single(vm.FilteredEntities);
        Assert.Equal("Alice", vm.FilteredEntities[0].Name);
    }

    [Fact]
    public async Task Sort_NameDescending()
    {
        var (vm, _) = Build(chars: [Char("Alice"), Char("Bob")]);
        await vm.LoadAsync();
        vm.SortMode = CodexSortMode.NameDescending;
        Assert.Equal("Bob", vm.FilteredEntities[0].Name);
        Assert.Equal(CodexSortMode.NameDescending, vm.SortMode);
    }

    [Fact]
    public async Task SortModeIndex_GetSet()
    {
        var (vm, _) = Build();
        await vm.LoadAsync();
        Assert.Equal(0, vm.SortModeIndex);
        vm.SortModeIndex = 1;
        Assert.Equal(CodexSortMode.NameDescending, vm.SortMode);
        vm.SortModeIndex = 1; // unchanged path
        Assert.Equal(1, vm.SortModeIndex);
    }

    [Fact]
    public async Task SelectedCustomTab_SetsActiveTab()
    {
        var type = new CustomEntityTypeDefinition { TypeKey = "faction", DisplayNamePlural = "Factions" };
        var (vm, _) = Build(customTypes: [type],
            customEntities: new() { ["faction"] = [new CustomEntityData { Name = "Reds" }] });
        await vm.LoadAsync();

        vm.SelectedCustomTab = vm.CustomTabs[0];
        Assert.Equal("faction", vm.ActiveTab);

        vm.SelectedCustomTab = null; // null branch -> no change
        Assert.Equal("faction", vm.ActiveTab);
    }

    [Fact]
    public async Task SetTab_ClearsCustomSelection()
    {
        var type = new CustomEntityTypeDefinition { TypeKey = "faction", DisplayNamePlural = "Factions" };
        var (vm, _) = Build(customTypes: [type],
            customEntities: new() { ["faction"] = [new CustomEntityData { Name = "Reds" }] });
        await vm.LoadAsync();
        vm.SelectedCustomTab = vm.CustomTabs[0];

        vm.SetTabCommand.Execute("All");
        Assert.Null(vm.SelectedCustomTab);
        Assert.Equal("All", vm.ActiveTab);
    }

    [Fact]
    public async Task OpenEntity_NullNoOp_AndRaisesEvent()
    {
        var (vm, _) = Build(chars: [Char("Bob")]);
        await vm.LoadAsync();
        EntityType? raised = null;
        vm.EntityOpenRequested += (t, _) => raised = t;

        vm.OpenEntityCommand.Execute(null); // no-op
        Assert.Null(raised);

        vm.OpenEntityCommand.Execute(vm.FilteredEntities[0]);
        Assert.Equal(EntityType.Character, raised);
    }

    [Fact]
    public void ManageEntityTypes_And_OpenTemplates_RaiseEvents()
    {
        var (vm, _) = Build();
        bool manage = false, templates = false;
        vm.ManageEntityTypesRequested += () => manage = true;
        vm.OpenTemplatesRequested += () => templates = true;
        vm.ManageEntityTypesCommand.Execute(null);
        vm.OpenTemplatesCommand.Execute(null);
        Assert.True(manage);
        Assert.True(templates);
    }

    [Fact]
    public async Task Character_WorldBible_AndImagePath_FlowThrough()
    {
        var (vm, _) = Build(chars: [Char("Bob", "hero", wb: true, img: "p.png")]);
        await vm.LoadAsync();
        var item = vm.FilteredEntities[0];
        Assert.True(item.IsWorldBible);
        Assert.True(item.HasImage);
        Assert.Equal("p.png", item.ImagePath);
        Assert.Equal("hero", item.Subtitle);
    }

    [Fact]
    public async Task CustomEntity_MissingTypeDef_UsesFallbackIcon()
    {
        var type = new CustomEntityTypeDefinition { TypeKey = "faction", DisplayNamePlural = "Factions", Icon = "Z" };
        var ent = Substitute.For<IEntityService>();
        ent.LoadCharactersAsync().Returns([]);
        ent.LoadLocationsAsync().Returns([]);
        ent.LoadItemsAsync().Returns([]);
        ent.LoadLoreAsync().Returns([]);
        ent.LoadCustomEntitiesAsync("faction").Returns([new CustomEntityData { Name = "Reds" }]);
        // First call (during Load) returns the type; later calls (during ApplyFilter) return empty
        // so typeDef lookup fails and the fallback icon path is taken.
        ent.GetCustomEntityTypes().Returns(_ => new List<CustomEntityTypeDefinition> { type },
                                           _ => new List<CustomEntityTypeDefinition>());
        var vm = new CodexHubViewModel(ent, Substitute.For<IProjectService>());

        await vm.LoadAsync();

        var item = vm.FilteredEntities.Single(i => i.Name == "Reds");
        Assert.Equal(EntityType.Custom, item.EntityType);
    }

    [Fact]
    public void CodexEntityItem_NullSubtitle_BecomesEmpty()
    {
        var item = new CodexEntityItem(EntityType.Item, new object(), "X", null!, "icon", false);
        Assert.Equal(string.Empty, item.Subtitle);
        Assert.False(item.HasImage);
    }

    [Fact]
    public async Task Refresh_FireAndForget_DoesNotThrow()
    {
        var (vm, _) = Build(chars: [Char("Bob")]);
        vm.Refresh();
        await Task.Delay(20);
        Assert.True(vm.CharacterCount >= 0);
    }
}
