using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

[Collection("Avalonia")]
public class EntityPanelViewModelTests
{
    private static (EntityPanelViewModel Vm, IEntityService Ent, IProjectService Proj) Build()
    {
        var ent = Substitute.For<IEntityService>();
        var proj = Substitute.For<IProjectService>();
        ent.LoadCharactersAsync().Returns(new List<CharacterData>());
        ent.LoadLocationsAsync().Returns(new List<LocationData>());
        ent.LoadItemsAsync().Returns(new List<ItemData>());
        ent.LoadLoreAsync().Returns(new List<LoreData>());
        ent.GetCustomEntityTypes().Returns(new List<CustomEntityTypeDefinition>());
        ent.LoadCustomEntitiesAsync(Arg.Any<string>()).Returns(new List<CustomEntityData>());
        return (new EntityPanelViewModel(ent, proj), ent, proj);
    }

    // ── Load ────────────────────────────────────────────────────────
    [AvaloniaFact]
    public async Task LoadAll_PopulatesAllLists_Sorted()
    {
        var (vm, ent, _) = Build();
        ent.LoadCharactersAsync().Returns(new List<CharacterData>
        {
            new() { Name = "Bob" }, new() { Name = "Alice" }
        });
        ent.LoadLocationsAsync().Returns(new List<LocationData> { new() { Name = "Town" } });
        ent.LoadItemsAsync().Returns(new List<ItemData> { new() { Name = "Sword" } });
        ent.LoadLoreAsync().Returns(new List<LoreData> { new() { Name = "Magic" } });

        await vm.LoadAllAsync();

        Assert.Equal("Alice", vm.Characters[0].DisplayName);
        Assert.Single(vm.Locations);
        Assert.Single(vm.Items);
        Assert.Single(vm.LoreEntries);
        Assert.NotEmpty(vm.CharacterGroups);
    }

    [AvaloniaFact]
    public async Task LoadAll_BuildsCustomTypesAndEntities()
    {
        var (vm, ent, _) = Build();
        ent.GetCustomEntityTypes().Returns(new List<CustomEntityTypeDefinition>
        {
            new() { TypeKey = "faction", DisplayName = "Faction", DisplayNamePlural = "Factions" }
        });
        ent.LoadCustomEntitiesAsync("faction").Returns(new List<CustomEntityData> { new() { Name = "Reds", EntityTypeKey = "faction" } });

        await vm.LoadAllAsync();

        Assert.Single(vm.CustomEntityTypes);
        Assert.Single(vm.GetCustomEntities("faction"));
        Assert.Empty(vm.GetCustomEntities("missing"));
    }

    [AvaloniaFact]
    public async Task LoadAll_MergesExtensionEntityTypes()
    {
        var (vm, ent, _) = Build();
        var firstCall = true;
        ent.GetCustomEntityTypes().Returns(_ =>
        {
            if (firstCall) { firstCall = false; return new List<CustomEntityTypeDefinition>(); }
            return new List<CustomEntityTypeDefinition> { new() { TypeKey = "race", DisplayName = "Race", DisplayNamePlural = "Races" } };
        });
        vm.ExtensionEntityTypes = new List<Novalist.Sdk.Models.EntityTypeDescriptor>
        {
            new() { TypeKey = "race", DisplayName = "Race", DisplayNamePlural = "Races", FolderName = "",
                    DefaultFields = [new() { Key = "homeland", DisplayName = "Homeland", TypeKey = "String", DefaultValue = "" }] }
        };

        await vm.LoadAllAsync();

        await ent.Received().SaveCustomEntityTypeAsync(Arg.Is<CustomEntityTypeDefinition>(d => d.TypeKey == "race" && d.Source == "extension"));
    }

    // ── Tab / grouping ─────────────────────────────────────────────
    [AvaloniaFact]
    public void SetEntityType_ParsesAndClearsCustomKey()
    {
        var (vm, _, _) = Build();
        vm.ActiveCustomTypeKey = "x";
        vm.SetEntityTypeCommand.Execute("Location");
        Assert.Equal(EntityType.Location, vm.ActiveEntityType);
        Assert.Null(vm.ActiveCustomTypeKey);
        vm.SetEntityTypeCommand.Execute("garbage"); // unparseable -> ignored
        Assert.Equal(EntityType.Location, vm.ActiveEntityType);
    }

    [AvaloniaFact]
    public void SetCustomEntityType_SetsCustomActive()
    {
        var (vm, _, _) = Build();
        vm.SetCustomEntityTypeCommand.Execute("faction");
        Assert.Equal(EntityType.Custom, vm.ActiveEntityType);
        Assert.Equal("faction", vm.ActiveCustomTypeKey);
    }

    [AvaloniaFact]
    public void GroupingMode_SetValidOnly()
    {
        var (vm, _, _) = Build();
        vm.SetCharacterGroupingModeCommand.Execute("Role");
        Assert.Equal("Role", vm.CharacterGroupingMode);
        vm.SetCharacterGroupingModeCommand.Execute("Group");
        Assert.Equal("Group", vm.CharacterGroupingMode);
        vm.SetCharacterGroupingModeCommand.Execute("Nope"); // invalid -> unchanged
        Assert.Equal("Group", vm.CharacterGroupingMode);
    }

    [AvaloniaFact]
    public void OpenEntity_NoSubscriber_NoThrow()
    {
        var (vm, _, _) = Build();
        vm.OpenEntityCommand.Execute(new CharacterData()); // EntityOpenRequested null -> guarded
    }

