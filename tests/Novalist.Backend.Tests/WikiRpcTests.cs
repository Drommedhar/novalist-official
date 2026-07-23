using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Covers the read-only Wiki projection: index grouping and article
/// assembly (lead, infobox, sections, relationships, stats, referenced-by,
/// appears-with, map pins, plotlines, appearances).</summary>
public sealed class WikiRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly EntityService _entities;
    private readonly WikiRpc _rpc;

    public WikiRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-wiki-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "WikiNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _entities = new EntityService(_workspace.Projects);
        _rpc = new WikiRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private async Task AddSceneAsync(
        string chapterTitle, string sceneTitle, string[] entityIds,
        string? synopsis = null, string? date = null, string? pov = null, string[]? plotlineIds = null)
    {
        var chapter = await _workspace.Projects.CreateChapterAsync(chapterTitle);
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, sceneTitle);
        scene.Synopsis = synopsis;
        if (date != null) scene.Date = date;
        if (pov != null) scene.AnalysisOverrides = new SceneAnalysisOverrides { Pov = pov };
        if (plotlineIds != null) scene.PlotlineIds = plotlineIds.ToList();
        var spans = string.Concat(entityIds.Select(eid =>
            $"<span class=\"nv-entity-mention\" data-entity-id=\"{eid}\">X</span>"));
        await _workspace.Projects.WriteSceneContentAsync(chapter, scene, $"<p>{spans}</p>");
    }

    // ── index ───────────────────────────────────────────────────────

    [Fact]
    public async Task Index_GroupsByScopeAndType_SortedByTitle()
    {
        await _entities.SaveCharacterAsync(new CharacterData { Name = "Zara" });
        await _entities.SaveCharacterAsync(new CharacterData
        {
            Name = "Aldric", Surname = "Vane", Role = "Knight",
            Images = { new EntityImage { Name = "portrait", Path = "Images/hero.png" } }
        });
        await _entities.SaveLocationAsync(new LocationData { Name = "Harbour", Type = "City", IsWorldBible = true });
        await _entities.SaveItemAsync(new ItemData { Name = "Frostbite", Type = "Sword" });
        await _entities.SaveLoreAsync(new LoreData { Name = "The Pact", Category = "History" });

        var index = await _rpc.IndexAsync();

        var book = index.Scopes.Single(s => !s.IsWorldBible);
        var characters = book.Types.Single(t => t.TypeKey == "character");
        Assert.Equal(new[] { "Aldric Vane", "Zara" }, characters.Entries.Select(e => e.Title));
        Assert.Equal("Knight", characters.Entries[0].Subtitle);
        Assert.NotNull(characters.Entries[0].ImageUrl);
        Assert.Null(characters.Entries[1].Subtitle);
        Assert.Null(characters.Entries[1].ImageUrl);
        Assert.False(characters.Entries[0].IsWorldBible);

        Assert.Equal("Frostbite", book.Types.Single(t => t.TypeKey == "item").Entries.Single().Title);
        Assert.Equal("The Pact", book.Types.Single(t => t.TypeKey == "lore").Entries.Single().Title);

        var wb = index.Scopes.Single(s => s.IsWorldBible);
        Assert.Equal("Harbour", wb.Types.Single(t => t.TypeKey == "location").Entries.Single().Title);
        Assert.DoesNotContain(book.Types, t => t.TypeKey == "location");
    }

    [Fact]
    public async Task Index_IncludesCustomTypesWithLabel()
    {
        await _entities.SaveCustomEntityTypeAsync(new CustomEntityTypeDefinition { TypeKey = "spell", DisplayName = "Spell" });
        await _entities.SaveCustomEntityAsync(new CustomEntityData { EntityTypeKey = "spell", Name = "Fireball" });

        var index = await _rpc.IndexAsync();

        var group = index.Scopes.Single(s => !s.IsWorldBible).Types.Single(t => t.TypeKey == "spell");
        Assert.Equal("Spell", group.CustomTypeLabel);
        Assert.Equal("Fireball", group.Entries.Single().Title);
    }

    [Fact]
    public async Task Index_EmptyProject_HasNoScopes()
    {
        var index = await _rpc.IndexAsync();
        Assert.Empty(index.Scopes);
    }

    // ── character article: the full derived surface ─────────────────

    [Fact]
    public async Task Article_Character_LeadInfoboxStatsCrossLinksAppearances()
    {
        await _entities.SaveCharacterAsync(new CharacterData { Id = "villain", Name = "Mordre",
            Relationships = { new EntityRelationship { Role = "Rival", Target = "Aldric Vane" } } });
        await _entities.SaveCharacterAsync(new CharacterData
        {
            Id = "hero", Name = "Aldric", Surname = "Vane",
            Role = "Knight", Group = "Grey Order", Gender = "", Age = "30",
            Aliases = { "The Grey" },
            Images = { new EntityImage { Name = "portrait", Path = "Images/hero.png" } },
            CustomProperties = { ["Motto"] = "Hold the line", ["Empty"] = "" },
            Relationships =
            {
                new EntityRelationship { Role = "Seeks", Target = "Nobody" }
            },
            Sections = { new EntitySection { Title = "History", Content = "<p>Trained under Mordre in the north.</p>" } }
        });

        // A custom entity that references the hero two ways.
        await _entities.SaveCustomEntityTypeAsync(new CustomEntityTypeDefinition
        {
            TypeKey = "spell", DisplayName = "Spell",
            DefaultFields = { new CustomEntityFieldDefinition { Key = "caster", DisplayName = "Caster", Type = CustomPropertyType.EntityRef } }
        });
        await _entities.SaveCustomEntityAsync(new CustomEntityData
        {
            Id = "fb", EntityTypeKey = "spell", Name = "Fireball",
            Fields = { ["caster"] = "Aldric Vane" },
            Relationships = { new EntityRelationship { Role = "Cast by", Target = "Aldric Vane" } }
        });

        _workspace.Projects.ActiveBook!.Plotlines.Add(new PlotlineData { Id = "p1", Name = "Main", Color = "#abc", Order = 0 });

        await AddSceneAsync("Ch1", "Early", ["hero", "villain"], "Aldric departs.", "2024-01-01", "Aldric Vane", ["p1"]);
        await AddSceneAsync("Ch2", "Later", ["hero"], date: "2024-02-01");
        await AddSceneAsync("Ch3", "Undated", ["hero"]);

        // A map pin pointing at the hero, plus a non-matching pin and a dangling map ref.
        var maps = new MapService(_workspace.Projects, _workspace.FileService);
        var map = await maps.CreateMapAsync("World Map");
        map.Pins.Add(new MapPin { Id = "pin1", Label = "Home", EntityId = "hero" });
        map.Pins.Add(new MapPin { Id = "pin2", Label = "Lair", EntityId = "villain" });
        await maps.SaveMapAsync(map);
        _workspace.Projects.ActiveBook!.Maps.Add(new MapReference { Id = "ghost", Name = "Ghost" });

        var article = await _rpc.ArticleAsync("character", "hero");

        // Lead + identity.
        Assert.Equal("Aldric Vane", article.Title);
        Assert.Equal(new[] { "The Grey" }, article.Aliases);
        Assert.Equal("Knight", article.Lead.Primary);
        Assert.Equal("Grey Order", article.Lead.Secondary);
        Assert.Equal("dot", article.Lead.SecondaryConnector);
        Assert.Null(article.Description);

        // Infobox: role present, gender skipped, custom prop present, blank prop skipped, image resolved.
        Assert.Contains(article.Infobox.Fields, f => f.LabelKey == "entityEditor.rolePlaceholder" && f.Value == "Knight");
        Assert.Contains(article.Infobox.Fields, f => f.LabelKey == "entityEditor.age" && f.Value == "30"); // numeric age
        Assert.DoesNotContain(article.Infobox.Fields, f => f.LabelKey == "entityEditor.gender");
        Assert.Contains(article.Infobox.Fields, f => f.LiteralLabel == "Motto");
        Assert.DoesNotContain(article.Infobox.Fields, f => f.LiteralLabel == "Empty");
        Assert.NotNull(article.Infobox.PrimaryImageUrl);

        Assert.Equal("History", article.Sections.Single().Title);
        // Section prose cross-links a bare entity mention to that entity's article.
        Assert.Contains("[Mordre](nventity:character/villain)", article.Sections.Single().Content);
        Assert.Equal("Seeks", article.Relationships.Single().Role);

        // Stats.
        Assert.NotNull(article.Stats);
        Assert.Equal(3, article.Stats!.AppearanceCount);
        Assert.Equal(3, article.Stats.ChapterCount);
        Assert.Equal(1, article.Stats.PovSceneCount);          // only "Early" has POV = Aldric Vane
        Assert.Equal("Early", article.Stats.First!.SceneTitle);
        Assert.Equal("Undated", article.Stats.Last!.SceneTitle);

        // Referenced by: a character relationship, a custom relationship, and a custom entity-ref field.
        Assert.Contains(article.ReferencedBy, r => r.EntityId == "villain" && r.Role == "Rival");
        Assert.Contains(article.ReferencedBy, r => r.EntityId == "fb" && r.Role == "Cast by");
        Assert.Contains(article.ReferencedBy, r => r.EntityId == "fb" && r.Role == "Caster");

        // Appears with: villain shares one scene.
        var co = Assert.Single(article.AppearsWith);
        Assert.Equal("villain", co.EntityId);
        Assert.Equal(1, co.SharedScenes);

        // Map pins: only the matching pin; dangling map ref ignored.
        var pin = Assert.Single(article.MapPins);
        Assert.Equal("pin1", pin.PinId);
        Assert.Equal("World Map", pin.MapName);

        // Plotlines resolved from the appearance scenes.
        Assert.Equal("Main", Assert.Single(article.Plotlines).Name);

        // Appearances chronological.
        Assert.Equal(new[] { "Early", "Later", "Undated" }, article.Appearances.Select(a => a.SceneTitle));
    }

    [Fact]
    public async Task Article_BirthDateLabel_GroupEqualsSurnameDropped_ReferencedByExcludesReciprocal()
    {
        // Reciprocal: hero references villain AND villain references hero.
        await _entities.SaveCharacterAsync(new CharacterData
        {
            Id = "villain", Name = "Mordre",
            Relationships = { new EntityRelationship { Role = "Rival", Target = "Elias Ward" } }
        });
        // One-way: ally references hero, hero does not reference ally.
        await _entities.SaveCharacterAsync(new CharacterData
        {
            Id = "ally", Name = "Mira",
            Relationships = { new EntityRelationship { Role = "Protects", Target = "Elias Ward" } }
        });
        await _entities.SaveCharacterAsync(new CharacterData
        {
            Id = "hero", Name = "Elias", Surname = "Ward",
            Group = "Ward",            // equals surname -> dropped
            Age = "1976-04-10",        // a date -> labelled birth date
            Relationships = { new EntityRelationship { Role = "Nemesis", Target = "Mordre" } }
        });

        var article = await _rpc.ArticleAsync("character", "hero");

        // Age holding a date is shown as a birth date, not "age".
        Assert.Contains(article.Infobox.Fields, f => f.LabelKey == "entityEditor.birthDate" && f.Value == "1976-04-10");
        Assert.DoesNotContain(article.Infobox.Fields, f => f.LabelKey == "entityEditor.age");

        // Group == surname is omitted from the infobox and the lead descriptor.
        Assert.DoesNotContain(article.Infobox.Fields, f => f.LabelKey == "entityEditor.groupPlaceholder");
        Assert.Null(article.Lead.Secondary);

        // Referenced-by drops the reciprocal (villain, already in Relationships) but
        // keeps the one-way reference (ally).
        Assert.DoesNotContain(article.ReferencedBy, r => r.EntityId == "villain");
        Assert.Contains(article.ReferencedBy, r => r.EntityId == "ally");
    }

    [Fact]
    public async Task Article_DetectsPovWhenNoOverride()
    {
        await _entities.SaveCharacterAsync(new CharacterData { Id = "hero", Name = "Aldric" });
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        var html = "<p>Aldric fought. Aldric won. Aldric rested near " +
                   "<span class=\"nv-entity-mention\" data-entity-id=\"hero\">Aldric</span>.</p>";
        await _workspace.Projects.WriteSceneContentAsync(chapter, scene, html);

        var article = await _rpc.ArticleAsync("character", "hero");

        Assert.Equal(1, article.Stats!.PovSceneCount);
    }

    [Fact]
    public async Task Article_NoAppearances_HasNullStatsAndEmptyDerived()
    {
        await _entities.SaveLoreAsync(new LoreData { Id = "pact", Name = "The Pact", Category = "History" });

        var article = await _rpc.ArticleAsync("lore", "pact");

        Assert.Null(article.Stats);
        Assert.Empty(article.Appearances);
        Assert.Empty(article.Plotlines);
        Assert.Empty(article.MapPins);
        Assert.Empty(article.AppearsWith);
        Assert.Empty(article.ReferencedBy);
        Assert.Contains(article.Infobox.Fields, f => f.LabelKey == "entityEditor.category" && f.Value == "History");
        Assert.Null(article.Lead.Secondary);        // lore lead has no secondary
    }

    [Fact]
    public async Task Article_Character_SurfacesOverridesAsChangesOverTime()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("Chapter Two");

        await _entities.SaveCharacterAsync(new CharacterData { Id = "ally", Name = "Mira" });
        await _entities.SaveCharacterAsync(new CharacterData
        {
            Id = "hero", Name = "Aldric",
            ChapterOverrides =
            {
                // Resolved by chapter GUID: scalar + custom-property + an image change.
                new CharacterOverride
                {
                    Chapter = chapter.Guid, Scene = "Duel",
                    Role = "Fallen Knight",
                    CustomProperties = new() { ["Status"] = "Exiled" },
                    Images = [new EntityImage { Name = "older", Path = "Images/aldric-older.png" }]
                },
                // Unknown chapter string: sorts last, label kept verbatim, plus an Act.
                new CharacterOverride { Act = "Act II", Chapter = "Prologue", Age = "40" },
                // List-type only: relationship, alias, and a section title override.
                new CharacterOverride
                {
                    Chapter = chapter.Guid, Scene = "Reunion",
                    Relationships = [new EntityRelationship { Role = "Foe", Target = "Mira" }],
                    Aliases = ["The Fallen"],
                    Sections = [new EntitySection { Title = "Fate", Content = "<p>...</p>" }]
                },
                // Nothing meaningful -> skipped.
                new CharacterOverride { Chapter = chapter.Guid, Scene = "Empty" }
            }
        });

        var article = await _rpc.ArticleAsync("character", "hero");

        Assert.Equal(3, article.Overrides.Length);   // the empty one is dropped

        var scalarOverride = article.Overrides.Single(o => o.Scope == "Chapter Two · Duel");
        Assert.Contains(scalarOverride.Changes, c => c.LabelKey == "entityEditor.rolePlaceholder" && c.Value == "Fallen Knight");
        Assert.Contains(scalarOverride.Changes, c => c.LiteralLabel == "Status" && c.Value == "Exiled");
        Assert.NotEmpty(scalarOverride.Images);       // per-chapter portrait

        var listOverride = article.Overrides.Single(o => o.Scope == "Chapter Two · Reunion");
        Assert.Equal("Foe", listOverride.Relationships.Single().Role);
        Assert.Equal("Mira", listOverride.Relationships.Single().Targets[0].Name);
        Assert.Equal(new[] { "The Fallen" }, listOverride.Aliases);
        Assert.Equal(new[] { "Fate" }, listOverride.SectionTitles);

        var actOverride = article.Overrides.Single(o => o.Scope == "Act II · Prologue");
        Assert.Contains(actOverride.Changes, c => c.LabelKey == "entityEditor.age");
    }

    [Fact]
    public async Task Article_NonCharacter_HasNoOverrides()
    {
        await _entities.SaveLoreAsync(new LoreData { Id = "pact", Name = "The Pact" });

        var article = await _rpc.ArticleAsync("lore", "pact");

        Assert.Empty(article.Overrides);
    }

    // ── AI summary layer (no extension host) ────────────────────────

    [Fact]
    public async Task Article_NoGenerator_NoSummary()
    {
        await _entities.SaveCharacterAsync(new CharacterData { Id = "hero", Name = "Aldric" });

        var article = await _rpc.ArticleAsync("character", "hero");

        Assert.False(article.GeneratorAvailable);
        Assert.Null(article.Generated);
        Assert.False(_rpc.GeneratorAvailable());
    }

    [Fact]
    public async Task Article_ShowsCachedSummary_StaleWhenInputChanged()
    {
        await _entities.SaveCharacterAsync(new CharacterData { Id = "hero", Name = "Aldric" });
        var cache = new WikiArticleCache(_workspace.Projects, _workspace.FileService);
        await cache.WriteAsync("hero", new WikiArticleCacheEntry
        {
            Summary = "Old summary.", InputHash = "STALEHASH", GeneratedAt = "2020-01-01T00:00:00Z"
        });

        var article = await _rpc.ArticleAsync("character", "hero");

        Assert.NotNull(article.Generated);
        Assert.Equal("Old summary.", article.Generated!.Summary);
        Assert.True(article.Generated.Stale);   // live dossier hash won't match "STALEHASH"
    }

    [Fact]
    public async Task Regenerate_NoHost_ReturnsNull()
    {
        await _entities.SaveCharacterAsync(new CharacterData { Id = "hero", Name = "Aldric" });
        Assert.Null(await _rpc.RegenerateAsync("character", "hero", CancellationToken.None));
    }

    [Fact]
    public async Task Article_UnknownId_Throws()
        => await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.ArticleAsync("character", "ghost"));

    [Fact]
    public async Task Article_UnknownType_Throws()
        => await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.ArticleAsync("dragon", "x"));

    // ── location / item / lore ──────────────────────────────────────

    [Fact]
    public async Task Article_Location_LeadDescriptionParentLink_AndNonCharacterPovNull()
    {
        await _entities.SaveLocationAsync(new LocationData { Id = "realm", Name = "Aldland" });
        await _entities.SaveLocationAsync(new LocationData
        {
            Id = "city", Name = "Harbour", Type = "City", Parent = "Aldland", Description = "A bustling port."
        });
        await AddSceneAsync("Ch1", "Arrival", ["city"], date: "2024-01-01");

        var article = await _rpc.ArticleAsync("location", "city");

        Assert.Equal("A bustling port.", article.Description);
        Assert.Equal("City", article.Lead.Primary);
        Assert.Equal("Aldland", article.Lead.Secondary);
        Assert.Equal("in", article.Lead.SecondaryConnector);
        var parent = article.Infobox.Fields.Single(f => f.LabelKey == "entityEditor.parentLocation");
        Assert.Equal("realm", parent.LinkEntityId);
        Assert.NotNull(article.Stats);
        Assert.Null(article.Stats!.PovSceneCount);   // non-character: no POV stat
        Assert.Empty(article.Contains);              // a leaf location contains nothing

        // The parent's article lists it back as a child.
        var realm = await _rpc.ArticleAsync("location", "realm");
        var child = Assert.Single(realm.Contains);
        Assert.Equal("Harbour", child.Name);
        Assert.Equal("city", child.EntityId);
        Assert.Equal("location", child.TypeKey);
    }

    [Fact]
    public async Task Article_ListsManualTimelineEventsNamingTheEntity()
    {
        await _entities.SaveCharacterAsync(new CharacterData { Id = "hero", Name = "Aldric" });
        await _entities.SaveLocationAsync(new LocationData { Id = "port", Name = "Harbour" });
        await _entities.SaveCharacterAsync(new CharacterData { Id = "solo", Name = "Nobody" });

        var timeline = _workspace.Projects.ProjectSettings.Timeline;
        timeline.ManualEvents.Add(new TimelineManualEvent
        {
            Id = "e1", Title = "Landfall", Date = "1043-03-02",
            Description = "The ship arrives.", Characters = { "Aldric" }
        });
        timeline.ManualEvents.Add(new TimelineManualEvent
        {
            Id = "e0", Title = "The Comet", Date = "1043-03-01", Locations = { "Harbour" }
        });
        timeline.ManualEvents.Add(new TimelineManualEvent
        {
            Id = "e2", Title = "Undated omen", Characters = { "Aldric" }
        });
        await _workspace.Projects.SaveProjectSettingsAsync();

        var hero = await _rpc.ArticleAsync("character", "hero");
        Assert.Equal(["Landfall", "Undated omen"], hero.Events.Select(e => e.Title));  // dated first
        Assert.Equal("The ship arrives.", hero.Events[0].Description);
        Assert.Null(hero.Events[1].Description);

        // A location named on an event gets it too; an unmentioned entity gets none.
        Assert.Equal("The Comet", Assert.Single((await _rpc.ArticleAsync("location", "port")).Events).Title);
        Assert.Empty((await _rpc.ArticleAsync("character", "solo")).Events);
    }

    [Fact]
    public async Task Article_SingleBook_DoesNotFlagMultipleBooks()
    {
        await _entities.SaveCharacterAsync(new CharacterData { Id = "hero", Name = "Aldric" });
        var article = await _rpc.ArticleAsync("character", "hero");
        Assert.False(article.MultipleBooks);
        Assert.NotEmpty(article.BookName);
    }

    [Fact]
    public async Task Article_ListsResearchLinkedToTheEntity()
    {
        await _entities.SaveCharacterAsync(new CharacterData { Id = "hero", Name = "Aldric" });
        await _entities.SaveCharacterAsync(new CharacterData { Id = "other", Name = "Mordre" });

        var library = new LibraryRpc(_workspace);
        await library.SaveResearchAsync(
            null, "Knightly orders", "Note", "Sworn brotherhoods.", [], ["hero"]);
        await library.SaveResearchAsync(null, "Unrelated", "Note", "Nothing.", [], []);

        var article = await _rpc.ArticleAsync("character", "hero");
        var item = Assert.Single(article.Research);
        Assert.Equal("Knightly orders", item.Title);
        Assert.Equal("Note", item.Type);

        Assert.Empty((await _rpc.ArticleAsync("character", "other")).Research);
    }

    [Fact]
    public async Task Article_Character_UsesStructuredBirthDateOverFreeTextAge()
    {
        await _entities.SaveCharacterAsync(new CharacterData
        {
            Id = "dated", Name = "Mira", Age = "30", AgeMode = "date", BirthDate = "1013-05-02"
        });
        // No age mode: a date typed straight into the age field is still labelled as one.
        await _entities.SaveCharacterAsync(new CharacterData
        {
            Id = "legacy", Name = "Corin", Age = "1020-01-01"
        });
        await _entities.SaveCharacterAsync(new CharacterData { Id = "plain", Name = "Bren", Age = "42" });

        var dated = await _rpc.ArticleAsync("character", "dated");
        var field = dated.Infobox.Fields.Single(f => f.LabelKey == "entityEditor.birthDate");
        Assert.Equal("1013-05-02", field.Value);           // structured field wins over "30"
        Assert.DoesNotContain(dated.Infobox.Fields, f => f.LabelKey == "entityEditor.age");

        var legacy = await _rpc.ArticleAsync("character", "legacy");
        Assert.Equal("1020-01-01",
            legacy.Infobox.Fields.Single(f => f.LabelKey == "entityEditor.birthDate").Value);

        var plain = await _rpc.ArticleAsync("character", "plain");
        Assert.Equal("42", plain.Infobox.Fields.Single(f => f.LabelKey == "entityEditor.age").Value);
    }

    [Fact]
    public async Task Article_NonCharacterTypes_CarryRelationships_AndItemOriginLinks()
    {
        await _entities.SaveLocationAsync(new LocationData { Id = "forge", Name = "Deepforge" });
        await _entities.SaveCharacterAsync(new CharacterData { Id = "hero", Name = "Aldric" });
        await _entities.SaveItemAsync(new ItemData
        {
            Id = "blade", Name = "Frostbite", Origin = "Deepforge",
            Relationships = { new EntityRelationship { Role = "Wielded by", Target = "Aldric" } }
        });
        await _entities.SaveLoreAsync(new LoreData
        {
            Id = "oath", Name = "Frost Oath",
            Relationships = { new EntityRelationship { Role = "Sworn at", Target = "Deepforge" } }
        });

        var item = await _rpc.ArticleAsync("item", "blade");
        var rel = Assert.Single(item.Relationships);
        Assert.Equal("Wielded by", rel.Role);
        Assert.Equal("hero", Assert.Single(rel.Targets).EntityId);
        // The origin names a known location, so it becomes a link.
        var origin = item.Infobox.Fields.Single(f => f.LabelKey == "entityEditor.origin");
        Assert.Equal("forge", origin.LinkEntityId);
        Assert.Equal("location", origin.LinkTypeKey);

        var lore = await _rpc.ArticleAsync("lore", "oath");
        Assert.Equal("Sworn at", Assert.Single(lore.Relationships).Role);

        // Reverse links now reach non-character sources too.
        var forge = await _rpc.ArticleAsync("location", "forge");
        Assert.Contains(forge.ReferencedBy, r => r.EntityId == "oath" && r.Role == "Sworn at");
    }

    [Fact]
    public async Task Article_Contains_SkipsUnresolvedParentsAndNonLocations()
    {
        await _entities.SaveLocationAsync(new LocationData { Id = "realm", Name = "Aldland" });
        await _entities.SaveLocationAsync(new LocationData { Id = "orphan", Name = "Hut", Parent = "Ghostland" });
        await _entities.SaveCharacterAsync(new CharacterData { Id = "hero", Name = "Aldric" });

        Assert.Empty((await _rpc.ArticleAsync("location", "realm")).Contains);
        Assert.Empty((await _rpc.ArticleAsync("character", "hero")).Contains);
    }

    [Fact]
    public async Task Article_Location_NoParent_AndUnresolvedParent()
    {
        await _entities.SaveLocationAsync(new LocationData { Id = "loose", Name = "Nowhere" });
        await _entities.SaveLocationAsync(new LocationData { Id = "orphan", Name = "Hut", Parent = "Ghostland" });

        var noParent = await _rpc.ArticleAsync("location", "loose");
        Assert.DoesNotContain(noParent.Infobox.Fields, f => f.LabelKey == "entityEditor.parentLocation");
        Assert.Null(noParent.Description);
        Assert.Null(noParent.Lead.Secondary);

        var orphan = await _rpc.ArticleAsync("location", "orphan");
        var parent = orphan.Infobox.Fields.Single(f => f.LabelKey == "entityEditor.parentLocation");
        Assert.Equal("Ghostland", parent.Value);
        Assert.Null(parent.LinkEntityId);
    }

    [Fact]
    public async Task Article_Item_ShowsTypeAndOrigin()
    {
        await _entities.SaveItemAsync(new ItemData
        {
            Id = "blade", Name = "Frostbite", Type = "Sword", Origin = "Forged in ice", Description = "A cold blade."
        });

        var article = await _rpc.ArticleAsync("item", "blade");

        Assert.Equal("A cold blade.", article.Description);
        Assert.Equal("Sword", article.Lead.Primary);
        Assert.Equal("Forged in ice", article.Lead.Secondary);
        Assert.Equal("from", article.Lead.SecondaryConnector);
        Assert.Contains(article.Infobox.Fields, f => f.LabelKey == "entityEditor.itemType" && f.Value == "Sword");
        Assert.Contains(article.Infobox.Fields, f => f.LabelKey == "entityEditor.origin");
    }

    // ── custom ──────────────────────────────────────────────────────

    [Fact]
    public async Task Article_Custom_FieldsRefRelationshipsAndLead()
    {
        await _entities.SaveCharacterAsync(new CharacterData { Id = "hero", Name = "Aldric" });
        await _entities.SaveCustomEntityTypeAsync(new CustomEntityTypeDefinition
        {
            TypeKey = "spell", DisplayName = "Spell",
            DefaultFields =
            {
                new CustomEntityFieldDefinition { Key = "element", DisplayName = "Element", Type = CustomPropertyType.String },
                new CustomEntityFieldDefinition { Key = "caster", DisplayName = "Caster", Type = CustomPropertyType.EntityRef }
            }
        });
        await _entities.SaveCustomEntityAsync(new CustomEntityData
        {
            Id = "fb", EntityTypeKey = "spell", Name = "Fireball",
            Fields = { ["element"] = "Fire", ["caster"] = "Aldric", ["undeclared"] = "Extra", ["blankField"] = "" },
            CustomProperties = { ["mana"] = "5", ["blank"] = "" },
            Relationships = { new EntityRelationship { Role = "Countered by", Target = "Aldric" } },
            Sections = { new EntitySection { Title = "Lore", Content = "<p>Ancient flame.</p>" } }
        });

        var article = await _rpc.ArticleAsync("spell", "fb");

        Assert.Equal("Spell", article.CustomTypeLabel);
        Assert.Equal("Spell", article.Lead.Primary);      // custom lead = type label
        Assert.Null(article.Lead.Secondary);
        Assert.Contains(article.Infobox.Fields, f => f.LiteralLabel == "Element" && f.Value == "Fire");
        Assert.Contains(article.Infobox.Fields, f => f.LiteralLabel == "undeclared");
        Assert.DoesNotContain(article.Infobox.Fields, f => f.LiteralLabel == "blankField");
        Assert.Contains(article.Infobox.Fields, f => f.LiteralLabel == "mana");
        Assert.DoesNotContain(article.Infobox.Fields, f => f.LiteralLabel == "blank");
        Assert.Contains(article.Relationships, r => r.Role == "Countered by" && r.Targets[0].EntityId == "hero");
        Assert.Contains(article.Relationships, r => r.Role == "Caster" && r.Targets[0].EntityId == "hero");
        Assert.Equal("Lore", article.Sections.Single().Title);
    }
}
