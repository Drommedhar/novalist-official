using System.IO.Compression;
using System.Text;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// The contents list, and the Word file a publisher sent back.
///
/// EPUB always emitted a flat chapter list with an English heading and nothing
/// to configure, and DOCX always wrote Novalist's own styles - so an agent's
/// house style had to be reapplied by hand after every single export.
/// </summary>
public class ExportTocTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly IProjectService _project = Substitute.For<IProjectService>();

    public void Dispose() => _dir.Dispose();

    // ── Contents depth ──

    [Fact]
    public async Task ByDefaultTheContentsListsChaptersAndNotScenes()
    {
        var nav = await NavAsync(new ExportOptions());

        Assert.Contains("chapter-1.xhtml", nav);
        Assert.DoesNotContain("#scene-", nav);
    }

    [Fact]
    public async Task DepthTwoNestsTheScenesUnderTheirChapter()
    {
        var nav = await NavAsync(new ExportOptions { TocDepth = 2 });

        Assert.Contains("chapter-1.xhtml#scene-1", nav);
        Assert.Contains("The Arrival", nav);
        // Nested, not flattened alongside the chapters: a reading system draws
        // the indent from the markup and nothing else.
        Assert.Contains("<ol>", nav[nav.IndexOf("chapter-1.xhtml", StringComparison.Ordinal)..]);
    }

    [Fact]
    public async Task AnUntitledSceneIsNotListed()
    {
        var nav = await NavAsync(new ExportOptions { TocDepth = 2 });

        // There is nothing to call it, and "Scene 2" is noise, not navigation.
        Assert.DoesNotContain("#scene-2", nav);
    }

    [Fact]
    public async Task AScenesAnchorExistsEvenWhenTheLayoutPrintsNoHeading()
    {
        var chapter = await ChapterXhtmlAsync(new ExportOptions { TocDepth = 2 });

        // No built-in layout prints scene titles. Without an invisible anchor,
        // choosing "chapters and scenes" would silently do nothing on a novel.
        Assert.Contains("id=\"scene-1\"", chapter);
    }

    [Fact]
    public async Task NoAnchorsAreWrittenWhenTheContentsIsFlat()
    {
        var chapter = await ChapterXhtmlAsync(new ExportOptions());

        Assert.DoesNotContain("id=\"scene-1\"", chapter);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    [InlineData(9, 2)]
    public void ADepthTheWritersCannotRenderIsClamped(int asked, int expected) =>
        Assert.Equal(expected, new ExportOptions { TocDepth = asked }.EffectiveTocDepth);

    [Fact]
    public async Task TheNcxDeclaresTheDepthItActuallyHas()
    {
        Assert.Contains(
            "name=\"dtb:depth\" content=\"2\"",
            await EntryAsync(new ExportOptions { TocDepth = 2 }, "OEBPS/toc.ncx"));
        Assert.Contains(
            "name=\"dtb:depth\" content=\"1\"",
            await EntryAsync(new ExportOptions(), "OEBPS/toc.ncx"));
    }

    [Fact]
    public async Task PlayOrderCountsEveryPointOnceInReadingOrder()
    {
        var ncx = await EntryAsync(new ExportOptions { TocDepth = 2 }, "OEBPS/toc.ncx");

        // The chapter is read before the scene inside it, so it takes the lower
        // number - even though the scene's markup is generated first.
        var chapterAt = ncx.IndexOf("playOrder=\"1\"", StringComparison.Ordinal);
        var sceneAt = ncx.IndexOf("playOrder=\"2\"", StringComparison.Ordinal);
        Assert.True(chapterAt >= 0 && sceneAt > chapterAt);
        Assert.Contains("chapter-1.xhtml#scene-1", ncx);
    }

    // ── Contents heading ──

    [Fact]
    public async Task TheContentsHeadingCanBeSaid()
    {
        var nav = await NavAsync(new ExportOptions { TocTitle = "  Inhalt  " });

        // Trimmed, because a heading with a trailing space renders as one.
        Assert.Contains("<h1>Inhalt</h1>", nav);
        Assert.DoesNotContain("Table of Contents", nav);
    }

    [Fact]
    public async Task SayingNothingKeepsTheEnglishHeading()
    {
        Assert.Contains("<h1>Table of Contents</h1>", await NavAsync(new ExportOptions()));
        Assert.Contains("<h1>Table of Contents</h1>", await NavAsync(new ExportOptions { TocTitle = "   " }));
    }

    [Fact]
    public async Task ATranslatedLabelIsUsedWhenTheWriterNamedNoHeading()
    {
        var nav = await NavAsync(new ExportOptions
        {
            Labels = new Dictionary<string, string> { ["tableOfContents"] = "Inhaltsverzeichnis" }
        });

        Assert.Contains("<h1>Inhaltsverzeichnis</h1>", nav);
    }

    // ── Reference document ──

    [Fact]
    public async Task AReferenceDocumentsStylesReplaceOurs()
    {
        var reference = WriteReferenceDocx(
            "<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"
            + "<w:style w:styleId=\"HouseBody\"/></w:styles>");

        var styles = await EntryAsync(
            new ExportOptions { Format = ExportFormat.Docx, ReferenceDocPath = reference },
            "word/styles.xml", ".docx");

        Assert.Contains("HouseBody", styles);
    }

    [Fact]
    public async Task WithoutAReferenceDocumentOurStylesAreWritten()
    {
        var styles = await EntryAsync(
            new ExportOptions { Format = ExportFormat.Docx }, "word/styles.xml", ".docx");

        Assert.Contains("<w:styles", styles);
        Assert.DoesNotContain("HouseBody", styles);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NoPathIsNoReference(string path) =>
        Assert.Null(ExportService.ReadReferenceStyles(path));

    [Fact]
    public void APathToNothingIsNoReference() =>
        Assert.Null(ExportService.ReadReferenceStyles(_dir.Combine("gone.docx")));

    [Fact]
    public void AFileThatIsNotAZipIsNoReference()
    {
        var path = _dir.Combine("not-really.docx");
        File.WriteAllText(path, "This is a text file someone renamed.");

        // A bad reference document is a reason to fall back to our styles,
        // never a reason to fail the export the writer asked for.
        Assert.Null(ExportService.ReadReferenceStyles(path));
    }

    [Fact]
    public void ADocxWithNoStylesPartIsNoReference()
    {
        var path = _dir.Combine("bare.docx");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            zip.CreateEntry("word/document.xml");

        Assert.Null(ExportService.ReadReferenceStyles(path));
    }

    [Fact]
    public void APartThatIsNotAStylesPartIsNoReference()
    {
        // Right name, wrong contents. Writing it would produce a file Word
        // refuses to open at all, which is worse than the wrong font.
        Assert.Null(ExportService.ReadReferenceStyles(
            WriteReferenceDocx("<html><body>Not styles.</body></html>")));
    }

    // ── Harness ──

    private string WriteReferenceDocx(string stylesXml)
    {
        var path = _dir.Combine($"reference-{Guid.NewGuid():N}.docx");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry("word/styles.xml").Open(), Encoding.UTF8);
        writer.Write(stylesXml);
        return path;
    }

    private async Task<string> EntryAsync(
        ExportOptions options, string entry, string extension = ".epub")
    {
        var chapter = new ChapterData { Title = "One", Order = 1 };
        var first = new SceneData
        {
            Id = "s1", Title = "The Arrival", Order = 1, ChapterGuid = chapter.Guid
        };
        var second = new SceneData { Id = "s2", Title = "", Order = 2, ChapterGuid = chapter.Guid };
        _project.ReadSceneContentAsync(chapter, Arg.Any<SceneData>()).Returns("<p>Story.</p>");
        _project.GetChaptersOrdered().Returns([chapter]);
        _project.GetScenesForChapter(chapter.Guid).Returns([first, second]);
        _project.ActiveBook.Returns(new BookData());

        options.Title = "T";
        options.SelectedChapterGuids = [chapter.Guid];
        var outPath = _dir.Combine($"book-{Guid.NewGuid():N}{extension}");
        await new ExportService(_project).ExportAsync(options, outPath);

        using var zip = ZipFile.OpenRead(outPath);
        using var reader = new StreamReader(zip.GetEntry(entry)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private Task<string> NavAsync(ExportOptions options) => EntryAsync(options, "OEBPS/nav.xhtml");

    private Task<string> ChapterXhtmlAsync(ExportOptions options)
        => EntryAsync(options, "OEBPS/chapter-1.xhtml");

    // ── Which parts of a Codex entry leave the project ──

    [Fact]
    public void ByDefaultEveryPartTravels()
    {
        var options = new ExportOptions();

        // Null means all of them, which is what every codex export did before
        // there was anything to choose.
        Assert.True(options.IncludesPart("images"));
        Assert.True(options.IncludesPart("fields"));
        Assert.True(options.IncludesPart("relationships"));
        Assert.True(options.IncludesSection("Secrets"));
    }

    [Fact]
    public void NamingPartsLeavesTheOthersBehind()
    {
        var options = new ExportOptions { CodexParts = ["fields"] };

        Assert.True(options.IncludesPart("fields"));
        Assert.False(options.IncludesPart("images"));
        Assert.False(options.IncludesSection("Secrets"));
    }

    [Fact]
    public void APartNameIsNotCaseSensitive()
        => Assert.True(new ExportOptions { CodexParts = ["Images"] }.IncludesPart("images"));

    [Fact]
    public void NamingNoPartsCarriesNothing()
    {
        // An empty list is a real answer: the writer unticked everything.
        var options = new ExportOptions { CodexParts = [] };

        Assert.False(options.IncludesPart("fields"));
    }

    [Fact]
    public void SectionsCanBeNamedOneByOne()
    {
        var options = new ExportOptions
        {
            CodexParts = ["sections"],
            SelectedSectionTitles = ["Appearance"]
        };

        // "Appearance but not Secrets" is the whole point: a series bible goes
        // out to readers and a spoiler section does not.
        Assert.True(options.IncludesSection("Appearance"));
        Assert.True(options.IncludesSection("appearance"));
        Assert.False(options.IncludesSection("Secrets"));
    }

    [Fact]
    public void SectionsOffBeatsAnyTitleList()
    {
        var options = new ExportOptions
        {
            CodexParts = ["fields"],
            SelectedSectionTitles = ["Appearance"]
        };

        Assert.False(options.IncludesSection("Appearance"));
    }

    [Fact]
    public async Task APortraitIsLeftOutWhenPicturesAreOff()
    {
        var markdown = await CodexAsync(new ExportOptions { CodexParts = ["fields", "sections"] });

        Assert.DoesNotContain("![", markdown);
        Assert.Contains("The Rookery", markdown);
    }

    [Fact]
    public async Task ASpoilerSectionCanBeHeldBack()
    {
        var markdown = await CodexAsync(new ExportOptions
        {
            SelectedSectionTitles = ["Appearance"]
        });

        Assert.Contains("Appearance", markdown);
        Assert.DoesNotContain("Secrets", markdown);
        Assert.DoesNotContain("she is the one who did it", markdown);
    }

    [Fact]
    public async Task WithNothingSaidEverySectionTravels()
    {
        var markdown = await CodexAsync(new ExportOptions());

        Assert.Contains("Appearance", markdown);
        Assert.Contains("Secrets", markdown);
    }

    /// <summary>A one-place Codex export, as markdown.</summary>
    private async Task<string> CodexAsync(ExportOptions options)
    {
        var place = new LocationData { Name = "The Rookery", Description = "A tower." };
        place.Images.Add(new EntityImage { Name = "tower", Path = "images/tower.png" });
        place.Sections.Add(new EntitySection { Title = "Appearance", Content = "<p>Tall.</p>" });
        place.Sections.Add(
            new EntitySection { Title = "Secrets", Content = "<p>she is the one who did it</p>" });

        var entities = Substitute.For<IEntityService>();
        entities.LoadCharactersAsync().Returns(new List<CharacterData>());
        entities.LoadLocationsAsync().Returns([place]);
        entities.LoadItemsAsync().Returns(new List<ItemData>());
        entities.LoadLoreAsync().Returns(new List<LoreData>());
        entities.GetCustomEntityTypes().Returns([]);
        _project.ActiveBook.Returns(new BookData());

        options.Format = ExportFormat.Codex;
        options.Title = "Bible";
        var outPath = _dir.Combine($"codex-{Guid.NewGuid():N}.md");
        await new ExportService(_project, entities).ExportCodexAsync(options, outPath);
        return await File.ReadAllTextAsync(outPath);
    }
}
