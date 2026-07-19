using System.Text.Json;
using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class EntitiesRpcTests : IDisposable
{
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
        // ImagePath is resolved to a project-root-relative path (book folder
        // prepended) so the renderer's project-rooted protocol can load it.
        Assert.EndsWith("/Images/mira.png", mira.ImagePath);
        Assert.NotEqual("Images/mira.png", mira.ImagePath);
        Assert.Contains(list, e => e.Name == "Solo" && e.ImagePath == null);
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
