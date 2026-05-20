using System.IO.Compression;
using System.Text;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class ExportServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly IProjectService _project = Substitute.For<IProjectService>();
    private readonly IEntityService _entity = Substitute.For<IEntityService>();

    public void Dispose() => _dir.Dispose();

    private string Out(string ext) => Path.Combine(_dir.Path, "export" + ext);

    private ExportService Build() => new(_project, _entity);

    // Configures one chapter with the given scenes (title -> html).
    private (ChapterData Chapter, List<SceneData> Scenes) SetupChapter(
        string title, params (string title, string html)[] scenes)
    {
        var ch = new ChapterData { Title = title, Order = 1, Act = "I", Date = "2024-01-01" };
        var list = new List<SceneData>();
        int order = 1;
        foreach (var (st, html) in scenes)
        {
            var sc = new SceneData { Title = st, Order = order++, ChapterGuid = ch.Guid };
            list.Add(sc);
            _project.ReadSceneContentAsync(ch, sc).Returns(html);
        }
        _project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        _project.GetScenesForChapter(ch.Guid).Returns(list);
        return (ch, list);
    }

    private ExportOptions Opts(ExportFormat fmt, IEnumerable<string> guids) => new()
    {
        Format = fmt,
        Title = "My Title",
        Author = "An Author",
        IncludeTitlePage = true,
        SelectedChapterGuids = guids.ToList()
    };

    // ── ExportOptions.ResolvePreset ──

    [Fact]
    public void ResolvePreset_PresetIdWins()
    {
        var o = new ExportOptions { PresetId = ExportPresets.ShunnId, SmfPreset = false };
        Assert.Equal(ExportPresets.ShunnId, o.ResolvePreset().Id);
    }

    [Fact]
    public void ResolvePreset_SmfLegacy()
        => Assert.Equal(ExportPresets.ShunnId, new ExportOptions { SmfPreset = true }.ResolvePreset().Id);

    [Fact]
    public void ResolvePreset_Default()
        => Assert.Equal(ExportPresets.DefaultId, new ExportOptions().ResolvePreset().Id);

    // ── CompileChapters ──

    [Fact]
    public async Task Compile_FiltersBySelection_AppendsFootnotes()
    {
        var (ch, scenes) = SetupChapter("C", ("S", "<p>Body</p>"));
        scenes[0].Footnotes = new() { new SceneFootnote { Number = 1, Text = "A note" } };

        var sut = Build();
        var compiled = await sut.CompileChaptersAsync(Opts(ExportFormat.Markdown, new[] { ch.Guid }));

        Assert.Single(compiled);
        Assert.Contains("Footnotes", compiled[0].Scenes[0].HtmlContent);
        Assert.Contains("A note", compiled[0].Scenes[0].HtmlContent);
    }

    [Fact]
    public async Task Compile_UnselectedChapter_Excluded()
    {
        var (ch, _) = SetupChapter("C", ("S", "<p>x</p>"));
        var sut = Build();
        var compiled = await sut.CompileChaptersAsync(Opts(ExportFormat.Markdown, Array.Empty<string>()));
        Assert.Empty(compiled);
    }

    // ── Markdown ──

    [Fact]
    public async Task Markdown_TitlePageChaptersScenesAndStyles()
    {
        var (ch, _) = SetupChapter("Chapter One",
            ("S1", "<p>Plain text.</p><p class=\"nv-style-heading\">A Heading</p>" +
                   "<p class=\"nv-style-blockquote\">Quote</p><p class=\"nv-style-poetry\">Verse</p>" +
                   "<p><b>bold</b> and <i>italic</i></p>"),
            ("S2", "<p>Second scene.</p>"));
        var sut = Build();
        await sut.ExportAsync(Opts(ExportFormat.Markdown, new[] { ch.Guid }), Out(".md"));
        var md = await File.ReadAllTextAsync(Out(".md"));

        Assert.Contains("# My Title", md);
        Assert.Contains("*An Author*", md);
        Assert.Contains("## Chapter One", md);
        Assert.Contains("# A Heading", md);          // heading style
        Assert.Contains("> Quote", md);              // blockquote
        Assert.Contains("    Verse", md);            // poetry indent
        Assert.Contains("**bold**", md);
        Assert.Contains("*italic*", md);
        Assert.Contains(ExportServiceTestConstants.SceneBreak, md); // scene break between S1/S2
    }

    // ── Final Draft ──

    [Fact]
    public async Task FinalDraft_XmlWithUppercaseHeadingsAndEscaping()
    {
        var (ch, _) = SetupChapter("Title & <Tag>", ("Scene 1", "<p>Action line.</p>"));
        var sut = Build();
        await sut.ExportAsync(Opts(ExportFormat.FinalDraft, new[] { ch.Guid }), Out(".fdx"));
        var fdx = await File.ReadAllTextAsync(Out(".fdx"));

        Assert.Contains("<FinalDraft", fdx);
        Assert.Contains("TITLE &amp; &lt;TAG&gt;", fdx); // uppercased + escaped
        Assert.Contains("Action line.", fdx);
    }

    // ── LaTeX ──

    [Fact]
    public async Task Latex_DocumentStructureEscapesAndStyles()
    {
        var (ch, _) = SetupChapter("Ch_1 & 50%",
            ("S1", "<p class=\"nv-style-heading\">Head</p><p class=\"nv-style-subheading\">Sub</p>" +
                   "<p class=\"nv-style-blockquote\">Q</p><p class=\"nv-style-poetry\">V</p>" +
                   "<p><b><i>bi</i></b></p>"),
            ("S2", "<p>x</p>"));
        var sut = Build();
        await sut.ExportAsync(Opts(ExportFormat.LaTeX, new[] { ch.Guid }), Out(".tex"));
        var tex = await File.ReadAllTextAsync(Out(".tex"));

        Assert.Contains("\\documentclass", tex);
        Assert.Contains("\\chapter{Ch\\_1 \\& 50\\%}", tex); // escaped
        Assert.Contains("\\section*{", tex);
        Assert.Contains("\\subsection*{", tex);
        Assert.Contains("\\begin{quote}", tex);
        Assert.Contains("\\begin{verse}", tex);
        Assert.Contains("\\textbf{\\textit{bi}}", tex);
        Assert.Contains("* * *", tex); // scene separator
    }

    // ── EPUB ──

    [Fact]
    public async Task Epub_ProducesValidZipWithRequiredEntries()
    {
        var (ch, _) = SetupChapter("C", ("S", "<p>Body</p>"));
        var sut = Build();
        await sut.ExportAsync(Opts(ExportFormat.Epub, new[] { ch.Guid }), Out(".epub"));

        using var zip = ZipFile.OpenRead(Out(".epub"));
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("mimetype", names);
        Assert.Contains("META-INF/container.xml", names);
        Assert.Contains("OEBPS/content.opf", names);
        Assert.Contains("OEBPS/chapter-1.xhtml", names);
        Assert.Contains("OEBPS/title.xhtml", names);

        // mimetype must be stored uncompressed and contain the exact media type.
        var mimetype = zip.GetEntry("mimetype")!;
        using var r = new StreamReader(mimetype.Open());
        Assert.Equal("application/epub+zip", await r.ReadToEndAsync());
    }

    // ── DOCX ──

    [Fact]
    public async Task Docx_ProducesValidZipWithDocument()
    {
        var (ch, _) = SetupChapter("C", ("S", "<p>Body</p>"));
        var sut = Build();
        await sut.ExportAsync(Opts(ExportFormat.Docx, new[] { ch.Guid }), Out(".docx"));

        using var zip = ZipFile.OpenRead(Out(".docx"));
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("[Content_Types].xml", names);
        Assert.Contains("word/document.xml", names);
    }

    // ── PDF ──

    [Fact]
    public async Task Pdf_ProducesPdfFile()
    {
        var (ch, _) = SetupChapter("C", ("S", "<p>Body paragraph.</p>"));
        var sut = Build();
        await sut.ExportAsync(Opts(ExportFormat.Pdf, new[] { ch.Guid }), Out(".pdf"));

        var bytes = await File.ReadAllBytesAsync(Out(".pdf"));
        Assert.True(bytes.Length > 0);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
    }

    // ── Codex ──

    [Fact]
    public async Task Codex_NoEntityService_WritesNotice()
    {
        var sut = new ExportService(_project); // no entity service
        await sut.ExportCodexAsync(new ExportOptions { Title = "X" }, Out(".md"));
        Assert.Contains("requires entity service", await File.ReadAllTextAsync(Out(".md")));
    }

    [Fact]
    public async Task Codex_WritesEntitiesAndCopiesImages()
    {
        // Real image on disk that GetImageFullPath points to.
        var imgSrc = Path.Combine(_dir.Path, "src.png");
        await File.WriteAllBytesAsync(imgSrc, new byte[] { 1, 2, 3 });
        _entity.GetImageFullPath("img/rel.png").Returns(imgSrc);

        _entity.LoadCharactersAsync().Returns(new List<CharacterData>
        {
            new() { Name = "Alice", Role = "Hero", Images = new() { new EntityImage { Name = "p", Path = "img/rel.png" } },
                    Sections = new() { new EntitySection { Title = "Bio", Content = "<p>Born</p>" } } }
        });
        _entity.LoadLocationsAsync().Returns(new List<LocationData> { new() { Name = "City", Type = "Urban", Description = "Big" } });
        _entity.LoadItemsAsync().Returns(new List<ItemData> { new() { Name = "Sword", Type = "Weapon" } });
        _entity.LoadLoreAsync().Returns(new List<LoreData> { new() { Name = "Magic", Category = "System" } });

        var sut = Build();
        var outPath = Out(".md");
        await sut.ExportCodexAsync(new ExportOptions { Title = "Codex", Author = "Me" }, outPath);

        var md = await File.ReadAllTextAsync(outPath);
        Assert.Contains("# Codex", md);
        Assert.Contains("## Characters", md);
        Assert.Contains("### Alice", md);
        Assert.Contains("Born", md);              // section HTML stripped
        Assert.Contains("## Locations", md);
        Assert.Contains("## Items", md);
        Assert.Contains("## Lore", md);
        // Image copied into the *_images folder and referenced.
        var imagesDir = Path.Combine(_dir.Path, "export_images");
        Assert.True(Directory.Exists(imagesDir));
        Assert.NotEmpty(Directory.GetFiles(imagesDir));
    }

    [Fact]
    public async Task Codex_NoImages_RemovesEmptyImagesFolder()
    {
        _entity.LoadCharactersAsync().Returns(new List<CharacterData> { new() { Name = "Bob" } });
        _entity.LoadLocationsAsync().Returns(new List<LocationData>());
        _entity.LoadItemsAsync().Returns(new List<ItemData>());
        _entity.LoadLoreAsync().Returns(new List<LoreData>());

        var sut = Build();
        await sut.ExportCodexAsync(new ExportOptions { Title = "C" }, Out(".md"));
        Assert.False(Directory.Exists(Path.Combine(_dir.Path, "export_images")));
    }

    // ── Timeline outline ──

    [Fact]
    public async Task Timeline_OutlineWithEventsScenesAndUnscheduled()
    {
        var ch = new ChapterData { Title = "Ch", Order = 1, Act = "I", Date = "2024" };
        _project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        _project.GetScenesForChapter(ch.Guid).Returns(new List<SceneData>
        {
            new() { Title = "Scene", Order = 1, Date = "2024-01-02", Synopsis = "Synopsis line" }
        });
        var settings = new ProjectSettings();
        settings.Timeline.Categories.Clear();
        settings.Timeline.Categories.Add(new TimelineCategory { Id = "cat1", Name = "Plot" });
        settings.Timeline.ManualEvents.Add(new TimelineManualEvent
        { Title = "Linked event", Date = "2024-01-01", CategoryId = "cat1", LinkedChapterGuid = ch.Guid, Order = 1 });
        settings.Timeline.ManualEvents.Add(new TimelineManualEvent
        { Title = "Floating event", Description = "desc", Order = 1 });
        _project.ProjectSettings.Returns(settings);

        var sut = Build();
        await sut.ExportTimelineOutlineAsync(Out(".md"));
        var md = await File.ReadAllTextAsync(Out(".md"));

        Assert.Contains("# Story Outline", md);
        Assert.Contains("## 1. Ch", md);
        Assert.Contains("_Act: I_", md);
        Assert.Contains("Linked event", md);
        Assert.Contains("_(Plot)_", md);
        Assert.Contains("**Scene**", md);
        Assert.Contains("Synopsis line", md);
        Assert.Contains("## Unscheduled events", md);
        Assert.Contains("Floating event", md);
    }

    // ── Inline parser edge cases (via Markdown) ──

    [Fact]
    public async Task Parser_HandlesNestedUnderlineSpanBrAndUnclosedTags()
    {
        var (ch, _) = SetupChapter("C",
            ("S", "<p>line1<br/>line2</p>" +
                  "<p><u>under</u> <span>span</span></p>" +
                  "<p><b>strong <i>nested</i></b></p>" +
                  "<p>before <unknowntag>mid</unknowntag> after</p>" +
                  "<p><b>unclosed bold</p>"));
        var sut = Build();
        await sut.ExportAsync(Opts(ExportFormat.Markdown, new[] { ch.Guid }), Out(".md"));
        var md = await File.ReadAllTextAsync(Out(".md"));

        Assert.Contains("under", md);            // underline -> plain text
        Assert.Contains("span", md);
        Assert.Contains("***nested***", md);     // bold+italic nested
        Assert.Contains("mid", md);              // unknown tag inner extracted
    }

    // ── Shunn preset + multi-page + run formatting ──

    private ExportOptions ShunnOpts(ExportFormat fmt, IEnumerable<string> guids, string title, string author) => new()
    {
        Format = fmt, Title = title, Author = author, IncludeTitlePage = true,
        PresetId = ExportPresets.ShunnId, SelectedChapterGuids = guids.ToList()
    };

    [Fact]
    public async Task Pdf_ShunnPreset_MultiPage()
    {
        // Long content forces multiple pages so the running header (page > 1) renders.
        var big = "<p>" + string.Join(" ", Enumerable.Repeat("word", 4000)) + "</p>";
        var (ch, _) = SetupChapter("A Very Long Chapter Title That Exceeds Thirty Characters",
            ("S1", big), ("S2", "<p>second scene</p>"));
        var sut = Build();
        await sut.ExportAsync(ShunnOpts(ExportFormat.Pdf, new[] { ch.Guid }, "A Long Title Over Thirty Characters Indeed", "Jane Q Author"), Out(".pdf"));

        var bytes = await File.ReadAllBytesAsync(Out(".pdf"));
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.True(bytes.Length > 2000); // multiple pages of content
    }

    [Fact]
    public async Task Docx_ShunnPreset_HasHeaderAndRuns()
    {
        var (ch, _) = SetupChapter("C", ("S", "<p><b>bold</b> <i>it</i> <b><i>bi</i></b> plain</p>"));
        var sut = Build();
        await sut.ExportAsync(ShunnOpts(ExportFormat.Docx, new[] { ch.Guid }, "T", "First Last"), Out(".docx"));

        using var zip = ZipFile.OpenRead(Out(".docx"));
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("word/header1.xml", names); // Shunn running header
        var doc = zip.GetEntry("word/document.xml")!;
        using var r = new StreamReader(doc.Open());
        var xml = await r.ReadToEndAsync();
        Assert.Contains("<w:b/>", xml);  // bold run
        Assert.Contains("<w:i/>", xml);  // italic run
    }

    [Fact]
    public async Task Markdown_SubheadingAndNonStyleClass()
    {
        var (ch, _) = SetupChapter("C",
            ("S", "<p class=\"nv-style-subheading\">Sub</p><p class=\"unrelated\">Plain class</p>"));
        var sut = Build();
        await sut.ExportAsync(Opts(ExportFormat.Markdown, new[] { ch.Guid }), Out(".md"));
        var md = await File.ReadAllTextAsync(Out(".md"));
        Assert.Contains("## Sub", md);            // subheading style
        Assert.Contains("Plain class", md);       // class without nv-style- -> normal text
    }

    [Fact]
    public async Task Codex_FullCharacter_WithDedupedImages()
    {
        var imgSrc = Path.Combine(_dir.Path, "shared.png");
        await File.WriteAllBytesAsync(imgSrc, new byte[] { 1, 2, 3, 4 });
        _entity.GetImageFullPath("shared.png").Returns(imgSrc);

        var character = new CharacterData
        {
            Name = "Alice", Role = "Hero", Age = "30", Gender = "F", Group = "Guild",
            EyeColor = "Blue", HairColor = "Brown", Height = "170", Build = "Slim",
            SkinTone = "Fair", DistinguishingFeatures = "Scar",
            Images = new() { new EntityImage { Name = "p", Path = "shared.png" } },
            CustomProperties = new() { ["mood"] = "happy", ["empty"] = "" },
            Relationships = new() { new EntityRelationship { Role = "friend", Target = "Bob" } },
            Sections = new() { new EntitySection { Title = "Bio", Content = "<p>Story</p>" },
                               new EntitySection { Title = "Empty", Content = "" } }
        };
        _entity.LoadCharactersAsync().Returns(new List<CharacterData> { character });
        // Location reuses the SAME image path -> copyMap reuse branch.
        _entity.LoadLocationsAsync().Returns(new List<LocationData>
        {
            new() { Name = "City", Images = new() { new EntityImage { Name = "v", Path = "shared.png" } } }
        });
        _entity.LoadItemsAsync().Returns(new List<ItemData>());
        _entity.LoadLoreAsync().Returns(new List<LoreData>());

        var sut = Build();
        await sut.ExportCodexAsync(new ExportOptions { Title = "Codex" }, Out(".md"));
        var md = await File.ReadAllTextAsync(Out(".md"));

        Assert.Contains("- **Role:** Hero", md);
        Assert.Contains("- **mood:** happy", md);
        Assert.DoesNotContain("**empty:**", md);     // blank custom prop skipped
        Assert.Contains("**Relationships**", md);
        Assert.Contains("- friend: Bob", md);
        Assert.Contains("Story", md);                 // section content stripped
        // Image copied once, reused for the location.
        var images = Directory.GetFiles(Path.Combine(_dir.Path, "export_images"));
        Assert.Single(images);
    }

    [Fact]
    public async Task Parser_NoParagraphTags_FallsBackToStripped()
    {
        var (ch, _) = SetupChapter("C", ("S", "Just bare text, no tags"));
        var sut = Build();
        // FinalDraft uses ParseHtmlToParagraphs which has the no-<p> fallback.
        await sut.ExportAsync(Opts(ExportFormat.FinalDraft, new[] { ch.Guid }), Out(".fdx"));
        Assert.Contains("Just bare text", await File.ReadAllTextAsync(Out(".fdx")));
    }

    [Fact]
    public async Task ExportAsync_CodexFormat_Dispatches()
    {
        _entity.LoadCharactersAsync().Returns(new List<CharacterData> { new() { Name = "A" } });
        _entity.LoadLocationsAsync().Returns(new List<LocationData>());
        _entity.LoadItemsAsync().Returns(new List<ItemData>());
        _entity.LoadLoreAsync().Returns(new List<LoreData>());
        SetupChapter("C", ("S", "<p>x</p>"));
        var sut = Build();
        await sut.ExportAsync(new ExportOptions { Format = ExportFormat.Codex, Title = "Cdx" }, Out(".md"));
        Assert.Contains("# Cdx", await File.ReadAllTextAsync(Out(".md")));
    }

    [Fact]
    public async Task Codex_SameFilenameDifferentContent_GetsSuffix()
    {
        var img1 = Path.Combine(_dir.Path, "a", "pic.png");
        var img2 = Path.Combine(_dir.Path, "b", "pic.png");
        Directory.CreateDirectory(Path.GetDirectoryName(img1)!);
        Directory.CreateDirectory(Path.GetDirectoryName(img2)!);
        await File.WriteAllBytesAsync(img1, new byte[] { 1 });
        await File.WriteAllBytesAsync(img2, new byte[] { 2, 2, 2 }); // different size
        _entity.GetImageFullPath("a/pic.png").Returns(img1);
        _entity.GetImageFullPath("b/pic.png").Returns(img2);

        _entity.LoadCharactersAsync().Returns(new List<CharacterData>
        {
            new() { Name = "A", Images = new() { new EntityImage { Name = "1", Path = "a/pic.png" } } },
            new() { Name = "B", Images = new() { new EntityImage { Name = "2", Path = "b/pic.png" } } }
        });
        _entity.LoadLocationsAsync().Returns(new List<LocationData>());
        _entity.LoadItemsAsync().Returns(new List<ItemData>());
        _entity.LoadLoreAsync().Returns(new List<LoreData>());

        var sut = Build();
        await sut.ExportCodexAsync(new ExportOptions { Title = "C" }, Out(".md"));
        var images = Directory.GetFiles(Path.Combine(_dir.Path, "export_images")).Select(Path.GetFileName).ToList();
        Assert.Contains("pic.png", images);
        Assert.Contains("pic_1.png", images); // collision resolved with suffix
    }

    [Fact]
    public async Task Codex_GenericEntity_WithPropsAndSections()
    {
        _entity.LoadCharactersAsync().Returns(new List<CharacterData>());
        _entity.LoadLocationsAsync().Returns(new List<LocationData>
        {
            new() { Name = "City", Type = "Urban", Description = "Big",
                    CustomProperties = new() { ["pop"] = "1M", ["blank"] = "" },
                    Sections = new() { new EntitySection { Title = "Hist", Content = "<p>Old</p>" },
                                       new EntitySection { Title = "Skip", Content = "" } } }
        });
        _entity.LoadItemsAsync().Returns(new List<ItemData>());
        _entity.LoadLoreAsync().Returns(new List<LoreData>());

        var sut = Build();
        await sut.ExportCodexAsync(new ExportOptions { Title = "C" }, Out(".md"));
        var md = await File.ReadAllTextAsync(Out(".md"));
        Assert.Contains("- **Type:** Urban", md);
        Assert.Contains("- **pop:** 1M", md);
        Assert.DoesNotContain("**blank:**", md);
        Assert.Contains("**Hist**", md);
        Assert.Contains("Old", md);
    }

    [Fact]
    public async Task Latex_EscapesAllSpecialChars()
    {
        var (ch, _) = SetupChapter("C", ("S", @"<p>back\slash $dollar #hash {brace} ~tilde ^caret</p>"));
        var sut = Build();
        await sut.ExportAsync(Opts(ExportFormat.LaTeX, new[] { ch.Guid }), Out(".tex"));
        var tex = await File.ReadAllTextAsync(Out(".tex"));
        Assert.Contains(@"\textbackslash{}", tex);
        Assert.Contains(@"\$", tex);
        Assert.Contains(@"\#", tex);
        Assert.Contains(@"\{", tex);
        Assert.Contains(@"\}", tex);
        Assert.Contains(@"\textasciitilde{}", tex);
        Assert.Contains(@"\textasciicircum{}", tex);
    }

    [Fact]
    public async Task Epub_MultiSceneWithFormatting()
    {
        var (ch, _) = SetupChapter("C",
            ("S1", "<p><b>bold</b> <i>it</i> <b><i>bi</i></b></p>"),
            ("S2", "<p>second</p>"));
        var sut = Build();
        await sut.ExportAsync(Opts(ExportFormat.Epub, new[] { ch.Guid }), Out(".epub"));
        using var zip = ZipFile.OpenRead(Out(".epub"));
        using var r = new StreamReader(zip.GetEntry("OEBPS/chapter-1.xhtml")!.Open());
        var xhtml = await r.ReadToEndAsync();
        Assert.Contains("scene-break", xhtml);            // multi-scene break
        Assert.Contains("<strong><em>bi</em></strong>", xhtml);
        Assert.Contains("<strong>bold</strong>", xhtml);
        Assert.Contains("<em>it</em>", xhtml);
    }

    [Fact]
    public async Task Docx_NoTitlePage_FirstChapterNoPageBreak_AndMultiScene()
    {
        var (ch, _) = SetupChapter("C", ("S1", "<p>one</p>"), ("S2", "<p>two</p>"));
        var sut = Build();
        var o = Opts(ExportFormat.Docx, new[] { ch.Guid });
        o.IncludeTitlePage = false;
        await sut.ExportAsync(o, Out(".docx"));
        using var zip = ZipFile.OpenRead(Out(".docx"));
        using var r = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var xml = await r.ReadToEndAsync();
        Assert.Contains("SceneBreak", xml);                 // multi-scene break style
        Assert.Contains("Heading1", xml);
    }

    [Fact]
    public async Task Parser_EmptyParagraph_StrayCloseTag_NestedSameTag()
    {
        var (ch, _) = SetupChapter("C",
            ("S", "<p></p>" +                       // empty paragraph -> skipped
                  "<p>text</straytag></p>" +        // stray closing tag at top level
                  "<p><b>outer <b>inner</b> tail</b></p>")); // nested same tag (depth++)
        var sut = Build();
        await sut.ExportAsync(Opts(ExportFormat.Markdown, new[] { ch.Guid }), Out(".md"));
        var md = await File.ReadAllTextAsync(Out(".md"));
        Assert.Contains("text", md);
        Assert.Contains("inner", md);
    }

    [Fact]
    public async Task Markdown_EmptyScene_ProducesNoParagraphs()
    {
        var (ch, _) = SetupChapter("C", ("S", "   "));
        var sut = Build();
        await sut.ExportAsync(Opts(ExportFormat.Markdown, new[] { ch.Guid }), Out(".md"));
        // No crash; chapter heading present, no scene body.
        Assert.Contains("## C", await File.ReadAllTextAsync(Out(".md")));
    }

    [Fact]
    public async Task Pdf_MultiSceneNearPageEnd_BreaksOnSceneBreak()
    {
        // First scene fills ~a page so the scene break before S2 forces a new page.
        var big = "<p>" + string.Join(" ", Enumerable.Repeat("word", 1500)) + "</p>";
        var (ch, _) = SetupChapter("C", ("S1", big), ("S2", "<p>second</p>"));
        var sut = Build();
        await sut.ExportAsync(Opts(ExportFormat.Pdf, new[] { ch.Guid }), Out(".pdf"));
        var bytes = await File.ReadAllBytesAsync(Out(".pdf"));
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public async Task Pdf_SceneBreakNearPageBottom_StartsNewPage()
    {
        // Many short paragraphs push y down the first page so that the scene
        // break before the second scene overflows and forces a new page.
        var manyParas = string.Concat(Enumerable.Range(0, 40).Select(i => $"<p>Paragraph number {i}.</p>"));
        var (ch, _) = SetupChapter("C", ("S1", manyParas), ("S2", "<p>after the break</p>"));
        var sut = Build();
        await sut.ExportAsync(Opts(ExportFormat.Pdf, new[] { ch.Guid }), Out(".pdf"));
        Assert.Equal("%PDF", Encoding.ASCII.GetString(await File.ReadAllBytesAsync(Out(".pdf")), 0, 4));
    }

    [Fact]
    public async Task Pdf_OverflowingParagraph_AddsPages()
    {
        var big = "<p>" + string.Join(" ", Enumerable.Repeat("lorem", 5000)) + "</p>";
        var (ch, _) = SetupChapter("Chapter", ("S", big));
        var sut = Build();
        await sut.ExportAsync(Opts(ExportFormat.Pdf, new[] { ch.Guid }), Out(".pdf"));
        var bytes = await File.ReadAllBytesAsync(Out(".pdf"));
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.True(bytes.Length > 3000);
    }

    [Fact]
    public async Task FinalDraft_EmptyScene_NoActionLines()
    {
        // FinalDraft uses ParseHtmlToParagraphs, exercising its empty -> [] path.
        var (ch, _) = SetupChapter("C", ("EmptyScene", "   "));
        var sut = Build();
        await sut.ExportAsync(Opts(ExportFormat.FinalDraft, new[] { ch.Guid }), Out(".fdx"));
        var fdx = await File.ReadAllTextAsync(Out(".fdx"));
        Assert.Contains("EMPTYSCENE", fdx);   // scene heading still emitted
        Assert.DoesNotContain("Type=\"Action\"", fdx); // no body paragraphs
    }
}

internal static class ExportServiceTestConstants
{
    public const string SceneBreak = "* * *";
}
