using System.IO.Compression;
using System.Text;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Publishing metadata reaching the exported file.
///
/// The whole point is what a retailer sees at ingestion, so the assertions are
/// on the bytes in the OPF rather than on the model round-tripping.
/// </summary>
public class PublishingMetadataTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly IProjectService _project = Substitute.For<IProjectService>();

    public void Dispose() => _dir.Dispose();

    private async Task<string> OpfAsync(PublishingMetadata publishing, string language = "en")
    {
        var chapter = new ChapterData { Title = "One", Order = 1 };
        var scene = new SceneData { Title = "S", Order = 1, ChapterGuid = chapter.Guid };
        _project.ReadSceneContentAsync(chapter, scene).Returns("<p>text</p>");
        _project.GetChaptersOrdered().Returns([chapter]);
        _project.GetScenesForChapter(chapter.Guid).Returns([scene]);
        _project.ActiveBook.Returns(new BookData { Publishing = publishing });

        var path = Path.Combine(_dir.Path, "out.epub");
        await new ExportService(_project).ExportAsync(
            new ExportOptions
            {
                Format = ExportFormat.Epub,
                Title = "The Book",
                Language = language,
                IncludeTitlePage = true,
                SelectedChapterGuids = [chapter.Guid]
            },
            path);

        return await ReadEntryAsync(path, "OEBPS/content.opf");
    }

    private static async Task<string> ReadEntryAsync(string zipPath, string entry)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        using var reader = new StreamReader(zip.GetEntry(entry)!.Open(), Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    // ── The ISBN ──

    [Theory]
    [InlineData("978-3-16-148410-0", "9783161484100")]
    [InlineData("9783161484100", "9783161484100")]
    [InlineData("0-306-40615-2", "0306406152")]
    [InlineData("155860832X", "155860832X")]
    [InlineData("15586-0832x", "155860832X")]
    public void AnIsbnIsNormalisedToTheDigitsARetailerKeysOn(string typed, string expected)
    {
        Assert.Equal(expected, new PublishingMetadata { Isbn = typed }.NormalizedIsbn());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not an isbn")]
    [InlineData("12345")]
    [InlineData("978316148410012345")]
    public void SomethingThatIsNotAnIsbnResolvesToNothing(string typed)
    {
        // A half-typed value must not become a broken identifier in the file.
        Assert.Empty(new PublishingMetadata { Isbn = typed }.NormalizedIsbn());
    }

    [Fact]
    public async Task AnIsbnBecomesThePackageIdentifier()
    {
        var opf = await OpfAsync(new PublishingMetadata { Isbn = "978-3-16-148410-0" });

        Assert.Contains("<dc:identifier id=\"BookId\">urn:isbn:9783161484100</dc:identifier>", opf);
        Assert.Contains("opf:scheme=\"ISBN\"", opf);
    }

    [Fact]
    public async Task WithoutAnIsbnTheGeneratedIdentifierIsStillThere()
    {
        var opf = await OpfAsync(new PublishingMetadata());

        Assert.Contains("<dc:identifier id=\"BookId\">", opf);
        Assert.DoesNotContain("urn:isbn:", opf);
    }

    [Fact]
    public async Task AnUnusableIsbnIsNotWrittenAtAll()
    {
        var opf = await OpfAsync(new PublishingMetadata { Isbn = "coming soon" });

        Assert.DoesNotContain("urn:isbn:", opf);
        Assert.DoesNotContain("opf:scheme", opf);
    }

    // ── The rest of the block ──

    [Fact]
    public async Task EveryFieldReachesTheMetadataBlock()
    {
        var opf = await OpfAsync(new PublishingMetadata
        {
            Publisher = "Raven Press",
            Description = "A book about ravens.",
            Rights = "Copyright 2026",
            PublicationDate = "2026-03-01",
            Subjects = ["Fantasy", "Epic"]
        });

        Assert.Contains("<dc:publisher>Raven Press</dc:publisher>", opf);
        Assert.Contains("<dc:description>A book about ravens.</dc:description>", opf);
        Assert.Contains("<dc:rights>Copyright 2026</dc:rights>", opf);
        Assert.Contains("<dc:date>2026-03-01</dc:date>", opf);
        Assert.Contains("<dc:subject>Fantasy</dc:subject>", opf);
        Assert.Contains("<dc:subject>Epic</dc:subject>", opf);
    }

    [Fact]
    public async Task NothingExtraIsWrittenWhenNothingWasSet()
    {
        var opf = await OpfAsync(new PublishingMetadata());

        Assert.DoesNotContain("<dc:publisher>", opf);
        Assert.DoesNotContain("<dc:subject>", opf);
        Assert.DoesNotContain("belongs-to-collection", opf);
    }

    [Fact]
    public async Task ABlankFieldIsSkippedRatherThanWrittenEmpty()
    {
        // An empty dc:publisher is a malformed record to some pipelines.
        var opf = await OpfAsync(new PublishingMetadata
        {
            Publisher = "   ",
            Description = "Real."
        });

        Assert.DoesNotContain("<dc:publisher>", opf);
        Assert.Contains("<dc:description>Real.</dc:description>", opf);
    }

    [Fact]
    public async Task MetadataIsXmlEscaped()
    {
        var opf = await OpfAsync(new PublishingMetadata { Publisher = "Bell & Sons <Ltd>" });

        Assert.Contains("Bell &amp; Sons &lt;Ltd&gt;", opf);
        Assert.DoesNotContain("<Ltd>", opf);
    }

    // ── Series ──

    [Fact]
    public async Task ASeriesIsStatedAsAnEpubCollection()
    {
        var opf = await OpfAsync(new PublishingMetadata
        {
            SeriesName = "The Ravens",
            SeriesPosition = "2"
        });

        Assert.Contains("belongs-to-collection\" id=\"series\">The Ravens</meta>", opf);
        Assert.Contains("property=\"collection-type\">series</meta>", opf);
        Assert.Contains("property=\"group-position\">2</meta>", opf);
    }

    [Fact]
    public async Task ASeriesWithNoPositionStillStatesTheSeries()
    {
        // A book whose place the writer has not decided still belongs to it.
        var opf = await OpfAsync(new PublishingMetadata { SeriesName = "The Ravens" });

        Assert.Contains("belongs-to-collection", opf);
        Assert.DoesNotContain("group-position", opf);
    }

    [Theory]
    [InlineData("The Ravens", "2", "The Ravens, Book 2")]
    [InlineData("The Ravens", "", "The Ravens")]
    [InlineData("The Ravens", "2.5", "The Ravens, Book 2.5")]
    public void TheTitlePageSeriesLineReadsLikeAPrintedOne(
        string name, string position, string expected)
    {
        Assert.Equal(
            expected,
            ExportService.SeriesLine(
                new PublishingMetadata { SeriesName = name, SeriesPosition = position }));
    }

    [Fact]
    public async Task TheTitlePageCarriesTheSeriesAndPublisher()
    {
        var chapter = new ChapterData { Title = "One", Order = 1 };
        var scene = new SceneData { Title = "S", Order = 1, ChapterGuid = chapter.Guid };
        _project.ReadSceneContentAsync(chapter, scene).Returns("<p>text</p>");
        _project.GetChaptersOrdered().Returns([chapter]);
        _project.GetScenesForChapter(chapter.Guid).Returns([scene]);
        _project.ActiveBook.Returns(new BookData
        {
            Publishing = new PublishingMetadata
            {
                SeriesName = "The Ravens",
                SeriesPosition = "2",
                Publisher = "Raven Press"
            }
        });

        var path = Path.Combine(_dir.Path, "title.epub");
        await new ExportService(_project).ExportAsync(
            new ExportOptions
            {
                Format = ExportFormat.Epub,
                Title = "The Book",
                IncludeTitlePage = true,
                SelectedChapterGuids = [chapter.Guid]
            },
            path);

        var title = await ReadEntryAsync(path, "OEBPS/title.xhtml");
        Assert.Contains("The Ravens, Book 2", title);
        Assert.Contains("Raven Press", title);
    }

    // ── The regression that started this ──

    [Fact]
    public async Task TheLanguageStillFollowsTheBookRatherThanBeingHardcoded()
    {
        var opf = await OpfAsync(new PublishingMetadata(), language: "de");

        Assert.Contains("<dc:language>de</dc:language>", opf);
    }

    [Fact]
    public void HasAnyIsFalseForAnUntouchedRecord()
    {
        Assert.False(new PublishingMetadata().HasAny);
        Assert.True(new PublishingMetadata { Publisher = "x" }.HasAny);
        Assert.True(new PublishingMetadata { Subjects = ["x"] }.HasAny);
    }

    [Fact]
    public void HasAnyIgnoresBlankSubjects()
    {
        Assert.False(new PublishingMetadata { Subjects = ["  ", ""] }.HasAny);
    }
}
