using NSubstitute;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Collections and tags over a project's pictures.
///
/// The Gallery could search file names and nothing else, so a folder of four
/// hundred references was navigable only by whatever the browser happened to
/// call each file when it was saved.
/// </summary>
public class PictureCatalogServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly IProjectService _project = Substitute.For<IProjectService>();
    private readonly PictureCatalogService _sut;

    public PictureCatalogServiceTests()
    {
        _project.ProjectRoot.Returns(_dir.Path);
        _sut = new PictureCatalogService(_project, new FileService());
    }

    public void Dispose() => _dir.Dispose();

    private string CatalogFile => Path.Combine(_dir.Path, ".novalist", "gallery.json");

    [Fact]
    public async Task AProjectThatHasFiledNothingHasAnEmptyCatalogue()
    {
        // No file is an empty catalogue, not a failure to open the Gallery.
        Assert.Empty((await _sut.LoadAsync()).Entries);
        Assert.False(File.Exists(CatalogFile));
    }

    [Fact]
    public async Task ACollectionSticks()
    {
        await _sut.SetCollectionAsync("Images/keep.png", " References ");

        var entry = Assert.Single((await _sut.LoadAsync()).Entries);
        Assert.Equal("Images/keep.png", entry.Path);
        Assert.Equal("References", entry.Collection);
    }

    [Fact]
    public async Task TagsStick()
    {
        await _sut.SetTagsAsync("Images/keep.png", [" coast ", "ruins"]);

        Assert.Equal(["coast", "ruins"], Assert.Single((await _sut.LoadAsync()).Entries).Tags);
    }

    [Fact]
    public async Task TheSameTagTwiceIsOneTag()
    {
        await _sut.SetTagsAsync("Images/keep.png", ["Coast", "coast", "", "  "]);

        // Case is a typo rather than a distinction anybody meant to draw, and
        // an empty tag is a stray comma.
        Assert.Equal(["Coast"], Assert.Single((await _sut.LoadAsync()).Entries).Tags);
    }

    [Fact]
    public async Task FilingAndTaggingTheSamePictureIsOneRow()
    {
        await _sut.SetCollectionAsync("Images/keep.png", "References");
        await _sut.SetTagsAsync("Images/keep.png", ["coast"]);

        var entry = Assert.Single((await _sut.LoadAsync()).Entries);
        Assert.Equal("References", entry.Collection);
        Assert.Equal(["coast"], entry.Tags);
    }

    [Fact]
    public async Task ASlashIsASlashWhicheverWayItLeans()
    {
        await _sut.SetCollectionAsync(@"Images\keep.png", "References");
        await _sut.SetTagsAsync("Images/keep.png", ["coast"]);

        // A Windows path and a stored path are the same picture; two rows for
        // it would file it twice and show neither.
        var entry = Assert.Single((await _sut.LoadAsync()).Entries);
        Assert.Equal("Images/keep.png", entry.Path);
        Assert.Equal("References", entry.Collection);
    }

    [Fact]
    public async Task SayingNothingAboutAPictureLeavesNoRow()
    {
        await _sut.SetCollectionAsync("Images/keep.png", "References");

        await _sut.SetCollectionAsync("Images/keep.png", "  ");

        // Keeping the row would grow the file by every picture ever
        // right-clicked and then left alone.
        Assert.Empty((await _sut.LoadAsync()).Entries);
    }

    [Fact]
    public async Task TheFileIsWrittenInAStableOrder()
    {
        await _sut.SetCollectionAsync("Images/b.png", "Two");
        await _sut.SetCollectionAsync("Images/a.png", "One");

        // Otherwise every filing rewrites the whole file and a project under
        // version control shows a diff nobody made.
        Assert.Equal(["Images/a.png", "Images/b.png"],
            (await _sut.LoadAsync()).Entries.Select(e => e.Path));
    }

    [Fact]
    public async Task ACorruptCatalogueLosesTheFilingAndNothingElse()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CatalogFile)!);
        await File.WriteAllTextAsync(CatalogFile, "{ not json");

        // Losing the filing is recoverable. Refusing to open the Gallery over
        // it is not.
        Assert.Empty((await _sut.LoadAsync()).Entries);
    }

    [Fact]
    public async Task WithNoProjectOpenNothingIsWritten()
    {
        _project.ProjectRoot.Returns((string?)null);

        var catalog = await new PictureCatalogService(_project, new FileService())
            .SetCollectionAsync("Images/keep.png", "References");

        Assert.Empty((await _sut.LoadAsync()).Entries);
        Assert.Single(catalog.Entries);
    }

    // ─── The vocabulary already in use ───────────────────────────────

    [Fact]
    public async Task TheCollectionsAndTagsInUseAreListedOnce()
    {
        await _sut.SetCollectionAsync("Images/a.png", "References");
        await _sut.SetCollectionAsync("Images/b.png", "references");
        await _sut.SetTagsAsync("Images/a.png", ["coast", "ruins"]);
        await _sut.SetTagsAsync("Images/b.png", ["Coast"]);

        var catalog = await _sut.LoadAsync();

        // A picker offering "References" and "references" as two choices is a
        // picker that guarantees the writer files into both.
        Assert.Equal(["References"], PictureCatalogService.Collections(catalog));
        Assert.Equal(["coast", "ruins"], PictureCatalogService.Tags(catalog));
    }

    [Fact]
    public async Task NothingFiledIsAnEmptyVocabulary()
    {
        var catalog = await _sut.LoadAsync();

        Assert.Empty(PictureCatalogService.Collections(catalog));
        Assert.Empty(PictureCatalogService.Tags(catalog));
    }

    [Fact]
    public async Task ClearingTheTagsClearsThem()
    {
        await _sut.SetTagsAsync("Images/a.png", ["coast"]);

        await _sut.SetTagsAsync("Images/a.png", null);

        Assert.Empty((await _sut.LoadAsync()).Entries);
    }
}
