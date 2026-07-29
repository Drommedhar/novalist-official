using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// The project above the book: every book at once, and where the shared Codex
/// entries appear across them.
/// </summary>
public sealed class SeriesRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly SeriesRpc _rpc;

    public SeriesRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-series-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "SeriesNovel", "Book One")
            .GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new SeriesRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>One chapter and one scene in the open book, with a cast.</summary>
    private async Task WriteSceneAsync(string title, params string[] cast)
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, title);
        scene.Cast = [.. cast];
        scene.WordCount = 100;
        await _workspace.Projects.SaveScenesAsync();
    }

    private async Task<CharacterData> SharedCharacterAsync(string id, string name)
    {
        var character = new CharacterData { Id = id, Name = name, IsWorldBible = true };
        await new EntityService(_workspace.Projects).SaveCharacterAsync(character);
        return character;
    }

    [Fact]
    public async Task OneBookIsReportedWithItsSizeAndShape()
    {
        await WriteSceneAsync("First");

        var overview = await _rpc.OverviewAsync();

        var book = Assert.Single(overview.Books);
        Assert.Equal("Book One", book.Name);
        Assert.Equal(1, book.Chapters);
        Assert.Equal(1, book.Scenes);
        Assert.Equal(100, book.Words);
    }

    [Fact]
    public async Task ASharedEntryIsReportedWithEveryBookItAppearsIn()
    {
        await SharedCharacterAsync("mira", "Mira");
        await WriteSceneAsync("First", "mira");
        var firstBookId = _workspace.Projects.ActiveBook!.Id;

        var second = await _workspace.Projects.CreateBookAsync("Book Two");
        // Creating a book does not open it; the scenes have to land in it.
        await _workspace.Projects.SwitchBookAsync(second.Id);
        await WriteSceneAsync("Second", "mira");
        var secondBookId = second.Id;

        var overview = await _rpc.OverviewAsync();

        Assert.Equal(2, overview.Books.Length);
        var mira = Assert.Single(overview.Entities);
        Assert.Equal("Mira", mira.Name);
        Assert.Equal(2, mira.BookCount);
        Assert.Contains(firstBookId, mira.BookIds);
        Assert.Contains(secondBookId, mira.BookIds);
    }

    [Fact]
    public async Task AnEntryInOneBookOfTwoSaysSo()
    {
        await SharedCharacterAsync("mira", "Mira");
        await SharedCharacterAsync("halden", "Halden");
        await WriteSceneAsync("First", "mira", "halden");

        var second = await _workspace.Projects.CreateBookAsync("Book Two");
        await _workspace.Projects.SwitchBookAsync(second.Id);
        await WriteSceneAsync("Second", "mira");

        var overview = await _rpc.OverviewAsync();

        // Sorted by how many books they reach, so the dropped thread is last.
        Assert.Equal(["Mira", "Halden"], overview.Entities.Select(e => e.Name));
        Assert.Equal(1, overview.Entities[1].BookCount);
    }

    [Fact]
    public async Task ReadingTheSeriesLeavesTheWriterInTheBookTheyWereIn()
    {
        await WriteSceneAsync("First");
        var second = await _workspace.Projects.CreateBookAsync("Book Two");
        await _workspace.Projects.SwitchBookAsync(second.Id);
        var openedWith = _workspace.Projects.ActiveBook!.Id;

        await _rpc.OverviewAsync();

        Assert.Equal(openedWith, _workspace.Projects.ActiveBook!.Id);
    }

    [Fact]
    public async Task ABookOnlyEntryIsNotASeriesRow()
    {
        // Not a World Bible entry: it cannot appear in another book, so a row
        // for it would be a row of one every time.
        await new EntityService(_workspace.Projects).SaveCharacterAsync(
            new CharacterData { Id = "local", Name = "Local", IsWorldBible = false });
        await WriteSceneAsync("First", "local");

        Assert.Empty((await _rpc.OverviewAsync()).Entities);
    }

    [Fact]
    public async Task AConfirmedMentionCountsAsAnAppearance()
    {
        await SharedCharacterAsync("mira", "Mira");
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "First");
        await _workspace.Projects.WriteSceneContentAsync(
            chapter, scene,
            "<p><span class=\"nv-entity-mention\" data-entity-id=\"mira\">Mira</span> waited.</p>");

        var overview = await _rpc.OverviewAsync();

        Assert.Equal("Mira", Assert.Single(overview.Entities).Name);
    }

    [Fact]
    public async Task EveryKindOfSharedEntryIsListed()
    {
        var entities = new EntityService(_workspace.Projects);
        await entities.SaveLocationAsync(
            new LocationData { Id = "ashport", Name = "Ashport", IsWorldBible = true });
        await entities.SaveItemAsync(
            new ItemData { Id = "rope", Name = "The Rope", IsWorldBible = true });
        await entities.SaveLoreAsync(
            new LoreData { Id = "rite", Name = "The Rite", IsWorldBible = true });
        await WriteSceneAsync("First", "ashport", "rope", "rite");

        var overview = await _rpc.OverviewAsync();

        Assert.Equal(
            ["Ashport", "The Rite", "The Rope"],
            overview.Entities.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheBookIsRestoredEvenWhenTheSeriesWalkEndedElsewhere()
    {
        // Two books, opened on the first: the walk ends on the last one, so
        // the restore has real work to do rather than being a no-op.
        await WriteSceneAsync("First");
        var first = _workspace.Projects.ActiveBook!.Id;
        var second = await _workspace.Projects.CreateBookAsync("Book Two");
        await _workspace.Projects.SwitchBookAsync(second.Id);
        await WriteSceneAsync("Second");
        await _workspace.Projects.SwitchBookAsync(first);

        await _rpc.OverviewAsync();

        Assert.Equal(first, _workspace.Projects.ActiveBook!.Id);
    }

    [Fact]
    public async Task WithNoProjectOpenThereIsNoSeries()
    {
        var bare = new Workspace(Path.Combine(_root, "bare-settings"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SeriesRpc(bare).OverviewAsync());
    }
}
