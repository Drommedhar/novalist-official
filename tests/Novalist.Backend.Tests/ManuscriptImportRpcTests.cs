using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Bringing an existing manuscript into a project. Preview must never write;
/// run must only ever append.
/// </summary>
public sealed class ManuscriptImportRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly ManuscriptImportRpc _rpc;

    public ManuscriptImportRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-msimport-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "ImportNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new ManuscriptImportRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string WriteMarkdown(string content)
    {
        var path = Path.Combine(_root, "manuscript.md");
        File.WriteAllText(path, content);
        return path;
    }

    private const string TwoChapters =
        "# The Arrival\n\nShe stepped off the train.\n\n***\n\nThe platform was empty.\n\n"
        + "# The Departure\n\nShe did not look back.";

    [Fact]
    public void Formats_ListTheSupportedExtensions()
    {
        var formats = _rpc.Formats();

        Assert.Contains(".docx", formats);
        Assert.Contains(".epub", formats);
        Assert.Contains(".md", formats);
    }

    [Fact]
    public void Preview_DescribesWhatWouldBeCreated()
    {
        var plan = _rpc.Preview(WriteMarkdown(TwoChapters));

        Assert.Equal("markdown", plan.Format);
        Assert.Equal(2, plan.ChapterCount);
        Assert.Equal(3, plan.SceneCount);
        Assert.True(plan.WordCount > 0);
        Assert.Equal("The Arrival", plan.Chapters[0].Title);
    }

    [Fact]
    public void Preview_WritesNothing()
    {
        _rpc.Preview(WriteMarkdown(TwoChapters));

        // The project must be untouched until the writer commits.
        Assert.Empty(_workspace.Projects.GetChaptersOrdered());
    }

    [Fact]
    public void Preview_UnreadableFile_IsEmptyNotAnError()
    {
        var plan = _rpc.Preview(Path.Combine(_root, "missing.docx"));

        Assert.Equal(0, plan.ChapterCount);
        Assert.Empty(plan.Chapters);
    }

    [Fact]
    public async Task Run_CreatesTheChaptersAndScenes()
    {
        var result = await _rpc.RunAsync(WriteMarkdown(TwoChapters));

        Assert.Equal(2, result.Chapters);
        Assert.Equal(3, result.Scenes);

        var chapters = _workspace.Projects.GetChaptersOrdered();
        Assert.Equal(2, chapters.Count);
        Assert.Equal("The Arrival", chapters[0].Title);
        Assert.Equal(2, _workspace.Projects.GetScenesForChapter(chapters[0].Guid).Count);
    }

    [Fact]
    public async Task Run_WritesTheProseIntoTheScenes()
    {
        await _rpc.RunAsync(WriteMarkdown(TwoChapters));

        var chapter = _workspace.Projects.GetChaptersOrdered()[0];
        var scene = _workspace.Projects.GetScenesForChapter(chapter.Guid)[0];
        var html = await _workspace.Projects.ReadSceneContentAsync(chapter, scene);

        Assert.Contains("She stepped off the train.", html);
    }

    [Fact]
    public async Task Run_SetsWordCounts()
    {
        await _rpc.RunAsync(WriteMarkdown("# One\n\nOne two three four five."));

        var chapter = _workspace.Projects.GetChaptersOrdered()[0];
        Assert.Equal(5, _workspace.Projects.GetScenesForChapter(chapter.Guid)[0].WordCount);
    }

    [Fact]
    public async Task Run_Appends_LeavingExistingChaptersAlone()
    {
        var existing = await _workspace.Projects.CreateChapterAsync("Already here");

        await _rpc.RunAsync(WriteMarkdown(TwoChapters));

        var chapters = _workspace.Projects.GetChaptersOrdered();
        Assert.Equal(3, chapters.Count);
        Assert.Contains(chapters, c => c.Guid == existing.Guid);
    }

    [Fact]
    public async Task Run_TwiceDuplicatesRatherThanDestroys()
    {
        var path = WriteMarkdown(TwoChapters);

        await _rpc.RunAsync(path);
        await _rpc.RunAsync(path);

        // Appending twice is recoverable; replacing would not be.
        Assert.Equal(4, _workspace.Projects.GetChaptersOrdered().Count);
    }

    [Fact]
    public async Task Run_UnreadableFile_CreatesNothing()
    {
        var result = await _rpc.RunAsync(Path.Combine(_root, "missing.docx"));

        Assert.Equal(0, result.Chapters);
        Assert.Empty(_workspace.Projects.GetChaptersOrdered());
    }

    [Fact]
    public async Task Run_WithoutAProject_Throws()
    {
        using var bare = new Workspace(Path.Combine(_root, "settings2"));
        var rpc = new ManuscriptImportRpc(bare);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => rpc.RunAsync(WriteMarkdown(TwoChapters)));
    }
}
