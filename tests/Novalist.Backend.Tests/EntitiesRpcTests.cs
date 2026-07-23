using System.Net;
using System.Text.Json;
using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class EntitiesRpcTests : IDisposable
{
    /// <summary>Stubs the HTTP transport so image-from-URL downloads never hit
    /// the network; returns the configured status and bytes.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public byte[] ContentBytes { get; set; } = [137, 80, 78, 71];
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(StatusCode)
            {
                Content = new ByteArrayContent(ContentBytes)
            });
        }
    }

    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly EntitiesRpc _rpc;

    public EntitiesRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-ent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "EntNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new EntitiesRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private EntityService Entities => new(_workspace.Projects);

    [Fact]
    public async Task List_Characters_ComposesNameAndSurname()
    {
        await Entities.SaveCharacterAsync(new CharacterData
        {
            Name = "Mira",
            Surname = "Frost",
            Role = "Protagonist",
            Images = [new EntityImage { Path = "Images/mira.png" }]
        });
        await Entities.SaveCharacterAsync(new CharacterData { Name = "Solo" });

        var list = await _rpc.ListAsync("character");

        var mira = list.Single(e => e.Name == "Mira Frost");
        Assert.Equal("Protagonist", mira.Detail);
        // The bare first name is exposed as an extra hover/mention target, and
        // is null when it already equals the composed display name.
        Assert.Equal("Mira", mira.FirstName);
        Assert.Null(list.Single(e => e.Name == "Solo").FirstName);
        // ImagePath is resolved to a project-root-relative path (book folder
        // prepended) so the renderer's project-rooted protocol can load it.
        Assert.EndsWith("/Images/mira.png", mira.ImagePath);
        Assert.NotEqual("Images/mira.png", mira.ImagePath);
        Assert.Contains(list, e => e.Name == "Solo" && e.ImagePath == null);
    }

    [Fact]
    public async Task List_EnrichesGroupGenderAndParent()
    {
        await Entities.SaveCharacterAsync(new CharacterData
        {
            Name = "Mira", Role = "Hero", Group = "Guild", Gender = "Female"
        });
        await Entities.SaveLocationAsync(new LocationData { Name = "Keep", Parent = "Realm" });

        var mira = (await _rpc.ListAsync("character")).Single(e => e.Name == "Mira");
        Assert.Equal("Guild", mira.Group);
        Assert.Equal("Female", mira.Gender);

        var keep = (await _rpc.ListAsync("location")).Single(e => e.Name == "Keep");
        Assert.Equal("Realm", keep.Parent);

        // Empty enrichment fields collapse to null.
        await Entities.SaveItemAsync(new ItemData { Name = "Plain" });
        var plain = (await _rpc.ListAsync("item")).Single(e => e.Name == "Plain");
        Assert.Null(plain.Group);
        Assert.Null(plain.Parent);
    }

    [Fact]
    public async Task SetRelationships_SyncsInverseAndLearnsPair()
    {
        var aliceId = (await _rpc.CreateAsync("character", "Alice")).GetProperty("id").GetString()!;
        var bobId = (await _rpc.CreateAsync("character", "Bob")).GetProperty("id").GetString()!;

        var result = await _rpc.SetRelationshipsAsync(aliceId,
        [
            new RelationshipEditRowDto("Mother", "Bob", "Son"),
            new RelationshipEditRowDto("Friend", "Nobody Here", "Friend"),
            // A row with no inverse role is stored but not synced.
            new RelationshipEditRowDto("Rival", "Bob", null)
        ]);
        Assert.Equal("Mother", result.GetProperty("relationships")[0].GetProperty("role").GetString());

        // Bob got the reciprocal "Son -> Alice".
        var bob = await _rpc.GetAsync("character", bobId);
        var bobRels = bob.GetProperty("relationships");
        Assert.Equal(1, bobRels.GetArrayLength());
        Assert.Equal("Son", bobRels[0].GetProperty("role").GetString());
        Assert.Equal("Alice", bobRels[0].GetProperty("target").GetString());

        // The pair is learned both ways.
        Assert.Equal("Son", _rpc.InverseRole("Mother"));
        Assert.Equal("Mother", _rpc.InverseRole("Son"));

        // Suggestions expose character names + known roles.
        var suggestions = await _rpc.RelationshipSuggestionsAsync();
        Assert.Contains("Bob", suggestions.CharacterNames);
        Assert.Contains("Mother", suggestions.Roles);

        // Re-running does not duplicate the reciprocal.
        await _rpc.SetRelationshipsAsync(aliceId, [new RelationshipEditRowDto("Mother", "Bob", "Son")]);
        Assert.Equal(1, (await _rpc.GetAsync("character", bobId)).GetProperty("relationships").GetArrayLength());

        Assert.Equal(string.Empty, _rpc.InverseRole("Unheard"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.SetRelationshipsAsync("missing", []));
    }

    [Fact]
    public async Task MoveToWorldBibleAndBack()
    {
        var created = await _rpc.CreateAsync("character", "Wanderer");
        var id = created.GetProperty("id").GetString()!;
        Assert.DoesNotContain(await _rpc.ListAsync("character"), e => e.Id == id && e.IsWorldBible);

        await _rpc.MoveToWorldBibleAsync("character", id);
        Assert.Contains(await _rpc.ListAsync("character"), e => e.Id == id && e.IsWorldBible);

        await _rpc.MoveToBookAsync("character", id);
        Assert.Contains(await _rpc.ListAsync("character"), e => e.Id == id && !e.IsWorldBible);

        // Every built-in type parses and round-trips through the world bible.
        foreach (var type in new[] { "location", "item", "lore" })
        {
            var madeId = (await _rpc.CreateAsync(type, $"WB-{type}")).GetProperty("id").GetString()!;
            await _rpc.MoveToWorldBibleAsync(type, madeId);
            Assert.Contains(await _rpc.ListAsync(type), e => e.Id == madeId && e.IsWorldBible);
            await _rpc.MoveToBookAsync(type, madeId);
            Assert.Contains(await _rpc.ListAsync(type), e => e.Id == madeId && !e.IsWorldBible);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.MoveToWorldBibleAsync("dragon", id));
    }

    [Fact]
    public async Task List_LocationsItemsLore_UseDescriptions()
    {
        await Entities.SaveLocationAsync(new LocationData { Name = "Eiswall", Description = "A wall of ice" });
        await Entities.SaveItemAsync(new ItemData { Name = "Dolch", Description = "Sharp" });
        await Entities.SaveLoreAsync(new LoreData { Name = "Der Schwur", Description = "Old oath" });

        Assert.Equal("A wall of ice", (await _rpc.ListAsync("location")).Single().Detail);
        Assert.Equal("Sharp", (await _rpc.ListAsync("item")).Single().Detail);
        Assert.Equal("Old oath", (await _rpc.ListAsync("lore")).Single().Detail);
    }

    [Fact]
    public async Task List_UnknownType_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.ListAsync("dragon"));
    }

    [Fact]
    public async Task Get_ReturnsFullRecord_ForEachType_AndThrowsForUnknown()
    {
        var character = new CharacterData { Name = "Mira", EyeColor = "grey" };
        await Entities.SaveCharacterAsync(character);
        var location = new LocationData { Name = "Eiswall" };
        await Entities.SaveLocationAsync(location);
        var item = new ItemData { Name = "Dolch" };
        await Entities.SaveItemAsync(item);
        var lore = new LoreData { Name = "Schwur" };
        await Entities.SaveLoreAsync(lore);

        Assert.Equal("grey", (await _rpc.GetAsync("character", character.Id)).GetProperty("eyeColor").GetString());
        Assert.Equal("Eiswall", (await _rpc.GetAsync("location", location.Id)).GetProperty("name").GetString());
        Assert.Equal("Dolch", (await _rpc.GetAsync("item", item.Id)).GetProperty("name").GetString());
        Assert.Equal("Schwur", (await _rpc.GetAsync("lore", lore.Id)).GetProperty("name").GetString());

        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.GetAsync("character", "missing"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.GetAsync("dragon", "x"));
    }

    [Fact]
    public async Task CreateUpdateDelete_AllTypes_RoundTrip()
    {
        foreach (var type in new[] { "character", "location", "item", "lore" })
        {
            var created = await _rpc.CreateAsync(type, $"New {type}");
            var id = created.GetProperty("id").GetString()!;
            Assert.Equal($"New {type}", created.GetProperty("name").GetString());

            var updated = await _rpc.UpdateAsync(type, id, new Dictionary<string, string>
            {
                ["name"] = $"Renamed {type}",
                ["notAProperty"] = "ignored"
            });
            Assert.Equal($"Renamed {type}", updated.GetProperty("name").GetString());
            Assert.Contains(await _rpc.ListAsync(type), e => e.Name.StartsWith("Renamed"));

            await _rpc.DeleteAsync(type, id, isWorldBible: false);
            Assert.DoesNotContain(await _rpc.ListAsync(type), e => e.Id == id);
        }
    }

    [Fact]
    public async Task SaveCustomType_GeneratesKeyPluralAndFields()
    {
        var types = await _rpc.SaveCustomTypeAsync(new CustomTypeSpecDto(
            TypeKey: null,
            DisplayName: " Magic System ",
            DisplayNamePlural: null,
            Fields:
            [
                new CustomFieldSpecDto(null, "Power Level", "Int", "1", null, Required: true),
                new CustomFieldSpecDto("Rarity", "Rarity", "Enum", null, ["Common", "Rare"], Required: false)
            ],
            IncludeImages: true,
            IncludeRelationships: false,
            IncludeSections: true));

        var def = Assert.Single(types);
        Assert.Equal("magic_system", def.TypeKey);
        Assert.Equal("Magic System", def.DisplayName);
        Assert.Equal("Magic Systems", def.DisplayNamePlural);
        Assert.Equal("magic_system", def.FolderName);
        Assert.True(def.IsUserSource);
        Assert.Equal("PowerLevel", def.DefaultFields[0].Key);
        Assert.True(def.DefaultFields[0].Required);
        Assert.Equal(["Common", "Rare"], def.DefaultFields[1].EnumOptions!);
        Assert.True(def.Features.IncludeImages);
        Assert.False(def.Features.IncludeRelationships);

        var edited = await _rpc.SaveCustomTypeAsync(new CustomTypeSpecDto(
            "magic_system", "Arcana", "Arcana", Fields: null,
            IncludeImages: false, IncludeRelationships: true, IncludeSections: false));
        var editedDef = Assert.Single(edited);
        Assert.Equal("magic_system", editedDef.TypeKey);
        Assert.Equal("Arcana", editedDef.DisplayName);
        Assert.Empty(editedDef.DefaultFields);

        var afterDelete = await _rpc.DeleteCustomTypeAsync("magic_system");
        Assert.Empty(afterDelete);
    }

    [Fact]
    public async Task SaveCustomType_BlankNameFallsBackToGeneratedKey()
    {
        var types = await _rpc.SaveCustomTypeAsync(new CustomTypeSpecDto(
            null, "  ", null, null, true, true, true));
        Assert.StartsWith("custom_", Assert.Single(types).TypeKey);
    }

    [Fact]
    public async Task CustomTypes_ExtensionSourceIsProtected()
    {
        await new Novalist.Core.Services.EntityService(_workspace.Projects)
            .SaveCustomEntityTypeAsync(new CustomEntityTypeDefinition
            {
                TypeKey = "ext_type",
                DisplayName = "Ext",
                DisplayNamePlural = "Exts",
                Source = "ext.some-extension"
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.SaveCustomTypeAsync(
            new CustomTypeSpecDto("ext_type", "Ext", null, null, true, true, true)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.DeleteCustomTypeAsync("ext_type"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.DeleteCustomTypeAsync("nope"));
    }

    [Fact]
    public async Task CustomEntityTypes_FullCrud()
    {
        await new Novalist.Core.Services.EntityService(_workspace.Projects)
            .SaveCustomEntityTypeAsync(new CustomEntityTypeDefinition
            {
                TypeKey = "faction",
                DisplayName = "Faction",
                DisplayNamePlural = "Factions",
                DefaultFields =
                    [new CustomEntityFieldDefinition { Key = "Motto", DefaultValue = "None" }]
            });

        var types = _rpc.GetCustomTypes();
        Assert.Single(types, t => t.TypeKey == "faction");

        var created = await _rpc.CreateAsync("faction", "Nordwacht");
        var id = created.GetProperty("id").GetString()!;
        Assert.Equal("None", created.GetProperty("fields").GetProperty("Motto").GetString());

        var list = await _rpc.ListAsync("faction");
        Assert.Single(list, e => e.Name == "Nordwacht");

        var updated = await _rpc.UpdateAsync("faction", id, new Dictionary<string, string>
        {
            ["name"] = "Suedwacht",
            ["Motto"] = "Endure"
        });
        Assert.Equal("Suedwacht", updated.GetProperty("name").GetString());
        Assert.Equal("Endure", updated.GetProperty("fields").GetProperty("Motto").GetString());

        var fetched = await _rpc.GetAsync("faction", id);
        Assert.Equal("Suedwacht", fetched.GetProperty("name").GetString());
        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.GetAsync("faction", "missing"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.UpdateAsync("faction", "missing", new Dictionary<string, string>()));

        await _rpc.DeleteAsync("faction", id, isWorldBible: false);
        Assert.Empty(await _rpc.ListAsync("faction"));
    }

    [Fact]
    public async Task CreateWithTemplate_AppliesFieldsPropsAndSections()
    {
        var book = _workspace.Projects.ActiveBook!;
        book.CharacterTemplates.Add(new CharacterTemplate
        {
            Id = "hero",
            Name = "Hero",
            Fields = [new TemplateField { Key = "role", DefaultValue = "Protagonist" }],
            CustomPropertyDefs =
                [new CustomPropertyDefinition { Key = "Allegiance", DefaultValue = "North" }],
            Sections = [new TemplateSection { Title = "Backstory", DefaultContent = "Unknown." }]
        });

        var templates = _rpc.GetTemplates("character");
        Assert.Single(templates, t => t.Name == "Hero");
        Assert.Empty(_rpc.GetTemplates("location"));
        Assert.Empty(_rpc.GetTemplates("item"));
        Assert.Empty(_rpc.GetTemplates("lore"));
        Assert.Empty(_rpc.GetTemplates("dragon"));

        var created = await _rpc.CreateAsync("character", "Mira", "hero");
        Assert.Equal("Protagonist", created.GetProperty("role").GetString());
        Assert.Equal("hero", created.GetProperty("templateId").GetString());
        Assert.Equal("North", created.GetProperty("customProperties").GetProperty("Allegiance").GetString());
        Assert.Equal("Backstory", created.GetProperty("sections")[0].GetProperty("title").GetString());

        // Unknown template id: entity still created, only TemplateId set.
        var plain = await _rpc.CreateAsync("character", "Solo", "missing");
        Assert.Equal("missing", plain.GetProperty("templateId").GetString());

        // Every type's template-lookup arm executes.
        foreach (var type in new[] { "location", "item", "lore" })
        {
            var typed = await _rpc.CreateAsync(type, "Templated", "any-id");
            Assert.Equal("any-id", typed.GetProperty("templateId").GetString());
        }
    }

    [Fact]
    public async Task Overrides_SetDiffAndRemove()
    {
        var created = await _rpc.CreateAsync("character", "Mira");
        var id = created.GetProperty("id").GetString()!;

        var withOverride = await _rpc.SetOverrideAsync(id, "ch-1", "Scene One",
            new Dictionary<string, string> { ["role"] = "Deserter", ["eyeColor"] = "" });
        var over = withOverride.GetProperty("chapterOverrides")[0];
        Assert.Equal("Deserter", over.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null,
            over.TryGetProperty("eyeColor", out var eye) ? eye.ValueKind : JsonValueKind.Null);

        // Updating the same scope reuses the entry rather than duplicating.
        var updated = await _rpc.SetOverrideAsync(id, "ch-1", "Scene One",
            new Dictionary<string, string> { ["role"] = "Captain" });
        Assert.Equal(1, updated.GetProperty("chapterOverrides").GetArrayLength());
        Assert.Equal("Captain",
            updated.GetProperty("chapterOverrides")[0].GetProperty("role").GetString());

        var removed = await _rpc.RemoveOverrideAsync(id, "ch-1", "Scene One");
        Assert.Equal(0, removed.GetProperty("chapterOverrides").GetArrayLength());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.SetOverrideAsync("missing", "ch-1", null, new Dictionary<string, string>()));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.RemoveOverrideAsync("missing", "ch-1", null));
    }

    [Fact]
    public async Task Overrides_StoreCustomPropertyDiff_AndClearOnEmpty()
    {
        var created = await _rpc.CreateAsync("character", "Mira");
        var id = created.GetProperty("id").GetString()!;

        // Blank custom-property values are dropped; only real overrides persist.
        var withProps = await _rpc.SetOverrideAsync(id, "ch-1", null,
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["Rank"] = "Warlord", ["Mood"] = "" });
        var props = withProps.GetProperty("chapterOverrides")[0].GetProperty("customProperties");
        Assert.Equal("Warlord", props.GetProperty("Rank").GetString());
        Assert.False(props.TryGetProperty("Mood", out _));

        // An all-blank map clears the override custom properties (inherit base).
        var cleared = await _rpc.SetOverrideAsync(id, "ch-1", null,
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["Rank"] = "" });
        var entry = cleared.GetProperty("chapterOverrides")[0];
        Assert.Equal(JsonValueKind.Null,
            entry.TryGetProperty("customProperties", out var cp) ? cp.ValueKind : JsonValueKind.Null);

        // Omitting the map entirely leaves the string-field override untouched.
        var stringOnly = await _rpc.SetOverrideAsync(id, "ch-1", null,
            new Dictionary<string, string> { ["role"] = "Captain" });
        Assert.Equal("Captain",
            stringOnly.GetProperty("chapterOverrides")[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task OverrideImages_SetReplacesClearInherits_AndPeekResolves()
    {
        var mira = new CharacterData
        {
            Name = "Mira",
            Images = { new EntityImage { Name = "base", Path = "Images/base.png" } }
        };
        await Entities.SaveCharacterAsync(mira);
        var id = (await Entities.LoadCharactersAsync()).Single().Id;

        // A non-null list replaces the base images for the scope.
        var set = await _rpc.SetOverrideImagesAsync(id, "ch-1", "Scene One",
            [new EntityImageDto("scene", "Images/scene.png")]);
        var ovr = set.GetProperty("chapterOverrides")[0];
        Assert.Equal("scene", ovr.GetProperty("images")[0].GetProperty("name").GetString());
        // The override image carries a resolved url for inline display.
        Assert.EndsWith("/Images/scene.png", ovr.GetProperty("images")[0].GetProperty("url").GetString());

        // The peek for that scope shows the overridden image, not the base one.
        var peek = await _rpc.PeekAsync("character", id, "ch-1", "Chapter One", "Scene One");
        Assert.EndsWith("/Images/scene.png", Assert.Single(peek.Images).Url);

        // An empty list is still an override (the scope shows no images).
        var emptied = await _rpc.SetOverrideImagesAsync(id, "ch-1", "Scene One", []);
        Assert.Empty(emptied.GetProperty("chapterOverrides")[0].GetProperty("images").EnumerateArray());
        var emptyPeek = await _rpc.PeekAsync("character", id, "ch-1", "Chapter One", "Scene One");
        Assert.Empty(emptyPeek.Images);

        // Null resets the scope to inherit the base images.
        var inherited = await _rpc.SetOverrideImagesAsync(id, "ch-1", "Scene One", null);
        Assert.Equal(JsonValueKind.Null,
            inherited.GetProperty("chapterOverrides")[0].GetProperty("images").ValueKind);
        var inheritPeek = await _rpc.PeekAsync("character", id, "ch-1", "Chapter One", "Scene One");
        Assert.EndsWith("/Images/base.png", Assert.Single(inheritPeek.Images).Url);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.SetOverrideImagesAsync("missing", "ch-1", null, null));
    }

    [Fact]
    public async Task AddOverrideImage_ImportsSeedsFromBase_AndAppends()
    {
        var mira = new CharacterData
        {
            Name = "Mira",
            Images = { new EntityImage { Name = "base", Path = "Images/base.png" } }
        };
        await Entities.SaveCharacterAsync(mira);
        var id = (await Entities.LoadCharactersAsync()).Single().Id;

        var source = Path.Combine(_root, "portrait.png");
        await File.WriteAllBytesAsync(source, [137, 80, 78, 71]);

        // First divergence seeds from the base list, then appends the import.
        var added = await _rpc.AddOverrideImageAsync(id, "ch-1", "Scene One", source);
        var images = added.GetProperty("chapterOverrides")[0].GetProperty("images");
        Assert.Equal(2, images.GetArrayLength());
        Assert.Equal("base", images[0].GetProperty("name").GetString());
        Assert.Equal("portrait", images[1].GetProperty("name").GetString());

        // A second add appends onto the now-existing override list.
        var source2 = Path.Combine(_root, "sketch.png");
        await File.WriteAllBytesAsync(source2, [137, 80, 78, 71]);
        var again = await _rpc.AddOverrideImageAsync(id, "ch-1", "Scene One", source2);
        Assert.Equal(3, again.GetProperty("chapterOverrides")[0].GetProperty("images").GetArrayLength());
    }

    [Fact]
    public async Task AddOverrideImageFromUrl_DownloadsAndAppends()
    {
        var handler = new StubHandler();
        var rpc = new EntitiesRpc(_workspace, new HttpClient(handler));
        var id = (await rpc.CreateAsync("character", "Mira")).GetProperty("id").GetString()!;

        var updated = await rpc.AddOverrideImageFromUrlAsync(
            id, "ch-1", null, "https://img.test/pics/dragon.png");
        var image = updated.GetProperty("chapterOverrides")[0].GetProperty("images")[0];
        Assert.Equal("dragon", image.GetProperty("name").GetString());
        Assert.EndsWith("dragon.png", image.GetProperty("path").GetString());
    }

    [Fact]
    public async Task OverrideRelationships_SetReplacesClearInherits_AndPeekResolves()
    {
        var mira = new CharacterData
        {
            Name = "Mira",
            Relationships = { new EntityRelationship { Role = "Sister", Target = "Nyla" } }
        };
        await Entities.SaveCharacterAsync(mira);
        await Entities.SaveCharacterAsync(new CharacterData { Name = "Rook" });
        var id = (await Entities.LoadCharactersAsync()).Single(c => c.Name == "Mira").Id;

        // A list replaces the base relationships for the scope; blank rows drop.
        var set = await _rpc.SetOverrideRelationshipsAsync(id, "ch-1", "Scene One",
        [
            new RelationshipRowDto("Enemy", "Rook"),
            new RelationshipRowDto("", "")
        ]);
        var rels = set.GetProperty("chapterOverrides")[0].GetProperty("relationships");
        Assert.Equal(1, rels.GetArrayLength());
        Assert.Equal("Enemy", rels[0].GetProperty("role").GetString());

        var peek = await _rpc.PeekAsync("character", id, "ch-1", "Chapter One", "Scene One");
        var rel = Assert.Single(peek.Relationships);
        Assert.Equal("Enemy", rel.Role);
        Assert.Equal("Rook", rel.Targets[0].Name);

        // Empty list = override to no relationships.
        var emptied = await _rpc.SetOverrideRelationshipsAsync(id, "ch-1", "Scene One", []);
        Assert.Empty(emptied.GetProperty("chapterOverrides")[0].GetProperty("relationships").EnumerateArray());
        Assert.Empty((await _rpc.PeekAsync("character", id, "ch-1", "Chapter One", "Scene One")).Relationships);

        // Null resets to inherit the base relationships.
        var inherited = await _rpc.SetOverrideRelationshipsAsync(id, "ch-1", "Scene One", null);
        Assert.Equal(JsonValueKind.Null,
            inherited.GetProperty("chapterOverrides")[0].GetProperty("relationships").ValueKind);
        Assert.Equal("Sister",
            Assert.Single((await _rpc.PeekAsync("character", id, "ch-1", "Chapter One", "Scene One")).Relationships).Role);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.SetOverrideRelationshipsAsync("missing", "ch-1", null, null));
    }

    [Fact]
    public async Task OverrideSections_SetReplacesClearInherits_AndPeekResolves()
    {
        var mira = new CharacterData
        {
            Name = "Mira",
            Sections = { new EntitySection { Title = "Backstory", Content = "Base tale" } }
        };
        await Entities.SaveCharacterAsync(mira);
        var id = (await Entities.LoadCharactersAsync()).Single().Id;

        var set = await _rpc.SetOverrideSectionsAsync(id, "ch-1", "Scene One",
            [new EntitySectionDto("Wounded", "Injured in the raid")]);
        var sections = set.GetProperty("chapterOverrides")[0].GetProperty("sections");
        Assert.Equal("Wounded", sections[0].GetProperty("title").GetString());

        var peek = await _rpc.PeekAsync("character", id, "ch-1", "Chapter One", "Scene One");
        var section = Assert.Single(peek.Sections);
        Assert.Equal("Wounded", section.Title);
        Assert.Equal("Injured in the raid", section.Content);

        // Empty list = override to no sections.
        var emptied = await _rpc.SetOverrideSectionsAsync(id, "ch-1", "Scene One", []);
        Assert.Empty(emptied.GetProperty("chapterOverrides")[0].GetProperty("sections").EnumerateArray());
        Assert.Empty((await _rpc.PeekAsync("character", id, "ch-1", "Chapter One", "Scene One")).Sections);

        // Null resets to inherit the base sections.
        var inherited = await _rpc.SetOverrideSectionsAsync(id, "ch-1", "Scene One", null);
        Assert.Equal(JsonValueKind.Null,
            inherited.GetProperty("chapterOverrides")[0].GetProperty("sections").ValueKind);
        Assert.Equal("Backstory",
            Assert.Single((await _rpc.PeekAsync("character", id, "ch-1", "Chapter One", "Scene One")).Sections).Title);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.SetOverrideSectionsAsync("missing", "ch-1", null, null));
    }

    [Fact]
    public async Task CustomProps_SetTypedAndRemove_WithTemplateDefs()
    {
        var book = _workspace.Projects.ActiveBook!;
        book.CharacterTemplates.Add(new CharacterTemplate
        {
            Id = "tpl1",
            Name = "Hero",
            CustomPropertyDefs =
            [
                new CustomPropertyDefinition
                {
                    Key = "Allegiance",
                    Type = CustomPropertyType.Enum,
                    EnumOptions = ["North", "South"]
                }
            ]
        });
        var created = await _rpc.CreateAsync("character", "Mira");
        var id = created.GetProperty("id").GetString()!;
        await _rpc.UpdateAsync("character", id, new Dictionary<string, string> { ["templateId"] = "tpl1" });

        var props = await _rpc.SetCustomPropAsync("character", id, "Allegiance", "North");
        var allegiance = props.Single();
        Assert.Equal("Enum", allegiance.PropType);
        Assert.Contains("South", allegiance.EnumOptions);

        var untyped = await _rpc.SetCustomPropAsync("character", id, "Motto", "Winter endures");
        Assert.Equal("String", untyped.Single(p => p.Key == "Motto").PropType);

        var removed = await _rpc.SetCustomPropAsync("character", id, "Motto", null);
        Assert.DoesNotContain(removed, p => p.Key == "Motto");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.GetCustomPropsAsync("dragon", id));
    }

    [Fact]
    public async Task CustomProps_OtherTypes_ResolveTemplateDefs()
    {
        var book = _workspace.Projects.ActiveBook!;
        book.LocationTemplates.Add(new LocationTemplate { Id = "lt", Name = "L" });
        book.ItemTemplates.Add(new ItemTemplate { Id = "it", Name = "I" });
        book.LoreTemplates.Add(new LoreTemplate { Id = "lo", Name = "O" });
        var templateIds = new Dictionary<string, string>
        {
            ["location"] = "lt",
            ["item"] = "it",
            ["lore"] = "lo"
        };

        foreach (var (type, templateId) in templateIds)
        {
            var created = await _rpc.CreateAsync(type, "Thing");
            var id = created.GetProperty("id").GetString()!;
            await _rpc.UpdateAsync(type, id, new Dictionary<string, string> { ["templateId"] = templateId });
            var props = await _rpc.SetCustomPropAsync(type, id, "Origin", "Old");
            Assert.Equal("String", props.Single().PropType);
        }
    }

    [Fact]
    public async Task Images_AddFromGalleryAndImport_ThenRemove()
    {
        var created = await _rpc.CreateAsync("character", "Mira");
        var id = created.GetProperty("id").GetString()!;

        var source = Path.Combine(_root, "portrait.png");
        File.WriteAllBytes(source, [137, 80, 78, 71]);
        var imported = await _rpc.AddImageAsync("character", id, source, import: true);
        var importedImage = imported.GetProperty("images")[0];
        var importedPath = importedImage.GetProperty("path").GetString()!;
        Assert.NotEqual(source, importedPath);
        // Each image also carries a resolved project-root-relative url for display.
        var importedUrl = importedImage.GetProperty("url").GetString()!;
        Assert.EndsWith(importedPath, importedUrl);
        Assert.NotEqual(importedPath, importedUrl);

        var addedExisting = await _rpc.AddImageAsync("character", id, importedPath, import: false);
        Assert.Equal(2, addedExisting.GetProperty("images").GetArrayLength());

        var removed = await _rpc.RemoveImageAsync("character", id, importedPath);
        Assert.Equal(0, removed.GetProperty("images").GetArrayLength());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.AddImageAsync("dragon", id, source, false));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.RemoveImageAsync("character", "missing", "x"));
    }

    [Fact]
    public async Task Images_MutateOnAllTypes()
    {
        foreach (var type in new[] { "location", "item", "lore" })
        {
            var created = await _rpc.CreateAsync(type, "Thing");
            var id = created.GetProperty("id").GetString()!;
            var updated = await _rpc.AddImageAsync(type, id, "Images/x.png", import: false);
            Assert.Equal(1, updated.GetProperty("images").GetArrayLength());
        }
    }

    [Fact]
    public async Task RenameImage_SetsNameKeepsPath_AndGuards()
    {
        var id = (await _rpc.CreateAsync("character", "Mira")).GetProperty("id").GetString()!;
        var added = await _rpc.AddImageAsync("character", id, "Images/mira.png", import: false);
        Assert.Equal("mira", added.GetProperty("images")[0].GetProperty("name").GetString());

        var renamed = await _rpc.RenameImageAsync("character", id, "Images/mira.png", "Portrait");
        var image = renamed.GetProperty("images")[0];
        Assert.Equal("Portrait", image.GetProperty("name").GetString());
        // The stored path (what add/remove match on) is untouched by a rename.
        Assert.Equal("Images/mira.png", image.GetProperty("path").GetString());

        // A path that matches nothing is a silent no-op, not an error.
        var untouched = await _rpc.RenameImageAsync("character", id, "Images/none.png", "X");
        Assert.Equal("Portrait", untouched.GetProperty("images")[0].GetProperty("name").GetString());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.RenameImageAsync("dragon", id, "x", "y"));
    }

    [Fact]
    public async Task ReplaceImage_SwapsPath_AndFillsEmptyName()
    {
        var id = (await _rpc.CreateAsync("character", "Mira")).GetProperty("id").GetString()!;
        await _rpc.AddImageAsync("character", id, "Images/a.png", import: false);

        // A named image keeps its name; only the stored path swaps.
        var swapped = await _rpc.ReplaceImageAsync("character", id, "Images/a.png", "Images/b.png");
        var image = swapped.GetProperty("images")[0];
        Assert.Equal("a", image.GetProperty("name").GetString());
        Assert.Equal("Images/b.png", image.GetProperty("path").GetString());

        // Emptying the name then swapping fills it from the new file name.
        await _rpc.RenameImageAsync("character", id, "Images/b.png", "");
        var refilled = await _rpc.ReplaceImageAsync("character", id, "Images/b.png", "Images/c.png");
        var filled = refilled.GetProperty("images")[0];
        Assert.Equal("c", filled.GetProperty("name").GetString());
        Assert.Equal("Images/c.png", filled.GetProperty("path").GetString());

        // No match is a no-op; an unknown entity throws.
        var noop = await _rpc.ReplaceImageAsync("character", id, "Images/zzz.png", "Images/d.png");
        Assert.Equal("Images/c.png", noop.GetProperty("images")[0].GetProperty("path").GetString());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.ReplaceImageAsync("character", "missing", "x", "y"));
    }

    [Fact]
    public async Task AddImageFromUrl_DownloadsImportsAndAttaches()
    {
        var handler = new StubHandler();
        var rpc = new EntitiesRpc(_workspace, new HttpClient(handler));
        var id = (await rpc.CreateAsync("character", "Mira")).GetProperty("id").GetString()!;

        var updated = await rpc.AddImageFromUrlAsync("character", id, "https://img.test/pics/dragon.png");
        var image = updated.GetProperty("images")[0];
        Assert.Equal("dragon", image.GetProperty("name").GetString());
        Assert.EndsWith("dragon.png", image.GetProperty("path").GetString());
        Assert.Equal("https://img.test/pics/dragon.png", handler.LastRequestUri!.ToString());

        // A URL segment without an image extension gets one appended.
        handler.ContentBytes = [1, 2, 3, 4, 5];
        var second = await rpc.AddImageFromUrlAsync("character", id, "https://img.test/gallery/portrait");
        var portrait = second.GetProperty("images")[1];
        Assert.Equal("portrait", portrait.GetProperty("name").GetString());
        Assert.EndsWith("portrait.png", portrait.GetProperty("path").GetString());
    }

    [Fact]
    public async Task AddImageFromUrl_FailedRequest_ThrowsCleanly()
    {
        var handler = new StubHandler { StatusCode = HttpStatusCode.NotFound };
        var rpc = new EntitiesRpc(_workspace, new HttpClient(handler));
        var id = (await rpc.CreateAsync("character", "Mira")).GetProperty("id").GetString()!;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => rpc.AddImageFromUrlAsync("character", id, "https://img.test/missing.png"));
    }

    [Fact]
    public async Task List_ExposesAliases_ForMentionAutocomplete()
    {
        await Entities.SaveCharacterAsync(new CharacterData
        {
            Name = "Mira", Surname = "Frost", Aliases = ["Die Klinge", "Nordwind"]
        });
        await Entities.SaveCharacterAsync(new CharacterData { Name = "Plain" });

        var list = await _rpc.ListAsync("character");
        Assert.Equal(new[] { "Die Klinge", "Nordwind" },
            list.Single(e => e.Name == "Mira Frost").Aliases);
        Assert.Empty(list.Single(e => e.Name == "Plain").Aliases);

        // Locations expose aliases too (item/lore/custom arms share the code path).
        await Entities.SaveLocationAsync(new LocationData { Name = "Keep", Aliases = ["Fortress"] });
        Assert.Equal(new[] { "Fortress" },
            (await _rpc.ListAsync("location")).Single(e => e.Name == "Keep").Aliases);
    }

    [Fact]
    public async Task UpdateLists_AliasesSectionsRelationships_Persist()
    {
        var created = await _rpc.CreateAsync("character", "Mira");
        var id = created.GetProperty("id").GetString()!;

        var updated = await _rpc.UpdateListsAsync(
            "character",
            id,
            ["Die Klinge", "", "Nordwind"],
            [new EntitySectionDto("Backstory", "Born in the ice.")],
            [new RelationshipRowDto("Mutter", "Lena Frost"), new RelationshipRowDto("", "")]);

        var aliases = updated.GetProperty("aliases").EnumerateArray().Select(a => a.GetString()).ToArray();
        Assert.Equal(new[] { "Die Klinge", "Nordwind" }, aliases);
        Assert.Equal("Backstory", updated.GetProperty("sections")[0].GetProperty("title").GetString());
        Assert.Single(updated.GetProperty("relationships").EnumerateArray());

        var location = await _rpc.CreateAsync("location", "Eiswall");
        var locationUpdated = await _rpc.UpdateListsAsync(
            "location", location.GetProperty("id").GetString()!, ["Wall"], null, null);
        Assert.Equal("Wall", locationUpdated.GetProperty("aliases")[0].GetString());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.UpdateListsAsync("dragon", "x", null, null, null));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.UpdateListsAsync("item", "missing", null, null, null));
    }

    [Fact]
    public async Task UpdateLists_ItemAndLore_SectionsPersist()
    {
        foreach (var type in new[] { "item", "lore" })
        {
            var created = await _rpc.CreateAsync(type, "Thing");
            var updated = await _rpc.UpdateListsAsync(
                type, created.GetProperty("id").GetString()!, null,
                [new EntitySectionDto("Notes", "Old")], null);
            Assert.Equal("Notes", updated.GetProperty("sections")[0].GetProperty("title").GetString());
        }
    }

    [Fact]
    public async Task Peek_SurfacesCachedChapterAnalysisFindingsForTheEntity()
    {
        var hero = await _rpc.CreateAsync("character", "Amy");
        var heroId = hero.GetProperty("id").GetString()!;
        await _rpc.UpdateAsync("character", heroId, new Dictionary<string, string>
        {
            ["surname"] = "Calder"
        });
        var other = await _rpc.CreateAsync("character", "Dana Harrow");

        // Shape mirrors what a chapter analysis stores: chapter -> scene -> findings,
        // each naming the entity it is about.
        _workspace.Projects.ProjectSettings.ChapterAnalysis = new()
        {
            ["ch-1"] = new Novalist.Sdk.Models.ChapterAnalysisResult
            {
                Scenes = new()
                {
                    ["s1"] = new Novalist.Sdk.Models.SceneAnalysisResult
                    {
                        Findings =
                        [
                            new() { Type = "reference", Title = "Amy meets Mina",
                                    Description = "Amy interacts with Mina.",
                                    Excerpt = "Amy trifft Mina Bryton", EntityName = "Amy Calder" },
                            new() { Type = "reference", Title = "About Dana",
                                    Description = "Not this entity.", EntityName = "Dana Harrow" },
                            new() { Type = "reference", Title = "Unattributed", EntityName = "" },
                            // Per-scene stats are not a remark about the entity.
                            new() { Type = "scene_stats", Title = "Stats", EntityName = "Amy Calder" }
                        ]
                    }
                }
            }
        };

        var peek = await _rpc.PeekAsync("character", heroId, "ch-1", "Chapter One", "Scene One");
        var finding = Assert.Single(peek.AiFindings!);   // scene_stats excluded
        Assert.Equal("Amy meets Mina", finding.Title);
        Assert.Equal("reference", finding.Type);
        Assert.Equal("Amy trifft Mina Bryton", finding.Excerpt);

        // Findings for a different entity, and unattributed ones, stay out.
        Assert.Null((await _rpc.PeekAsync("character", other.GetProperty("id").GetString()!,
            "ch-2", null, null)).AiFindings);
    }

    [Fact]
    public async Task Peek_NoAnalysisOrNoChapter_HasNoFindings()
    {
        var created = await _rpc.CreateAsync("character", "Mira");
        var id = created.GetProperty("id").GetString()!;

        // No analysis stored at all.
        Assert.Null((await _rpc.PeekAsync("character", id, "ch-1", null, null)).AiFindings);

        // Analysis exists, but the peek carries no chapter scope.
        _workspace.Projects.ProjectSettings.ChapterAnalysis = new()
        {
            ["ch-1"] = new Novalist.Sdk.Models.ChapterAnalysisResult
            {
                Scenes = new()
                {
                    ["s1"] = new Novalist.Sdk.Models.SceneAnalysisResult
                    {
                        Findings = [new() { Title = "x", EntityName = "Mira" }]
                    }
                }
            }
        };
        Assert.Null((await _rpc.PeekAsync("character", id)).AiFindings);

        // A chapter with no stored analysis, and one analysed but with no scenes.
        Assert.Null((await _rpc.PeekAsync("character", id, "ch-other", null, null)).AiFindings);
        _workspace.Projects.ProjectSettings.ChapterAnalysis["ch-empty"] =
            new Novalist.Sdk.Models.ChapterAnalysisResult();
        Assert.Null((await _rpc.PeekAsync("character", id, "ch-empty", null, null)).AiFindings);
    }

    [Fact]
    public async Task Peek_PrefersPerSceneRecords_AndFallsBackToLegacyPerScene()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var analysed = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Analysed");
        var legacyOnly = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "LegacyOnly");

        var created = await _rpc.CreateAsync("character", "Amy");
        var id = created.GetProperty("id").GetString()!;

        // Legacy blob covers both scenes...
        _workspace.Projects.ProjectSettings.ChapterAnalysis = new()
        {
            [chapter.Guid] = new Novalist.Sdk.Models.ChapterAnalysisResult
            {
                Scenes = new()
                {
                    [analysed.Id] = new Novalist.Sdk.Models.SceneAnalysisResult
                    {
                        Findings = [new() { Type = "reference", Title = "stale legacy", EntityName = "Amy" }]
                    },
                    [legacyOnly.Id] = new Novalist.Sdk.Models.SceneAnalysisResult
                    {
                        Findings = [new() { Type = "reference", Title = "legacy kept", EntityName = "Amy" }]
                    }
                }
            }
        };

        // ...but one scene has a newer per-scene record, which must win for it.
        var store = new Novalist.Core.Services.SceneAnalysisStore(
            _workspace.Projects, _workspace.FileService);
        await store.WriteAsync(new Novalist.Sdk.Models.SceneAnalysisRecord
        {
            SceneId = analysed.Id,
            ChapterGuid = chapter.Guid,
            Findings = [new() { Type = "reference", Title = "fresh record", EntityName = "Amy" }]
        }, "scene text");

        var titles = (await _rpc.PeekAsync("character", id, chapter.Guid, "C", null))
            .AiFindings!.Select(f => f.Title).ToArray();

        Assert.Contains("fresh record", titles);
        Assert.DoesNotContain("stale legacy", titles);   // superseded per scene
        Assert.Contains("legacy kept", titles);          // no record for that scene yet
    }

    [Fact]
    public async Task Peek_FindingsMatchAliasesAndNonCharacterTypes()
    {
        var place = await _rpc.CreateAsync("location", "Harbour");
        var placeId = place.GetProperty("id").GetString()!;
        await _rpc.UpdateListsAsync("location", placeId, ["The Docks"], null, null);

        _workspace.Projects.ProjectSettings.ChapterAnalysis = new()
        {
            ["ch-1"] = new Novalist.Sdk.Models.ChapterAnalysisResult
            {
                Scenes = new()
                {
                    ["s1"] = new Novalist.Sdk.Models.SceneAnalysisResult
                    {
                        // Refers to the location by its alias.
                        Findings = [new() { Title = "Dock scene", EntityName = "The Docks" }]
                    }
                }
            }
        };

        var peek = await _rpc.PeekAsync("location", placeId, "ch-1", null, null);
        Assert.Equal("Dock scene", Assert.Single(peek.AiFindings!).Title);
    }

    [Fact]
    public async Task AppendToSection_CreatesSectionThenAppendsWithBlankLine()
    {
        var created = await _rpc.CreateAsync("character", "Mira");
        var id = created.GetProperty("id").GetString()!;

        var first = await _rpc.AppendToSectionAsync("character", id, "Notes", " She hums when nervous. ");
        Assert.Equal("Notes", first.GetProperty("sections")[0].GetProperty("title").GetString());
        Assert.Equal(
            "She hums when nervous.",
            first.GetProperty("sections")[0].GetProperty("content").GetString());

        // A second append reuses the section (matched case-insensitively).
        var second = await _rpc.AppendToSectionAsync("character", id, "notes", "She fears deep water.");
        Assert.Single(second.GetProperty("sections").EnumerateArray());
        Assert.Equal(
            "She hums when nervous.\n\nShe fears deep water.",
            second.GetProperty("sections")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task AppendToSection_WorksForEveryBuiltInTypeAndCustomTypes()
    {
        foreach (var type in new[] { "location", "item", "lore" })
        {
            var created = await _rpc.CreateAsync(type, $"Thing-{type}");
            var updated = await _rpc.AppendToSectionAsync(
                type, created.GetProperty("id").GetString()!, "Notes", "Captured.");
            Assert.Equal("Captured.", updated.GetProperty("sections")[0].GetProperty("content").GetString());
        }

        var types = await _rpc.SaveCustomTypeAsync(new CustomTypeSpecDto(
            TypeKey: null,
            DisplayName: "Faction",
            DisplayNamePlural: null,
            Fields: null,
            IncludeImages: false,
            IncludeRelationships: false,
            IncludeSections: true));
        var factionKey = types.Single(t => t.DisplayName == "Faction").TypeKey;
        var faction = await _rpc.CreateAsync(factionKey, "Grey Order");
        var appended = await _rpc.AppendToSectionAsync(
            factionKey, faction.GetProperty("id").GetString()!, "Notes", "Sworn to the crown.");
        Assert.Equal(
            "Sworn to the crown.",
            appended.GetProperty("sections")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task AppendToSection_BlankTitleOrUnknownTarget_Throws()
    {
        var created = await _rpc.CreateAsync("character", "Mira");
        var id = created.GetProperty("id").GetString()!;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.AppendToSectionAsync("character", id, "   ", "text"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.AppendToSectionAsync("character", "missing", "Notes", "text"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.AppendToSectionAsync("dragon", id, "Notes", "text"));
    }

    [Fact]
    public async Task CreateUpdateDelete_UnknownType_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.CreateAsync("dragon", "x"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.UpdateAsync("dragon", "x", new Dictionary<string, string>()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.DeleteAsync("dragon", "x", false));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.UpdateAsync("character", "missing", new Dictionary<string, string>()));
    }

    [Fact]
    public async Task RelationshipsGraph_CarriesEdgesAndDisplayNames()
    {
        await Entities.SaveCharacterAsync(new CharacterData
        {
            Name = "Mira",
            Surname = "Frost",
            Group = "Nordwacht",
            Relationships = [new EntityRelationship { Role = "Mutter", Target = "Lena Frost" }]
        });
        await Entities.SaveCharacterAsync(new CharacterData { Name = "Lena", Surname = "Frost" });

        var graph = await new RelationshipsRpc(_workspace).GetGraphAsync();

        var mira = graph.Single(c => c.DisplayName == "Mira Frost");
        Assert.Equal("Nordwacht", mira.Group);
        Assert.Equal("Mutter", mira.Relationships.Single().Role);
        Assert.Equal("Lena Frost", mira.Relationships.Single().Target);
        Assert.Contains(graph, c => c.DisplayName == "Lena Frost" && c.Relationships.Count == 0);
    }

    [Fact]
    public async Task GetMeta_ReturnsSynopsisAndNotes()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        await _rpc.SetSynopsisAsync(chapter.Guid, scene.Id, "Syn");
        await _rpc.SetNotesAsync(chapter.Guid, scene.Id, "Note");

        var meta = new ScenesRpc(_workspace).GetMeta(chapter.Guid, scene.Id);

        Assert.Equal("Syn", meta.Synopsis);
        Assert.Equal("Note", meta.Notes);
    }

    [Fact]
    public async Task ArchiveAndRestore_Scene_RoundTrip()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Doomed");
        await _workspace.WriteSceneAsync(chapter.Guid, scene.Id, "<p>kept text</p>", "kept text");
        var scenes = new ScenesRpc(_workspace);

        await scenes.ArchiveAsync(chapter.Guid, scene.Id);
        var archived = scenes.GetArchived();
        Assert.Single(archived, s => s.Title == "Doomed");
        Assert.DoesNotContain(
            _workspace.BuildState().Chapters.Single(c => c.Guid == chapter.Guid).Scenes,
            s => s.Id == scene.Id);

        await scenes.RestoreArchivedAsync(scene.Id, chapter.Guid);
        Assert.Empty(scenes.GetArchived());
        var restored = await scenes.ReadAsync(chapter.Guid, scene.Id);
        Assert.Contains("kept text", restored.Html);
    }

    [Fact]
    public async Task Annotations_RoundTripAndClear()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        var scenes = new ScenesRpc(_workspace);

        await scenes.SetAnnotationsAsync(chapter.Guid, scene.Id,
            [new SceneCommentDto("c1", "cold wind", "Too clichéd?", false)],
            [new SceneFootnoteDto("f1", 1, "Historical note.")]);

        var annotations = scenes.GetAnnotations(chapter.Guid, scene.Id);
        Assert.Equal("Too clichéd?", annotations.Comments.Single().Text);
        Assert.Equal(1, annotations.Footnotes.Single().Number);

        await scenes.SetAnnotationsAsync(chapter.Guid, scene.Id, [], []);
        var cleared = scenes.GetAnnotations(chapter.Guid, scene.Id);
        Assert.Empty(cleared.Comments);
        Assert.Empty(cleared.Footnotes);
    }

    [Fact]
    public async Task SetSynopsisAndNotes_PersistAndClearWhenEmpty()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");

        await _rpc.SetSynopsisAsync(chapter.Guid, scene.Id, "The opening");
        await _rpc.SetNotesAsync(chapter.Guid, scene.Id, "Fix pacing");
        var (_, s1) = _workspace.ResolveScene(chapter.Guid, scene.Id);
        Assert.Equal("The opening", s1.Synopsis);
        Assert.Equal("Fix pacing", s1.Notes);

        await _rpc.SetSynopsisAsync(chapter.Guid, scene.Id, "");
        await _rpc.SetNotesAsync(chapter.Guid, scene.Id, "");
        var (_, s2) = _workspace.ResolveScene(chapter.Guid, scene.Id);
        Assert.Null(s2.Synopsis);
        Assert.Null(s2.Notes);
    }
}