    [AvaloniaFact]
    public async Task ApplyCustomEntityTemplate_TemplateNotMatching_NoApply()
    {
        var (vm, ent, proj) = Build();
        var book = new BookData();
        book.CustomEntityTemplates.Add(new CustomEntityTemplate { Id = "ct", EntityTypeKey = "other" }); // type mismatch
        proj.ActiveBook.Returns(book);
        ent.GetCustomEntityTypes().Returns(new List<CustomEntityTypeDefinition> { new() { TypeKey = "faction", DisplayName = "F" } });
        await vm.LoadAllAsync();
        vm.SetCustomEntityTypeCommand.Execute("faction");
        CustomEntityData? saved = null;
        ent.SaveCustomEntityAsync(Arg.Do<CustomEntityData>(e => saved = e)).Returns(Task.CompletedTask);
        vm.ShowEntityCreationDialog = (_, _, _) => Task.FromResult<EntityCreationResult?>(new EntityCreationResult("Reds", "ct"));
        await vm.CreateCustomEntityCommand.ExecuteAsync(null);
        Assert.Empty(saved!.Fields); // template not applied (EntityTypeKey mismatch)
    }

    [AvaloniaFact]
    public async Task RefreshGroups_ByRoleAndGroup_WithUnassigned()
    {
        var (vm, ent, _) = Build();
        ent.LoadCharactersAsync().Returns(new List<CharacterData>
        {
            new() { Name = "A", Role = "Hero", Group = "G1" },
            new() { Name = "B", Role = "", Group = "" }, // unassigned
        });
        await vm.LoadAllAsync();
        Assert.Equal(2, vm.CharacterGroups.Count); // Hero + unassigned

        vm.CharacterGroupingMode = "Group"; // OnChanged -> refresh
        Assert.Equal(2, vm.CharacterGroups.Count); // G1 + no-group
    }

    // ── Character selection ─────────────────────────────────────────
    private static async Task<EntityPanelViewModel> WithChars(params string[] names)
    {
        var (vm, ent, _) = Build();
        ent.LoadCharactersAsync().Returns(names.Select(n => new CharacterData { Name = n, Role = "R" }).ToList());
        await vm.LoadAllAsync();
        return vm;
    }

    [AvaloniaFact]
    public async Task Selection_Single_Ctrl_Shift()
    {
        var vm = await WithChars("A", "B", "C");
        var items = vm.CharacterGroups.SelectMany(g => g.Items).ToList();

        EntityType? opened = null;
        vm.EntityOpenRequested += (t, _) => opened = t;

        vm.HandleCharacterSelection(items[0], ctrl: false, shift: false, openEntity: true);
        Assert.True(items[0].IsSelected);
        Assert.Equal(EntityType.Character, opened);

        vm.HandleCharacterSelection(items[1], ctrl: true, shift: false, openEntity: false); // add
        Assert.True(items[1].IsSelected);
        vm.HandleCharacterSelection(items[1], ctrl: true, shift: false, openEntity: false); // toggle off
        Assert.False(items[1].IsSelected);

        vm.HandleCharacterSelection(items[0], ctrl: false, shift: false, openEntity: false); // anchor
        vm.HandleCharacterSelection(items[2], ctrl: false, shift: true, openEntity: false);  // range 0..2
        Assert.All(items, i => Assert.True(i.IsSelected));
    }

    [AvaloniaFact]
    public async Task Selection_ShiftWithoutAnchor_FallsBackToSingle()
    {
        var vm = await WithChars("A", "B");
        var items = vm.CharacterGroups.SelectMany(g => g.Items).ToList();
        vm.HandleCharacterSelection(items[1], ctrl: false, shift: true, openEntity: false);
        Assert.True(items[1].IsSelected);
        Assert.False(items[0].IsSelected);
    }

    [AvaloniaFact]
    public async Task PrepareCharacterDrag_SelectsIfNotSelected()
    {
        var vm = await WithChars("A", "B");
        var items = vm.CharacterGroups.SelectMany(g => g.Items).ToList();
        var ids = vm.PrepareCharacterDrag(items[0]);
        Assert.Contains(items[0].Character.Id, ids);
    }

    [AvaloniaFact]
    public async Task MoveCharactersToGroup_RoleAndGroupModes()
    {
        var (vm, ent, _) = Build();
        var a = new CharacterData { Name = "A", Role = "Old", Group = "OldG" };
        ent.LoadCharactersAsync().Returns(new List<CharacterData> { a });
        await vm.LoadAllAsync();

        await vm.MoveCharactersToGroupAsync([a.Id], "NewRole");
        Assert.Equal("NewRole", a.Role);

        vm.CharacterGroupingMode = "Group";
        await vm.MoveCharactersToGroupAsync([a.Id], "NewGroup");
        Assert.Equal("NewGroup", a.Group);

        await vm.MoveCharactersToGroupAsync([], "x"); // empty -> no-op
    }

    // ── Create commands ─────────────────────────────────────────────
    [AvaloniaFact]
    public async Task CreateCharacter_DialogNull_NoOp()
    {
        var (vm, ent, proj) = Build();
        proj.ActiveBook.Returns((BookData?)null);
        await vm.CreateCharacterCommand.ExecuteAsync(null); // ShowEntityCreationDialog null
        await ent.DidNotReceive().SaveCharacterAsync(Arg.Any<CharacterData>());
    }

