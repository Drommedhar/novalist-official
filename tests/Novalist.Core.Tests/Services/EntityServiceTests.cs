using System.Text.Json;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class EntityServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly string _bookRoot;
    private readonly string _wbRoot;
    private readonly IProjectService _project = Substitute.For<IProjectService>();
    private readonly ProjectMetadata _meta = new();
    private readonly BookData _book = new();
    private readonly EntityService _sut;

    public EntityServiceTests()
    {
        _bookRoot = Path.Combine(_dir.Path, "Books", "Book1");
        _wbRoot = Path.Combine(_dir.Path, "WorldBible");
        Directory.CreateDirectory(_bookRoot);
        Directory.CreateDirectory(_wbRoot);

        _project.ProjectRoot.Returns(_dir.Path);
        _project.ActiveBookRoot.Returns(_bookRoot);
        _project.WorldBibleRoot.Returns(_wbRoot);
        _project.ActiveBook.Returns(_book);
        _project.CurrentProject.Returns(_meta);
        _sut = new EntityService(_project);
    }

    public void Dispose() => _dir.Dispose();

    private void WriteBookChar(string id, string name) => WriteJson(
        Path.Combine(_bookRoot, _book.CharacterFolder, id + ".json"), new CharacterData { Id = id, Name = name });

    private void WriteWbChar(string id, string name) => WriteJson(
        Path.Combine(_wbRoot, _meta.CharacterFolder, id + ".json"), new CharacterData { Id = id, Name = name });

    private static void WriteJson(string path, object obj)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(obj));
    }

    // ── load / save / delete (generic helpers via Character) ──

    [Fact]
    public async Task LoadCharacters_MergesBookAndWorldBible_DedupesAndSkipsMalformed()
    {
        WriteBookChar("c1", "Alice");
        WriteWbChar("c2", "Bob");          // WB-only -> included, flagged WB
        WriteWbChar("c1", "AliceWB");      // dup of book id -> skipped
        // malformed files in both book and WB dirs -> skipped, load still succeeds
        File.WriteAllText(Path.Combine(_bookRoot, _book.CharacterFolder, "broken.json"), "{ not json");
        File.WriteAllText(Path.Combine(_wbRoot, _meta.CharacterFolder, "wbbroken.json"), "{ not json");

        var chars = await _sut.LoadCharactersAsync();

        Assert.Equal(2, chars.Count(c => c.Id is "c1" or "c2"));
        Assert.False(chars.First(c => c.Id == "c1").IsWorldBible);
        Assert.True(chars.First(c => c.Id == "c2").IsWorldBible);
    }

    [Fact]
    public async Task LoadCharacters_NoBookDir_NoWorldBible_Empty()
    {
        _project.WorldBibleRoot.Returns((string?)null);
        Assert.Empty(await _sut.LoadCharactersAsync());
    }

    [Fact]
    public async Task SaveCharacter_WritesToBookFolder()
    {
        await _sut.SaveCharacterAsync(new CharacterData { Id = "c1", Name = "X" });
        Assert.True(File.Exists(Path.Combine(_bookRoot, _book.CharacterFolder, "c1.json")));
    }

    // ── Relationship-duplicate migration ────────────────────────────
    [Fact]
    public static void Dedup_CollapsesDuplicateRole_MergesTargets()
    {
        // The real-world Liam case: a multi-target row plus a duplicate single-target row.
        var liam = new CharacterData
        {
            Name = "Liam",
            Relationships =
            {
                new EntityRelationship { Role = "Mutter", Target = "Amy Calder" },
                new EntityRelationship { Role = "Freund", Target = "Finn Drent, Noah Bryton" },
                new EntityRelationship { Role = "Freund", Target = "Finn Drent" }, // duplicate
            },
        };

        var changed = EntityService.DeduplicateCharacterRelationships(liam);

        Assert.True(changed);
        Assert.Equal(2, liam.Relationships.Count);
        var freund = liam.Relationships.Single(r => r.Role == "Freund");
        Assert.Equal("Finn Drent, Noah Bryton", freund.Target); // no duplicate "Finn Drent"
        Assert.Contains(liam.Relationships, r => r.Role == "Mutter" && r.Target == "Amy Calder");
    }

    [Fact]
    public static void Dedup_NoDuplicates_ReturnsFalse_Unchanged()
    {
        var c = new CharacterData
        {
            Relationships =
            {
                new EntityRelationship { Role = "father", Target = "Ned" },
                new EntityRelationship { Role = "friend", Target = "Sam, Pip" },
            },
        };
        Assert.False(EntityService.DeduplicateCharacterRelationships(c));
        Assert.Equal(2, c.Relationships.Count);
    }

    [Fact]
    public static void Dedup_SameRowCount_DifferentTargets_ReturnsTrue()
    {
        // One row whose own target list has a duplicate -> count stays 1 but content changes.
        var c = new CharacterData
        {
            Relationships = { new EntityRelationship { Role = "friend", Target = "Bob, bob" } },
        };
        Assert.True(EntityService.DeduplicateCharacterRelationships(c));
        Assert.Equal("Bob", Assert.Single(c.Relationships).Target);
    }

    [Fact]
    public static void Dedup_CaseInsensitiveRoleAndTarget()
    {
        var c = new CharacterData
        {
            Relationships =
            {
                new EntityRelationship { Role = "Friend", Target = "Bob" },
                new EntityRelationship { Role = "friend", Target = "bob, Carol" }, // same role + dup target (diff case)
            },
        };
        Assert.True(EntityService.DeduplicateCharacterRelationships(c));
        var row = Assert.Single(c.Relationships);
        Assert.Equal("Friend", row.Role);     // first-seen role text preserved
        Assert.Equal("Bob, Carol", row.Target); // "bob" deduped against "Bob"
    }

    [Fact]
    public async Task Migrate_DeduplicatesAffectedFiles_AndCountsThem()
    {
        WriteJson(Path.Combine(_bookRoot, _book.CharacterFolder, "liam.json"), new CharacterData
        {
            Id = "liam", Name = "Liam",
            Relationships =
            {
                new EntityRelationship { Role = "Freund", Target = "Finn Drent, Noah Bryton" },
                new EntityRelationship { Role = "Freund", Target = "Finn Drent" },
            },
        });
        WriteJson(Path.Combine(_bookRoot, _book.CharacterFolder, "clean.json"), new CharacterData
        {
            Id = "clean", Name = "Clean",
            Relationships = { new EntityRelationship { Role = "father", Target = "X" } },
        });

        var changed = await _sut.MigrateRelationshipDuplicatesAsync();

        Assert.Equal(1, changed); // only Liam rewritten
        var liam = (await _sut.LoadCharactersAsync()).Single(c => c.Id == "liam");
        Assert.Single(liam.Relationships);
        Assert.Equal("Finn Drent, Noah Bryton", liam.Relationships[0].Target);
    }

    [Fact]
    public async Task SaveCharacter_WorldBible_WritesToWbFolder()
    {
        await _sut.SaveCharacterAsync(new CharacterData { Id = "c1", IsWorldBible = true });
        Assert.True(File.Exists(Path.Combine(_wbRoot, _meta.CharacterFolder, "c1.json")));
    }

    [Fact]
    public async Task SaveCharacter_WorldBibleButNoWbRoot_FallsBackToBook()
    {
        _project.WorldBibleRoot.Returns((string?)null);
        await _sut.SaveCharacterAsync(new CharacterData { Id = "c1", IsWorldBible = true });
        Assert.True(File.Exists(Path.Combine(_bookRoot, _book.CharacterFolder, "c1.json")));
    }

    [Fact]
    public async Task DeleteCharacter_RemovesFile_AndNoOpWhenMissing()
    {
        WriteBookChar("c1", "X");
        await _sut.DeleteCharacterAsync("c1");
        Assert.False(File.Exists(Path.Combine(_bookRoot, _book.CharacterFolder, "c1.json")));
        await _sut.DeleteCharacterAsync("c1"); // no-op, no throw
    }

    [Fact]
    public async Task DeleteCharacter_WorldBible_RemovesFromWb()
    {
        WriteWbChar("c1", "X");
        await _sut.DeleteCharacterAsync("c1", isWorldBible: true);
        Assert.False(File.Exists(Path.Combine(_wbRoot, _meta.CharacterFolder, "c1.json")));
    }

    [Fact]
    public async Task Location_Item_Lore_RoundTrip()
    {
        await _sut.SaveLocationAsync(new LocationData { Id = "l1" });
        await _sut.SaveItemAsync(new ItemData { Id = "i1" });
        await _sut.SaveLoreAsync(new LoreData { Id = "o1" });
        Assert.Single(await _sut.LoadLocationsAsync());
        Assert.Single(await _sut.LoadItemsAsync());
        Assert.Single(await _sut.LoadLoreAsync());
        await _sut.DeleteLocationAsync("l1");
        await _sut.DeleteItemAsync("i1");
        await _sut.DeleteLoreAsync("o1");
        Assert.Empty(await _sut.LoadLocationsAsync());
    }

    // ── world-bible move ──

    [Fact]
    public async Task MoveEntityToWorldBible_NoWbRoot_NoOp()
    {
        _project.WorldBibleRoot.Returns((string?)null);
        await _sut.MoveEntityToWorldBibleAsync(EntityType.Character, "c1"); // no throw
    }

    [Fact]
    public async Task MoveEntityToWorldBible_MovesFile()
    {
        WriteBookChar("c1", "X");
        await _sut.MoveEntityToWorldBibleAsync(EntityType.Character, "c1");
        Assert.False(File.Exists(Path.Combine(_bookRoot, _book.CharacterFolder, "c1.json")));
        Assert.True(File.Exists(Path.Combine(_wbRoot, _meta.CharacterFolder, "c1.json")));
    }

    [Fact]
    public async Task MoveEntityToWorldBible_MissingFile_NoOp()
    {
        await _sut.MoveEntityToWorldBibleAsync(EntityType.Location, "ghost"); // no throw
    }

    [Fact]
    public async Task MoveEntityToBook_MovesFile()
    {
        WriteWbChar("c1", "X");
        await _sut.MoveEntityToBookAsync(EntityType.Character, "c1");
        Assert.True(File.Exists(Path.Combine(_bookRoot, _book.CharacterFolder, "c1.json")));
    }

    [Fact]
    public async Task MoveEntityToBook_NoWbRoot_NoOp()
    {
        _project.WorldBibleRoot.Returns((string?)null);
        await _sut.MoveEntityToBookAsync(EntityType.Item, "x");
    }

    [Theory]
    [InlineData(EntityType.Character)]
    [InlineData(EntityType.Location)]
    [InlineData(EntityType.Item)]
    [InlineData(EntityType.Lore)]
    public async Task GetEntityFolders_KnownTypes(EntityType type)
        => await _sut.MoveEntityToWorldBibleAsync(type, "none"); // exercises each switch arm

    [Fact]
    public async Task GetEntityFolders_Custom_Throws()
        => await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _sut.MoveEntityToWorldBibleAsync(EntityType.Custom, "x"));

    // ── custom entity types ──

    private CustomEntityTypeDefinition RegisterFaction()
    {
        var def = new CustomEntityTypeDefinition { TypeKey = "faction", FolderName = "Factions" };
        _meta.CustomEntityTypes.Add(def);
        return def;
    }

    [Fact]
    public void GetCustomEntityTypes_ReturnsProjectList()
    {
        RegisterFaction();
        Assert.Single(_sut.GetCustomEntityTypes());
    }

    [Fact]
    public async Task SaveCustomEntityType_AddsThenUpdates()
    {
        await _sut.SaveCustomEntityTypeAsync(new CustomEntityTypeDefinition { TypeKey = "faction", FolderName = "F" });
        await _sut.SaveCustomEntityTypeAsync(new CustomEntityTypeDefinition { TypeKey = "faction", FolderName = "F2" });
        Assert.Single(_meta.CustomEntityTypes);
        Assert.Equal("F2", _meta.CustomEntityTypes[0].FolderName);
        await _project.Received(2).SaveProjectAsync();
    }

    [Fact]
    public async Task DeleteCustomEntityType_Removes()
    {
        RegisterFaction();
        await _sut.DeleteCustomEntityTypeAsync("faction");
        Assert.Empty(_meta.CustomEntityTypes);
        await _project.Received(1).SaveProjectAsync();
    }

    [Fact]
    public async Task CustomEntity_SaveLoadDelete()
    {
        RegisterFaction();
        await _sut.SaveCustomEntityAsync(new CustomEntityData { Id = "f1", EntityTypeKey = "faction" });
        Assert.Single(await _sut.LoadCustomEntitiesAsync("faction"));
        await _sut.DeleteCustomEntityAsync("faction", "f1");
        Assert.Empty(await _sut.LoadCustomEntitiesAsync("faction"));
    }

    [Fact]
    public async Task CustomEntity_UnknownType_Throws()
        => await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.LoadCustomEntitiesAsync("nope"));

    [Fact]
    public async Task MoveCustomEntity_ToWorldBibleAndBack()
    {
        RegisterFaction();
        await _sut.SaveCustomEntityAsync(new CustomEntityData { Id = "f1", EntityTypeKey = "faction" });
        await _sut.MoveCustomEntityToWorldBibleAsync("faction", "f1");
        Assert.True(File.Exists(Path.Combine(_wbRoot, "Factions", "f1.json")));
        await _sut.MoveCustomEntityToBookAsync("faction", "f1");
        Assert.True(File.Exists(Path.Combine(_bookRoot, "Factions", "f1.json")));
    }

    [Fact]
    public async Task MoveCustomEntity_NoWbRoot_NoOp()
    {
        RegisterFaction();
        _project.WorldBibleRoot.Returns((string?)null);
        await _sut.MoveCustomEntityToWorldBibleAsync("faction", "f1");
        await _sut.MoveCustomEntityToBookAsync("faction", "f1");
    }

    // ── images ──

    [Fact]
    public async Task ImportImage_CopiesNewFile()
    {
        var src = Path.Combine(_dir.Path, "pic.png");
        await File.WriteAllBytesAsync(src, new byte[] { 1, 2, 3 });
        var rel = await _sut.ImportImageAsync(src);
        Assert.Equal("Images/pic.png", rel);
        Assert.True(File.Exists(Path.Combine(_bookRoot, "Images", "pic.png")));
    }

    [Fact]
    public async Task ImportImage_SameContentDifferentName_Deduplicates()
    {
        var src = Path.Combine(_dir.Path, "a.png");
        await File.WriteAllBytesAsync(src, new byte[] { 9, 9, 9 });
        await _sut.ImportImageAsync(src);

        var src2 = Path.Combine(_dir.Path, "b.png");
        await File.WriteAllBytesAsync(src2, new byte[] { 9, 9, 9 }); // identical content
        var rel = await _sut.ImportImageAsync(src2);
        Assert.Equal("Images/a.png", rel); // returns the existing match
    }

    [Fact]
    public async Task ImportImage_SamePathReturnsExisting()
    {
        // Import a file that already lives in the image dir.
        var imageDir = Path.Combine(_bookRoot, "Images");
        Directory.CreateDirectory(imageDir);
        var inside = Path.Combine(imageDir, "existing.png");
        await File.WriteAllBytesAsync(inside, new byte[] { 5 });
        var rel = await _sut.ImportImageAsync(inside);
        Assert.Equal("Images/existing.png", rel);
    }

    [Fact]
    public async Task ImportImage_NameClashDifferentContent_GetsUniqueName()
    {
        var imageDir = Path.Combine(_bookRoot, "Images");
        Directory.CreateDirectory(imageDir);
        await File.WriteAllBytesAsync(Path.Combine(imageDir, "dup.png"), new byte[] { 1 });

        var src = Path.Combine(_dir.Path, "dup.png");
        await File.WriteAllBytesAsync(src, new byte[] { 2, 2 }); // same name, different content
        var rel = await _sut.ImportImageAsync(src);
        Assert.Equal("Images/dup (2).png", rel);
    }

    [Fact]
    public void GetProjectImages_ListsBookAndWorldBible_Sorted()
    {
        var bookImg = Path.Combine(_bookRoot, "Images");
        Directory.CreateDirectory(bookImg);
        File.WriteAllBytes(Path.Combine(bookImg, "b.png"), new byte[] { 1 });
        File.WriteAllText(Path.Combine(bookImg, "notimage.txt"), "x"); // filtered out

        var wbImg = Path.Combine(_wbRoot, _meta.ImageFolder);
        Directory.CreateDirectory(wbImg);
        File.WriteAllBytes(Path.Combine(wbImg, "a.png"), new byte[] { 2 });

        var images = _sut.GetProjectImages();
        Assert.Contains(images, p => p.EndsWith("b.png"));
        Assert.Contains(images, p => p.Contains(_meta.WorldBibleFolder) && p.EndsWith("a.png"));
        Assert.DoesNotContain(images, p => p.EndsWith("notimage.txt"));
    }

    [Fact]
    public void GetProjectImages_NoDirs_Empty()
    {
        _project.WorldBibleRoot.Returns((string?)null);
        Assert.Empty(_sut.GetProjectImages());
    }

    [Fact]
    public void GetImageFullPath_NoProject_Throws()
    {
        _project.ProjectRoot.Returns((string?)null);
        Assert.Throws<InvalidOperationException>(() => _sut.GetImageFullPath("Images/x.png"));
    }

    [Fact]
    public void GetImageFullPath_WorldBiblePrefix_UsesProjectRoot()
    {
        var rel = _meta.WorldBibleFolder + "/Images/x.png";
        Assert.Equal(Path.Combine(_dir.Path, rel), _sut.GetImageFullPath(rel));
    }

    [Fact]
    public void GetImageFullPath_BookImage_UsesBookRoot()
        => Assert.Equal(Path.Combine(_bookRoot, "Images/x.png"), _sut.GetImageFullPath("Images/x.png"));

    // ── null-state guards ──

    [Fact]
    public async Task NoActiveBook_Throws()
    {
        _project.ActiveBook.Returns((BookData?)null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.LoadCharactersAsync());
    }
}
