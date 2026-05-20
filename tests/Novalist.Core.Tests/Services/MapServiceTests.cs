using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class MapServiceTests
{
    private const string Root = "/draft";

    private static (MapService Sut, IProjectService Project, InMemoryFileService Files, BookData Book) Build(bool withBook = true)
    {
        var project = Substitute.For<IProjectService>();
        var files = new InMemoryFileService();
        var book = new BookData();
        if (withBook)
        {
            project.ActiveBook.Returns(book);
            project.ActiveDraftRoot.Returns(Root);
        }
        else
        {
            // NSubstitute returns "" (not null) for unset string members; null them
            // explicitly so the no-root throw path matches production behaviour.
            project.ActiveDraftRoot.Returns((string?)null);
            project.ActiveBookRoot.Returns((string?)null);
        }
        return (new MapService(project, files), project, files, book);
    }

    [Fact]
    public void GetMapsRoot_NoBook_Throws()
    {
        var (sut, _, _, _) = Build(withBook: false);
        Assert.Throws<InvalidOperationException>(() => sut.GetMapsRoot());
    }

    [Fact]
    public void GetMapsRoot_FallsBackToBookRoot()
    {
        var project = Substitute.For<IProjectService>();
        project.ActiveDraftRoot.Returns((string?)null);
        project.ActiveBookRoot.Returns("/book");
        var sut = new MapService(project, new InMemoryFileService());
        Assert.Contains("Maps", sut.GetMapsRoot());
    }

    [Fact]
    public async Task CreateMapAsync_NoBook_Throws()
    {
        var (sut, _, _, _) = Build(withBook: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateMapAsync("World"));
    }

    [Fact]
    public async Task CreateMapAsync_CreatesFileAndReference()
    {
        var (sut, project, files, book) = Build();
        var map = await sut.CreateMapAsync("World");

        Assert.Equal("World", map.Name);
        Assert.StartsWith("map-", map.Id);
        Assert.Single(files.Files);
        Assert.Single(book.Maps);
        await project.Received(1).SaveProjectAsync();
    }

    [Fact]
    public async Task LoadMapAsync_NoBook_ReturnsNull()
    {
        var (sut, _, _, _) = Build(withBook: false);
        Assert.Null(await sut.LoadMapAsync("x"));
    }

    [Fact]
    public async Task LoadMapAsync_MissingReference_ReturnsNull()
    {
        var (sut, _, _, _) = Build();
        Assert.Null(await sut.LoadMapAsync("nope"));
    }

    [Fact]
    public async Task LoadMapAsync_MissingFile_ReturnsNull()
    {
        var (sut, _, _, book) = Build();
        book.Maps.Add(new MapReference { Id = "m1", FileName = "m1.json" });
        Assert.Null(await sut.LoadMapAsync("m1"));
    }

    [Fact]
    public async Task CreateThenLoad_RoundTrips()
    {
        var (sut, _, _, _) = Build();
        var created = await sut.CreateMapAsync("World");
        var loaded = await sut.LoadMapAsync(created.Id);
        Assert.Equal("World", loaded!.Name);
    }

    [Fact]
    public async Task SaveMapAsync_UsesIdWhenFileNameEmpty()
    {
        var (sut, _, files, _) = Build();
        await sut.SaveMapAsync(new MapData { Id = "m9", FileName = "" });
        Assert.Contains(files.Files.Keys, k => k.Contains("m9.json"));
    }

    [Fact]
    public async Task DeleteMapAsync_NoBook_NoOp()
    {
        var (sut, project, _, _) = Build(withBook: false);
        await sut.DeleteMapAsync("x");
        await project.DidNotReceive().SaveProjectAsync();
    }

    [Fact]
    public async Task DeleteMapAsync_MissingReference_NoOp()
    {
        var (sut, project, _, _) = Build();
        await sut.DeleteMapAsync("nope");
        await project.DidNotReceive().SaveProjectAsync();
    }

    [Fact]
    public async Task DeleteMapAsync_RemovesFileAndReference()
    {
        var (sut, project, files, book) = Build();
        var map = await sut.CreateMapAsync("World");
        await sut.DeleteMapAsync(map.Id);
        Assert.Empty(book.Maps);
        Assert.Empty(files.Files);
        await project.Received(2).SaveProjectAsync(); // create + delete
    }

    [Fact]
    public async Task DeleteMapAsync_MissingFile_StillRemovesReference()
    {
        var (sut, _, _, book) = Build();
        book.Maps.Add(new MapReference { Id = "m1", FileName = "gone.json" });
        await sut.DeleteMapAsync("m1");
        Assert.Empty(book.Maps);
    }

    [Fact]
    public async Task RenameMapAsync_NoBook_NoOp()
    {
        var (sut, project, _, _) = Build(withBook: false);
        await sut.RenameMapAsync("x", "new");
        await project.DidNotReceive().SaveProjectAsync();
    }

    [Fact]
    public async Task RenameMapAsync_MissingReference_NoOp()
    {
        var (sut, project, _, _) = Build();
        await sut.RenameMapAsync("nope", "new");
        await project.DidNotReceive().SaveProjectAsync();
    }

    [Fact]
    public async Task RenameMapAsync_RenamesReferenceAndFile()
    {
        var (sut, project, _, book) = Build();
        var map = await sut.CreateMapAsync("Old");
        await sut.RenameMapAsync(map.Id, "New");
        Assert.Equal("New", book.Maps[0].Name);
        var reloaded = await sut.LoadMapAsync(map.Id);
        Assert.Equal("New", reloaded!.Name);
    }

    [Fact]
    public async Task RenameMapAsync_ReferenceWithoutFile_RenamesReferenceOnly()
    {
        var (sut, _, _, book) = Build();
        book.Maps.Add(new MapReference { Id = "m1", FileName = "missing.json", Name = "Old" });
        await sut.RenameMapAsync("m1", "New");
        Assert.Equal("New", book.Maps[0].Name);
    }

    // ---- DeserializeWithMigration (internal) ----

    [Fact]
    public void Deserialize_NullJson_ReturnsNull()
        => Assert.Null(MapService.DeserializeWithMigration("null"));

    [Fact]
    public void Deserialize_V2_Direct()
    {
        var map = MapService.DeserializeWithMigration("""{"name":"V2","layers":[],"version":2}""");
        Assert.Equal("V2", map!.Name);
    }

    [Fact]
    public void Deserialize_V1_MigratesGroupsToLayers()
    {
        const string v1 = """
        {
          "name": "V1",
          "groups": [
            { "id": "g1", "name": "G1", "isConnectedSet": true, "defaultMemberLayerId": "d1",
              "layers": [ { "id": "l1", "name": "L1" } ] },
            { "id": "g2", "name": "G2", "layers": [] },
            42
          ]
        }
        """;
        var map = MapService.DeserializeWithMigration(v1);

        Assert.Equal("V1", map!.Name);
        Assert.Equal(2, map.Version);
        Assert.Equal(2, map.Layers.Count);        // the non-object entry (42) is skipped
        Assert.Single(map.Layers[0].Children);    // g1's old layer became a child
        Assert.Empty(map.Layers[1].Children);     // g2 had empty layers
    }

    [Fact]
    public void Deserialize_V1_GroupWithNonObjectLayer_Skipped()
    {
        const string v1 = """
        { "name": "X", "groups": [ { "id": "g", "name": "G", "layers": [ 7, { "id": "l" } ] } ] }
        """;
        var map = MapService.DeserializeWithMigration(v1);
        Assert.Single(map!.Layers[0].Children); // only the object layer kept
    }
}
