using System.Collections.ObjectModel;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Dialogs;
using Novalist.Desktop.Localization;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

// No [Collection("Avalonia")]: this VM touches no Avalonia objects (only Loc, models,
// ObservableObject). Staying out of the Avalonia collection means no AvaloniaSynchronizationContext
// is current when the debounced auto-save is scheduled, so its delayed continuation resumes on the
// thread pool instead of leaking onto the headless Dispatcher and poisoning sibling tests.
public class EntityEditorViewModelTests
{
    static EntityEditorViewModelTests()
    {
        var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Locales");
        Loc.Instance.Initialize(dir, "en");
    }

    private sealed class H
    {
        public IEntityService Entity = null!;
        public ISettingsService Settings = null!;
        public IProjectService Proj = null!;
        public AppSettings App = null!;
        public EntityEditorViewModel Vm = null!;
    }

    private static H Build(bool loaded = false, BookData? book = null)
    {
        var h = new H();
        h.Entity = Substitute.For<IEntityService>();
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData>());
        h.Entity.LoadLocationsAsync().Returns(new List<LocationData>());
        h.Entity.LoadItemsAsync().Returns(new List<ItemData>());
        h.Entity.LoadLoreAsync().Returns(new List<LoreData>());
        h.Entity.LoadCustomEntitiesAsync(Arg.Any<string>()).Returns(new List<CustomEntityData>());
        h.Entity.GetCustomEntityTypes().Returns(new List<CustomEntityTypeDefinition>());
        h.Entity.SaveCharacterAsync(Arg.Any<CharacterData>()).Returns(Task.CompletedTask);
        h.Entity.SaveLocationAsync(Arg.Any<LocationData>()).Returns(Task.CompletedTask);
        h.Entity.SaveItemAsync(Arg.Any<ItemData>()).Returns(Task.CompletedTask);
        h.Entity.SaveLoreAsync(Arg.Any<LoreData>()).Returns(Task.CompletedTask);
        h.Entity.SaveCustomEntityAsync(Arg.Any<CustomEntityData>()).Returns(Task.CompletedTask);
        h.Entity.ImportImageAsync(Arg.Any<string>()).Returns(ci => Task.FromResult("images/" + System.IO.Path.GetFileName((string)ci[0])));
        h.Entity.GetImageFullPath(Arg.Any<string>()).Returns(ci => "C:/proj/" + (string)ci[0]);

        h.App = new AppSettings();
        h.Settings = Substitute.For<ISettingsService>();
        h.Settings.Settings.Returns(h.App);
        h.Settings.Effective.Returns(h.App);
        h.Settings.SaveAsync().Returns(Task.CompletedTask);

        h.Proj = Substitute.For<IProjectService>();
        h.Proj.IsProjectLoaded.Returns(loaded);
        h.Proj.ActiveBook.Returns(book);
        h.Proj.GetChaptersOrdered().Returns(new List<ChapterData>());
        h.Proj.GetScenesForChapter(Arg.Any<string>()).Returns(new List<SceneData>());
        h.Proj.CurrentProject.Returns((ProjectMetadata?)null);

