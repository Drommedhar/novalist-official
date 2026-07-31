using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class LibraryRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly LibraryRpc _rpc;

    public LibraryRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-lib-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "LibNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new LibraryRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string WriteSource(string name, int bytes = 4)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    [Fact]
    public void Gallery_ListsImportedImages()
    {
        Assert.Empty(_rpc.ListImages());
        var source = Path.Combine(_root, "pic.png");
        File.WriteAllBytes(source, [137, 80, 78, 71]);
        new Novalist.Core.Services.EntityService(_workspace.Projects)
            .ImportImageAsync(source).GetAwaiter().GetResult();
        var listed = _rpc.ListImages();
        var image = Assert.Single(listed);
        // Stored path stays book-relative; url is project-root-relative for display.
        Assert.EndsWith(image.Path, image.Url);
        Assert.NotEqual(image.Path, image.Url);
    }

    [Fact]
    public async Task Research_SaveUpdateDelete_RoundTrip()
    {
        var saved = await _rpc.SaveResearchAsync(null, "Norse winters", "Note", "Very cold.", ["climate", " "]);
        var item = saved.Single();
        Assert.Equal("Norse winters", item.Title);
        Assert.Equal(new[] { "climate" }, item.Tags);
        // A note carries no file metadata.
        Assert.Empty(item.FileSize);
        Assert.Empty(item.Modified);

        var updated = await _rpc.SaveResearchAsync(item.Id, "Winters", "Link", "https://example.org", []);
        Assert.Equal("Link", updated.Single().Type);

        Assert.Empty(await _rpc.DeleteResearchAsync(item.Id));
    }

    [Theory]
    [InlineData("notes.pdf", "Pdf")]
    [InlineData("diagram.png", "Image")]
    [InlineData("interview.mp3", "Audio")]
    [InlineData("walkthrough.mp4", "Video")]
    // Not something the app can play: a File, which opens externally rather
    // than showing controls that do nothing.
    [InlineData("tape.aiff", "File")]
    [InlineData("data.csv", "File")]
    public async Task Research_Import_ClassifiesByExtension(string fileName, string expectedType)
    {
        var source = WriteSource(fileName);
        var listed = await _rpc.ImportResearchAsync(source);

        var item = Assert.Single(listed);
        Assert.Equal(expectedType, item.Type);
        Assert.Equal(Path.GetFileNameWithoutExtension(fileName), item.Title);
        // Content is a project-relative path under the Research folder.
        Assert.StartsWith("Research/", item.Content);
        // The imported file exists on disk, so metadata is populated.
        Assert.NotEmpty(item.FileSize);
        Assert.NotEmpty(item.Modified);
    }

    [Fact]
    public async Task Research_FileItem_WithMissingTarget_HasNoMetadata()
    {
        var listed = await _rpc.SaveResearchAsync(null, "Ghost", "File", "Research/ghost.txt", []);
        var item = listed.Single();
        Assert.Equal("File", item.Type);
        Assert.Empty(item.FileSize);
        Assert.Empty(item.Modified);
    }

    [Theory]
    [InlineData(-1, "")]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2.0 KB")]
    [InlineData(3 * 1024 * 1024, "3.00 MB")]
    public void FormatSize_CoversAllBranches(long bytes, string expected)
    {
        Assert.Equal(expected, LibraryRpc.FormatSize(bytes));
    }

    [Fact]
    public async Task QuickCapture_CreatesInboxTaggedNote()
    {
        var items = await _rpc.QuickCaptureAsync("  The frost oath binds both ways.\nCheck ch. 4.  ");

        var item = Assert.Single(items);
        Assert.Equal("Note", item.Type);
        Assert.Equal("The frost oath binds both ways.", item.Title);   // first line
        Assert.Equal("The frost oath binds both ways.\nCheck ch. 4.", item.Content);
        Assert.Contains("inbox", item.Tags);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n  ")]
    public async Task QuickCapture_BlankText_Throws(string text)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.QuickCaptureAsync(text));
    }

    [Fact]
    public async Task FetchLinkTitle_UnreachableUrl_ReturnsNull()
    {
        // Nothing is listening on this reserved-for-docs address, so the lookup
        // fails and the caller keeps the raw URL as the title.
        Assert.Null(await _rpc.FetchLinkTitleAsync("not-a-url", CancellationToken.None));
    }

    [Fact]
    public async Task SaveResearch_RoundTripsEntityLinks()
    {
        var items = await _rpc.SaveResearchAsync(
            null, "Orders", "Note", "Body", ["tag"], ["hero", "", "hero"]);
        var item = Assert.Single(items);
        Assert.Equal(["hero"], item.EntityRefs);   // blanks and duplicates dropped

        // Omitting the argument leaves existing links untouched.
        var again = await _rpc.SaveResearchAsync(item.Id, "Orders", "Note", "Body 2", ["tag"]);
        Assert.Equal(["hero"], again.Single().EntityRefs);
    }

    [Fact]
    public void DeriveTitle_ClipsLongFirstLine()
    {
        var longLine = new string('a', 100);
        var title = LibraryRpc.DeriveTitle(longLine);
        Assert.Equal(63, title.Length);          // 60 chars + the ellipsis
        Assert.EndsWith("...", title);

        // A leading blank line is skipped in favour of the first real line.
        Assert.Equal("Real title", LibraryRpc.DeriveTitle("\n\nReal title\nmore"));
    }

    [Fact]
    public async Task ImportingAnImageStoresItInTheBookAndReportsBothForms()
    {
        var source = Path.Combine(_root, "map.png");
        await File.WriteAllBytesAsync(source, [0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0]);

        var image = await _rpc.ImportImageAsync(source);

        // Stored book-relative so the project survives being moved; the URL is
        // project-relative because that is what the renderer can display.
        Assert.StartsWith("Images/", image.Path);
        Assert.EndsWith("map.png", image.Path);
        Assert.Contains("Images/map.png", image.Url);
        Assert.True(File.Exists(Path.Combine(
            _workspace.Projects.ActiveBookRoot!, image.Path.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task TheImageBaseIsWhatAStoredPathHangsOff()
    {
        var source = Path.Combine(_root, "map2.png");
        await File.WriteAllBytesAsync(source, [0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0]);
        var image = await _rpc.ImportImageAsync(source);

        var url = _rpc.ImageBase() + image.Path;

        Assert.Equal(image.Url, url);
    }

    // ── Where an item stands, and what refers to what ──

    [Fact]
    public async Task Lifecycle_StatusAndRatingRoundTrip()
    {
        var items = await _rpc.SaveResearchAsync(null, "Bridge", "Note", "1755?", []);
        var id = items.Single().Id;

        // Every item starts with nothing said, which is where most stay.
        Assert.Equal("None", items.Single().Status);
        Assert.Equal(0, items.Single().Rating);

        var open = await _rpc.SetLifecycleAsync(id, "Open", 4);
        Assert.Equal("Open", open.Single().Status);
        Assert.Equal(4, open.Single().Rating);

        // Rating and status move independently: null leaves one alone.
        var resolved = await _rpc.SetLifecycleAsync(id, "Resolved", null);
        Assert.Equal("Resolved", resolved.Single().Status);
        Assert.Equal(4, resolved.Single().Rating);
    }

    [Fact]
    public async Task Lifecycle_NonsenseFallsBackRatherThanThrowing()
    {
        var id = (await _rpc.SaveResearchAsync(null, "A", "Note", "", [])).Single().Id;

        var items = await _rpc.SetLifecycleAsync(id, "rhubarb", 99);

        Assert.Equal("None", items.Single().Status);
        // Clamped: five is the top of the scale, zero is how a rating is undone.
        Assert.Equal(5, items.Single().Rating);
        Assert.Equal(0, (await _rpc.SetLifecycleAsync(id, null, -1)).Single().Rating);

        // And an item that is not there changes nothing.
        Assert.Single(await _rpc.SetLifecycleAsync("no-such-id", "Open", 3));
    }

    [Fact]
    public async Task Links_AreWrittenBothWays()
    {
        var a = (await _rpc.SaveResearchAsync(null, "Question", "Note", "", [])).Single().Id;
        var items = await _rpc.SaveResearchAsync(null, "Answer", "Note", "", []);
        var b = items.Single(i => i.Title == "Answer").Id;

        var linked = await _rpc.LinkResearchAsync(a, b, true);

        // The end worth finding is usually the other one, so both carry it.
        Assert.Contains(b, linked.Single(i => i.Id == a).RelatedIds);
        Assert.Contains(a, linked.Single(i => i.Id == b).RelatedIds);

        var unlinked = await _rpc.LinkResearchAsync(b, a, false);
        Assert.Empty(unlinked.Single(i => i.Id == a).RelatedIds);
        Assert.Empty(unlinked.Single(i => i.Id == b).RelatedIds);
    }

    [Fact]
    public async Task Links_IgnoreNonsense()
    {
        var a = (await _rpc.SaveResearchAsync(null, "Only", "Note", "", [])).Single().Id;

        // An item cannot refer to itself, and an id that is not there is not a
        // link - both leave the shelf exactly as it was.
        Assert.Empty((await _rpc.LinkResearchAsync(a, a, true)).Single().RelatedIds);
        Assert.Empty((await _rpc.LinkResearchAsync(a, "ghost", true)).Single().RelatedIds);
    }

    [Fact]
    public async Task DeletingAnItemTakesTheLinksToItWithIt()
    {
        var a = (await _rpc.SaveResearchAsync(null, "Keeper", "Note", "", [])).Single().Id;
        var b = (await _rpc.SaveResearchAsync(null, "Doomed", "Note", "", []))
            .Single(i => i.Title == "Doomed").Id;
        await _rpc.LinkResearchAsync(a, b, true);

        var left = await _rpc.DeleteResearchAsync(b);

        // A reference to something that is gone reads as a source somebody can
        // still open, which it is not.
        Assert.Empty(left.Single().RelatedIds);
    }

    // ── The scratchpad: notes that outlive every project ──

    [Fact]
    public async Task Scratchpad_TakesANoteWithNoProjectInvolved()
    {
        var notes = await _rpc.AddScratchpadAsync("  Check the tide tables.  ");

        Assert.Equal("Check the tide tables.", Assert.Single(notes).Text);
        Assert.Single(_rpc.ListScratchpad());

        await _rpc.DeleteScratchpadAsync(notes.Single().Id);
        Assert.Empty(_rpc.ListScratchpad());
    }

    [Fact]
    public async Task Scratchpad_FilingMovesTheNoteIntoTheProjectInbox()
    {
        var note = (await _rpc.AddScratchpadAsync("The bridge did not exist yet.")).Single();

        await _rpc.FileScratchpadAsync(note.Id);

        // Gone from the scratchpad, present in the inbox, which is where it was
        // going to end up anyway.
        Assert.Empty(_rpc.ListScratchpad());
        var filed = _rpc.ListResearch().Single(i => i.Title.StartsWith("The bridge"));
        Assert.Contains("inbox", filed.Tags);
    }

    [Fact]
    public async Task Scratchpad_FilingNeedsSomewhereToFileTo()
    {
        var note = (await _rpc.AddScratchpadAsync("Nowhere to go")).Single();
        var bare = new LibraryRpc(new Workspace(Path.Combine(_root, "no-project")));

        // Silently doing nothing would look like it worked, and the note would
        // seem to have been filed somewhere the writer could not find.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bare.FileScratchpadAsync(note.Id));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.FileScratchpadAsync("no-such-note"));
    }

    // ─── Collections and tags over the pictures ──────────────────────

    /// <summary>A picture actually in the project, since filing needs one.</summary>
    private async Task<string> ImportPictureAsync(string name)
    {
        var source = Path.Combine(_root, name);
        await File.WriteAllBytesAsync(source, [137, 80, 78, 71]);
        return (await _rpc.ImportImageAsync(source)).Path;
    }

    [Fact]
    public async Task Catalog_ListsEveryPictureWithNothingFiled()
    {
        await ImportPictureAsync("pic.png");

        var catalog = await _rpc.CatalogAsync();

        var image = Assert.Single(catalog.Images);
        Assert.Equal(string.Empty, image.Collection);
        Assert.Empty(image.Tags);
        Assert.Empty(catalog.Collections);
        Assert.Empty(catalog.Tags);
    }

    [Fact]
    public async Task Catalog_CarriesTheFilingBackWithThePicture()
    {
        var path = await ImportPictureAsync("pic.png");

        await _rpc.SetCollectionAsync(path, "References");
        var catalog = await _rpc.SetTagsAsync(path, ["coast", "ruins"]);

        var image = Assert.Single(catalog.Images);
        Assert.Equal("References", image.Collection);
        Assert.Equal(["coast", "ruins"], image.Tags);
        // The vocabulary rides along so a picker offers what is already in use
        // rather than asking how it was spelled last time.
        Assert.Equal(["References"], catalog.Collections);
        Assert.Equal(["coast", "ruins"], catalog.Tags);
    }

    [Fact]
    public async Task Catalog_FilingSurvivesReopeningTheProject()
    {
        var path = await ImportPictureAsync("pic.png");
        await _rpc.SetCollectionAsync(path, "References");

        await _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!);

        Assert.Equal("References", Assert.Single((await _rpc.CatalogAsync()).Images).Collection);
    }

    [Fact]
    public async Task Catalog_FilingAPictureThatIsGoneShowsNothing()
    {
        await _rpc.SetCollectionAsync("Images/never-existed.png", "References");

        // The catalogue is keyed on the pictures that are actually there, so a
        // stale row does not conjure a picture into the Gallery.
        Assert.Empty((await _rpc.CatalogAsync()).Images);
    }

    [Fact]
    public async Task AResearchItemKeepsWhatItSaidBeforeThroughTheSavePath()
    {
        // Through the RPC on purpose: the save path edits the very object the
        // project holds, so a version taken inside the service would compare
        // an item with itself and record nothing.
        var created = await _rpc.SaveResearchAsync(
            null, "The bridge", "Note", "Built 1846.", []);
        var id = created[0].Id;

        await _rpc.SaveResearchAsync(id, "The bridge", "Note", "Pasted over.", []);

        var history = _rpc.ResearchHistory(id);
        Assert.NotEmpty(history);

        var restored = await _rpc.RestoreResearchRevisionAsync(id, history[0].Id);
        Assert.Equal("Built 1846.", restored.First(r => r.Id == id).Content);
    }

    [Fact]
    public async Task ANewResearchItemHasNoVersionsYet()
    {
        var created = await _rpc.SaveResearchAsync(null, "New", "Note", "x", []);

        Assert.Empty(_rpc.ResearchHistory(created[0].Id));
    }

    [Fact]
    public async Task ARestoreOfAResearchRevisionThatIsGoneIsRefused()
    {
        var created = await _rpc.SaveResearchAsync(null, "New", "Note", "x", []);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.RestoreResearchRevisionAsync(created[0].Id, "no-such-revision"));
    }
}