    [AvaloniaFact]
    public async Task CreateCharacter_Success_SavesAddsOpens()
    {
        var (vm, ent, proj) = Build();
        proj.ActiveBook.Returns(new BookData());
        EntityType? opened = null;
        vm.EntityOpenRequested += (t, _) => opened = t;
        vm.ShowEntityCreationDialog = (_, _, _) => Task.FromResult<EntityCreationResult?>(new EntityCreationResult("Hero", null));
        await vm.CreateCharacterCommand.ExecuteAsync(null);
        await ent.Received().SaveCharacterAsync(Arg.Is<CharacterData>(c => c.Name == "Hero"));
        Assert.Equal(EntityType.Character, opened);
    }

    [AvaloniaFact]
    public async Task CreateCharacter_WithTemplateAndWizard()
    {
        var (vm, ent, proj) = Build();
        var book = new BookData();
        book.CharacterTemplates.Add(new CharacterTemplate { Id = "t1", Name = "T", Fields = [new TemplateField { Key = "Role", DefaultValue = "Knight" }] });
        proj.ActiveBook.Returns(book);
        bool wizardRan = false;
        vm.RunEntityWizardRequested = (_, _, _) => { wizardRan = true; return Task.CompletedTask; };
        vm.ShowEntityCreationDialog = (_, _, _) => Task.FromResult<EntityCreationResult?>(new EntityCreationResult("Hero", "t1", useWizard: true));
        await vm.CreateCharacterCommand.ExecuteAsync(null);
        await ent.Received().SaveCharacterAsync(Arg.Is<CharacterData>(c => c.Role == "Knight" && c.TemplateId == "t1"));
        Assert.True(wizardRan);
    }

    [AvaloniaFact]
    public async Task CreateLocation_Item_Lore_Success()
    {
        var (vm, ent, proj) = Build();
        proj.ActiveBook.Returns(new BookData());
        vm.ShowEntityCreationDialog = (_, _, _) => Task.FromResult<EntityCreationResult?>(new EntityCreationResult("X", null));
        await vm.CreateLocationCommand.ExecuteAsync(null);
        await vm.CreateItemCommand.ExecuteAsync(null);
        await vm.CreateLoreCommand.ExecuteAsync(null);
        await ent.Received().SaveLocationAsync(Arg.Any<LocationData>());
        await ent.Received().SaveItemAsync(Arg.Any<ItemData>());
        await ent.Received().SaveLoreAsync(Arg.Any<LoreData>());
    }

    [AvaloniaFact]
    public void OpenEntity_TypeSwitch()
    {
        var (vm, _, _) = Build();
        var types = new List<EntityType>();
        vm.EntityOpenRequested += (t, _) => types.Add(t);
        vm.OpenEntityCommand.Execute(null); // no-op
        vm.OpenEntityCommand.Execute(new CharacterData());
        vm.OpenEntityCommand.Execute(new LocationData());
        vm.OpenEntityCommand.Execute(new ItemData());
        vm.OpenEntityCommand.Execute(new LoreData());
        vm.OpenEntityCommand.Execute(new CustomEntityData());
        vm.OpenEntityCommand.Execute("unknown"); // default -> Character
        Assert.Equal(
            new[] { EntityType.Character, EntityType.Location, EntityType.Item, EntityType.Lore, EntityType.Custom, EntityType.Character },
            types);
    }

    // ── Custom entity create/delete/toggle ─────────────────────────
    [AvaloniaFact]
    public async Task CreateCustomEntity_NoActiveType_NoOp()
    {
        var (vm, ent, _) = Build();
        await vm.CreateCustomEntityCommand.ExecuteAsync(null);
        await ent.DidNotReceive().SaveCustomEntityAsync(Arg.Any<CustomEntityData>());
    }

    [AvaloniaFact]
    public async Task CreateCustomEntity_Success_AppliesDefaultFields()
    {
        var (vm, ent, proj) = Build();
        proj.ActiveBook.Returns(new BookData());
        ent.GetCustomEntityTypes().Returns(new List<CustomEntityTypeDefinition>
        {
            new() { TypeKey = "faction", DisplayName = "Faction", DefaultFields = [new() { Key = "motto", DefaultValue = "win" }] }
        });
        await vm.LoadAllAsync();
        vm.SetCustomEntityTypeCommand.Execute("faction");
        vm.ShowEntityCreationDialog = (_, _, _) => Task.FromResult<EntityCreationResult?>(new EntityCreationResult("Reds", null));

        await vm.CreateCustomEntityCommand.ExecuteAsync(null);

        await ent.Received().SaveCustomEntityAsync(Arg.Is<CustomEntityData>(e => e.Name == "Reds" && e.Fields["motto"] == "win"));
        Assert.Single(vm.GetCustomEntities("faction"));
    }

    [AvaloniaFact]
    public async Task DeleteCustomEntity_ConfirmGate()
    {
        var (vm, ent, proj) = Build();
        ent.GetCustomEntityTypes().Returns(new List<CustomEntityTypeDefinition> { new() { TypeKey = "faction", DisplayName = "F" } });
        var entity = new CustomEntityData { Name = "Reds", EntityTypeKey = "faction" };
        ent.LoadCustomEntitiesAsync("faction").Returns(new List<CustomEntityData> { entity });
        await vm.LoadAllAsync();

        vm.ShowConfirmDialog = (_, _) => Task.FromResult(false);
        await vm.DeleteCustomEntityCommand.ExecuteAsync(entity);
        await ent.DidNotReceive().DeleteCustomEntityAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());