        h.Vm = new EntityEditorViewModel(h.Entity, h.Settings, h.Proj);
        return h;
    }

    // ── Open methods ────────────────────────────────────────────────
    [AvaloniaFact]
    public void OpenCharacter_PopulatesFields()
    {
        var h = Build();
        var c = new CharacterData
        {
            Name = "Jon", Surname = "Snow", Gender = "M", Age = "18", Role = "Hero", Group = "Watch",
            EyeColor = "Grey", HairColor = "Brown", Aliases = { "Lord Snow" },
            Relationships = { new EntityRelationship { Role = "brother", Target = "Robb" } },
            Sections = { new EntitySection { Title = "Bio", Content = "..." } },
            CustomProperties = { ["House"] = "Stark" },
        };
        h.Vm.OpenCharacter(c);
        Assert.True(h.Vm.IsOpen);
        Assert.Equal(EntityType.Character, h.Vm.EntityType);
        Assert.Equal("Jon", h.Vm.Name);
        Assert.Equal("Snow", h.Vm.Surname);
        Assert.Contains("Lord Snow", h.Vm.Aliases);
        Assert.Single(h.Vm.Relationships);
        Assert.Single(h.Vm.Sections);
        Assert.Equal("Jon Snow", h.Vm.Title);
    }

    [AvaloniaFact]
    public void OpenCharacter_DateAge_ComputesAge()
    {
        var h = Build();
        var c = new CharacterData { Name = "Old", AgeMode = "date", BirthDate = "2000-01-01", AgeIntervalUnit = IntervalUnit.Years };
        h.Vm.OpenCharacter(c);
        Assert.True(h.Vm.IsDateAge);
        Assert.NotNull(h.Vm.BirthDate);
        Assert.False(string.IsNullOrEmpty(h.Vm.ComputedAge));
        Assert.True(h.Vm.ShowAgeDatePicker);
        Assert.False(h.Vm.ShowAgeTextField);
    }

    [AvaloniaFact]
    public void OpenCharacter_DateAge_BadDate_NullBirthDate()
    {
        var h = Build();
        var c = new CharacterData { Name = "X", AgeMode = "date", BirthDate = "not-a-date" };
        h.Vm.OpenCharacter(c);
        Assert.True(h.Vm.IsDateAge);
        Assert.Null(h.Vm.BirthDate);
        Assert.Equal(string.Empty, h.Vm.ComputedAge);
    }

    [AvaloniaFact]
    public void OpenLocation_Item_Lore_Populate()
    {
        var h = Build();
        h.Vm.OpenLocation(new LocationData { Name = "Winterfell", Type = "Castle", Parent = "North", Description = "cold" });
        Assert.Equal(EntityType.Location, h.Vm.EntityType);
        Assert.Equal("Castle", h.Vm.LocationType);
        Assert.Equal("North", h.Vm.ParentLocation);

        h.Vm.OpenItem(new ItemData { Name = "Sword", Type = "Weapon", Origin = "Forged" });
        Assert.Equal(EntityType.Item, h.Vm.EntityType);
        Assert.Equal("Weapon", h.Vm.ItemType);
        Assert.Equal("Forged", h.Vm.Origin);

        h.Vm.OpenLore(new LoreData { Name = "Magic", Category = "History", Description = "old" });
        Assert.Equal(EntityType.Lore, h.Vm.EntityType);
        Assert.Equal("History", h.Vm.Category);
    }

    [AvaloniaFact]
    public void OpenCustomEntity_BuildsTypedFields()
    {
        var h = Build();
        var typeDef = new CustomEntityTypeDefinition
        {
            TypeKey = "faction", DisplayName = "Faction",
            DefaultFields =
            {
                new CustomEntityFieldDefinition { Key = "Description", DisplayName = "Description" },
                new CustomEntityFieldDefinition { Key = "Leader", DisplayName = "Leader", Type = CustomPropertyType.String },
                new CustomEntityFieldDefinition { Key = "Color", DisplayName = "Color", Type = CustomPropertyType.Enum, EnumOptions = ["Red", "Blue"] },
                new CustomEntityFieldDefinition { Key = "Ally", DisplayName = "Ally", Type = CustomPropertyType.EntityRef, EnumOptions = ["Character"] },
            },
            Features = new CustomEntityFeatures { IncludeImages = true, IncludeRelationships = true, IncludeSections = true },
        };
        h.Entity.GetCustomEntityTypes().Returns(new List<CustomEntityTypeDefinition> { typeDef });

        var e = new CustomEntityData
        {
            EntityTypeKey = "faction", Name = "Lannister",
            Fields = { ["Description"] = "rich", ["Leader"] = "Tywin", ["Color"] = "Red", ["Ally"] = "Cersei" },
        };
        h.Vm.OpenCustomEntity(e);
        Assert.Equal(EntityType.Custom, h.Vm.EntityType);
        Assert.Equal("rich", h.Vm.Description);
        Assert.Equal(3, h.Vm.CustomEntityFields.Count); // Description excluded
        Assert.Contains(h.Vm.CustomEntityFields, f => f.IsEnumType);
        Assert.Contains(h.Vm.CustomEntityFields, f => f.IsEntityRefType && f.EntityRefTargetType == "Character");
    }

    // ── Save ────────────────────────────────────────────────────────
    [AvaloniaFact]
    public async Task SaveCharacter_WritesBack_RaisesSaved()
    {
        var h = Build();
        var c = new CharacterData { Name = "A" };
        h.Vm.OpenCharacter(c);
        h.Vm.Name = "Renamed";
        IEntityData? saved = null;
        h.Vm.Saved += e => saved = e;
        await h.Vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Renamed", c.Name);
        await h.Entity.Received().SaveCharacterAsync(c);
        Assert.Same(c, saved);
    }

    [AvaloniaFact]
    public async Task SaveLocation_Item_Lore_Custom()
    {
        var h = Build();
        h.Vm.OpenLocation(new LocationData { Name = "L" });
        await h.Vm.SaveCommand.ExecuteAsync(null);
        await h.Entity.Received().SaveLocationAsync(Arg.Any<LocationData>());

        h.Vm.OpenItem(new ItemData { Name = "I" });
        await h.Vm.SaveCommand.ExecuteAsync(null);
        await h.Entity.Received().SaveItemAsync(Arg.Any<ItemData>());

        h.Vm.OpenLore(new LoreData { Name = "Lo" });
        await h.Vm.SaveCommand.ExecuteAsync(null);
        await h.Entity.Received().SaveLoreAsync(Arg.Any<LoreData>());

        h.Vm.OpenCustomEntity(new CustomEntityData { EntityTypeKey = "k", Name = "C" });
        await h.Vm.SaveCommand.ExecuteAsync(null);
        await h.Entity.Received().SaveCustomEntityAsync(Arg.Any<CustomEntityData>());
    }

    [AvaloniaFact]
    public async Task Close_SavesAndClears()
    {
        var h = Build();
        h.Vm.OpenItem(new ItemData { Name = "X" });
        await h.Vm.CloseCommand.ExecuteAsync(null);
        Assert.False(h.Vm.IsOpen);
    }

    // ── Delete ──────────────────────────────────────────────────────
    [AvaloniaFact]
    public async Task Delete_Confirmed_DeletesAndFiresDeleted()
    {
        var h = Build();
        var c = new CharacterData { Name = "Doomed" };
        h.Vm.OpenCharacter(c);
        h.Vm.ConfirmDeleteRequested = (_, _) => Task.FromResult(true);
        bool deleted = false;
        h.Vm.Deleted += () => deleted = true;
        await h.Vm.DeleteCommand.ExecuteAsync(null);
        await h.Entity.Received().DeleteCharacterAsync(c.Id);
        Assert.True(deleted);
        Assert.False(h.Vm.IsOpen);
    }

    [AvaloniaFact]
    public async Task Delete_Cancelled_NoOp()
    {
        var h = Build();
        h.Vm.OpenLocation(new LocationData { Name = "Keep" });
        h.Vm.ConfirmDeleteRequested = (_, _) => Task.FromResult(false);
        await h.Vm.DeleteCommand.ExecuteAsync(null);
        await h.Entity.DidNotReceive().DeleteLocationAsync(Arg.Any<string>());
        Assert.True(h.Vm.IsOpen);
    }

    [AvaloniaFact]
    public async Task Delete_Item_Lore_Custom()
    {
        var h = Build();
        h.Vm.ConfirmDeleteRequested = (_, _) => Task.FromResult(true);

        var i = new ItemData { Name = "i" };
        h.Vm.OpenItem(i);
        await h.Vm.DeleteCommand.ExecuteAsync(null);
        await h.Entity.Received().DeleteItemAsync(i.Id);

        var lo = new LoreData { Name = "lo" };
        h.Vm.OpenLore(lo);
        await h.Vm.DeleteCommand.ExecuteAsync(null);
        await h.Entity.Received().DeleteLoreAsync(lo.Id);

        var ce = new CustomEntityData { EntityTypeKey = "k", Name = "c" };
        h.Vm.OpenCustomEntity(ce);
        await h.Vm.DeleteCommand.ExecuteAsync(null);
        await h.Entity.Received().DeleteCustomEntityAsync("k", ce.Id);
    }

    // ── Collections ─────────────────────────────────────────────────
    [AvaloniaFact]
    public void Relationship_AddRemove()
    {
        var h = Build();
        h.Vm.OpenCharacter(new CharacterData { Name = "C" });
        h.Vm.AddRelationshipCommand.Execute(null);
        Assert.Single(h.Vm.Relationships);
        var r = h.Vm.Relationships[0];
        h.Vm.RemoveRelationshipCommand.Execute(r);
        Assert.Empty(h.Vm.Relationships);
        h.Vm.RemoveRelationshipCommand.Execute(null); // null -> no throw
    }

    [AvaloniaFact]
    public void CustomProperty_AddRemove()
    {
        var h = Build();
        h.Vm.OpenCharacter(new CharacterData { Name = "C" });
        h.Vm.AddCustomPropertyCommand.Execute(null);
        Assert.Single(h.Vm.CustomProperties);
        h.Vm.RemoveCustomPropertyCommand.Execute(h.Vm.CustomProperties[0]);
        Assert.Empty(h.Vm.CustomProperties);
        h.Vm.RemoveCustomPropertyCommand.Execute(null);
    }

    [AvaloniaFact]
    public void Section_AddRemove()
    {
        var h = Build();
        h.Vm.OpenCharacter(new CharacterData { Name = "C" });
        h.Vm.AddSectionCommand.Execute(null);
        Assert.Single(h.Vm.Sections);
        h.Vm.RemoveSectionCommand.Execute(h.Vm.Sections[0]);
        Assert.Empty(h.Vm.Sections);
        h.Vm.RemoveSectionCommand.Execute(null);
    }

    [AvaloniaFact]
    public void Image_RemoveAndFullPath()
    {
        var h = Build();
        h.Vm.OpenCharacter(new CharacterData { Name = "C", Images = { new EntityImage { Name = "p", Path = "a.png" } } });
        Assert.Single(h.Vm.Images);
        Assert.Equal("C:/proj/a.png", h.Vm.GetImageFullPath("a.png"));
        h.Vm.RemoveImageCommand.Execute(h.Vm.Images[0]);
        Assert.Empty(h.Vm.Images);
        h.Vm.RemoveImageCommand.Execute(null);
    }

    [AvaloniaFact]
    public async Task AddImage_Library_Import_External_Cancel()
    {
        var h = Build();
        h.Vm.OpenCharacter(new CharacterData { Name = "C" });

        // No source chosen -> no-op
        h.Vm.ChooseAddImageSourceRequested = () => Task.FromResult<AddImageSourceChoice?>(null);
        await h.Vm.AddImageCommand.ExecuteAsync(null);
        Assert.Empty(h.Vm.Images);

        // Library
        h.Vm.ChooseAddImageSourceRequested = () => Task.FromResult<AddImageSourceChoice?>(AddImageSourceChoice.Library);
        h.Vm.PickProjectImageRequested = _ => Task.FromResult<string?>("lib/pic.png");
        await h.Vm.AddImageCommand.ExecuteAsync(null);
        Assert.Single(h.Vm.Images);
        Assert.Equal("pic", h.Vm.Images[0].Name);

        // Import
        h.Vm.ChooseAddImageSourceRequested = () => Task.FromResult<AddImageSourceChoice?>(AddImageSourceChoice.Import);
        h.Vm.BrowseImageRequested = () => Task.FromResult<string?>("C:/ext/photo.jpg");
        await h.Vm.AddImageCommand.ExecuteAsync(null);
        Assert.Equal(2, h.Vm.Images.Count);

        // Clipboard (external)
        h.Vm.ChooseAddImageSourceRequested = () => Task.FromResult<AddImageSourceChoice?>(AddImageSourceChoice.Clipboard);
        h.Vm.ImportExternalImageRequested = _ => Task.FromResult<string?>("C:/clip/img.png");
        await h.Vm.AddImageCommand.ExecuteAsync(null);
        Assert.Equal(3, h.Vm.Images.Count);
    }

    [AvaloniaFact]
    public async Task AddImage_EmptyResults_NoAdd()
    {
        var h = Build();
        h.Vm.OpenCharacter(new CharacterData { Name = "C" });
        h.Vm.ChooseAddImageSourceRequested = () => Task.FromResult<AddImageSourceChoice?>(AddImageSourceChoice.Library);
        h.Vm.PickProjectImageRequested = _ => Task.FromResult<string?>(null);
        await h.Vm.AddImageCommand.ExecuteAsync(null);
        Assert.Empty(h.Vm.Images);

        h.Vm.ChooseAddImageSourceRequested = () => Task.FromResult<AddImageSourceChoice?>(AddImageSourceChoice.Import);
        h.Vm.BrowseImageRequested = () => Task.FromResult<string?>(null);
        await h.Vm.AddImageCommand.ExecuteAsync(null);
        Assert.Empty(h.Vm.Images);

        h.Vm.ChooseAddImageSourceRequested = () => Task.FromResult<AddImageSourceChoice?>(AddImageSourceChoice.Url);
        h.Vm.ImportExternalImageRequested = _ => Task.FromResult<string?>(null);
        await h.Vm.AddImageCommand.ExecuteAsync(null);
        Assert.Empty(h.Vm.Images);
    }

    [AvaloniaFact]
    public async Task SelectProjectImage_ChangesPath()
    {
        var h = Build();
        var img = new EntityImage { Name = "", Path = "old.png" };
        h.Vm.OpenCharacter(new CharacterData { Name = "C", Images = { img } });
        var target = h.Vm.Images[0];

        await h.Vm.SelectProjectImageAsync(null); // null -> no-op

        h.Vm.PickProjectImageRequested = _ => Task.FromResult<string?>(null);
        await h.Vm.SelectProjectImageAsync(target); // empty -> no-op
        Assert.Equal("old.png", target.Path);

        h.Vm.PickProjectImageRequested = _ => Task.FromResult<string?>("new.png");
        await h.Vm.SelectProjectImageAsync(target);
        Assert.Equal("new.png", target.Path);
        Assert.Equal("new", target.Name);
    }

    // ── Aliases ─────────────────────────────────────────────────────
    [AvaloniaFact]
    public void Alias_AddRemove_AndDedup()
    {
        var h = Build();
        h.Vm.OpenCharacter(new CharacterData { Name = "Jon", Surname = "Snow" });
        h.Vm.NewAliasInput = "Lord Snow";
        h.Vm.AddAliasCommand.Execute(null);
        Assert.Contains("Lord Snow", h.Vm.Aliases);
        Assert.Equal(string.Empty, h.Vm.NewAliasInput);

        h.Vm.NewAliasInput = "   "; // blank -> no-op
        h.Vm.AddAliasCommand.Execute(null);
        h.Vm.NewAliasInput = "Jon"; // equals Name -> skipped
        h.Vm.AddAliasCommand.Execute(null);
        h.Vm.NewAliasInput = "Snow"; // equals Surname -> skipped
        h.Vm.AddAliasCommand.Execute(null);
        h.Vm.NewAliasInput = "lord snow"; // dup -> skipped
        h.Vm.AddAliasCommand.Execute(null);
        Assert.Single(h.Vm.Aliases);

        h.Vm.RemoveAliasCommand.Execute("LORD SNOW");
        Assert.Empty(h.Vm.Aliases);
        h.Vm.RemoveAliasCommand.Execute(null);
        h.Vm.RemoveAliasCommand.Execute("ghost"); // not found
    }

    // ── Auto-save trigger ───────────────────────────────────────────
    [AvaloniaFact]
    public void EditingTrackedProperty_SchedulesAutoSave()
    {
        var h = Build();
        h.Vm.OpenCharacter(new CharacterData { Name = "C" });
        h.Vm.Description = "changed"; // tracked property -> schedules autosave (no throw)
        h.Vm.Role = "Mentor";
        Assert.Equal("Mentor", h.Vm.Role);
    }

    // ── Relationship suggestions / inverse sync ─────────────────────
    [AvaloniaFact]
    public async Task RefreshRelationshipSuggestions_PullsRolesAndCharacters()
    {
        var h = Build();
        h.App.RelationshipPairs["father"] = ["son", "daughter"];
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData>
        {
            new() { Name = "Other", Relationships = { new EntityRelationship { Role = "rival", Target = "X" } } },
        });
        h.Vm.OpenCharacter(new CharacterData { Name = "Me" });
        await h.Vm.RefreshRelationshipSuggestionsAsync();
        Assert.Contains("father", h.Vm.RelationshipRoleSuggestions);
        Assert.Contains("son", h.Vm.RelationshipRoleSuggestions);
        Assert.Contains("rival", h.Vm.RelationshipRoleSuggestions);
        Assert.Contains("Other", h.Vm.CharacterRelationshipSuggestions);
    }

    [AvaloniaFact]
    public async Task AddRelationshipTarget_CreatesInverseViaDialog()
    {
        var h = Build();
        var target = new CharacterData { Name = "Robb" };
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData> { target });
        h.Vm.OpenCharacter(new CharacterData { Name = "Jon" });
        h.Vm.ShowInverseRelationshipDialog = (_, _, _, _) => Task.FromResult<string?>("brother");

        var rel = new ObservableRelationship("brother", "");
        h.Vm.Relationships.Add(rel);
        rel.PendingTarget = "Robb";
        await h.Vm.AddRelationshipTargetAsync(rel);
        Assert.True(rel.HasTargets);

        // Inverse is created on save (single-path sync), not on the interactive add.
        await h.Vm.SaveCommand.ExecuteAsync(null);
        Assert.Contains(target.Relationships, r => r.Role == "brother" && r.Target == "Jon");
        await h.Entity.Received().SaveCharacterAsync(target);
    }

    [AvaloniaFact]
    public async Task AddRelationshipTarget_NullOrEmpty_NoOp()
    {
        var h = Build();
        h.Vm.OpenCharacter(new CharacterData { Name = "C" });
        await h.Vm.AddRelationshipTargetAsync(null);
        var rel = new ObservableRelationship("", "");
        rel.PendingTarget = "";
        await h.Vm.AddRelationshipTargetAsync(rel); // no targets parsed
        Assert.False(rel.HasTargets);
    }

    [AvaloniaFact]
    public void RemoveRelationshipTarget_Removes()
    {
        var h = Build();
        h.Vm.OpenCharacter(new CharacterData { Name = "C" });
        var rel = new ObservableRelationship("brother", "Robb, Bran");
        h.Vm.Relationships.Add(rel);
        var t = rel.Targets[0];
        h.Vm.RemoveRelationshipTargetCommand.Execute(t);
        Assert.DoesNotContain(t, rel.Targets);
        h.Vm.RemoveRelationshipTargetCommand.Execute(null);
    }

    [AvaloniaFact]
    public async Task Save_SyncsInverseRelationships()
    {
        var h = Build();
        h.App.RelationshipPairs["father"] = ["son"];
        var robb = new CharacterData { Name = "Robb" };
        h.Entity.LoadCharactersAsync().Returns(_ => new List<CharacterData> { robb });
        var ned = new CharacterData { Name = "Ned", Relationships = { new EntityRelationship { Role = "father", Target = "Robb" } } };
        h.Vm.OpenCharacter(ned);
        await h.Vm.SaveCommand.ExecuteAsync(null);
        Assert.Contains(robb.Relationships, r => r.Role == "son" && r.Target == "Ned");
    }

    // ── Character interview ─────────────────────────────────────────
    [AvaloniaFact]
    public async Task RunCharacterInterview_InvokesHookAndReloads()
    {
        var h = Build();
        var c = new CharacterData { Name = "C" };
        h.Vm.OpenCharacter(c);
        bool ran = false;
        h.Vm.RunCharacterInterviewRequested = _ => { ran = true; return Task.CompletedTask; };
        await h.Vm.RunCharacterInterviewCommand.ExecuteAsync(null);
        Assert.True(ran);

        // No hook / no character -> no throw
        h.Vm.RunCharacterInterviewRequested = null;
        await h.Vm.RunCharacterInterviewCommand.ExecuteAsync(null);
    }

    // ── Location suggestions / parent ───────────────────────────────
    [AvaloniaFact]
    public async Task LoadLocationNames_FiltersSelf()
    {
        var h = Build();
        var self = new LocationData { Name = "Here" };
        h.Entity.LoadLocationsAsync().Returns(new List<LocationData> { self, new() { Name = "There" } });
        h.Vm.OpenLocation(self);
        await h.Vm.LoadLocationNamesAsync();
        Assert.Contains("There", h.Vm.AllLocationNames);
        Assert.DoesNotContain("Here", h.Vm.AllLocationNames);
    }

    [AvaloniaFact]
    public void ParentLocationSuggestions_SetHideAndUpdate()
    {
        var h = Build();
        var loc = new LocationData { Name = "L" };
        h.Vm.OpenLocation(loc);
        h.Vm.SetParentLocationSuggestions(["A", "B"]);
        Assert.True(h.Vm.IsParentLocationSuggestionOpen);
        Assert.True(h.Vm.ParentLocationSuggestionsVisible);
        h.Vm.HideParentLocationSuggestions();
        Assert.False(h.Vm.ParentLocationSuggestionsVisible);

        loc.Parent = "NewParent";
        h.Vm.UpdateLocationParent(loc);
        Assert.Equal("NewParent", h.Vm.ParentLocation);
    }

    // ── EntityRef population ────────────────────────────────────────
    [AvaloniaFact]
    public void PopulateEntityRefNames_AllBranches()
    {
        var h = Build();
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData> { new() { Name = "Char" } });
        h.Entity.LoadLocationsAsync().Returns(new List<LocationData> { new() { Name = "Loc" } });
        h.Entity.LoadItemsAsync().Returns(new List<ItemData> { new() { Name = "Itm" } });
        h.Entity.LoadLoreAsync().Returns(new List<LoreData> { new() { Name = "Lor" } });
        h.Entity.LoadCustomEntitiesAsync("faction").Returns(new List<CustomEntityData> { new() { Name = "Fac" } });
        var meta = new ProjectMetadata();
        meta.CustomEntityTypes.Add(new CustomEntityTypeDefinition { TypeKey = "faction", DisplayName = "Faction" });
        h.Proj.CurrentProject.Returns(meta);

        var typeDef = new CustomEntityTypeDefinition
        {
            TypeKey = "thing",
            DefaultFields =
            {
                new() { Key = "C", DisplayName = "C", Type = CustomPropertyType.EntityRef, EnumOptions = ["Character"] },
                new() { Key = "L", DisplayName = "L", Type = CustomPropertyType.EntityRef, EnumOptions = ["Location"] },
                new() { Key = "I", DisplayName = "I", Type = CustomPropertyType.EntityRef, EnumOptions = ["Item"] },
                new() { Key = "Lo", DisplayName = "Lo", Type = CustomPropertyType.EntityRef, EnumOptions = ["Lore"] },
                new() { Key = "F", DisplayName = "F", Type = CustomPropertyType.EntityRef, EnumOptions = ["Faction"] },
            },
        };
        h.Entity.GetCustomEntityTypes().Returns(new List<CustomEntityTypeDefinition> { typeDef });
        h.Vm.OpenCustomEntity(new CustomEntityData { EntityTypeKey = "thing", Name = "T" });

        Assert.Contains(h.Vm.CustomEntityFields, f => f.AllEntityRefNames.Contains("Char"));
        Assert.Contains(h.Vm.CustomEntityFields, f => f.AllEntityRefNames.Contains("Loc"));
        Assert.Contains(h.Vm.CustomEntityFields, f => f.AllEntityRefNames.Contains("Itm"));
        Assert.Contains(h.Vm.CustomEntityFields, f => f.AllEntityRefNames.Contains("Lor"));
        Assert.Contains(h.Vm.CustomEntityFields, f => f.AllEntityRefNames.Contains("Fac"));
    }

    // ── Override management ─────────────────────────────────────────
    private static H BuildWithChapters(out CharacterData chr)
    {
        var h = Build(loaded: true);
        var ch = new ChapterData { Guid = "ch1", Title = "Chapter One", Act = "Act 1" };
        h.Proj.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        h.Proj.GetScenesForChapter("ch1").Returns(new List<SceneData> { new() { Title = "Scene A" } });
        chr = new CharacterData { Name = "Hero", Role = "base" };
        return h;
    }

    [AvaloniaFact]
    public void Override_CreateEditRemove()
    {
        var h = BuildWithChapters(out var chr);
        h.Vm.OpenCharacter(chr);
        Assert.NotEmpty(h.Vm.AvailableChapters);

        h.Vm.SelectedOverrideChapter = h.Vm.AvailableChapters[0];
        Assert.NotEmpty(h.Vm.AvailableScenes);
        h.Vm.SelectedOverrideScene = h.Vm.AvailableScenes[0];

        h.Vm.EditOrCreateOverrideCommand.Execute(null);
        Assert.True(h.Vm.IsOverrideMode);
        Assert.Single(h.Vm.ChapterOverrides);
        Assert.NotEmpty(h.Vm.OverrideItems);

        // Edit override role then write back differs from base
        h.Vm.Role = "overridden";

        // Re-invoke EditOrCreate finds existing -> enters that override
        h.Vm.EditOrCreateOverrideCommand.Execute(null);
        Assert.True(h.Vm.IsOverrideMode);

        var item = h.Vm.OverrideItems[0];
        h.Vm.EditExistingOverrideCommand.Execute(item);
        Assert.True(h.Vm.IsOverrideMode);

        h.Vm.RemoveOverrideCommand.Execute(item);
        Assert.Empty(h.Vm.ChapterOverrides);
        Assert.False(h.Vm.IsOverrideMode);
    }

    [AvaloniaFact]
    public async Task Override_StopMode_WritesBackAndSaves()
    {
        var h = BuildWithChapters(out var chr);
        h.Vm.OpenCharacter(chr);
        h.Vm.SelectedOverrideChapter = h.Vm.AvailableChapters[0];
        h.Vm.EditOrCreateOverrideCommand.Execute(null);
        h.Vm.Role = "diff-role";
        await h.Vm.StopOverrideModeCommand.ExecuteAsync(null);
        Assert.False(h.Vm.IsOverrideMode);
        var ov = h.Vm.ChapterOverrides[0];
        Assert.Equal("diff-role", ov.Role);
    }

    [AvaloniaFact]
    public void Override_RemoveActiveOverride_ReloadsBase()
    {
        var h = BuildWithChapters(out var chr);
        h.Vm.OpenCharacter(chr);
        h.Vm.SelectedOverrideChapter = h.Vm.AvailableChapters[0];
        h.Vm.EditOrCreateOverrideCommand.Execute(null);
        h.Vm.Role = "temp";
        var active = h.Vm.OverrideItems[0];
        h.Vm.RemoveOverrideCommand.Execute(active);
        Assert.Equal("base", h.Vm.Role); // reloaded from base
    }

    [AvaloniaFact]
    public void Override_SelectChapterNull_ClearsScenes()
    {
        var h = BuildWithChapters(out var chr);
        h.Vm.OpenCharacter(chr);
        h.Vm.SelectedOverrideChapter = h.Vm.AvailableChapters[0];
        Assert.NotEmpty(h.Vm.AvailableScenes);
        h.Vm.SelectedOverrideChapter = null;
        Assert.Empty(h.Vm.AvailableScenes);
    }

    [AvaloniaFact]
    public void Override_NullGuards()
    {
        var h = Build();
        h.Vm.OpenItem(new ItemData { Name = "x" }); // not a character
        h.Vm.EditOrCreateOverrideCommand.Execute(null); // _character null -> no-op
        h.Vm.EditExistingOverrideCommand.Execute(null); // null item
        h.Vm.RemoveOverrideCommand.Execute(null);
        Assert.False(h.Vm.IsOverrideMode);
    }

    // ── Sub view models ─────────────────────────────────────────────
    [AvaloniaFact]
    public void ObservableKeyValue_TypeFlagsAndSync()
    {
        var date = new ObservableKeyValue("d", "2020-05-01", CustomPropertyType.Date);
        Assert.True(date.IsDateType);
        Assert.NotNull(date.DateValue);
        date.DateValue = new DateTime(2021, 6, 2);
        Assert.Equal("2021-06-02", date.Value);
        date.DateValue = null;
        Assert.Equal(string.Empty, date.Value);

        var b = new ObservableKeyValue("b", "true", CustomPropertyType.Bool);
        Assert.True(b.IsBoolType);
        Assert.True(b.BoolValue);
        b.BoolValue = false;
        Assert.Equal("false", b.Value);

        var en = new ObservableKeyValue("e", "X", CustomPropertyType.Enum, ["X", "Y"]);
        Assert.True(en.IsEnumType);

        var plain = new ObservableKeyValue("k", "v");
        Assert.True(plain.IsTextType);

        var er = new ObservableKeyValue("r", "", CustomPropertyType.EntityRef);
        Assert.True(er.IsEntityRefType);
        er.SetEntityRefSuggestions(["A", "B"]);
        Assert.True(er.IsEntityRefSuggestionOpen);
        Assert.True(er.EntityRefSuggestionsVisible);
        er.HideEntityRefSuggestions();
        Assert.False(er.IsEntityRefSuggestionOpen);
    }

    [AvaloniaFact]
    public void ObservableRelationship_TargetsAndSuggestions()
    {
        var r = new ObservableRelationship("brother", "[[Robb]], Bran, Robb");
        Assert.Equal(2, r.Targets.Count); // dedup Robb
        Assert.True(r.HasTargets);
        Assert.False(r.AddTarget("bran")); // dup
        Assert.True(r.AddTarget("Arya"));

        r.SetRoleSuggestions(["father", "mother"]);
        Assert.True(r.HasRoleSuggestions);
        Assert.True(r.RoleSuggestionsVisible);
        r.HideRoleSuggestions();
        Assert.False(r.RoleSuggestionsVisible);

        r.SetTargetSuggestions(["Sansa"]);
        Assert.True(r.HasTargetSuggestions);
        Assert.True(r.TargetSuggestionsVisible);
        r.HideTargetSuggestions();
        Assert.False(r.TargetSuggestionsVisible);

        var ent = r.ToEntityRelationship();
        Assert.Equal("brother", ent.Role);
        Assert.Contains("Robb", ent.Target);

        var first = r.Targets[0];
        r.RemoveTarget(first);
        Assert.DoesNotContain(first, r.Targets);
    }

    [AvaloniaFact]
    public void ObservableRelationship_OpenFlagsTriggerVisibility()
    {
        var r = new ObservableRelationship("x", "T");
        r.RoleSuggestions = new ObservableCollection<string> { "a" };
        r.IsRoleSuggestionOpen = true;
        Assert.True(r.RoleSuggestionsVisible);
        r.TargetSuggestions = new ObservableCollection<string> { "b" };
        r.IsTargetSuggestionOpen = true;
        Assert.True(r.TargetSuggestionsVisible);
    }

    [AvaloniaFact]
    public void Records_AndWrappers()
    {
        var ch = new ChapterScopeOption("g", "Title", "Act 1");
        Assert.Equal("Title (Act 1)", ch.DisplayTitle);
        var ch2 = new ChapterScopeOption("g", "Title", null);
        Assert.Equal("Title", ch2.DisplayTitle);

        var sc = new SceneScopeOption("Scene");
        Assert.Equal("Scene", sc.Title);

        var ov = new CharacterOverride { Chapter = "c" };
        var item = new OverrideListItemViewModel(ov, "Label");
        Assert.Same(ov, item.Override);
        Assert.Equal("Label", item.DisplayLabel);

        var sec = new ObservableSection("t", "c");
        sec.Title = "t2";
        Assert.Equal("t2", sec.Title);

        var owner = new ObservableRelationship("r", "");
        var tgt = new ObservableRelationshipTarget("name", owner);
        Assert.Equal("name", tgt.Name);
        Assert.Same(owner, tgt.Owner);
    }

    // ── Gap coverage ────────────────────────────────────────────────
    [AvaloniaFact]
    public void LoreCategories_Exposed()
    {
        var h = Build();
        Assert.Same(LoreData.Categories, h.Vm.LoreCategories);
    }

    [AvaloniaFact]
    public async Task Delete_Location_Confirmed()
    {
        var h = Build();
        var l = new LocationData { Name = "Keep" };
        h.Vm.OpenLocation(l);
        h.Vm.ConfirmDeleteRequested = (_, _) => Task.FromResult(true);
        await h.Vm.DeleteCommand.ExecuteAsync(null);
        await h.Entity.Received().DeleteLocationAsync(l.Id);
    }

    [AvaloniaFact]
    public async Task Close_InOverrideMode_ExitsFirst()
    {
        var h = BuildWithChapters(out var chr);
        h.Vm.OpenCharacter(chr);
        h.Vm.SelectedOverrideChapter = h.Vm.AvailableChapters[0];
        h.Vm.EditOrCreateOverrideCommand.Execute(null);
        Assert.True(h.Vm.IsOverrideMode);
        await h.Vm.CloseCommand.ExecuteAsync(null);
        Assert.False(h.Vm.IsOpen);
        Assert.False(h.Vm.IsOverrideMode);
    }

    [AvaloniaFact]
    public async Task Save_InOverrideMode_WritesBackOverride()
    {
        var h = BuildWithChapters(out var chr);
        h.Vm.OpenCharacter(chr);
        h.Vm.SelectedOverrideChapter = h.Vm.AvailableChapters[0];
        h.Vm.EditOrCreateOverrideCommand.Execute(null);
        h.Vm.Role = "override-role";
        await h.Vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("override-role", h.Vm.ChapterOverrides[0].Role);
        await h.Entity.Received().SaveCharacterAsync(chr);
    }

    [AvaloniaFact]
    public void DelayedAutoSave_FiresAfterDebounce()
    {
        // Run on a scratch thread so the Task.Delay yield does not bounce the
        // Avalonia collection runner thread (headless cross-thread poison).
        Task.Run(async () =>
        {
            var h = Build();
            var c = new CharacterData { Name = "C" };
            h.Vm.OpenCharacter(c);
            h.Vm.Description = "edit"; // schedules autosave
            await Task.Delay(1800);
            await h.Entity.Received().SaveCharacterAsync(c);
        }).GetAwaiter().GetResult();
    }

    [AvaloniaFact]
    public async Task AddImage_UnknownSource_NoOp()
    {
        var h = Build();
        h.Vm.OpenCharacter(new CharacterData { Name = "C" });
        h.Vm.ChooseAddImageSourceRequested = () => Task.FromResult<AddImageSourceChoice?>((AddImageSourceChoice)999);
        await h.Vm.AddImageCommand.ExecuteAsync(null);
        Assert.Empty(h.Vm.Images);
    }

    [AvaloniaFact]
    public async Task EnsureInverse_BlankRole_NoCharacter_TargetMissing()
    {
        var h = Build();

        // No entity open -> _character null -> Ensure returns early
        var rel0 = new ObservableRelationship("brother", "");
        h.Vm.Relationships.Add(rel0);
        rel0.PendingTarget = "Robb";
        await h.Vm.AddRelationshipTargetAsync(rel0); // _character null branch

        h.Vm.OpenCharacter(new CharacterData { Name = "Jon" });

        // Blank role -> Ensure returns early
        var rel1 = new ObservableRelationship("", "");
        h.Vm.Relationships.Add(rel1);
        rel1.PendingTarget = "Robb";
        await h.Vm.AddRelationshipTargetAsync(rel1);

        // Target not found -> Ensure returns early
        var rel2 = new ObservableRelationship("brother", "");
        h.Vm.Relationships.Add(rel2);
        rel2.PendingTarget = "Nobody";
        await h.Vm.AddRelationshipTargetAsync(rel2);
        Assert.True(rel2.HasTargets); // target added locally even if no inverse
    }

    [AvaloniaFact]
    public async Task EnsureInverse_InverseBlank_AndAlreadyExists()
    {
        var h = Build();
        var robb = new CharacterData { Name = "Robb" };
        h.Entity.LoadCharactersAsync().Returns(_ => new List<CharacterData> { robb });
        h.Vm.OpenCharacter(new CharacterData { Name = "Jon" });

        // Dialog returns null -> inverse blank -> no inverse added
        h.Vm.ShowInverseRelationshipDialog = (_, _, _, _) => Task.FromResult<string?>(null);
        var rel = new ObservableRelationship("brother", "");
        h.Vm.Relationships.Add(rel);
        rel.PendingTarget = "Robb";
        await h.Vm.AddRelationshipTargetAsync(rel);
        Assert.Empty(robb.Relationships);

        // Now known inverse exists AND target already has it -> already-exists branch
        h.App.RelationshipPairs["friend"] = ["friend"];
        robb.Relationships.Add(new EntityRelationship { Role = "friend", Target = "Jon" });
        var rel2 = new ObservableRelationship("friend", "");
        h.Vm.Relationships.Add(rel2);
        rel2.PendingTarget = "Robb";
        await h.Vm.AddRelationshipTargetAsync(rel2);
        Assert.Single(robb.Relationships); // not duplicated
    }

    [AvaloniaFact]
    public async Task AddRelationshipTarget_AllDuplicates_NoOp()
    {
        var h = Build();
        h.Vm.OpenCharacter(new CharacterData { Name = "C" });
        var rel = new ObservableRelationship("brother", "Robb");
        h.Vm.Relationships.Add(rel);
        rel.PendingTarget = "Robb"; // already a target -> AddTarget false -> !anyAdded return
        await h.Vm.AddRelationshipTargetAsync(rel);
        Assert.Single(rel.Targets);
    }

    [AvaloniaFact]
    public async Task RefreshSuggestions_SkipsSelf()
    {
        var h = Build();
        var self = new CharacterData { Name = "Me" };
        h.Entity.LoadCharactersAsync().Returns(new List<CharacterData> { self });
        h.Vm.OpenCharacter(self);
        await h.Vm.RefreshRelationshipSuggestionsAsync();
        Assert.DoesNotContain("Me", h.Vm.CharacterRelationshipSuggestions);
    }

    [AvaloniaFact]
    public async Task SyncInverse_AllSkipBranches()
    {
        var h = Build();
        var robb = new CharacterData { Name = "Robb" };
        h.Entity.LoadCharactersAsync().Returns(_ => new List<CharacterData> { robb });

        var ned = new CharacterData
        {
            Name = "Ned",
            Relationships =
            {
                new EntityRelationship { Role = "", Target = "Robb" },        // blank role -> continue
                new EntityRelationship { Role = "father", Target = "Ghost" }, // target not found -> continue
                new EntityRelationship { Role = "mystery", Target = "Robb" }, // inverse unknown, dialog null -> continue
            },
        };
        h.Vm.ShowInverseRelationshipDialog = (_, _, _, _) => Task.FromResult<string?>(null);
        h.Vm.OpenCharacter(ned);
        await h.Vm.SaveCommand.ExecuteAsync(null);
        Assert.Empty(robb.Relationships); // none added

        // Already-exists branch: known pair + target already has inverse
        h.App.RelationshipPairs["father"] = ["son"];
        robb.Relationships.Add(new EntityRelationship { Role = "son", Target = "Ned" });
        var ned2 = new CharacterData { Name = "Ned", Relationships = { new EntityRelationship { Role = "father", Target = "Robb" } } };
        h.Vm.OpenCharacter(ned2);
        await h.Vm.SaveCommand.ExecuteAsync(null);
        Assert.Single(robb.Relationships); // not duplicated
    }

    // ── Regression: relationship doubling + inverse re-prompt ───────
    [AvaloniaFact]
    public async Task SyncInverse_MultiTargetBackReference_NoDuplicate()
    {
        // Bug 1: Liam already lists Finn inside a multi-target ("Finn Drent, Noah Bryton").
        // Saving Finn (who references Liam) must NOT append a second "Freund -> Finn Drent" to Liam.
        var h = Build();
        h.App.RelationshipPairs["Freund"] = ["Freund"]; // known inverse -> no prompt
        var liam = new CharacterData
        {
            Name = "Liam", Surname = "Calder",
            Relationships = { new EntityRelationship { Role = "Freund", Target = "Finn Drent, Noah Bryton" } },
        };
        var finn = new CharacterData
        {
            Name = "Finn", Surname = "Drent",
            Relationships = { new EntityRelationship { Role = "Freund", Target = "Liam" } },
        };
        h.Entity.LoadCharactersAsync().Returns(_ => new List<CharacterData> { liam, finn });
        h.Vm.OpenCharacter(finn);
        await h.Vm.SaveCommand.ExecuteAsync(null);

        Assert.Single(liam.Relationships); // no duplicate row appended
        Assert.DoesNotContain(liam.Relationships, r => r.Target is "Finn Drent" or "Finn");
    }

    [AvaloniaFact]
    public async Task SyncInverse_TargetHasUnrelatedRelationship_StillAddsInverse()
    {
        // Target has relationships, but none point back to the source -> back-ref scan
        // completes without a match, and the inverse is still added.
        var h = Build();
        h.App.RelationshipPairs["father"] = ["son"];
        var robb = new CharacterData { Name = "Robb", Relationships = { new EntityRelationship { Role = "friend", Target = "Theon" } } };
        h.Entity.LoadCharactersAsync().Returns(_ => new List<CharacterData> { robb });
        var ned = new CharacterData { Name = "Ned", Relationships = { new EntityRelationship { Role = "father", Target = "Robb" } } };
        h.Vm.OpenCharacter(ned);
        await h.Vm.SaveCommand.ExecuteAsync(null);

        Assert.Contains(robb.Relationships, r => r.Role == "son" && r.Target == "Ned");
        Assert.Equal(2, robb.Relationships.Count);
    }

    [AvaloniaFact]
    public async Task SyncInverse_AlreadyReciprocated_DoesNotPrompt()
    {
        // Bug 2: target already references the source back -> no inverse dialog, no duplicate.
        var h = Build();
        var robb = new CharacterData { Name = "Robb", Relationships = { new EntityRelationship { Role = "rival", Target = "Ned" } } };
        h.Entity.LoadCharactersAsync().Returns(_ => new List<CharacterData> { robb });
        var prompted = false;
        h.Vm.ShowInverseRelationshipDialog = (_, _, _, _) => { prompted = true; return Task.FromResult<string?>("rival"); };
        var ned = new CharacterData { Name = "Ned", Relationships = { new EntityRelationship { Role = "rival", Target = "Robb" } } };
        h.Vm.OpenCharacter(ned);
        await h.Vm.SaveCommand.ExecuteAsync(null);

        Assert.False(prompted);            // already reciprocated -> skip the prompt
        Assert.Single(robb.Relationships); // and no duplicate reciprocal
    }

    [AvaloniaFact]
    public void ResolveCustomPropertyDefs_AllEntityTypes()
    {
        var book = new BookData();
        book.CharacterTemplates.Add(new CharacterTemplate { Id = "ct", CustomPropertyDefs = { new() { Key = "Rank", Type = CustomPropertyType.Enum, EnumOptions = ["A"] } } });
        book.LocationTemplates.Add(new LocationTemplate { Id = "lt", CustomPropertyDefs = { new() { Key = "Climate", Type = CustomPropertyType.String } } });
        book.ItemTemplates.Add(new ItemTemplate { Id = "it", CustomPropertyDefs = { new() { Key = "Weight", Type = CustomPropertyType.Int } } });
        book.LoreTemplates.Add(new LoreTemplate { Id = "lot", CustomPropertyDefs = { new() { Key = "Era", Type = CustomPropertyType.String } } });
        book.CustomEntityTemplates.Add(new CustomEntityTemplate { Id = "cet", CustomPropertyDefs = { new() { Key = "Tier", Type = CustomPropertyType.String } } });

        var h = Build(loaded: true, book: book);
        h.Vm.OpenCharacter(new CharacterData { Name = "C", TemplateId = "ct", CustomProperties = { ["Rank"] = "A" } });
        Assert.Contains(h.Vm.CustomProperties, p => p.Key == "Rank" && p.IsEnumType);

        h.Vm.OpenLocation(new LocationData { Name = "L", TemplateId = "lt", CustomProperties = { ["Climate"] = "Cold" } });
        Assert.Contains(h.Vm.CustomProperties, p => p.Key == "Climate");

        h.Vm.OpenItem(new ItemData { Name = "I", TemplateId = "it", CustomProperties = { ["Weight"] = "5" } });
        Assert.Contains(h.Vm.CustomProperties, p => p.Key == "Weight");

        h.Vm.OpenLore(new LoreData { Name = "Lo", TemplateId = "lot", CustomProperties = { ["Era"] = "Old" } });
        Assert.Contains(h.Vm.CustomProperties, p => p.Key == "Era");

        h.Entity.GetCustomEntityTypes().Returns(new List<CustomEntityTypeDefinition>());
        h.Vm.OpenCustomEntity(new CustomEntityData { EntityTypeKey = "k", Name = "Ce", TemplateId = "cet", CustomProperties = { ["Tier"] = "1" } });
        Assert.Contains(h.Vm.CustomProperties, p => p.Key == "Tier");
    }

    [AvaloniaFact]
    public void OverrideDisplayLabel_ActOnly()
    {
        var h = Build(loaded: true);
        var ch = new CharacterData { Name = "Hero" };
        ch.ChapterOverrides.Add(new CharacterOverride { Act = "Act 2", Chapter = "" });
        h.Vm.OpenCharacter(ch);
        Assert.Contains(h.Vm.OverrideItems, o => o.DisplayLabel.Contains("Act 2"));
    }

    [AvaloniaFact]
    public void Override_LoadsAllOverrideFields()
    {
        var h = BuildWithChapters(out var chr);
        chr.ChapterOverrides.Add(new CharacterOverride
        {
            Chapter = "ch1",
            Name = "AltName",
            CustomProperties = new() { ["Mood"] = "dark" },
            Images = [new EntityImage { Name = "x", Path = "alt.png" }],
            Relationships = [new EntityRelationship { Role = "foe", Target = "Z" }],
            Sections = [new EntitySection { Title = "Sec", Content = "c" }],
        });
        h.Vm.OpenCharacter(chr);
        var item = h.Vm.OverrideItems[0];
        h.Vm.EditExistingOverrideCommand.Execute(item);
        Assert.Equal("AltName", h.Vm.Name);
        Assert.Contains(h.Vm.CustomProperties, p => p.Key == "Mood");
        Assert.Contains(h.Vm.Images, i => i.Path == "alt.png");
        Assert.Contains(h.Vm.Relationships, r => r.Targets.Any(t => t.Name == "Z"));
        Assert.Contains(h.Vm.Sections, s => s.Title == "Sec");
    }

    [AvaloniaFact]
    public async Task Override_ReloadBase_DateAge()
    {
        var h = BuildWithChapters(out var chr);
        chr.AgeMode = "date";
        chr.BirthDate = "1990-03-04";
        chr.AgeIntervalUnit = IntervalUnit.Years;
        h.Vm.OpenCharacter(chr);
        h.Vm.SelectedOverrideChapter = h.Vm.AvailableChapters[0];
        h.Vm.EditOrCreateOverrideCommand.Execute(null);
        await h.Vm.StopOverrideModeCommand.ExecuteAsync(null); // ExitOverrideMode -> ReloadBaseToFields date branch
        Assert.True(h.Vm.IsDateAge);
        Assert.NotNull(h.Vm.BirthDate);
    }

    [AvaloniaFact]
    public void RelationshipRoleChange_OnOpenedRelationship_Schedules()
    {
        var h = Build();
        h.Vm.OpenCharacter(new CharacterData { Name = "C", Relationships = { new EntityRelationship { Role = "old", Target = "T" } } });
        // The relationship was created via CreateObservableRelationship -> subscribed
        h.Vm.Relationships[0].Role = "new"; // fires OnRelationshipPropertyChanged
        Assert.Equal("new", h.Vm.Relationships[0].Role);
    }

    [AvaloniaFact]
    public async Task SaveCustomEntity_WithTypeDef_WritesFieldsAndRelationships()
    {
        var h = Build();
        var typeDef = new CustomEntityTypeDefinition
        {
            TypeKey = "faction",
            DefaultFields =
            {
                new CustomEntityFieldDefinition { Key = "Description", DisplayName = "Description" },
                new CustomEntityFieldDefinition { Key = "Leader", DisplayName = "Leader" },
            },
            Features = new CustomEntityFeatures { IncludeImages = true, IncludeRelationships = true, IncludeSections = true },
        };
        h.Entity.GetCustomEntityTypes().Returns(new List<CustomEntityTypeDefinition> { typeDef });
        var e = new CustomEntityData { EntityTypeKey = "faction", Name = "House", Fields = { ["Leader"] = "Old", ["Description"] = "x" } };
        h.Vm.OpenCustomEntity(e);
        h.Vm.Description = "A great house";
        h.Vm.CustomEntityFields[0].Value = "Tywin"; // Leader
        h.Vm.Relationships.Add(new ObservableRelationship("ally", "Stark"));

        await h.Vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Tywin", e.Fields["Leader"]);
        Assert.Equal("A great house", e.Fields["Description"]);
        Assert.Contains(e.Relationships, r => r.Role == "ally");
    }

    [AvaloniaFact]
    public void ResolveDefs_BookNull_TemplateIdSet_ReturnsEmpty()
    {
        var h = Build(loaded: false); // ActiveBook null
        h.Vm.OpenCharacter(new CharacterData { Name = "C", TemplateId = "ct", CustomProperties = { ["K"] = "V" } });
        // No template defs resolvable -> plain (untyped) property
        Assert.Contains(h.Vm.CustomProperties, p => p.Key == "K" && p.IsTextType);
    }

    [AvaloniaFact]
    public async Task Override_WriteBack_ImagesChanged()
    {
        var h = BuildWithChapters(out var chr);
        chr.Images.Add(new EntityImage { Name = "base", Path = "base.png" });
        h.Vm.OpenCharacter(chr);
        h.Vm.SelectedOverrideChapter = h.Vm.AvailableChapters[0];
        h.Vm.EditOrCreateOverrideCommand.Execute(null);
        h.Vm.Images.Add(new EntityImage { Name = "extra", Path = "extra.png" }); // differs from base
        h.Vm.Relationships.Add(new ObservableRelationship("foe", "Villain")); // exercises rel writeback
        await h.Vm.StopOverrideModeCommand.ExecuteAsync(null);
        var ov = h.Vm.ChapterOverrides[0];
        Assert.NotNull(ov.Images);
        Assert.Equal(2, ov.Images!.Count);
        Assert.NotNull(ov.Relationships);
    }
}
