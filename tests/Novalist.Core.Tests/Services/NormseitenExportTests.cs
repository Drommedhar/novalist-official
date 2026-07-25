using System.IO.Compression;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Novalist.Core.Utilities;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>Normseiten DOCX: the 60x30 grid, its page furniture, and the block mapping.</summary>
public class NormseitenExportTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly IProjectService _project = Substitute.For<IProjectService>();

    public void Dispose() => _dir.Dispose();

    private string Out() => Path.Combine(_dir.Path, "normseiten.docx");

    private static ExportOptions Opts(string title = "Frostschwur", string author = "Eine Autorin",
        bool titlePage = true, params string[] guids) => new()
        {
            Format = ExportFormat.Docx,
            Title = title,
            Author = author,
            IncludeTitlePage = titlePage,
            PresetId = ExportPresets.NormseitenId,
            SelectedChapterGuids = guids.ToList()
        };

    private static async Task<string> ReadEntryAsync(string path, string entry)
    {
        using var zip = ZipFile.OpenRead(path);
        using var reader = new StreamReader(zip.GetEntry(entry)!.Open());
        return await reader.ReadToEndAsync();
    }

    private (ChapterData Chapter, ExportService Service) SetupChapter(params (string title, string html)[] scenes)
    {
        var chapter = new ChapterData { Title = "Kapitel Eins", Order = 1 };
        var list = new List<SceneData>();
        var order = 1;
        foreach (var (title, html) in scenes)
        {
            var scene = new SceneData { Title = title, Order = order++, ChapterGuid = chapter.Guid };
            list.Add(scene);
            _project.ReadSceneContentAsync(chapter, scene).Returns(html);
        }
        _project.GetChaptersOrdered().Returns([chapter]);
        _project.GetScenesForChapter(chapter.Guid).Returns(list);
        return (chapter, new ExportService(_project));
    }

    // ── Preset ──

    [Fact]
    public void Preset_IsRegisteredWithGridMetrics()
    {
        var preset = ExportPresets.GetById(ExportPresets.NormseitenId);
        Assert.True(preset.NormseitenGrid);
        Assert.Equal(60, preset.GridColumns);
        Assert.Equal(30, preset.GridLines);
        Assert.Equal("Courier New", preset.BodyFontFamily);
        Assert.Equal(20, preset.LineHeightPt);
        // A4 less the left and right margins, i.e. the width 60 Courier columns need.
        Assert.Equal(15.3, preset.TextWidthCm, 3);
    }

    // ── HTML to blocks ──

    [Fact]
    public void HtmlToBlocks_EmptyHtmlYieldsNothing()
        => Assert.Empty(ExportService.HtmlToNormseitenBlocks("   "));

    [Fact]
    public void HtmlToBlocks_WithoutParagraphTagsFallsBackToStrippedText()
    {
        var blocks = ExportService.HtmlToNormseitenBlocks("<div>Nur <b>Text</b></div>");
        Assert.Equal([NormseitenBlock.Body("Nur Text")], blocks);
    }

    [Fact]
    public void HtmlToBlocks_WithoutParagraphTagsAndNoTextYieldsNothing()
        => Assert.Empty(ExportService.HtmlToNormseitenBlocks("<div><br/></div>"));

    [Fact]
    public void HtmlToBlocks_MapsHeadingStylesAndBlankParagraphs()
    {
        var blocks = ExportService.HtmlToNormseitenBlocks(
            "<p class=\"nv-style-heading\">Titel</p><p class=\"nv-style-subheading\">Unter</p>" +
            "<p></p><p>Fliesstext</p>");
        Assert.Equal(NormseitenBlockKind.Heading, blocks[0].Kind);
        Assert.Equal(NormseitenBlockKind.Heading, blocks[2].Kind);
        Assert.Equal(NormseitenBlockKind.Blank, blocks[4].Kind);
        Assert.Equal(NormseitenBlockKind.Text, blocks[5].Kind);
        Assert.Equal("Fliesstext", blocks[5].Text);
    }

    // ── DOCX package ──

    [Fact]
    public async Task Docx_HasGridPackageAndHeader()
    {
        var (chapter, service) = SetupChapter(("Szene", "<p>Es war eine dunkle Nacht.</p>"));
        await service.ExportAsync(Opts(guids: chapter.Guid), Out());

        using (var zip = ZipFile.OpenRead(Out()))
        {
            var names = zip.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("word/document.xml", names);
            Assert.Contains("word/styles.xml", names);
            Assert.Contains("word/header1.xml", names);
        }

        // Every part has to be well-formed OOXML or Word refuses the document.
        foreach (var part in new[] { "word/document.xml", "word/styles.xml", "word/header1.xml" })
            System.Xml.Linq.XDocument.Parse(await ReadEntryAsync(Out(), part));

        var header = await ReadEntryAsync(Out(), "word/header1.xml");
        Assert.Contains("Frostschwur", header);
        Assert.Contains("Seite ", header);
        Assert.Contains(" PAGE ", header);
        Assert.Contains(" von 1", header);

        var styles = await ReadEntryAsync(Out(), "word/styles.xml");
        Assert.Contains("Courier New", styles);
        Assert.Contains("w:line=\"400\" w:lineRule=\"exact\"", styles); // 20pt exact leading
        Assert.Contains("w:sz w:val=\"24\"", styles);                   // 12pt body

        var doc = await ReadEntryAsync(Out(), "word/document.xml");
        Assert.Contains("w:w=\"11906\" w:h=\"16838\"", doc);            // A4 in twips
        Assert.Contains("w:top=\"1701\"", doc);                         // 3.0 cm
        Assert.Contains("w:bottom=\"2551\"", doc);                      // 4.5 cm
        Assert.Contains("KAPITEL EINS", doc);                           // heading upper-cased
        Assert.Contains("FROSTSCHWUR", doc);                            // title block
        Assert.Contains("Eine Autorin", doc);
    }

    [Fact]
    public async Task Docx_BreaksThePageEveryThirtyLines()
    {
        // 200 short paragraphs: one grid line each plus a blank separator.
        var paragraphs = string.Concat(Enumerable.Range(0, 200).Select(i => $"<p>Absatz {i}.</p>"));
        var (chapter, service) = SetupChapter(("Szene", paragraphs));
        await service.ExportAsync(Opts(titlePage: false, guids: chapter.Guid), Out());

        var doc = await ReadEntryAsync(Out(), "word/document.xml");
        var lines = doc.Split("<w:p>").Length - 1;
        var breaks = doc.Split("<w:br w:type=\"page\"/>").Length - 1;
        Assert.Equal((lines - 1) / 30, breaks);
        Assert.True(breaks > 10);

        var header = await ReadEntryAsync(Out(), "word/header1.xml");
        Assert.Contains($" von {breaks + 1}", header);
    }

    [Fact]
    public async Task Docx_SceneBreakSeparatesScenes()
    {
        var (chapter, service) = SetupChapter(("Eins", "<p>Erste Szene.</p>"), ("Zwei", "<p>Zweite Szene.</p>"));
        await service.ExportAsync(Opts(titlePage: false, guids: chapter.Guid), Out());

        var doc = await ReadEntryAsync(Out(), "word/document.xml");
        Assert.Contains("* * *", doc);
        Assert.Contains("Erste Szene.", doc);
        Assert.Contains("Zweite Szene.", doc);
    }

    [Fact]
    public async Task Docx_UntitledExportOmitsTheHeaderTitle()
    {
        var (chapter, service) = SetupChapter(("Szene", "<p>Text.</p>"));
        await service.ExportAsync(Opts(title: "", author: "", guids: chapter.Guid), Out());

        var header = await ReadEntryAsync(Out(), "word/header1.xml");
        Assert.Contains("<w:r><w:t xml:space=\"preserve\"></w:t></w:r>", header);
    }

    [Fact]
    public async Task Docx_EmptyDocumentStillReportsOnePage()
    {
        await ExportService.WriteNormseitenDocxAsync(
            [], new ExportOptions { PresetId = ExportPresets.NormseitenId, Title = "Leer" }, Out());

        var header = await ReadEntryAsync(Out(), "word/header1.xml");
        Assert.Contains(" von 1", header);
        var doc = await ReadEntryAsync(Out(), "word/document.xml");
        Assert.DoesNotContain("<w:br", doc);
    }

    [Fact]
    public async Task Docx_EscapesMarkupInTheText()
    {
        var (chapter, service) = SetupChapter(("Szene", "<p>Fuenf &lt; sieben &amp; wahr</p>"));
        await service.ExportAsync(Opts(titlePage: false, guids: chapter.Guid), Out());

        var doc = await ReadEntryAsync(Out(), "word/document.xml");
        Assert.Contains("Fuenf &lt; sieben &amp; wahr", doc);
    }
}