        bool deletedEvent = false;
        vm.EntityDeleted += () => deletedEvent = true;
        vm.ShowConfirmDialog = (_, _) => Task.FromResult(true);
        await vm.DeleteCustomEntityCommand.ExecuteAsync(entity);
        await ent.Received().DeleteCustomEntityAsync("faction", entity.Id, false);
        Assert.True(deletedEvent);
        Assert.Empty(vm.GetCustomEntities("faction"));

        await vm.DeleteCustomEntityCommand.ExecuteAsync(null); // null -> no-op
    }

    [AvaloniaFact]
    public async Task ToggleWorldBibleCustomEntity_UsesCorrectArgOrder()
    {
        var (vm, ent, _) = Build();
        var entity = new CustomEntityData { Name = "R", EntityTypeKey = "faction", IsWorldBible = false };
        await vm.ToggleWorldBibleCustomEntityCommand.ExecuteAsync(entity);
        await ent.Received().MoveCustomEntityToWorldBibleAsync("faction", entity.Id); // (typeKey, id)
        Assert.True(entity.IsWorldBible);

        await vm.ToggleWorldBibleCustomEntityCommand.ExecuteAsync(entity);
        await ent.Received().MoveCustomEntityToBookAsync("faction", entity.Id);
        Assert.False(entity.IsWorldBible);

        await vm.ToggleWorldBibleCustomEntityCommand.ExecuteAsync(null); // no-op
    }

    // ── Entity-type management ─────────────────────────────────────
    [AvaloniaFact]
    public async Task CreateEntityType_SavesWhenDialogConfirmed()
    {
        var (vm, ent, _) = Build();
        vm.ShowEntityTypeManagerDialog = mgr => Task.FromResult(true);
        await vm.CreateEntityTypeCommand.ExecuteAsync(null);
        await ent.Received().SaveCustomEntityTypeAsync(Arg.Any<CustomEntityTypeDefinition>());
    }

    [AvaloniaFact]
    public async Task CreateEntityType_CancelledDialog_NoSave()
    {
        var (vm, ent, _) = Build();
        vm.ShowEntityTypeManagerDialog = mgr => Task.FromResult(false);
        await vm.CreateEntityTypeCommand.ExecuteAsync(null);
        await ent.DidNotReceive().SaveCustomEntityTypeAsync(Arg.Any<CustomEntityTypeDefinition>());
    }

    [AvaloniaFact]
    public async Task EditEntityType_OnlyUserSource()
    {
        var (vm, ent, _) = Build();
        vm.ShowEntityTypeManagerDialog = mgr => Task.FromResult(true);
        await vm.EditEntityTypeCommand.ExecuteAsync(new CustomEntityTypeDefinition { TypeKey = "x", Source = "extension" }); // gated
        await vm.EditEntityTypeCommand.ExecuteAsync(null); // gated
        await ent.DidNotReceive().SaveCustomEntityTypeAsync(Arg.Any<CustomEntityTypeDefinition>());

        await vm.EditEntityTypeCommand.ExecuteAsync(new CustomEntityTypeDefinition { TypeKey = "x", DisplayName = "X", Source = "user" });
        await ent.Received().SaveCustomEntityTypeAsync(Arg.Any<CustomEntityTypeDefinition>());
    }

    [AvaloniaFact]
    public async Task DeleteEntityType_UserSource_ConfirmGate_ResetsActive()
    {
        var (vm, ent, _) = Build();
        var def = new CustomEntityTypeDefinition { TypeKey = "faction", DisplayName = "F", Source = "user" };
        ent.GetCustomEntityTypes().Returns(new List<CustomEntityTypeDefinition> { def });
        await vm.LoadAllAsync();
        vm.SetCustomEntityTypeCommand.Execute("faction");

        await vm.DeleteEntityTypeCommand.ExecuteAsync(new CustomEntityTypeDefinition { Source = "extension" }); // gated
        await vm.DeleteEntityTypeCommand.ExecuteAsync(null); // gated

        vm.ShowConfirmDialog = (_, _) => Task.FromResult(false);
        await vm.DeleteEntityTypeCommand.ExecuteAsync(vm.CustomEntityTypes[0]);
        await ent.DidNotReceive().DeleteCustomEntityTypeAsync(Arg.Any<string>());

        vm.ShowConfirmDialog = (_, _) => Task.FromResult(true);
        await vm.DeleteEntityTypeCommand.ExecuteAsync(vm.CustomEntityTypes[0]);
        await ent.Received().DeleteCustomEntityTypeAsync("faction");
        Assert.Equal(EntityType.Character, vm.ActiveEntityType);
        Assert.Null(vm.ActiveCustomTypeKey);
    }

    // ── Delete entity commands ─────────────────────────────────────
    [AvaloniaFact]
    public async Task DeleteCharacter_Location_Item_Lore_ConfirmAndNull()
    {
        var (vm, ent, _) = Build();
        var ch = new CharacterData { Name = "C" };
        var lo = new LocationData { Name = "L" };
        var it = new ItemData { Name = "I" };
        var lr = new LoreData { Name = "R" };
        ent.LoadCharactersAsync().Returns(new List<CharacterData> { ch });
        ent.LoadLocationsAsync().Returns(new List<LocationData> { lo });
        ent.LoadItemsAsync().Returns(new List<ItemData> { it });
        ent.LoadLoreAsync().Returns(new List<LoreData> { lr });
        await vm.LoadAllAsync();
        vm.ShowConfirmDialog = (_, _) => Task.FromResult(true);

        await vm.DeleteCharacterCommand.ExecuteAsync(null); // null no-op
        await vm.DeleteCharacterCommand.ExecuteAsync(ch);
        await vm.DeleteLocationCommand.ExecuteAsync(lo);
        await vm.DeleteItemCommand.ExecuteAsync(it);
        await vm.DeleteLoreCommand.ExecuteAsync(lr);

        await ent.Received().DeleteCharacterAsync(ch.Id, false);
        await ent.Received().DeleteLocationAsync(lo.Id, false);
        await ent.Received().DeleteItemAsync(it.Id, false);
        await ent.Received().DeleteLoreAsync(lr.Id, false);
        Assert.Empty(vm.Characters);
    }

    [AvaloniaFact]
    public async Task DeleteCharacter_NotConfirmed_NoDelete()
    {
        var (vm, ent, _) = Build();
        var ch = new CharacterData { Name = "C" };
        ent.LoadCharactersAsync().Returns(new List<CharacterData> { ch });
        await vm.LoadAllAsync();
        vm.ShowConfirmDialog = (_, _) => Task.FromResult(false);
        await vm.DeleteCharacterCommand.ExecuteAsync(ch);
        await ent.DidNotReceive().DeleteCharacterAsync(Arg.Any<string>(), Arg.Any<bool>());
    }

    // ── World-bible toggles for built-in types ─────────────────────
    [AvaloniaFact]
    public async Task ToggleWorldBible_AllBuiltInTypes()
    {
        var (vm, ent, _) = Build();
        var ch = new CharacterData { IsWorldBible = false };
        var lo = new LocationData { IsWorldBible = true };
        var it = new ItemData { IsWorldBible = false };
        var lr = new LoreData { IsWorldBible = true };

        await vm.ToggleWorldBibleCharacterCommand.ExecuteAsync(ch);
        await ent.Received().MoveEntityToWorldBibleAsync(EntityType.Character, ch.Id);
        Assert.True(ch.IsWorldBible);

        await vm.ToggleWorldBibleLocationCommand.ExecuteAsync(lo);
        await ent.Received().MoveEntityToBookAsync(EntityType.Location, lo.Id);
        Assert.False(lo.IsWorldBible);

        await vm.ToggleWorldBibleItemCommand.ExecuteAsync(it);
        await ent.Received().MoveEntityToWorldBibleAsync(EntityType.Item, it.Id);

        await vm.ToggleWorldBibleLoreCommand.ExecuteAsync(lr);
        await ent.Received().MoveEntityToBookAsync(EntityType.Lore, lr.Id);

        await vm.ToggleWorldBibleCharacterCommand.ExecuteAsync(null); // no-op
        await vm.ToggleWorldBibleLocationCommand.ExecuteAsync(null);
        await vm.ToggleWorldBibleItemCommand.ExecuteAsync(null);
        await vm.ToggleWorldBibleLoreCommand.ExecuteAsync(null);
    }

    // ── Location tree ──────────────────────────────────────────────
    [AvaloniaFact]
    public async Task BuildLocationTree_NestsByParentName_SortsRoots()
    {
        var (vm, ent, _) = Build();
        ent.LoadLocationsAsync().Returns(new List<LocationData>
        {
            new() { Name = "Country" },
            new() { Name = "City", Parent = "Country" },
            new() { Name = "Alone" },
        });
        await vm.LoadAllAsync();

        Assert.Equal(2, vm.LocationTree.Count); // Country + Alone (roots), sorted
        var country = vm.LocationTree.First(n => n.Location.Name == "Country");
        Assert.True(country.HasChildren);
        Assert.Equal("City", country.Children[0].Location.Name);
        Assert.True(country.Children[0].HasParent);
    }

    [AvaloniaFact]
    public async Task BuildLocationTree_BreaksCircularChain()
    {
        var (vm, ent, _) = Build();
        // A -> B -> A  (circular by name)
        ent.LoadLocationsAsync().Returns(new List<LocationData>
        {
            new() { Name = "A", Parent = "B" },
            new() { Name = "B", Parent = "A" },
        });
        await vm.LoadAllAsync();
        // Must not stack-overflow; both end up placed somewhere.
        var all = Flatten(vm.LocationTree).ToList();
        Assert.Equal(2, all.Count);
    }

    private static IEnumerable<LocationTreeItemViewModel> Flatten(IEnumerable<LocationTreeItemViewModel> nodes)
    {
        foreach (var n in nodes)
        {
            yield return n;
            foreach (var c in Flatten(n.Children)) yield return c;
        }
    }

    [AvaloniaFact]
    public async Task SetLocationParent_SavesAndRebuilds()
    {
        var (vm, ent, _) = Build();
        var city = new LocationData { Name = "City" };
        ent.LoadLocationsAsync().Returns(new List<LocationData> { new() { Name = "Country" }, city });
        await vm.LoadAllAsync();
        bool changed = false;
        vm.LocationParentChanged += _ => changed = true;
        await vm.SetLocationParentAsync(city, "Country");
        Assert.Equal("Country", city.Parent);
        await ent.Received().SaveLocationAsync(city);
        Assert.True(changed);
    }

    [AvaloniaFact]
    public async Task BuildLocationTree_PreservesExpandState()
    {
        var (vm, ent, _) = Build();
        var city = new LocationData { Name = "City", Parent = "Country" };
        ent.LoadLocationsAsync().Returns(new List<LocationData> { new() { Name = "Country" }, city });
        await vm.LoadAllAsync();
        var country = vm.LocationTree.First(n => n.Location.Name == "Country");
        country.IsExpanded = false;
        vm.BuildLocationTree(); // rebuild -> restore collapsed state
        country = vm.LocationTree.First(n => n.Location.Name == "Country");
        Assert.False(country.IsExpanded);
    }

    // ── Sub view models ────────────────────────────────────────────
    [AvaloniaTheory]
    [InlineData("female", "#4C6A92")]
    [InlineData("male", "#585B70")]
    [InlineData("other", "#45475A")]
    [InlineData("", "#45475A")]
    public void CharacterListItem_GenderBadge(string gender, string expectedHex)
    {
        var item = new CharacterListItemViewModel(new CharacterData { Name = "N", Gender = gender, IsWorldBible = true });
        var brush = Assert.IsType<Avalonia.Media.SolidColorBrush>(item.GenderBadgeBackground);
        Assert.Equal(Avalonia.Media.Color.Parse(expectedHex), brush.Color);
        Assert.Equal(!string.IsNullOrWhiteSpace(gender), item.HasGender);
        Assert.Equal("N", item.DisplayName);
        Assert.True(item.IsWorldBible);
    }

    [AvaloniaFact]
    public void LocationTreeItem_Flags()
    {
        var node = new LocationTreeItemViewModel(new LocationData { Name = "L", Parent = "P", IsWorldBible = true });
        Assert.True(node.HasParent);
        Assert.True(node.IsWorldBible);
        Assert.False(node.HasChildren);
        node.Children.Add(new LocationTreeItemViewModel(new LocationData()));
        Assert.True(node.HasChildren);
    }

    [AvaloniaFact]
    public void EntityCreation_Records()
    {
        var opt = new EntityCreationTemplateOption("id", "name");
        Assert.Equal("id", opt.Id);
        Assert.Equal("name", opt.Name);
        var res = new EntityCreationResult("n", "t", useWizard: true);
        Assert.True(res.UseWizard);
        Assert.Equal("t", res.TemplateId);
    }

    // ── Apply*Template via Create* with a populated template ───────
    [AvaloniaFact]
    public async Task ApplyCharacterTemplate_FullFields_Props_Sections_DateAge()
    {
        var (vm, ent, proj) = Build();
        var book = new BookData();
        book.CharacterTemplates.Add(new CharacterTemplate
        {
            Id = "tc",
            AgeMode = "date",
            AgeIntervalUnit = IntervalUnit.Months,
            Fields =
            [
                new TemplateField { Key = "Gender", DefaultValue = "" },   // empty -> guard return
                new TemplateField { Key = "Gender", DefaultValue = "Female" },
                new TemplateField { Key = "Age", DefaultValue = "30" },
                new TemplateField { Key = "Role", DefaultValue = "Knight" },
                new TemplateField { Key = "EyeColor", DefaultValue = "Blue" },
                new TemplateField { Key = "HairColor", DefaultValue = "Brown" },
                new TemplateField { Key = "HairLength", DefaultValue = "Long" },
                new TemplateField { Key = "Height", DefaultValue = "180" },
                new TemplateField { Key = "Build", DefaultValue = "Slim" },
                new TemplateField { Key = "SkinTone", DefaultValue = "Pale" },
                new TemplateField { Key = "DistinguishingFeatures", DefaultValue = "Scar" },
            ],
            CustomPropertyDefs = [new CustomPropertyDefinition { Key = "rank", DefaultValue = "A" }],
            Sections = [new TemplateSection { Title = "Bio", DefaultContent = "x" }],
        });
        proj.ActiveBook.Returns(book);
        CharacterData? saved = null;
        ent.SaveCharacterAsync(Arg.Do<CharacterData>(c => saved = c)).Returns(Task.CompletedTask);
        vm.ShowEntityCreationDialog = (_, _, _) => Task.FromResult<EntityCreationResult?>(new EntityCreationResult("Hero", "tc"));

        await vm.CreateCharacterCommand.ExecuteAsync(null);

        Assert.NotNull(saved);
        Assert.Equal("date", saved!.AgeMode);
        Assert.Equal(IntervalUnit.Months, saved.AgeIntervalUnit);
        Assert.Equal("Female", saved.Gender);
        Assert.Equal("Scar", saved.DistinguishingFeatures);
        Assert.Equal("A", saved.CustomProperties["rank"]);
        Assert.Contains(saved.Sections, s => s.Title == "Bio");
    }

    [AvaloniaFact]
    public async Task ApplyCharacterTemplate_BookNull_AndTemplateMissing_Guards()
    {
        // book == null guard
        var (vm1, ent1, proj1) = Build();
        proj1.ActiveBook.Returns((BookData?)null);
        vm1.ShowEntityCreationDialog = (_, _, _) => Task.FromResult<EntityCreationResult?>(new EntityCreationResult("H", "ghost"));
        await vm1.CreateCharacterCommand.ExecuteAsync(null);
        await ent1.Received().SaveCharacterAsync(Arg.Is<CharacterData>(c => c.TemplateId == null || c.TemplateId == string.Empty));

        // template == null guard (book present, no matching id)
        var (vm2, ent2, proj2) = Build();
        proj2.ActiveBook.Returns(new BookData());
        vm2.ShowEntityCreationDialog = (_, _, _) => Task.FromResult<EntityCreationResult?>(new EntityCreationResult("H", "missing"));
        await vm2.CreateCharacterCommand.ExecuteAsync(null);
        await ent2.Received().SaveCharacterAsync(Arg.Any<CharacterData>());
    }

    [AvaloniaFact]
    public async Task ApplyLocation_Item_Lore_Templates()
    {
        var (vm, ent, proj) = Build();
        var book = new BookData();
        book.LocationTemplates.Add(new LocationTemplate
        {
            Id = "tl",
            Fields = [new TemplateField { Key = "Type", DefaultValue = "City" },
                      new TemplateField { Key = "Description", DefaultValue = "Big" },
                      new TemplateField { Key = "Ignored", DefaultValue = "" }], // continue guard
            CustomPropertyDefs = [new CustomPropertyDefinition { Key = "pop", DefaultValue = "1000" }],
            Sections = [new TemplateSection { Title = "Geo", DefaultContent = "g" }],
        });
        book.ItemTemplates.Add(new ItemTemplate
        {
            Id = "ti",
            Fields = [new TemplateField { Key = "Type", DefaultValue = "Weapon" },
                      new TemplateField { Key = "Description", DefaultValue = "Sharp" },
                      new TemplateField { Key = "Origin", DefaultValue = "Forge" }],
            CustomPropertyDefs = [new CustomPropertyDefinition { Key = "w", DefaultValue = "5" }],
            Sections = [new TemplateSection { Title = "Lore", DefaultContent = "l" }],
        });
        book.LoreTemplates.Add(new LoreTemplate
        {
            Id = "tr",
            Fields = [new TemplateField { Key = "Category", DefaultValue = "Myth" },
                      new TemplateField { Key = "Description", DefaultValue = "Old" }],
            CustomPropertyDefs = [new CustomPropertyDefinition { Key = "era", DefaultValue = "Ancient" }],
            Sections = [new TemplateSection { Title = "Origin", DefaultContent = "o" }],
        });
        proj.ActiveBook.Returns(book);

        LocationData? loc = null; ItemData? it = null; LoreData? lr = null;
        ent.SaveLocationAsync(Arg.Do<LocationData>(x => loc = x)).Returns(Task.CompletedTask);
        ent.SaveItemAsync(Arg.Do<ItemData>(x => it = x)).Returns(Task.CompletedTask);
        ent.SaveLoreAsync(Arg.Do<LoreData>(x => lr = x)).Returns(Task.CompletedTask);

        vm.ShowEntityCreationDialog = (title, _, _) =>
            Task.FromResult<EntityCreationResult?>(new EntityCreationResult("X",
                title.Contains("ocation") ? "tl" : title.Contains("tem") ? "ti" : "tr"));

        await vm.CreateLocationCommand.ExecuteAsync(null);
        await vm.CreateItemCommand.ExecuteAsync(null);
        await vm.CreateLoreCommand.ExecuteAsync(null);

        Assert.Equal("City", loc!.Type);
        Assert.Equal("Big", loc.Description);
        Assert.Equal("1000", loc.CustomProperties["pop"]);
        Assert.Contains(loc.Sections, s => s.Title == "Geo");
        Assert.Equal("Weapon", it!.Type);
        Assert.Equal("Forge", it.Origin);
        Assert.Equal("Myth", lr!.Category);
        Assert.Contains(lr.Sections, s => s.Title == "Origin");
    }

    [AvaloniaFact]
    public async Task ApplyCustomEntityTemplate_FullFlow()
    {
        var (vm, ent, proj) = Build();
        var book = new BookData();
        book.CustomEntityTemplates.Add(new CustomEntityTemplate
        {
            Id = "ct", EntityTypeKey = "faction",
            Fields = [new TemplateField { Key = "motto", DefaultValue = "Win" },
                      new TemplateField { Key = "empty", DefaultValue = "" }],
            CustomPropertyDefs = [new CustomPropertyDefinition { Key = "size", DefaultValue = "Large" }],
            Sections = [new TemplateSection { Title = "History", DefaultContent = "h" }],
        });
        proj.ActiveBook.Returns(book);
        ent.GetCustomEntityTypes().Returns(new List<CustomEntityTypeDefinition> { new() { TypeKey = "faction", DisplayName = "Faction" } });
        await vm.LoadAllAsync();
        vm.SetCustomEntityTypeCommand.Execute("faction");

        CustomEntityData? saved = null;
        ent.SaveCustomEntityAsync(Arg.Do<CustomEntityData>(e => saved = e)).Returns(Task.CompletedTask);
        vm.ShowEntityCreationDialog = (_, _, _) => Task.FromResult<EntityCreationResult?>(new EntityCreationResult("Reds", "ct"));

        await vm.CreateCustomEntityCommand.ExecuteAsync(null);

        Assert.Equal("Win", saved!.Fields["motto"]);
        Assert.Equal("Large", saved.CustomProperties["size"]);
        Assert.Contains(saved.Sections, s => s.Title == "History");
    }

    [AvaloniaFact]
    public async Task CreateCustomEntity_TypeNotFound_NoOp()
    {
        var (vm, ent, _) = Build();
        vm.SetCustomEntityTypeCommand.Execute("ghost"); // active key set, but no such type loaded
        await vm.CreateCustomEntityCommand.ExecuteAsync(null);
        await ent.DidNotReceive().SaveCustomEntityAsync(Arg.Any<CustomEntityData>());
    }

    [AvaloniaFact]
    public async Task Selection_ShiftAnchorNotInOrder_FallsBackToSingle()
    {
        var vm = await WithChars("A", "B");
        var items = vm.CharacterGroups.SelectMany(g => g.Items).ToList();
        vm.HandleCharacterSelection(items[0], ctrl: false, shift: false, openEntity: false); // anchor = A

        // Stray item whose Character.Id is not present in the visual order.
        var stray = new CharacterListItemViewModel(new CharacterData { Name = "Z", Role = "R" });
        vm.HandleCharacterSelection(stray, ctrl: false, shift: true, openEntity: false); // end == -1 -> fallback
        Assert.True(stray.IsSelected);
    }

    [AvaloniaFact]
    public async Task Selection_ShiftStartNotFound_FallsBack()
    {
        var (vm, ent, _) = Build();
        var a = new CharacterData { Name = "A", Role = "R" };
        var b = new CharacterData { Name = "B", Role = "R" };
        ent.LoadCharactersAsync().Returns(new List<CharacterData> { a, b });
        await vm.LoadAllAsync();
        var aItem = vm.CharacterGroups.SelectMany(g => g.Items).First(i => i.Character.Id == a.Id);
        vm.HandleCharacterSelection(aItem, ctrl: false, shift: false, openEntity: false); // anchor = A.id

        // Remove A so the anchor id is no longer in the visual order.
        vm.ShowConfirmDialog = (_, _) => Task.FromResult(true);
        await vm.DeleteCharacterCommand.ExecuteAsync(a);

        var bItem = vm.CharacterGroups.SelectMany(g => g.Items).First(i => i.Character.Id == b.Id);
        vm.HandleCharacterSelection(bItem, ctrl: false, shift: true, openEntity: false); // start == -1 -> fallback
        Assert.True(bItem.IsSelected);
    }

    // ── Create with wizard (Location/Item/Lore/Custom) ──────────────
    [AvaloniaFact]
    public async Task CreateEntities_WithWizard_InvokesWizardHook()
    {
        var (vm, ent, _) = Build();
        var wizardTypes = new List<EntityType>();
        vm.ShowEntityCreationDialog = (_, _, _) => Task.FromResult<EntityCreationResult?>(new EntityCreationResult("New", null, useWizard: true));
        vm.RunEntityWizardRequested = (type, _, _) => { wizardTypes.Add(type); return Task.CompletedTask; };
        await vm.CreateLocationCommand.ExecuteAsync(null);
        await vm.CreateItemCommand.ExecuteAsync(null);
        await vm.CreateLoreCommand.ExecuteAsync(null);
        Assert.Contains(EntityType.Location, wizardTypes);
        Assert.Contains(EntityType.Item, wizardTypes);
        Assert.Contains(EntityType.Lore, wizardTypes);
    }

    [AvaloniaFact]
    public async Task CreateCustomEntity_WithWizard()
    {
        var (vm, ent, _) = Build();
        ent.GetCustomEntityTypes().Returns(new List<CustomEntityTypeDefinition>
        {
            new() { TypeKey = "faction", DisplayName = "Faction", DisplayNamePlural = "Factions" }
        });
        await vm.LoadAllAsync();
        vm.ActiveCustomTypeKey = "faction";
        var wizardFired = false;
        vm.ShowEntityCreationDialog = (_, _, _) => Task.FromResult<EntityCreationResult?>(new EntityCreationResult("House", null, useWizard: true));
        vm.RunEntityWizardRequested = (type, _, key) => { wizardFired = type == EntityType.Custom && key == "faction"; return Task.CompletedTask; };
        await vm.CreateCustomEntityCommand.ExecuteAsync(null);
        Assert.True(wizardFired);
        Assert.NotEmpty(vm.GetCustomEntities("faction"));
    }

    // ── Toggle World Bible: move back to book (IsWorldBible == true) ─
    [AvaloniaFact]
    public async Task ToggleWorldBible_MovesBackToBook_AllTypes()
    {
        var (vm, ent, _) = Build();
        ent.MoveEntityToBookAsync(Arg.Any<EntityType>(), Arg.Any<string>()).Returns(Task.CompletedTask);

        var c = new CharacterData { Id = "c", Name = "C", IsWorldBible = true };
        await vm.ToggleWorldBibleCharacterCommand.ExecuteAsync(c);
        await ent.Received().MoveEntityToBookAsync(EntityType.Character, "c");
        Assert.False(c.IsWorldBible);

        var l = new LocationData { Id = "l", Name = "L", IsWorldBible = true };
        await vm.ToggleWorldBibleLocationCommand.ExecuteAsync(l);
        await ent.Received().MoveEntityToBookAsync(EntityType.Location, "l");

        var i = new ItemData { Id = "i", Name = "I", IsWorldBible = true };
        await vm.ToggleWorldBibleItemCommand.ExecuteAsync(i);
        await ent.Received().MoveEntityToBookAsync(EntityType.Item, "i");

        var lo = new LoreData { Id = "lo", Name = "Lo", IsWorldBible = true };
        await vm.ToggleWorldBibleLoreCommand.ExecuteAsync(lo);
        await ent.Received().MoveEntityToBookAsync(EntityType.Lore, "lo");
    }

    [AvaloniaFact]
    public async Task ToggleWorldBible_MovesToWorldBible_LocationAndLore()
    {
        var (vm, ent, _) = Build();
        ent.MoveEntityToWorldBibleAsync(Arg.Any<EntityType>(), Arg.Any<string>()).Returns(Task.CompletedTask);
        var l = new LocationData { Id = "l", Name = "L", IsWorldBible = false };
        await vm.ToggleWorldBibleLocationCommand.ExecuteAsync(l);
        await ent.Received().MoveEntityToWorldBibleAsync(EntityType.Location, "l");
        var lo = new LoreData { Id = "lo", Name = "Lo", IsWorldBible = false };
        await vm.ToggleWorldBibleLoreCommand.ExecuteAsync(lo);
        await ent.Received().MoveEntityToWorldBibleAsync(EntityType.Lore, "lo");
    }
}
