using System.IO.Compression;
using System.Text;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// User-authored export layouts, and the writers honouring them.
///
/// The rule worth guarding is that built-ins stay read-only: a preset named
/// after a submission standard that no longer matches it is worse than no
/// preset, because nothing would tell the writer.
/// </summary>
public class ExportPresetServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProjectService _projects = new(new FileService());
    private readonly ExportPresetService _sut;

    public ExportPresetServiceTests()
    {
        _sut = new ExportPresetService(_projects);
    }

    public void Dispose() => _dir.Dispose();

    private Task BookAsync() => _projects.CreateProjectAsync(_dir.Path, "P", "Book");

    // ── The list ──

    [Fact]
    public async Task TheFourBuiltInsAreAlwaysThere()
    {
        await BookAsync();

        Assert.Equal(ExportPresets.All.Count, _sut.All().Count);
        Assert.All(_sut.All(), p => Assert.False(p.IsCustom));
    }

    [Fact]
    public async Task ADuplicateAppearsAfterTheBuiltIns()
    {
        await BookAsync();

        await _sut.DuplicateAsync(ExportPresets.DefaultId, "Mine");

        Assert.Equal("Mine", _sut.All().Last().DisplayName);
        Assert.True(_sut.All().Last().IsCustom);
    }

    [Fact]
    public async Task ADuplicateGetsItsOwnIdRatherThanShadowingTheSource()
    {
        await BookAsync();

        var copy = await _sut.DuplicateAsync(ExportPresets.ShunnId, "Mine");

        Assert.NotEqual(ExportPresets.ShunnId, copy!.Id);
        Assert.Equal(copy.Id, _sut.ById(copy.Id).Id);
    }

    [Fact]
    public async Task ADuplicateCarriesTheSourcesSettings()
    {
        // An empty layout would be a worse starting point than any of the four
        // that already work.
        await BookAsync();
        var source = ExportPresets.GetById(ExportPresets.ShunnId);

        var copy = await _sut.DuplicateAsync(ExportPresets.ShunnId, "Mine");

        Assert.Equal(source.BodyFontFamily, copy!.BodyFontFamily);
        Assert.Equal(source.DoubleSpaced, copy.DoubleSpaced);
    }

    [Fact]
    public async Task ADuplicateWithNoNameSaysWhatItCameFrom()
    {
        await BookAsync();

        var copy = await _sut.DuplicateAsync(ExportPresets.DefaultId, "");

        Assert.Contains("copy", copy!.DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DuplicatingWithNoBookOpenDoesNothing()
    {
        Assert.Null(await _sut.DuplicateAsync(ExportPresets.DefaultId, "Mine"));
    }

    // ── Editing ──

    [Fact]
    public async Task AUserPresetCanBeEdited()
    {
        await BookAsync();
        var copy = await _sut.DuplicateAsync(ExportPresets.DefaultId, "Mine");

        await _sut.SaveAsync(copy! with { SceneSeparator = "~~~" });

        Assert.Equal("~~~", _sut.ById(copy!.Id).SceneSeparator);
    }

    [Fact]
    public async Task ABuiltInCannotBeEdited()
    {
        await BookAsync();
        var builtIn = ExportPresets.GetById(ExportPresets.DefaultId);

        Assert.False(await _sut.SaveAsync(builtIn with { SceneSeparator = "~~~" }));
        Assert.Equal(builtIn.SceneSeparator, _sut.ById(ExportPresets.DefaultId).SceneSeparator);
    }

    [Fact]
    public async Task AUserPresetCanBeDeleted()
    {
        await BookAsync();
        var copy = await _sut.DuplicateAsync(ExportPresets.DefaultId, "Mine");

        Assert.True(await _sut.DeleteAsync(copy!.Id));
        Assert.DoesNotContain(_sut.All(), p => p.Id == copy.Id);
    }

    [Fact]
    public async Task ABuiltInCannotBeDeleted()
    {
        await BookAsync();

        Assert.False(await _sut.DeleteAsync(ExportPresets.DefaultId));
        Assert.Contains(_sut.All(), p => p.Id == ExportPresets.DefaultId);
    }

    [Fact]
    public async Task AnIdThatNamesNothingFallsBackToTheDefault()
    {
        // An export against a deleted preset should still produce a readable
        // file rather than throwing at the writer.
        await BookAsync();

        Assert.Equal(ExportPresets.DefaultId, _sut.ById("gone").Id);
    }

    [Fact]
    public async Task UserPresetsSurviveAReload()
    {
        await BookAsync();
        await _sut.DuplicateAsync(ExportPresets.DefaultId, "Mine");
        var root = _projects.ProjectRoot!;

        var reopened = new ProjectService(new FileService());
        await reopened.LoadProjectAsync(root);

        Assert.Contains(new ExportPresetService(reopened).All(), p => p.DisplayName == "Mine");
    }

    // ── The chapter heading format ──

    [Theory]
    [InlineData("{title}", 3, "The Fall", "The Fall")]
    [InlineData("Chapter {number}", 3, "The Fall", "Chapter 3")]
    [InlineData("Chapter {number}: {title}", 3, "The Fall", "Chapter 3: The Fall")]
    [InlineData("", 3, "The Fall", "The Fall")]
    public void TheChapterHeadingFormatIsSubstituted(
        string format, int number, string title, string expected)
    {
        var preset = ExportPresets.GetById(ExportPresets.DefaultId) with
        {
            ChapterTitleFormat = format
        };

        Assert.Equal(expected, preset.ChapterHeading(number, title));
    }

    // ── The writers honouring it ──

    private readonly IProjectService _mock = Substitute.For<IProjectService>();

    private async Task<string> EpubChapterAsync(ExportPreset preset)
    {
        var chapter = new ChapterData { Title = "The Fall", Order = 1 };
        var scene = new SceneData { Title = "The Fire", Order = 1, ChapterGuid = chapter.Guid };
        _mock.ReadSceneContentAsync(chapter, scene).Returns("<p>text</p>");
        _mock.GetChaptersOrdered().Returns([chapter]);
        _mock.GetScenesForChapter(chapter.Guid).Returns([scene]);
        _mock.ActiveBook.Returns(new BookData { ExportPresets = [preset] });

        var path = Path.Combine(_dir.Path, $"out-{Guid.NewGuid():N}.epub");
        await new ExportService(_mock).ExportAsync(
            new ExportOptions
            {
                Format = ExportFormat.Epub,
                Title = "Book",
                PresetId = preset.Id,
                SelectedChapterGuids = [chapter.Guid]
            },
            path);

        using var zip = ZipFile.OpenRead(path);
        using var reader = new StreamReader(zip.GetEntry("OEBPS/chapter-1.xhtml")!.Open(), Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    /// <summary>A user preset to hang assertions off, starting from the default.</summary>
    private static ExportPreset Custom()
        => ExportPresets.GetById(ExportPresets.DefaultId) with { Id = "custom-x", IsCustom = true };

    [Theory]
    [InlineData(ChapterNumberStyle.Arabic, 7, "7")]
    [InlineData(ChapterNumberStyle.RomanUpper, 7, "VII")]
    [InlineData(ChapterNumberStyle.RomanUpper, 1994, "MCMXCIV")]
    [InlineData(ChapterNumberStyle.RomanLower, 4, "iv")]
    [InlineData(ChapterNumberStyle.Words, 7, "Seven")]
    [InlineData(ChapterNumberStyle.Words, 21, "Twenty-one")]
    [InlineData(ChapterNumberStyle.Words, 100, "One Hundred")]
    [InlineData(ChapterNumberStyle.Words, 342, "Three Hundred forty-two")]
    public void ChapterNumbersAreWrittenInTheLayoutsNumerals(
        ChapterNumberStyle style, int number, string expected)
        => Assert.Equal(expected, ExportPreset.FormatNumber(number, style));

    [Theory]
    // Nothing sensible to write: the digit stands in rather than the heading
    // quietly losing its number.
    [InlineData(ChapterNumberStyle.RomanUpper, 0, "0")]
    [InlineData(ChapterNumberStyle.Words, -1, "-1")]
    [InlineData(ChapterNumberStyle.Words, 1000, "1000")]
    public void OutOfRangeChapterNumbersFallBackToDigits(
        ChapterNumberStyle style, int number, string expected)
        => Assert.Equal(expected, ExportPreset.FormatNumber(number, style));

    [Fact]
    public void TheHeadingCanBeSetInCapitals()
    {
        var preset = ExportPresets.GetById(ExportPresets.DefaultId) with
        {
            ChapterTitleFormat = "Chapter {number}: {title}",
            ChapterNumberStyle = ChapterNumberStyle.Words,
            ChapterHeadingUppercase = true
        };

        Assert.Equal("CHAPTER SEVEN: THE FALL", preset.ChapterHeading(7, "The Fall"));
    }

    [Fact]
    public async Task TheEpubUsesTheChapterHeadingFormat()
    {
        var xhtml = await EpubChapterAsync(
            Custom() with { ChapterTitleFormat = "Chapter {number}: {title}" });

        Assert.Contains("Chapter 1: The Fall", xhtml);
    }

    [Fact]
    public async Task SceneTitlesAreOffUnlessTheLayoutAsksForThem()
    {
        var xhtml = await EpubChapterAsync(Custom());

        Assert.DoesNotContain("scene-title", xhtml);
    }

    [Fact]
    public async Task SceneTitlesArePrintedWhenTheLayoutAsks()
    {
        var xhtml = await EpubChapterAsync(Custom() with { ShowSceneTitles = true });

        Assert.Contains("<h3 class=\"scene-title\" id=\"scene-1\">The Fire</h3>", xhtml);
    }

    [Fact]
    public async Task TheLayoutsCssIsAppendedToTheStylesheet()
    {
        var preset = Custom() with { EbookCss = "p { color: rebeccapurple; }" };
        var chapter = new ChapterData { Title = "One", Order = 1 };
        var scene = new SceneData { Title = "S", Order = 1, ChapterGuid = chapter.Guid };
        _mock.ReadSceneContentAsync(chapter, scene).Returns("<p>text</p>");
        _mock.GetChaptersOrdered().Returns([chapter]);
        _mock.GetScenesForChapter(chapter.Guid).Returns([scene]);
        _mock.ActiveBook.Returns(new BookData { ExportPresets = [preset] });

        var path = Path.Combine(_dir.Path, "css.epub");
        await new ExportService(_mock).ExportAsync(
            new ExportOptions
            {
                Format = ExportFormat.Epub,
                Title = "Book",
                PresetId = preset.Id,
                SelectedChapterGuids = [chapter.Guid]
            },
            path);

        using var zip = ZipFile.OpenRead(path);
        using var reader = new StreamReader(zip.GetEntry("OEBPS/styles.css")!.Open(), Encoding.UTF8);
        var css = await reader.ReadToEndAsync();

        // Appended, so the writer's rules win by cascade order.
        Assert.Contains("rebeccapurple", css);
        Assert.True(css.IndexOf("rebeccapurple", StringComparison.Ordinal)
                    > css.IndexOf("h1.chapter-title", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheLayoutsSceneSeparatorIsUsed()
    {
        var preset = Custom() with { SceneSeparator = "~ ~ ~" };
        var chapter = new ChapterData { Title = "One", Order = 1 };
        var first = new SceneData { Title = "A", Order = 1, ChapterGuid = chapter.Guid };
        var second = new SceneData { Title = "B", Order = 2, ChapterGuid = chapter.Guid };
        _mock.ReadSceneContentAsync(chapter, first).Returns("<p>one</p>");
        _mock.ReadSceneContentAsync(chapter, second).Returns("<p>two</p>");
        _mock.GetChaptersOrdered().Returns([chapter]);
        _mock.GetScenesForChapter(chapter.Guid).Returns([first, second]);
        _mock.ActiveBook.Returns(new BookData { ExportPresets = [preset] });

        var path = Path.Combine(_dir.Path, "sep.epub");
        await new ExportService(_mock).ExportAsync(
            new ExportOptions
            {
                Format = ExportFormat.Epub,
                Title = "Book",
                PresetId = preset.Id,
                SelectedChapterGuids = [chapter.Guid]
            },
            path);

        using var zip = ZipFile.OpenRead(path);
        using var reader = new StreamReader(zip.GetEntry("OEBPS/chapter-1.xhtml")!.Open(), Encoding.UTF8);
        Assert.Contains("~ ~ ~", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task ACustomPresetIdResolvesToTheWritersLayoutRatherThanTheDefault()
    {
        // Without CustomPresets on the options, a custom id would silently fall
        // back and the export would look like the default.
        var preset = Custom() with { SceneSeparator = "###" };
        var options = new ExportOptions { PresetId = preset.Id, CustomPresets = [preset] };

        Assert.Equal("###", options.ResolvePreset().SceneSeparator);
        await Task.CompletedTask;
    }
}
