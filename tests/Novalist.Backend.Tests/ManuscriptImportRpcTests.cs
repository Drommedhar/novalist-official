using Novalist.Backend;
using Novalist.Backend.Extensions;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Bringing an existing manuscript into a project. Preview must never write;
/// run must only ever append.
/// </summary>
[Collection("BackendStatics")]
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
        Log.EnableFileLogging(false);
        Log.SetSinkOverride(null);
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
        Assert.Contains(".scriv", formats);
        Assert.Contains(".scrivx", formats);
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
    public void Preview_DiagnosticsRecordInputShapeAndCountsWithoutNamesPathsOrProse()
    {
        Log.SetSinkOverride(new LogFileSink(Path.Combine(_root, "logs")));
        Log.EnableFileLogging(true);

        _rpc.Preview(WriteMarkdown("# Private chapter title\n\nPrivate manuscript sentence."));
        _rpc.Preview(Path.Combine(_root, "missing.docx"));
        _rpc.Preview(Path.Combine(_root, "missing.private-suffix"));
        _rpc.Preview(Path.Combine(_root, "missing.scrivx"));
        var malformedBinder = Path.Combine(_root, "Private Binder.scrivx");
        File.WriteAllText(malformedBinder, "<PrivateElement></SecretEndTag>");
        _rpc.Preview(malformedBinder);

        var content = File.ReadAllText(Log.CurrentLogPath);
        Assert.Contains("manuscriptImport/preview complete source=file extension=.md", content);
        Assert.Contains("chapters=1 scenes=1", content);
        Assert.Contains("manuscriptImport/preview empty source=missing extension=.docx", content);
        Assert.Contains("manuscriptImport/preview empty source=missing extension=other", content);
        Assert.DoesNotContain("private-suffix", content);
        Assert.Contains(
            "manuscriptImport/preview scrivener stage=path reason=not-found type=none.",
            content);
        const string invalidXml =
            "manuscriptImport/preview scrivener stage=manifest reason=invalid-xml type=XmlException";
        Assert.Contains(invalidXml, content);
        Assert.Equal(1, content.Split(invalidXml, StringSplitOptions.None).Length - 1);
        Assert.Contains("line=1 position=", content);
        Assert.Contains("code=0x", content);
        Assert.Contains("message=\"", content);
        Assert.Contains("An XML start tag does not match its end tag.", content);
        Assert.DoesNotContain("PrivateElement", content);
        Assert.DoesNotContain("SecretEndTag", content);
        Assert.DoesNotContain(_root, content);
        Assert.DoesNotContain("manuscript.md", content);
        Assert.DoesNotContain("Private Binder", content);
        Assert.DoesNotContain("Private chapter title", content);
        Assert.DoesNotContain("Private manuscript sentence", content);
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
        Log.SetSinkOverride(new LogFileSink(Path.Combine(_root, "logs")));
        Log.EnableFileLogging(true);
        using var bare = new Workspace(Path.Combine(_root, "settings2"));
        var rpc = new ManuscriptImportRpc(bare);
        var path = Path.Combine(_root, "Private manuscript.md");
        File.WriteAllText(path, "# Private chapter title\n\nPrivate manuscript sentence.");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => rpc.RunAsync(path));

        var content = File.ReadAllText(Log.CurrentLogPath);
        Assert.Contains(
            "manuscriptImport/run failed stage=validate source=file extension=.md",
            content);
        Assert.Contains("type=System.InvalidOperationException", content);
        Assert.DoesNotContain(_root, content);
        Assert.DoesNotContain("Private manuscript", content);
        Assert.DoesNotContain("Private chapter title", content);
        Assert.DoesNotContain("Private manuscript sentence", content);
        Assert.DoesNotContain("No project open", content);
    }
}
