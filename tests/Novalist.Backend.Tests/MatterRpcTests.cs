using System.IO.Compression;
using System.Text;
using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Front and back matter: typed pages around the story, and their route into an
/// exported book.
/// </summary>
public sealed class MatterRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly MatterRpc _rpc;

    public MatterRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-matter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "MatterNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new MatterRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private async Task<MatterDto> AddAsync(string kind, string content)
    {
        var list = await _rpc.CreateAsync(kind);
        var created = list.Last(m => m.Kind == kind);
        await _rpc.UpdateAsync(created.Id, null, content, null, null, null);
        return _rpc.List().Single(m => m.Id == created.Id);
    }

    [Fact]
    public void Kinds_IncludeTheConventionalPages()
    {
        var kinds = _rpc.Kinds();

        Assert.Contains("Dedication", kinds);
        Assert.Contains("Copyright", kinds);
        Assert.Contains("Acknowledgments", kinds);
        Assert.Contains("AboutTheAuthor", kinds);
    }

    [Fact]
    public async Task Create_PutsEachKindWhereItConventionallyBelongs()
    {
        await _rpc.CreateAsync("Dedication");
        await _rpc.CreateAsync("Acknowledgments");

        var list = _rpc.List();

        Assert.Equal("Front", list.Single(m => m.Kind == "Dedication").Placement);
        Assert.Equal("Back", list.Single(m => m.Kind == "Acknowledgments").Placement);
    }

    [Fact]
    public async Task Create_TableOfContentsDefaultsFollowConvention()
    {
        await _rpc.CreateAsync("Copyright");
        await _rpc.CreateAsync("Prologue");

        var list = _rpc.List();

        // A copyright page in the contents is a mistake; a prologue belongs there.
        Assert.False(list.Single(m => m.Kind == "Copyright").InTableOfContents);
        Assert.True(list.Single(m => m.Kind == "Prologue").InTableOfContents);
    }

    [Fact]
    public async Task Create_UnknownKind_BecomesCustom()
    {
        var list = await _rpc.CreateAsync("NotARealKind");
        Assert.Equal("Custom", list.Single().Kind);
    }

    [Fact]
    public async Task Update_ChangesTitleContentAndFlags()
    {
        var created = (await _rpc.CreateAsync("Dedication")).Single();

        await _rpc.UpdateAsync(created.Id, "For R", "<p>Who read it first.</p>", false, true, "Back");

        var updated = _rpc.List().Single();
        Assert.Equal("For R", updated.Title);
        Assert.Contains("Who read it first", updated.Content);
        Assert.False(updated.Included);
        Assert.True(updated.InTableOfContents);
        Assert.Equal("Back", updated.Placement);
    }

    [Fact]
    public async Task Update_UnknownId_IsANoOp()
    {
        await _rpc.CreateAsync("Dedication");
        var before = _rpc.List();

        var after = await _rpc.UpdateAsync("nope", "x", "y", true, true, "Back");

        Assert.Equal(before.Length, after.Length);
        Assert.Equal(before[0].Title, after[0].Title);
    }

    [Fact]
    public async Task Reorder_MovesWithinItsPlacementGroup()
    {
        await _rpc.CreateAsync("Dedication");
        await _rpc.CreateAsync("Epigraph");
        var list = _rpc.List();
        var second = list[1];

        var reordered = await _rpc.ReorderAsync(second.Id, -1);

        Assert.Equal(second.Id, reordered[0].Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5)]
    public async Task Reorder_OutOfRangeOrZero_ChangesNothing(int delta)
    {
        await _rpc.CreateAsync("Dedication");
        var before = _rpc.List()[0].Id;

        var after = await _rpc.ReorderAsync(before, delta);

        Assert.Equal(before, after[0].Id);
    }

    [Fact]
    public async Task Reorder_UnknownId_ChangesNothing()
    {
        await _rpc.CreateAsync("Dedication");
        Assert.Single(await _rpc.ReorderAsync("nope", 1));
    }

    [Fact]
    public async Task Delete_RemovesThePage()
    {
        var created = (await _rpc.CreateAsync("Dedication")).Single();

        Assert.Empty(await _rpc.DeleteAsync(created.Id));
    }

    [Fact]
    public async Task Delete_UnknownId_ChangesNothing()
    {
        await _rpc.CreateAsync("Dedication");
        Assert.Single(await _rpc.DeleteAsync("nope"));
    }

    [Fact]
    public async Task ShowsHeadingByDefault_IsFalseForPagesThatCarryNoHeading()
    {
        await _rpc.CreateAsync("Dedication");
        await _rpc.CreateAsync("Foreword");

        var list = _rpc.List();

        Assert.False(list.Single(m => m.Kind == "Dedication").ShowsHeadingByDefault);
        Assert.True(list.Single(m => m.Kind == "Foreword").ShowsHeadingByDefault);
    }

    // ── Reaching the exported book ──

    private async Task<string> ExportEpubAsync()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        await _workspace.WriteSceneAsync(chapter.Guid, scene.Id, "<p>Story.</p>", "Story.");

        var outPath = Path.Combine(_root, "book.epub");
        await new ExportRpc(_workspace).RunAsync(
            "Epub", outPath, "T", "A", true, [chapter.Guid]);
        return outPath;
    }

    private static string ReadEntry(string epub, string entry)
    {
        using var zip = ZipFile.OpenRead(epub);
        using var reader = new StreamReader(zip.GetEntry(entry)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [Fact]
    public async Task Export_FrontMatterPrecedesTheStoryInTheSpine()
    {
        await AddAsync("Dedication", "<p>For R.</p>");

        var opf = ReadEntry(await ExportEpubAsync(), "OEBPS/content.opf");

        var matterAt = opf.IndexOf("idref=\"matter-1\"", StringComparison.Ordinal);
        var chapterAt = opf.IndexOf("idref=\"chapter-1\"", StringComparison.Ordinal);
        Assert.True(matterAt >= 0 && matterAt < chapterAt);
    }

    [Fact]
    public async Task Export_BackMatterFollowsTheStory()
    {
        await AddAsync("Acknowledgments", "<p>Thanks.</p>");

        var opf = ReadEntry(await ExportEpubAsync(), "OEBPS/content.opf");

        var matterAt = opf.IndexOf("idref=\"matter-1\"", StringComparison.Ordinal);
        var chapterAt = opf.IndexOf("idref=\"chapter-1\"", StringComparison.Ordinal);
        Assert.True(matterAt > chapterAt);
    }

    [Fact]
    public async Task Export_MatterPageCarriesItsKindAsEpubType()
    {
        await AddAsync("Copyright", "<p>All rights reserved.</p>");

        var page = ReadEntry(await ExportEpubAsync(), "OEBPS/matter-1.xhtml");

        Assert.Contains("epub:type=\"copyright-page\"", page);
        Assert.Contains("matter-copyright", page);
        Assert.Contains("All rights reserved", page);
    }

    [Fact]
    public async Task Export_KindsWithNoConventionalHeadingPrintNone()
    {
        await AddAsync("Dedication", "<p>For R.</p>");

        var page = ReadEntry(await ExportEpubAsync(), "OEBPS/matter-1.xhtml");

        // A dedication with the word "Dedication" over it is not how books are set.
        Assert.DoesNotContain("matter-title", page);
    }

    [Fact]
    public async Task Export_KindsWithAConventionalHeadingPrintIt()
    {
        await AddAsync("Foreword", "<p>Some context.</p>");

        var page = ReadEntry(await ExportEpubAsync(), "OEBPS/matter-1.xhtml");

        Assert.Contains("<h1 class=\"matter-title\">Foreword</h1>", page);
    }

    [Fact]
    public async Task Export_ExplicitTitleOverridesTheDefaultHeading()
    {
        var created = await AddAsync("Dedication", "<p>For R.</p>");
        await _rpc.UpdateAsync(created.Id, "For Rachel", null, null, null, null);

        var page = ReadEntry(await ExportEpubAsync(), "OEBPS/matter-1.xhtml");

        Assert.Contains("For Rachel", page);
    }

    [Fact]
    public async Task Export_ExcludedPagesAreLeftOut()
    {
        var created = await AddAsync("Dedication", "<p>For R.</p>");
        await _rpc.UpdateAsync(created.Id, null, null, false, null, null);

        using var zip = ZipFile.OpenRead(await ExportEpubAsync());
        Assert.Null(zip.GetEntry("OEBPS/matter-1.xhtml"));
    }

    [Fact]
    public async Task Export_EmptyPagesAreLeftOut()
    {
        // A page that exists but has never been written is not a book page yet.
        await _rpc.CreateAsync("Dedication");

        using var zip = ZipFile.OpenRead(await ExportEpubAsync());
        Assert.Null(zip.GetEntry("OEBPS/matter-1.xhtml"));
    }

    [Fact]
    public async Task Export_OnlyTocMarkedMatterIsListedInTheContents()
    {
        await AddAsync("Copyright", "<p>Small print.</p>");
        await AddAsync("Foreword", "<p>Context.</p>");

        var nav = ReadEntry(await ExportEpubAsync(), "OEBPS/nav.xhtml");

        Assert.Contains("Foreword", nav);
        Assert.DoesNotContain("Copyright", nav);
    }

    [Fact]
    public async Task Export_MatterReachesDocxToo()
    {
        await AddAsync("Dedication", "<p>For R.</p>");
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        await _workspace.WriteSceneAsync(chapter.Guid, scene.Id, "<p>Story.</p>", "Story.");

        var outPath = Path.Combine(_root, "book.docx");
        await new ExportRpc(_workspace).RunAsync("Docx", outPath, "T", "A", true, [chapter.Guid]);

        using var zip = ZipFile.OpenRead(outPath);
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open(), Encoding.UTF8);
        var document = reader.ReadToEnd();

        Assert.Contains("For R.", document);
        Assert.True(
            document.IndexOf("For R.", StringComparison.Ordinal)
            < document.IndexOf("Story.", StringComparison.Ordinal));
    }
}
