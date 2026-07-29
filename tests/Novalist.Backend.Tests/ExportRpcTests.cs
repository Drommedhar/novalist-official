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
    public void Presets_ListNamedPresets()
    {
        var presets = _rpc.Presets();
        Assert.Contains(presets, p => p.Id == "default");
        Assert.Contains(presets, p => p.Id == "shunn-manuscript" && p.DisplayName == "Shunn Manuscript Format");
        Assert.All(presets, p => Assert.False(string.IsNullOrWhiteSpace(p.Description)));
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

        var formats = _rpc.ExtensionFormats();
        Assert.Contains(formats, f => f.FormatKey == "fountain" && f.FileExtension == ".fountain");
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
            "Docx", output, "ExpNovel", "Tester", true, [], "shunn-manuscript", smf: true);
        Assert.True(result.Success);
        Assert.True(result.SizeBytes > 0);
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
            "Codex", output, "ExpNovel", "Tester", true, [], null, false,
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
            presetId: null, smf: false, selectedEntityKeys: null, labels: null, includeCover: false);

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
}
