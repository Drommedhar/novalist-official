using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Covers the rich focus-peek payload (entities/peek): per-type pills,
/// appearance, relationships with target resolution, description, sections, and
/// linked map pins.</summary>
public sealed class EntitiesPeekRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly EntitiesRpc _rpc;

    public EntitiesPeekRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-peek-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "PeekNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new EntitiesRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private EntityService Entities => new(_workspace.Projects);
    private MapService Maps => new(_workspace.Projects, _workspace.FileService);

    [Fact]
    public async Task Peek_Character_BuildsBadgePillsAppearanceRelationshipsSectionsImages()
    {
        // A resolvable relationship target and a couple of aliases across types
        // so the name-resolution index is exercised on every branch.
        await Entities.SaveCharacterAsync(new CharacterData { Name = "Rex", Surname = "Vane", Aliases = { "The Fox" } });
        var mira = new CharacterData
        {
            Name = "Mira",
            Surname = "Frost",
            Role = "Protagonist",
            Gender = "", // empty pill is skipped
            Age = "29",
            Group = "The Circle",
            EyeColor = "Green",
            HairColor = "Auburn",
            HairLength = "", // empty appearance prop is skipped
            Height = "170cm",
            Aliases = { "Frostbite" },
            Relationships =
            {
                // one resolvable (bracketed) + one plain + one unknown target
                new EntityRelationship { Role = "Ally", Target = "[[Rex Vane]], Ghost Who Walks" }
            },
            CustomProperties = { ["Rank"] = "Captain", ["Empty"] = "" },
            Sections = { new EntitySection { Title = "Backstory", Content = "Born in the north." } },
            Images =
            {
                new EntityImage { Name = "portrait", Path = "Images/mira.png" },
                new EntityImage { Name = "armored", Path = "Images/mira2.png" }
            }
        };
        await Entities.SaveCharacterAsync(mira);
        var id = (await Entities.LoadCharactersAsync()).Single(c => c.Name == "Mira").Id;

        var peek = await _rpc.PeekAsync("character", id);

        Assert.Equal("character", peek.TypeKey);
        Assert.Equal("Mira Frost", peek.Title);
        Assert.Equal("#5B3F7A", peek.BadgeColor);
        Assert.Null(peek.CustomTypeLabel);
        Assert.Equal(2, peek.Images.Length);
        Assert.EndsWith("/Images/mira.png", peek.Images[0].Url);

        // Role literal, no gender (empty), age as a "{0}" template pill, group,
        // and the relationship-count pill carries the users icon geometry.
        Assert.Contains(peek.Pills, p => p.Text == "Protagonist");
        Assert.DoesNotContain(peek.Pills, p => p.Text == "");
        Assert.Contains(peek.Pills, p => p.LabelKey == "focusPeek.agePill" && p.Arg == "29");
        Assert.Contains(peek.Pills, p => p.Text == "The Circle" && p.Dim);
        Assert.Contains(peek.Pills, p => p.Icon != null && p.Text == "1");

        // Appearance: localized keys, blanks dropped.
        Assert.Contains(peek.AppearanceProps, p => p.Key == "focusPeek.eyes" && p.Value == "Green");
        Assert.DoesNotContain(peek.AppearanceProps, p => p.Key == "focusPeek.hairLength");

        // Custom props: blanks dropped.
        Assert.Contains(peek.CustomProps, p => p.Key == "Rank" && p.Value == "Captain");
        Assert.DoesNotContain(peek.CustomProps, p => p.Key == "Empty");

        // Relationship targets: bracket-stripped + resolved, unknown one is null.
        var rel = Assert.Single(peek.Relationships);
        Assert.Equal("Ally", rel.Role);
        var resolved = rel.Targets.Single(t => t.Name == "Rex Vane");
        Assert.NotNull(resolved.EntityId);
        Assert.Equal("character", resolved.TypeKey);
        var unknown = rel.Targets.Single(t => t.Name == "Ghost Who Walks");
        Assert.Null(unknown.EntityId);
        Assert.Null(unknown.TypeKey);

        Assert.Equal("Backstory", Assert.Single(peek.Sections).Title);
        Assert.Empty(peek.MapPins);
    }

    [Fact]
    public async Task Peek_Character_WithoutSurname_UsesBareName()
    {
        await Entities.SaveCharacterAsync(new CharacterData { Name = "Solo" });
        var id = (await Entities.LoadCharactersAsync()).Single().Id;

        var peek = await _rpc.PeekAsync("character", id);

        Assert.Equal("Solo", peek.Title);
    }

    [Fact]
    public async Task Peek_Location_HasInPill_And_SublocationCount()
    {
        await Entities.SaveLocationAsync(new LocationData { Name = "Kingdom", Type = "Realm", Description = "A vast land.", Aliases = { "The Realm" } });
        await Entities.SaveLocationAsync(new LocationData { Name = "Harbor", Parent = "[[Kingdom]]" });
        var locations = await Entities.LoadLocationsAsync();
        var kingdomId = locations.Single(l => l.Name == "Kingdom").Id;
        var harborId = locations.Single(l => l.Name == "Harbor").Id;

        var kingdom = await _rpc.PeekAsync("location", kingdomId);
        Assert.Equal("location", kingdom.TypeKey);
        Assert.Equal("#355C7D", kingdom.BadgeColor);
        Assert.Equal("A vast land.", kingdom.Description);
        Assert.Contains(kingdom.Pills, p => p.LabelKey == "focusPeek.sublocationsPill" && p.Arg == "1");

        var harbor = await _rpc.PeekAsync("location", harborId);
        Assert.Contains(harbor.Pills, p => p.LabelKey == "focusPeek.inPill" && p.Arg == "Kingdom");
    }

    [Fact]
    public async Task Peek_Item_And_Lore_BuildTypePills()
    {
        await Entities.SaveItemAsync(new ItemData { Name = "Blade", Type = "Weapon", Origin = "Forged", Description = "Sharp.", Aliases = { "Edge" } });
        await Entities.SaveLoreAsync(new LoreData { Name = "The Sundering", Category = "Event", Description = "Long ago.", Aliases = { "Break" } });
        var itemId = (await Entities.LoadItemsAsync()).Single().Id;
        var loreId = (await Entities.LoadLoreAsync()).Single().Id;

        var item = await _rpc.PeekAsync("item", itemId);
        Assert.Equal("#6A4D2F", item.BadgeColor);
        Assert.Contains(item.Pills, p => p.Text == "Weapon");
        Assert.Contains(item.Pills, p => p.Text == "Forged" && p.Dim);
        Assert.Equal("Sharp.", item.Description);

        var lore = await _rpc.PeekAsync("lore", loreId);
        Assert.Equal("#4B5A73", lore.BadgeColor);
        Assert.Contains(lore.Pills, p => p.Text == "Event");
    }

    [Fact]
    public async Task Peek_Custom_EntityRefFieldBecomesRelationship()
    {
        await Entities.SaveCharacterAsync(new CharacterData { Name = "Rex", Surname = "Vane" });
        await Entities.SaveCustomEntityTypeAsync(new CustomEntityTypeDefinition
        {
            TypeKey = "faction",
            DisplayName = "Faction",
            DefaultFields =
            {
                new CustomEntityFieldDefinition { Key = "leader", DisplayName = "Leader", Type = CustomPropertyType.EntityRef },
                new CustomEntityFieldDefinition { Key = "motto", DisplayName = "Motto", Type = CustomPropertyType.String }
            }
        });
        await Entities.SaveCustomEntityAsync(new CustomEntityData
        {
            EntityTypeKey = "faction",
            Name = "The Guild",
            Aliases = { "Guild" },
            Fields = { ["leader"] = "Rex Vane", ["motto"] = "Unity", ["blank"] = "" },
            CustomProperties = { ["Founded"] = "Year 1" },
            Relationships = { new EntityRelationship { Role = "Rival", Target = "Nobody" } }
        });
        var id = (await Entities.LoadCustomEntitiesAsync("faction")).Single().Id;

        var peek = await _rpc.PeekAsync("faction", id);

        Assert.Equal("faction", peek.TypeKey);
        Assert.Equal("Faction", peek.CustomTypeLabel);
        Assert.Equal("#4A6A5A", peek.BadgeColor);
        // relationship-count pill from the explicit relationship list
        Assert.Contains(peek.Pills, p => p.Icon != null && p.Text == "1");
        // string field -> property; entity-ref field -> relationship
        Assert.Contains(peek.CustomProps, p => p.Key == "Motto" && p.Value == "Unity");
        Assert.Contains(peek.CustomProps, p => p.Key == "Founded");
        Assert.DoesNotContain(peek.CustomProps, p => p.Value == "");
        Assert.Contains(peek.Relationships, r => r.Role == "Leader" && r.Targets.Single().EntityId != null);
        Assert.Contains(peek.Relationships, r => r.Role == "Rival" && r.Targets.Single().EntityId == null);
    }

    [Fact]
    public async Task Peek_AmbiguousName_DoesNotResolveTarget()
    {
        // Two characters share a name -> that name is dropped from the index.
        await Entities.SaveCharacterAsync(new CharacterData { Name = "Sam", Surname = "One" });
        await Entities.SaveCharacterAsync(new CharacterData { Name = "Sam", Surname = "Two" });
        var subject = new CharacterData
        {
            Name = "Lead",
            Relationships = { new EntityRelationship { Role = "Knows", Target = "Sam" } }
        };
        await Entities.SaveCharacterAsync(subject);
        var id = (await Entities.LoadCharactersAsync()).Single(c => c.Name == "Lead").Id;

        var peek = await _rpc.PeekAsync("character", id);

        var target = Assert.Single(Assert.Single(peek.Relationships).Targets);
        // "Sam" alone is ambiguous (two records) so it stays an unresolved name;
        // but the full "Sam One"/"Sam Two" would resolve.
        Assert.Equal("Sam", target.Name);
        Assert.Null(target.EntityId);
    }

    [Fact]
    public async Task Peek_MapPins_ListsLinkedPins_AndSkipsUnloadableMaps()
    {
        await Entities.SaveCharacterAsync(new CharacterData { Name = "Mira", Surname = "Frost" });
        var id = (await Entities.LoadCharactersAsync()).Single().Id;

        var map = await Maps.CreateMapAsync("Westlande");
        map.Pins.Add(new MapPin { Id = "pin1", EntityId = id, Label = "Home" });
        map.Pins.Add(new MapPin { Id = "pin2", EntityId = "someone-else" });
        await Maps.SaveMapAsync(map);

        // A dangling map reference (missing file) must be skipped, not thrown.
        _workspace.Projects.ActiveBook!.Maps.Add(new MapReference
        {
            Id = "ghost", Name = "Ghost", FileName = "does-not-exist.json"
        });

        var peek = await _rpc.PeekAsync("character", id);

        var pin = Assert.Single(peek.MapPins);
        Assert.Equal("Westlande", pin.MapName);
        Assert.Equal("pin1", pin.PinId);
        Assert.Equal("Home", pin.PinLabel);
    }

    [Fact]
    public async Task Peek_UnknownType_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.PeekAsync("planet", "x"));
    }

    [Fact]
    public async Task Peek_UnknownId_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.PeekAsync("character", "missing"));
    }

    [Fact]
    public async Task Peek_UnknownCustomId_Throws()
    {
        await Entities.SaveCustomEntityTypeAsync(new CustomEntityTypeDefinition { TypeKey = "faction", DisplayName = "Faction" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.PeekAsync("faction", "missing"));
    }

    /// <summary>Builds a character carrying both a chapter-wide override (matched
    /// by GUID) and a scene-specific one (matched by chapter title) for the
    /// override-resolution tests.</summary>
    private async Task<string> SeedOverriddenCharacterAsync()
    {
        var mira = new CharacterData
        {
            Name = "Mira",
            Surname = "Frost",
            Role = "Protagonist",
            Age = "29",
            EyeColor = "Green",
            CustomProperties = { ["Rank"] = "Captain" },
            Images = { new EntityImage { Name = "base", Path = "Images/base.png" } },
            ChapterOverrides =
            {
                new CharacterOverride { Chapter = "chapter-guid", Scene = null, Role = "Villain", Age = "40" },
                new CharacterOverride
                {
                    Chapter = "Chapter One",
                    Scene = "Scene One",
                    Name = "Masked",
                    EyeColor = "Red",
                    CustomProperties = new() { ["Rank"] = "Warlord", ["Mood"] = "Grim" },
                    Images = [new EntityImage { Name = "masked", Path = "Images/masked.png" }]
                }
            }
        };
        await Entities.SaveCharacterAsync(mira);
        return (await Entities.LoadCharactersAsync()).Single(c => c.Name == "Mira").Id;
    }

    [Fact]
    public async Task Peek_Character_SceneOverride_WinsAndAppliesOverriddenFields()
    {
        var id = await SeedOverriddenCharacterAsync();

        var peek = await _rpc.PeekAsync("character", id, "chapter-guid", "Chapter One", "Scene One");

        // Scene-specific override wins: name + eye colour + custom props + images
        // are overridden; surname/role/age (blank in the scene override) inherit.
        Assert.Equal("Masked Frost", peek.Title);
        Assert.Contains(peek.Pills, p => p.Text == "Protagonist");
        Assert.Contains(peek.Pills, p => p.LabelKey == "focusPeek.agePill" && p.Arg == "29");
        Assert.Contains(peek.AppearanceProps, p => p.Key == "focusPeek.eyes" && p.Value == "Red");
        Assert.Contains(peek.CustomProps, p => p.Key == "Rank" && p.Value == "Warlord");
        Assert.Contains(peek.CustomProps, p => p.Key == "Mood" && p.Value == "Grim");
        Assert.EndsWith("/Images/masked.png", Assert.Single(peek.Images).Url);
        Assert.Equal("Ch: Chapter One → Sc: Scene One", peek.ScopeLabel);
    }

    [Fact]
    public async Task Peek_Character_TitleOnlyMatch_ResolvesSceneOverride()
    {
        var id = await SeedOverriddenCharacterAsync();

        // No GUID passed: the scene override must still resolve via chapter title.
        var peek = await _rpc.PeekAsync("character", id, null, "Chapter One", "Scene One");

        Assert.Equal("Masked Frost", peek.Title);
    }

    [Fact]
    public async Task Peek_Character_ChapterWideOverride_WhenSceneDiffers()
    {
        var id = await SeedOverriddenCharacterAsync();

        // Scene does not match the scene override; the GUID-matched chapter-wide
        // override applies (role + age). Scope label carries only the chapter (the
        // stored GUID, since no friendly title was supplied) and has no scene part.
        var peek = await _rpc.PeekAsync("character", id, "chapter-guid", null, "Nonexistent Scene");

        Assert.Contains(peek.Pills, p => p.Text == "Villain");
        Assert.Contains(peek.Pills, p => p.LabelKey == "focusPeek.agePill" && p.Arg == "40");
        Assert.Equal("Ch: chapter-guid", peek.ScopeLabel);
    }

    [Fact]
    public async Task Peek_Character_NoMatchingOverride_UsesBaseAndNullScope()
    {
        var id = await SeedOverriddenCharacterAsync();

        var peek = await _rpc.PeekAsync("character", id, "other-guid", "Other Chapter", "Other Scene");

        Assert.Equal("Mira Frost", peek.Title);
        Assert.Contains(peek.Pills, p => p.Text == "Protagonist");
        Assert.Null(peek.ScopeLabel);
    }

    [Fact]
    public async Task Peek_Character_AgePill_ComputedFromBirthDateAndSceneDate()
    {
        await Entities.SaveCharacterAsync(new CharacterData
        {
            Name = "Mira",
            AgeMode = "date",
            BirthDate = "1990-03-04",
            Age = "" // raw age blank; the pill must come from the computation
        });
        var id = (await Entities.LoadCharactersAsync()).Single().Id;
        var chapter = await _workspace.Projects.CreateChapterAsync("Chapter One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Scene One");
        scene.Date = "2015-03-04";

        var peek = await _rpc.PeekAsync("character", id, chapter.Guid, "Chapter One", "Scene One");

        Assert.Contains(peek.Pills, p => p.LabelKey == "focusPeek.agePill" && p.Arg == "25");
    }

    [Fact]
    public async Task Peek_Character_AgePill_FallsBackToChapterDate()
    {
        await Entities.SaveCharacterAsync(new CharacterData
        {
            Name = "Mira", AgeMode = "date", BirthDate = "1990-03-04"
        });
        var id = (await Entities.LoadCharactersAsync()).Single().Id;
        var chapter = await _workspace.Projects.CreateChapterAsync("Chapter One");
        chapter.Date = "2010-03-04";
        await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Scene One"); // no scene date

        var peek = await _rpc.PeekAsync("character", id, chapter.Guid, "Chapter One", "Scene One");

        Assert.Contains(peek.Pills, p => p.LabelKey == "focusPeek.agePill" && p.Arg == "20");
    }

    [Fact]
    public async Task Peek_Character_AgePill_MonthsUnit_NoChapterDateUsesToday()
    {
        // AgeIntervalUnit=Months + unknown chapter (no context date) -> computed
        // against today, still yielding a non-empty pill (covers the null-date arm).
        await Entities.SaveCharacterAsync(new CharacterData
        {
            Name = "Mira", AgeMode = "date", BirthDate = "2000-01-01",
            AgeIntervalUnit = IntervalUnit.Months
        });
        var id = (await Entities.LoadCharactersAsync()).Single().Id;

        // Unknown chapter guid: no scene/chapter date resolves, so today is used.
        var peek = await _rpc.PeekAsync("character", id, "no-such-guid", "No Chapter", "No Scene");
        Assert.Contains(peek.Pills, p => p.LabelKey == "focusPeek.agePill" && p.Arg != null && p.Arg != "");

        // No editor context at all (null chapter guid) still computes against today.
        var noContext = await _rpc.PeekAsync("character", id);
        Assert.Contains(noContext.Pills, p => p.LabelKey == "focusPeek.agePill" && p.Arg != null && p.Arg != "");
    }

    [Fact]
    public async Task Peek_Character_AgePill_NonDateMode_UsesOverrideThenBase()
    {
        // AgeMode not "date" -> the raw/override age wins; the computation is skipped.
        await Entities.SaveCharacterAsync(new CharacterData
        {
            Name = "Mira", Age = "29",
            ChapterOverrides =
            {
                new CharacterOverride { Chapter = "chapter-guid", Scene = null, Age = "40" }
            }
        });
        var id = (await Entities.LoadCharactersAsync()).Single().Id;

        var overridden = await _rpc.PeekAsync("character", id, "chapter-guid", null, null);
        Assert.Contains(overridden.Pills, p => p.LabelKey == "focusPeek.agePill" && p.Arg == "40");

        var baseAge = await _rpc.PeekAsync("character", id);
        Assert.Contains(baseAge.Pills, p => p.LabelKey == "focusPeek.agePill" && p.Arg == "29");
    }

    [Fact]
    public async Task Peek_Character_DateMode_BirthAfterReference_FallsBackToRawAge()
    {
        // AgeMode "date" but birth date is after the scene date -> the computation
        // yields nothing, so the raw age is shown (covers the fall-through arm).
        await Entities.SaveCharacterAsync(new CharacterData
        {
            Name = "Mira", Age = "unborn", AgeMode = "date", BirthDate = "2030-01-01"
        });
        var id = (await Entities.LoadCharactersAsync()).Single().Id;
        var chapter = await _workspace.Projects.CreateChapterAsync("Chapter One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Scene One");
        scene.Date = "2015-03-04";

        var peek = await _rpc.PeekAsync("character", id, chapter.Guid, "Chapter One", "Scene One");

        Assert.Contains(peek.Pills, p => p.LabelKey == "focusPeek.agePill" && p.Arg == "unborn");
    }

    [Fact]
    public async Task Peek_Character_NoContext_LeavesScopeNull()
    {
        var id = await SeedOverriddenCharacterAsync();

        var peek = await _rpc.PeekAsync("character", id);

        Assert.Equal("Mira Frost", peek.Title);
        Assert.Null(peek.ScopeLabel);
    }
}
