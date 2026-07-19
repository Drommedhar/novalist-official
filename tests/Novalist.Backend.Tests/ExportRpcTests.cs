using Novalist.Backend;
using Novalist.Backend.Rpc;
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
    public void Formats_ListsAllSeven()
    {
        Assert.Equal(
            new[] { "Epub", "Docx", "Pdf", "Markdown", "FinalDraft", "LaTeX", "Codex" },
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
    public async Task Run_UnknownFormat_Throws()
    {
        var output = Path.Combine(_root, "out.xyz");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.RunAsync("no-such-format", output, "ExpNovel", "Tester", true, []));
    }
}
