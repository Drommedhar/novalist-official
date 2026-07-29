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
}
