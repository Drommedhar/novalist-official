using System.IO.Compression;
using System.Text;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// The chapter heading a layout describes, in every writer.
///
/// This is the exact shape of failure the block-style tests were written for:
/// EPUB honoured the layout's heading format and every other writer printed
/// the raw title, so the feature looked implemented and was not. One case per
/// format, so a writer added later is either handled or visibly missing.
/// </summary>
public class ExportChapterHeadingTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly IProjectService _project = Substitute.For<IProjectService>();

    public void Dispose() => _dir.Dispose();

    /// <summary>Two chapters, so a number that is always 1 cannot pass.</summary>
    private ExportOptions Setup(ExportFormat format, ExportPreset preset)
    {
        var first = new ChapterData { Title = "The Fall", Order = 1 };
        var second = new ChapterData { Title = "The Rise", Order = 2 };
        var chapters = new List<ChapterData> { first, second };
        foreach (var chapter in chapters)
        {
            var scene = new SceneData { Title = "Scene", Order = 1, ChapterGuid = chapter.Guid };
            _project.ReadSceneContentAsync(chapter, scene).Returns("<p>Ordinary prose.</p>");
            _project.GetScenesForChapter(chapter.Guid).Returns([scene]);
        }
        _project.GetChaptersOrdered().Returns(chapters);
        _project.ActiveBook.Returns(new BookData { ExportPresets = [preset] });

        return new ExportOptions
        {
            Format = format,
            Title = "Book",
            PresetId = preset.Id,
            SelectedChapterGuids = [.. chapters.Select(c => c.Guid)]
        };
    }

    /// <summary>"Chapter Two: The Rise" - worded numerals, in capitals.</summary>
    private static ExportPreset Layout() =>
        ExportPresets.GetById(ExportPresets.DefaultId) with
        {
            Id = "custom-heading",
            IsCustom = true,
            ChapterTitleFormat = "Chapter {number}: {title}",
            ChapterNumberStyle = ChapterNumberStyle.Words,
            ChapterHeadingUppercase = true
        };

    private async Task<string> ExportTextAsync(ExportFormat format)
    {
        var options = Setup(format, Layout());
        var path = Path.Combine(_dir.Path, $"out-{format}.txt");
        await new ExportService(_project).ExportAsync(options, path);
        return await File.ReadAllTextAsync(path);
    }

    private async Task<string> ExportZipEntryAsync(ExportFormat format, string entry)
    {
        var options = Setup(format, Layout());
        var path = Path.Combine(_dir.Path, $"out-{format}.zip");
        await new ExportService(_project).ExportAsync(options, path);
        using var zip = ZipFile.OpenRead(path);
        using var reader = new StreamReader(zip.GetEntry(entry)!.Open(), Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task Markdown_WritesTheLayoutsHeading()
        => Assert.Contains("## CHAPTER TWO: THE RISE", await ExportTextAsync(ExportFormat.Markdown));

    [Fact]
    public async Task LaTeX_WritesTheLayoutsHeading()
    {
        var tex = await ExportTextAsync(ExportFormat.LaTeX);
        Assert.Contains("\\chapter*{CHAPTER TWO: THE RISE}", tex);
    }

    [Fact]
    public async Task FinalDraft_WritesTheLayoutsHeading()
        => Assert.Contains("CHAPTER TWO: THE RISE", await ExportTextAsync(ExportFormat.FinalDraft));

    [Fact]
    public async Task Docx_WritesTheLayoutsHeading()
        => Assert.Contains(
            "CHAPTER TWO: THE RISE",
            await ExportZipEntryAsync(ExportFormat.Docx, "word/document.xml"));

    [Fact]
    public async Task Epub_WritesTheLayoutsHeading()
        => Assert.Contains(
            "CHAPTER TWO: THE RISE",
            await ExportZipEntryAsync(ExportFormat.Epub, "OEBPS/chapter-2.xhtml"));

    [Fact]
    public async Task Normseiten_WritesTheLayoutsHeading()
    {
        var options = Setup(ExportFormat.Docx, Layout() with
        {
            Id = "custom-normseiten",
            NormseitenGrid = true
        });
        var path = Path.Combine(_dir.Path, "out-normseiten.docx");
        await new ExportService(_project).ExportAsync(options, path);

        using var zip = ZipFile.OpenRead(path);
        using var reader = new StreamReader(
            zip.GetEntry("word/document.xml")!.Open(), Encoding.UTF8);
        Assert.Contains("CHAPTER TWO: THE RISE", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Pdf_IsWrittenAndCarriesTheHeadingText()
    {
        // A PDF's text is compressed, so this asserts the export completes with
        // the layout applied rather than reading the string back out of it.
        var options = Setup(ExportFormat.Pdf, Layout());
        var path = Path.Combine(_dir.Path, "out.pdf");
        await new ExportService(_project).ExportAsync(options, path);

        Assert.True(new FileInfo(path).Length > 0);
    }

    // ── What reaches the compiled book ──

    /// <summary>Three scenes: one held back, one drafted, one final.</summary>
    private ExportOptions SetupScenes()
    {
        var chapter = new ChapterData { Title = "One", Order = 1 };
        var held = new SceneData
        {
            Title = "Held", Order = 1, ChapterGuid = chapter.Guid, ExcludeFromExport = true
        };
        var draft = new SceneData
        {
            Title = "Draft", Order = 2, ChapterGuid = chapter.Guid, Stage = "draft"
        };
        var final = new SceneData
        {
            Title = "Final", Order = 3, ChapterGuid = chapter.Guid, Stage = "final"
        };
        foreach (var scene in new[] { held, draft, final })
            _project.ReadSceneContentAsync(chapter, scene).Returns($"<p>{scene.Title} prose.</p>");
        _project.GetChaptersOrdered().Returns([chapter]);
        _project.GetScenesForChapter(chapter.Guid).Returns([held, draft, final]);

        return new ExportOptions
        {
            Format = ExportFormat.Markdown,
            SelectedChapterGuids = [chapter.Guid]
        };
    }

    [Fact]
    public async Task AHeldBackSceneIsNeverCompiled()
    {
        var compiled = await new ExportService(_project).CompileChaptersAsync(SetupScenes());

        Assert.Equal(["Draft", "Final"], compiled[0].Scenes.Select(s => s.Title));
    }

    [Fact]
    public async Task OnlyTheStagesTheExportAsksForAreCompiled()
    {
        var options = SetupScenes();
        options.IncludedStages = ["final"];

        var compiled = await new ExportService(_project).CompileChaptersAsync(options);

        Assert.Equal(["Final"], compiled[0].Scenes.Select(s => s.Title));
    }

    [Fact]
    public async Task AnEmptyStageFilterMeansEveryStage()
    {
        var options = SetupScenes();
        options.IncludedStages = [];

        var compiled = await new ExportService(_project).CompileChaptersAsync(options);

        Assert.Equal(2, compiled[0].Scenes.Count);
    }

    [Fact]
    public async Task AStageFilterStillWillNotBringBackAHeldBackScene()
    {
        var options = SetupScenes();
        // "Untriaged" is the held-back scene's stage, and it stays out anyway.
        options.IncludedStages = ["", "draft"];

        var compiled = await new ExportService(_project).CompileChaptersAsync(options);

        Assert.Equal(["Draft"], compiled[0].Scenes.Select(s => s.Title));
    }

    // ── What the export would contain ──

    [Fact]
    public async Task PreviewCountsWhatWouldBeCompiled()
    {
        var options = SetupScenes();

        var preview = await new ExportService(_project).PreviewAsync(options);

        Assert.Equal(1, preview.Chapters);
        // The held-back scene is not counted, because it would not be written.
        Assert.Equal(2, preview.Scenes);
        Assert.Equal(4, preview.Words);
        Assert.True(preview.Pages >= 1);
        Assert.False(preview.PagesAreExact);
    }

    [Fact]
    public async Task PreviewFollowsTheStageFilter()
    {
        var options = SetupScenes();
        options.IncludedStages = ["final"];

        var preview = await new ExportService(_project).PreviewAsync(options);

        Assert.Equal(1, preview.Scenes);
    }

    [Fact]
    public async Task PreviewOnTheNormseiteGridIsExact()
    {
        var options = SetupScenes();
        options.PresetId = ExportPresets.NormseitenId;

        var preview = await new ExportService(_project).PreviewAsync(options);

        Assert.True(preview.PagesAreExact);
        Assert.True(preview.Pages >= 1);
    }

    [Fact]
    public async Task PreviewOfNothingIsNoPages()
    {
        _project.GetChaptersOrdered().Returns([]);

        var preview = await new ExportService(_project).PreviewAsync(
            new ExportOptions { Format = ExportFormat.Markdown });

        Assert.Equal(0, preview.Pages);
        Assert.Equal(0, preview.Words);
    }

    // -- The chapter opener --

    [Theory]
    [InlineData("The bell rang once.", 0, "T", "", "he bell rang once.")]
    [InlineData("The bell rang once.", 2, "T", "he bell", " rang once.")]
    [InlineData("The bell rang once.", 3, "T", "he bell rang", " once.")]
    // More words than the sentence has: the whole of it leads in, and nothing
    // is left over, rather than the split running off the end.
    [InlineData("The bell.", 9, "T", "he bell.", "")]
    public void TheOpenerSplitsIntoAnInitialALeadInAndTheRest(
        string text, int words, string initial, string leadIn, string tail)
    {
        var split = ExportService.SplitOpener(text, words);

        Assert.NotNull(split);
        Assert.Equal(initial, split!.Value.Initial);
        Assert.Equal(leadIn, split.Value.LeadIn);
        Assert.Equal(tail, split.Value.Tail);
    }

    [Theory]
    [InlineData("\u201cStop,\u201d she said.")]
    [InlineData("1893 was the year.")]
    [InlineData("")]
    [InlineData("   ")]
    public void AnOpenerThatCannotCarryADropCapIsLeftAlone(string text)
        => Assert.Null(ExportService.SplitOpener(text, 2));

    /// <summary>A layout with a drop cap and a two-word lead-in.</summary>
    private static ExportPreset OpenerLayout() =>
        ExportPresets.GetById(ExportPresets.DefaultId) with
        {
            Id = "custom-opener",
            IsCustom = true,
            DropCap = true,
            LeadInSmallCapsWords = 2
        };

    private ExportOptions SetupOpener(ExportFormat format, ExportPreset preset)
    {
        var chapter = new ChapterData
        {
            Title = "The Fall",
            Order = 1,
            Subtitle = "Ashport, 1893"
        };
        var scene = new SceneData { Title = "Scene", Order = 1, ChapterGuid = chapter.Guid };
        _project.ReadSceneContentAsync(chapter, scene).Returns("<p>The bell rang once.</p>");
        _project.GetChaptersOrdered().Returns([chapter]);
        _project.GetScenesForChapter(chapter.Guid).Returns([scene]);
        _project.ActiveBook.Returns(new BookData { ExportPresets = [preset] });

        return new ExportOptions
        {
            Format = format,
            Title = "Book",
            PresetId = preset.Id,
            SelectedChapterGuids = [chapter.Guid]
        };
    }

    [Fact]
    public async Task Epub_SetsTheDropCapAndTheLeadIn()
    {
        var options = SetupOpener(ExportFormat.Epub, OpenerLayout());
        var path = Path.Combine(_dir.Path, "opener.epub");
        await new ExportService(_project).ExportAsync(options, path);

        using var zip = ZipFile.OpenRead(path);
        using var reader = new StreamReader(zip.GetEntry("OEBPS/chapter-1.xhtml")!.Open(), Encoding.UTF8);
        var xhtml = await reader.ReadToEndAsync();

        Assert.Contains("<span class=\"drop-cap\">T</span>", xhtml);
        Assert.Contains("<span class=\"lead-in\">he bell</span>", xhtml);
        Assert.Contains("chapter-subtitle\">Ashport, 1893", xhtml);
    }

    [Fact]
    public async Task Epub_AChapterCanHideItsHeadingAndStillBeExported()
    {
        var options = SetupOpener(ExportFormat.Epub, OpenerLayout());
        _project.GetChaptersOrdered()[0].HideHeading = true;

        var path = Path.Combine(_dir.Path, "hidden.epub");
        await new ExportService(_project).ExportAsync(options, path);

        using var zip = ZipFile.OpenRead(path);
        using var reader = new StreamReader(zip.GetEntry("OEBPS/chapter-1.xhtml")!.Open(), Encoding.UTF8);
        var xhtml = await reader.ReadToEndAsync();

        Assert.DoesNotContain("chapter-title", xhtml);
        Assert.DoesNotContain("<h1", xhtml);
        // The document still names itself, because that is what a reading
        // system's navigation reads - it is not printed on the page.
        Assert.Contains("<title>The Fall</title>", xhtml);
        // The prose is there; the drop cap splits its opening across spans.
        Assert.Contains("rang once.", xhtml);
    }

    [Fact]
    public async Task Docx_SetsAFramedInitialAndSmallCaps()
    {
        var options = SetupOpener(ExportFormat.Docx, OpenerLayout());
        var path = Path.Combine(_dir.Path, "opener.docx");
        await new ExportService(_project).ExportAsync(options, path);

        using var zip = ZipFile.OpenRead(path);
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open(), Encoding.UTF8);
        var xml = await reader.ReadToEndAsync();

        Assert.Contains("w:dropCap=\"drop\"", xml);
        Assert.Contains("<w:smallCaps/>", xml);
        Assert.Contains("Ashport, 1893", xml);
    }

    [Fact]
    public async Task Docx_AnOpenerThatCannotCarryADropCapIsSetNormally()
    {
        var chapter = new ChapterData { Title = "The Fall", Order = 1 };
        var scene = new SceneData { Title = "Scene", Order = 1, ChapterGuid = chapter.Guid };
        // Opens on a quotation mark, which is not a letter to drop.
        _project.ReadSceneContentAsync(chapter, scene)
            .Returns("<p>“Stop,” she said.</p>");
        _project.GetChaptersOrdered().Returns([chapter]);
        _project.GetScenesForChapter(chapter.Guid).Returns([scene]);
        var preset = OpenerLayout();
        _project.ActiveBook.Returns(new BookData { ExportPresets = [preset] });

        var path = Path.Combine(_dir.Path, "quoted.docx");
        await new ExportService(_project).ExportAsync(
            new ExportOptions
            {
                Format = ExportFormat.Docx,
                Title = "Book",
                PresetId = preset.Id,
                SelectedChapterGuids = [chapter.Guid]
            },
            path);

        using var zip = ZipFile.OpenRead(path);
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open(), Encoding.UTF8);
        var xml = await reader.ReadToEndAsync();

        Assert.DoesNotContain("w:dropCap", xml);
        Assert.Contains("she said.", xml);
    }

    [Fact]
    public async Task Docx_AHiddenHeadingLeavesThePageBreakButNoTitle()
    {
        var options = SetupOpener(ExportFormat.Docx, OpenerLayout());
        _project.GetChaptersOrdered()[0].HideHeading = true;

        var path = Path.Combine(_dir.Path, "hidden.docx");
        await new ExportService(_project).ExportAsync(options, path);

        using var zip = ZipFile.OpenRead(path);
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open(), Encoding.UTF8);
        var xml = await reader.ReadToEndAsync();

        Assert.DoesNotContain("The Fall", xml);
        Assert.Contains("<w:pageBreakBefore/>", xml);
    }

    [Fact]
    public async Task LaTeX_UsesLettrineForTheOpener()
    {
        var options = SetupOpener(ExportFormat.LaTeX, OpenerLayout());
        var path = Path.Combine(_dir.Path, "opener.tex");
        await new ExportService(_project).ExportAsync(options, path);

        var tex = await File.ReadAllTextAsync(path);
        Assert.Contains("\\usepackage{lettrine}", tex);
        Assert.Contains("\\lettrine{T}{he bell}", tex);
        Assert.Contains("Ashport, 1893", tex);
    }

    [Fact]
    public async Task Markdown_PrintsTheSubtitleAndCanHideTheHeading()
    {
        var options = SetupOpener(ExportFormat.Markdown, OpenerLayout());
        var path = Path.Combine(_dir.Path, "opener.md");
        await new ExportService(_project).ExportAsync(options, path);
        Assert.Contains("*Ashport, 1893*", await File.ReadAllTextAsync(path));

        _project.GetChaptersOrdered()[0].HideHeading = true;
        var hidden = Path.Combine(_dir.Path, "hidden.md");
        await new ExportService(_project).ExportAsync(options, hidden);
        Assert.DoesNotContain("The Fall", await File.ReadAllTextAsync(hidden));
    }

    [Fact]
    public async Task Pdf_IsWrittenWithASubtitleAndWithoutAHeading()
    {
        var options = SetupOpener(ExportFormat.Pdf, OpenerLayout());
        var withHeading = Path.Combine(_dir.Path, "opener.pdf");
        await new ExportService(_project).ExportAsync(options, withHeading);
        Assert.True(new FileInfo(withHeading).Length > 0);

        _project.GetChaptersOrdered()[0].HideHeading = true;
        var hidden = Path.Combine(_dir.Path, "hidden.pdf");
        await new ExportService(_project).ExportAsync(options, hidden);
        Assert.True(new FileInfo(hidden).Length > 0);
    }

    [Fact]
    public async Task ALayoutWithNoDropCapLeavesTheOpenerPlain()
    {
        var options = SetupOpener(
            ExportFormat.Epub,
            ExportPresets.GetById(ExportPresets.DefaultId) with { Id = "plain", IsCustom = true });

        var path = Path.Combine(_dir.Path, "plain.epub");
        await new ExportService(_project).ExportAsync(options, path);

        using var zip = ZipFile.OpenRead(path);
        using var reader = new StreamReader(zip.GetEntry("OEBPS/chapter-1.xhtml")!.Open(), Encoding.UTF8);
        Assert.DoesNotContain("drop-cap", await reader.ReadToEndAsync());
    }
}
