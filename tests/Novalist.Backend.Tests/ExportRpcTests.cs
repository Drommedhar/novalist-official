using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Services;
using Novalist.Sdk.Models;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class ExportRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly ExportRpc _rpc;

    public ExportRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-exp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "ExpNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        var chapter = _workspace.Projects.CreateChapterAsync("Kapitel Eins").GetAwaiter().GetResult();
        var scene = _workspace.Projects.CreateSceneAsync(chapter.Guid, "Anfang").GetAwaiter().GetResult();
        _workspace.WriteSceneAsync(chapter.Guid, scene.Id,
            "<p>Es war eine dunkle und stuermische Nacht.</p>",
            "Es war eine dunkle und stuermische Nacht.").GetAwaiter().GetResult();
        _rpc = new ExportRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void Formats_ListsAllBuiltIns()
    {
        Assert.Equal(
            new[] { "Epub", "Docx", "Pdf", "Markdown", "FinalDraft", "LaTeX", "Codex", "CodexPdf" },
            _rpc.Formats());
    }

    [Fact]
    public void ExtensionFormats_EmptyByDefault_ReflectsContributions()
    {
        Assert.Empty(_rpc.ExtensionFormats());

        _workspace.ExtensionsHost.ExportFormats.Add(new ExportFormatDescriptor
        {
            FormatKey = "fountain",
            DisplayName = "Fountain",
            FileExtension = ".fountain"
        });
        _workspace.ExtensionsHost.ExportFormats.Add(new ExportFormatDescriptor
        {
            FormatKey = "html",
            DisplayName = "Web page",
            FileExtension = ".html",
            SupportsCover = true
        });

        var formats = _rpc.ExtensionFormats();
        // A screenplay has nowhere to put a cover; a web page does. The Export
        // view shows its cover toggle from this.
        Assert.Contains(formats, f => f.FormatKey == "fountain" && !f.SupportsCover);
        Assert.Contains(formats, f => f.FormatKey == "html" && f.SupportsCover);
    }

    [Fact]
    public async Task TimelineOutline_ProducesFile()
    {
        var output = Path.Combine(_root, "outline.md");
        var result = await _rpc.TimelineOutlineAsync(output);
        Assert.True(result.Success);
    }

    [Theory]
    [InlineData("Markdown", ".md")]
    [InlineData("Epub", ".epub")]
    [InlineData("Docx", ".docx")]
    [InlineData("Pdf", ".pdf")]
    [InlineData("FinalDraft", ".fdx")]
    [InlineData("LaTeX", ".tex")]
    [InlineData("Codex", ".md")]
    [InlineData("CodexPdf", ".pdf")]
    public async Task Run_ProducesNonEmptyFile(string format, string extension)
    {
        var output = Path.Combine(_root, $"out-{format}{extension}");

        var result = await _rpc.RunAsync(format, output, "ExpNovel", "Tester", true, []);

        Assert.True(result.Success);
        Assert.True(result.SizeBytes > 0);
    }

    [Fact]
    public async Task Run_WithShunnPreset_ProducesFile()
    {
        var output = Path.Combine(_root, "shunn.docx");
        var result = await _rpc.RunAsync(
            "Docx", output, "ExpNovel", "Tester", true, [], "shunn-manuscript");
        Assert.True(result.Success);
        Assert.True(result.SizeBytes > 0);
    }

    [Fact]
    public async Task Run_CustomLayout_IsUsedRatherThanTheDefault()
    {
        // A layout is only worth picking if the file comes out in it. The
        // export reads custom layouts off the open book, so one saved through
        // the layout editor has to reach ExportOptions.
        var presets = new ExportPresetRpc(_workspace);
        var made = await presets.DuplicateAsync("default", "Wide margins");
        var mine = made.Single(p => p.IsCustom) with { EbookCss = "p { color: rebeccapurple; }" };
        await presets.SaveAsync(mine);

        var output = Path.Combine(_root, "custom.epub");
        await _rpc.RunAsync("Epub", output, "ExpNovel", "Tester", true,
            AllChapterGuids(), mine.Id);

        using var zip = System.IO.Compression.ZipFile.OpenRead(output);
        using var css = new StreamReader(zip.GetEntry("OEBPS/styles.css")!.Open());
        Assert.Contains("rebeccapurple", await css.ReadToEndAsync());
    }

    [Fact]
    public async Task Run_ExtensionFormat_InvokesContributedHandler()
    {
        _workspace.ExtensionsHost.ExportFormats.Add(new ExportFormatDescriptor
        {
            FormatKey = "fountain",
            DisplayName = "Fountain",
            FileExtension = ".fountain",
            Export = ctx => File.WriteAllTextAsync(ctx.OutputPath, "TITLE: " + ctx.BookName)
        });

        var output = Path.Combine(_root, "out.fountain");
        var result = await _rpc.RunAsync("fountain", output, "", "Tester", true, []);
        Assert.True(result.Success);
        Assert.Contains("Untitled", await File.ReadAllTextAsync(output));
    }

    [Fact]
    public async Task Run_Codex_HonoursEntitySelection()
    {
        var entities = new EntityService(_workspace.Projects);
        var kept = new Novalist.Core.Models.CharacterData { Name = "Mira" };
        var dropped = new Novalist.Core.Models.CharacterData { Name = "Jonas" };
        await entities.SaveCharacterAsync(kept);
        await entities.SaveCharacterAsync(dropped);

        var output = Path.Combine(_root, "selected-codex.md");
        var result = await _rpc.RunAsync(
            "Codex", output, "ExpNovel", "Tester", true, [], null,
            [$"character:{kept.Id}"],
            new Dictionary<string, string> { ["characters"] = "Charaktere" });

        Assert.True(result.Success);
        var md = await File.ReadAllTextAsync(output);
        Assert.Contains("Mira", md);
        Assert.DoesNotContain("Jonas", md);
        Assert.Contains("## Charaktere", md);   // labels arrive in the UI language
    }

    [Fact]
    public async Task Run_UnknownFormat_Throws()
    {
        var output = Path.Combine(_root, "out.xyz");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.RunAsync("no-such-format", output, "ExpNovel", "Tester", true, []));
    }

    // ── Cover and language reaching the exported file ──

    /// <summary>Smallest valid PNG: a 1x1 opaque pixel.</summary>
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>Puts a cover image on the active book, the way the Dashboard does.</summary>
    private void SetBookCover()
    {
        var bookRoot = _workspace.Projects.ActiveBookRoot!;
        var images = Path.Combine(bookRoot, "Images");
        Directory.CreateDirectory(images);
        File.WriteAllBytes(Path.Combine(images, "cover.png"), OnePixelPng);
        _workspace.Projects.ActiveBook!.CoverImage = "Images/cover.png";
    }

    private string[] AllChapterGuids() =>
        _workspace.Projects.GetChaptersOrdered().Select(c => c.Guid).ToArray();

    [Fact]
    public async Task Run_Epub_EmbedsTheBookCover()
    {
        SetBookCover();
        var outPath = Path.Combine(_root, "with-cover.epub");

        await _rpc.RunAsync("Epub", outPath, "Titel", "Autor", true, AllChapterGuids());

        using var zip = System.IO.Compression.ZipFile.OpenRead(outPath);
        Assert.NotNull(zip.GetEntry("OEBPS/cover.png"));
        Assert.NotNull(zip.GetEntry("OEBPS/cover.xhtml"));
    }

    [Fact]
    public async Task Run_Epub_IncludeCoverFalse_LeavesItOut()
    {
        SetBookCover();
        var outPath = Path.Combine(_root, "no-cover.epub");

        await _rpc.RunAsync("Epub", outPath, "Titel", "Autor", true, AllChapterGuids(),
            presetId: null, selectedEntityKeys: null, labels: null, includeCover: false);

        using var zip = System.IO.Compression.ZipFile.OpenRead(outPath);
        Assert.Null(zip.GetEntry("OEBPS/cover.png"));
    }

    [Fact]
    public async Task Run_Epub_NoCoverSet_StillExports()
    {
        var outPath = Path.Combine(_root, "plain.epub");

        var result = await _rpc.RunAsync("Epub", outPath, "Titel", "Autor", true, AllChapterGuids());

        Assert.True(result.Success);
        using var zip = System.IO.Compression.ZipFile.OpenRead(outPath);
        Assert.Null(zip.GetEntry("OEBPS/cover.xhtml"));
    }

    [Fact]
    public async Task Run_Epub_LanguageComesFromTheWritingLanguage()
    {
        _workspace.Settings.Settings.AutoReplacementLanguage = "de-guillemet";
        var outPath = Path.Combine(_root, "german.epub");

        await _rpc.RunAsync("Epub", outPath, "Titel", "Autor", true, AllChapterGuids());

        using var zip = System.IO.Compression.ZipFile.OpenRead(outPath);
        using var reader = new StreamReader(zip.GetEntry("OEBPS/content.opf")!.Open());
        Assert.Contains("<dc:language>de</dc:language>", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Run_ExtensionFormat_GetsTheSameLanguageAndCoverAsABuiltIn()
    {
        // A contributed format that is told only the path and the title produces
        // a file that claims to be English and has no cover, whatever the writer
        // set. Everything the built-in formats resolve is handed over too.
        SetBookCover();
        _workspace.Settings.Settings.AutoReplacementLanguage = "de-guillemet";

        ExportContext? seen = null;
        _workspace.ExtensionsHost.ExportFormats.Add(new ExportFormatDescriptor
        {
            FormatKey = "site",
            DisplayName = "Site",
            FileExtension = ".html",
            SupportsCover = true,
            Export = ctx =>
            {
                seen = ctx;
                return File.WriteAllTextAsync(ctx.OutputPath, "x");
            }
        });

        await _rpc.RunAsync("site", Path.Combine(_root, "out.html"), "Titel", "Autor",
            includeTitlePage: false, AllChapterGuids());

        Assert.NotNull(seen);
        Assert.Equal("Titel", seen!.BookName);
        Assert.Equal("Autor", seen.Author);
        Assert.Equal("de", seen.Language);
        Assert.EndsWith("cover.png", seen.CoverImagePath);
        Assert.False(seen.IncludeTitlePage);
        Assert.Equal(_workspace.Projects.ProjectRoot, seen.ProjectRoot);
    }

    [Fact]
    public async Task Run_ExtensionFormat_NoCoverWhenTheWriterAskedForNone()
    {
        SetBookCover();

        ExportContext? seen = null;
        _workspace.ExtensionsHost.ExportFormats.Add(new ExportFormatDescriptor
        {
            FormatKey = "site",
            DisplayName = "Site",
            FileExtension = ".html",
            SupportsCover = true,
            Export = ctx =>
            {
                seen = ctx;
                return File.WriteAllTextAsync(ctx.OutputPath, "x");
            }
        });

        await _rpc.RunAsync("site", Path.Combine(_root, "bare.html"), "Titel", "Autor", true,
            AllChapterGuids(), presetId: null, selectedEntityKeys: null, labels: null,
            includeCover: false);

        Assert.Equal(string.Empty, seen!.CoverImagePath);
    }

    [Fact]
    public async Task Run_ExtensionFormat_ThatCannotHoldACoverIsNotGivenOne()
    {
        // The Export view hides the toggle for these, so includeCover arrives at
        // its default. A screenplay handed a cover path would either ignore it or
        // do something odd with it; neither is worth the risk.
        SetBookCover();

        ExportContext? seen = null;
        _workspace.ExtensionsHost.ExportFormats.Add(new ExportFormatDescriptor
        {
            FormatKey = "fountain",
            DisplayName = "Fountain",
            FileExtension = ".fountain",
            Export = ctx =>
            {
                seen = ctx;
                return File.WriteAllTextAsync(ctx.OutputPath, "x");
            }
        });

        await _rpc.RunAsync("fountain", Path.Combine(_root, "script.fountain"), "Titel", "Autor",
            true, AllChapterGuids());

        Assert.Equal(string.Empty, seen!.CoverImagePath);
    }

    // -- Footnotes --
    //
    // The manual has promised Word footnotes in DOCX and Markdown footnote
    // syntax for a long time. Every format used to get the same thing instead:
    // a paragraph of literal text glued onto the end of the scene, with the
    // anchor in the prose reduced to a loose digit.

    /// <summary>A scene whose prose carries one real footnote anchor.</summary>
    private async Task<string[]> SceneWithAFootnoteAsync()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("Kapitel Zwei");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Anfang");
        scene.Footnotes = [new Novalist.Core.Models.SceneFootnote
        {
            Id = "note-a",
            Number = 1,
            Text = "The bell tower was rebuilt in 1911."
        }];
        await _workspace.Projects.SaveScenesAsync();
        await _workspace.WriteSceneAsync(
            chapter.Guid, scene.Id,
            "<p>She counted the bells<sup class=\"nv-fn\" data-fn-id=\"note-a\">1</sup> again.</p>",
            "She counted the bells again.");
        return [chapter.Guid, scene.Id];
    }

    [Fact]
    public async Task Markdown_WritesRealFootnoteSyntax()
    {
        await SceneWithAFootnoteAsync();
        var output = Path.Combine(_root, "notes.md");

        await _rpc.RunAsync("Markdown", output, "ExpNovel", "Tester", false, AllChapterGuids());

        var md = await File.ReadAllTextAsync(output);
        Assert.Contains("bells[^1] again", md);
        Assert.Contains("[^1]: The bell tower was rebuilt in 1911.", md);
        // The block that used to be appended to every format is gone.
        Assert.DoesNotContain("Footnotes", md);
    }

    [Fact]
    public async Task LaTeX_WritesARealFootnote()
    {
        await SceneWithAFootnoteAsync();
        var output = Path.Combine(_root, "notes.tex");

        await _rpc.RunAsync("LaTeX", output, "ExpNovel", "Tester", false, AllChapterGuids());

        var tex = await File.ReadAllTextAsync(output);
        Assert.Contains("\\footnote{The bell tower was rebuilt in 1911.}", tex);
    }

    [Fact]
    public async Task Epub_WritesNoterefsAndAsides()
    {
        await SceneWithAFootnoteAsync();
        var output = Path.Combine(_root, "notes.epub");

        await _rpc.RunAsync("Epub", output, "ExpNovel", "Tester", false, AllChapterGuids());

        using var zip = System.IO.Compression.ZipFile.OpenRead(output);
        var entry = zip.Entries.Single(e => e.FullName == "OEBPS/chapter-2.xhtml");
        using var reader = new StreamReader(entry.Open());
        var xhtml = await reader.ReadToEndAsync();

        // EPUB 3 popup notes: a noteref pointing at an aside, both in the file.
        Assert.Contains("epub:type=\"noteref\"", xhtml);
        Assert.Contains("href=\"#fn1\"", xhtml);
        Assert.Contains("epub:type=\"footnote\" id=\"fn1\"", xhtml);
        Assert.Contains("The bell tower was rebuilt in 1911.", xhtml);
    }

    [Fact]
    public async Task Docx_WritesRealWordFootnotes()
    {
        await SceneWithAFootnoteAsync();
        var output = Path.Combine(_root, "notes.docx");

        await _rpc.RunAsync("Docx", output, "ExpNovel", "Tester", false, AllChapterGuids());

        using var zip = System.IO.Compression.ZipFile.OpenRead(output);

        async Task<string> ReadAsync(string name)
        {
            using var stream = zip.Entries.Single(e => e.FullName == name).Open();
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }

        // The part, its content type and its relationship all have to be there
        // or Word refuses to open the file at all.
        Assert.Contains("wordprocessingml.footnotes+xml", await ReadAsync("[Content_Types].xml"));
        Assert.Contains("relationships/footnotes", await ReadAsync("word/_rels/document.xml.rels"));

        var footnotes = await ReadAsync("word/footnotes.xml");
        Assert.Contains("w:id=\"0\" w:type=\"separator\"", footnotes);
        Assert.Contains("w:id=\"1\" w:type=\"continuationSeparator\"", footnotes);
        // The writer's own notes start at 2, after Word's two required ones.
        Assert.Contains("w:id=\"2\"", footnotes);
        Assert.Contains("The bell tower was rebuilt in 1911.", footnotes);

        var document = await ReadAsync("word/document.xml");
        Assert.Contains("<w:footnoteReference w:id=\"2\"/>", document);
        // The note text belongs in the footnote part, not in the body.
        Assert.DoesNotContain("bell tower was rebuilt", document);
    }

    [Fact]
    public async Task Pdf_SetsNotesUnderTheChapter()
    {
        await SceneWithAFootnoteAsync();
        var output = Path.Combine(_root, "notes.pdf");

        var result = await _rpc.RunAsync("Pdf", output, "ExpNovel", "Tester", false, AllChapterGuids());

        // PdfSharpCore lays text out a line at a time and cannot reserve the
        // foot of a page mid-paragraph, so the notes are set as chapter
        // endnotes. Only that the file is produced is assertable here.
        Assert.True(result.Success);
        Assert.True(result.SizeBytes > 0);
    }

    [Fact]
    public async Task AnAnchorWithNoNoteBehindItLeavesNoStrayDigit()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("Kapitel Drei");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Waise");
        await _workspace.WriteSceneAsync(
            chapter.Guid, scene.Id,
            "<p>She counted the bells<sup class=\"nv-fn\" data-fn-id=\"gone\">4</sup> again.</p>",
            "She counted the bells again.");
        var output = Path.Combine(_root, "orphan.md");

        await _rpc.RunAsync("Markdown", output, "ExpNovel", "Tester", false, AllChapterGuids());

        // An anchor whose note was deleted used to print its number into the
        // middle of the sentence.
        Assert.Contains("bells again", await File.ReadAllTextAsync(output));
    }

    [Fact]
    public async Task PreviewReportsWhatTheExportWouldContain()
    {
        var preview = await _rpc.PreviewAsync(AllChapterGuids());

        Assert.True(preview.Chapters > 0);
        Assert.True(preview.Scenes > 0);
        Assert.True(preview.Words > 0);
        Assert.True(preview.Pages >= 1);
        Assert.False(preview.PagesAreExact);
    }

    [Fact]
    public async Task PreviewOfNoChaptersIsEmpty()
    {
        var preview = await _rpc.PreviewAsync([]);

        Assert.Equal(0, preview.Chapters);
        Assert.Equal(0, preview.Words);
    }

    [Fact]
    public async Task Export_LeavesOutSceneesTakenOutOfTheBook()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("Chapter");
        var kept = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Kept");
        var parked = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Parked");
        await _workspace.WriteSceneAsync(chapter.Guid, kept.Id, "<p>Kept prose.</p>", "Kept prose.");
        await _workspace.WriteSceneAsync(
            chapter.Guid, parked.Id, "<p>Parked prose.</p>", "Parked prose.");

        parked.Inactive = true;
        await _workspace.Projects.SaveScenesAsync();

        var output = Path.Combine(_root, "parked.md");
        await _rpc.RunAsync("Markdown", output, "T", "A", true, [chapter.Guid]);

        var markdown = await File.ReadAllTextAsync(output);
        Assert.Contains("Kept prose.", markdown);
        Assert.DoesNotContain("Parked prose.", markdown);
    }


    // ── Substitutions that touch the output and never the prose ──

    [Fact]
    public async Task Replacements_RoundTripInOrderAndReachTheFile()
    {
        Assert.Empty(_rpc.Replacements());
        Assert.Contains("title", _rpc.Tokens());

        var saved = await _rpc.SaveReplacementsAsync(
        [
            new ExportReplacementDto("", "dunkle", "finstere", false, false, true, 0),
            // Blank rules are not rules; this one is dropped rather than stored.
            new ExportReplacementDto("", "  ", "x", false, false, true, 1),
            new ExportReplacementDto("", "finstere", "rabenschwarze", false, false, true, 2)
        ]);

        // Order is the list's order, and it is renumbered from zero so a
        // reordering edit does not leave gaps behind.
        Assert.Equal(["dunkle", "finstere"], saved.Select(r => r.Find));
        Assert.Equal([0, 1], saved.Select(r => r.Order));
        Assert.All(saved, r => Assert.NotEmpty(r.Id));

        var output = Path.Combine(_root, "replaced.md");
        await _rpc.RunAsync("Markdown", output, "T", "A", true, AllChapterGuids());

        // Rules ran in order, so the first rule's output fed the second - and
        // the scene on disk is untouched, unlike a Replace All.
        var markdown = await File.ReadAllTextAsync(output);
        Assert.Contains("rabenschwarze", markdown);
        Assert.DoesNotContain("dunkle", markdown);
    }

    [Fact]
    public async Task Replacements_NeedABookAndSurviveNothingBeingPassed()
    {
        var bare = new ExportRpc(new Workspace(Path.Combine(_root, "no-project")));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bare.SaveReplacementsAsync([]));

        Assert.Empty(await _rpc.SaveReplacementsAsync(null!));
    }

}
