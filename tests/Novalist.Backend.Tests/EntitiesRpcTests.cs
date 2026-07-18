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
        Assert.Equal("Images/mira.png", mira.ImagePath);
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
