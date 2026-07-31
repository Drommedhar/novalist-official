using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Reading a book that is not the open one.
///
/// Every other path on the project service resolves against the active book,
/// which is right for editing and wrong for anything that reads across a
/// multi-book project. Doing it by switching the active book mid-run mutates
/// state the watcher and the UI both read, and leaves the app on the wrong book
/// if anything throws.
/// </summary>
public sealed class ProjectServiceOtherBookTests : IDisposable
{
    private readonly string _root;
    private readonly ProjectService _sut;

    public ProjectServiceOtherBookTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-books-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _sut = new ProjectService(new FileService());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>Two books, each with a chapter and a scene, the first left open.</summary>
    private async Task<(BookData First, BookData Second)> SeedAsync()
    {
        await _sut.CreateProjectAsync(_root, "Series", "Book One");
        await _sut.LoadProjectAsync(_sut.ProjectRoot!);

        var chapterOne = await _sut.CreateChapterAsync("One");
        var sceneOne = await _sut.CreateSceneAsync(chapterOne.Guid, "Opening");
        await _sut.WriteSceneContentAsync(chapterOne, sceneOne, "<p>The first book.</p>");

        var second = await _sut.CreateBookAsync("Book Two");
        await _sut.SwitchBookAsync(second.Id);
        var chapterTwo = await _sut.CreateChapterAsync("Later");
        var sceneTwo = await _sut.CreateSceneAsync(chapterTwo.Guid, "Elsewhere");
        await _sut.WriteSceneContentAsync(chapterTwo, sceneTwo, "<p>The second book.</p>");

        // Back to the first, so the second is genuinely "not the open one".
        var first = _sut.CurrentProject!.Books.Single(b => b.Name == "Book One");
        await _sut.SwitchBookAsync(first.Id);
        return (first, _sut.CurrentProject!.Books.Single(b => b.Name == "Book Two"));
    }

    [Fact]
    public async Task TheClosedBooksSceneIsReadableWithoutSwitchingToIt()
    {
        var (_, second) = await SeedAsync();
        var openBefore = _sut.ActiveBook!.Id;

        var manifest = await _sut.LoadScenesManifestForAsync(second);
        var chapter = second.Chapters.Single();
        var scene = manifest!.Chapters[chapter.Guid].Single();
        var text = await _sut.ReadSceneContentForAsync(second, chapter, scene);

        Assert.Contains("The second book.", text);
        // And nothing about the open book moved.
        Assert.Equal(openBefore, _sut.ActiveBook!.Id);
        Assert.Equal("Book One", _sut.ActiveBook.Name);
    }

    [Fact]
    public async Task TheOpenBookReadsThroughTheSameCallsAsAnyOther()
    {
        var (first, _) = await SeedAsync();

        var manifest = await _sut.LoadScenesManifestForAsync(first);
        var chapter = first.Chapters.Single();
        var scene = manifest!.Chapters[chapter.Guid].Single();

        Assert.Contains("The first book.",
            await _sut.ReadSceneContentForAsync(first, chapter, scene));
    }

    [Fact]
    public async Task EachBooksPathsAreItsOwn()
    {
        var (first, second) = await SeedAsync();

        Assert.NotEqual(_sut.BookRootFor(first), _sut.BookRootFor(second));
        Assert.StartsWith(_sut.BookRootFor(second)!, _sut.DraftRootFor(second)!);
        Assert.StartsWith(
            _sut.DraftRootFor(second)!,
            _sut.ChapterFolderPathFor(second, second.Chapters.Single())!);
    }

    [Fact]
    public async Task ASceneFileThatIsNotThereReadsAsEmpty()
    {
        var (_, second) = await SeedAsync();

        var text = await _sut.ReadSceneContentForAsync(
            second, second.Chapters.Single(), new SceneData { FileName = "gone.md" });

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public async Task ABookWithNoManifestYetIsNothingRatherThanAnError()
    {
        await SeedAsync();
        // A book record pointing at a folder that was never written.
        var ghost = new BookData { Name = "Ghost", FolderName = "Ghost" };

        Assert.Null(await _sut.LoadScenesManifestForAsync(ghost));
    }

    [Fact]
    public async Task ABookInTheOldLayoutIsStillReadable()
    {
        await SeedAsync();
        // Before drafts existed, a book kept its manifest under .book/ and its
        // chapters directly beneath itself. Projects made then still open.
        var legacy = new BookData { Name = "Old", FolderName = "Old", ChapterFolder = "Chapters" };
        var chapter = new ChapterData { Guid = "c-old", FolderName = "One", Title = "One" };
        legacy.Chapters.Add(chapter);

        var bookRoot = _sut.BookRootFor(legacy)!;
        Directory.CreateDirectory(Path.Combine(bookRoot, ".book"));
        Directory.CreateDirectory(Path.Combine(bookRoot, "Chapters", "One"));
        await File.WriteAllTextAsync(
            Path.Combine(bookRoot, ".book", "scenes.json"),
            """{"chapters":{"c-old":[{"id":"s-old","fileName":"scene.md","title":"Old","order":0}]}}""");
        await File.WriteAllTextAsync(
            Path.Combine(bookRoot, "Chapters", "One", "scene.md"), "<p>From the old layout.</p>");

        var manifest = await _sut.LoadScenesManifestForAsync(legacy);
        var scene = manifest!.Chapters["c-old"].Single();

        Assert.Contains("From the old layout.",
            await _sut.ReadSceneContentForAsync(legacy, chapter, scene));
    }

    [Fact]
    public async Task ABookWithADraftButNoManifestFallsThroughRatherThanFailing()
    {
        var (_, second) = await SeedAsync();
        // The draft folder is there and the manifest inside it is not - a
        // half-written book, or one whose scenes.json somebody removed.
        File.Delete(Path.Combine(_sut.DraftRootFor(second)!, "scenes.json"));

        Assert.NotNull(_sut.DraftRootFor(second));
        Assert.Null(await _sut.LoadScenesManifestForAsync(second));
    }

    [Fact]
    public void WithNoProjectOpenEveryPathIsNothing()
    {
        var fresh = new ProjectService(new FileService());
        var book = new BookData { Name = "Anything", FolderName = "Anything" };

        Assert.Null(fresh.BookRootFor(book));
        Assert.Null(fresh.DraftRootFor(book));
        Assert.Null(fresh.ChapterFolderPathFor(book, new ChapterData()));
    }

    [Fact]
    public async Task ABookWithNoActiveDraftFallsBackToItsOwnFolder()
    {
        await SeedAsync();
        var draftless = new BookData { Name = "Old", FolderName = "Old", ChapterFolder = "Chapters" };

        Assert.Null(_sut.DraftRootFor(draftless));
        // The pre-drafts layout kept chapters directly under the book.
        Assert.StartsWith(
            _sut.BookRootFor(draftless)!,
            _sut.ChapterFolderPathFor(draftless, new ChapterData { FolderName = "One" })!);
    }
}
