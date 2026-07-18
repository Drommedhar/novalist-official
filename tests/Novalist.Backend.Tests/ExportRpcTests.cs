using Novalist.Backend;
using Novalist.Backend.Rpc;
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
}
