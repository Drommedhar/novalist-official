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
}
