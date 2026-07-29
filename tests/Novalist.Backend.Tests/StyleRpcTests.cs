using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Craft reports over a scene, a chapter, and the whole book.</summary>
public sealed class StyleRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly StyleRpc _rpc;
    private readonly string _chapterGuid;
    private readonly string _sceneId;

    public StyleRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-style-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "StyleNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();

        var chapter = _workspace.Projects.CreateChapterAsync("One").GetAwaiter().GetResult();
        var scene = _workspace.Projects.CreateSceneAsync(chapter.Guid, "S").GetAwaiter().GetResult();
        _chapterGuid = chapter.Guid;
        _sceneId = scene.Id;
        _workspace.WriteSceneAsync(chapter.Guid, scene.Id,
            "<p>She walked slowly. She saw the door. The door was opened by the guard.</p>",
            "She walked slowly. She saw the door. The door was opened by the guard.")
            .GetAwaiter().GetResult();

        _rpc = new StyleRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static StyleFindingDto Find(StyleReportDto r, string key) =>
        r.Findings.Single(f => f.Key == key);

    [Fact]
    public async Task Scene_ReportsAdverbsFilterWordsAndPassive()
    {
        var report = await _rpc.SceneAsync(_chapterGuid, _sceneId);

        Assert.True(report.WordCount > 0);
        Assert.Equal(3, report.SentenceCount);
        Assert.Equal(1, Find(report, "adverbs").Count);
        Assert.Equal(1, Find(report, "filterWords").Count);
        Assert.Equal(1, Find(report, "passiveVoice").Count);
    }

    [Fact]
    public async Task Scene_StripsMarkupBeforeCounting()
    {
        // "p" and "strong" must not be counted as words.
        await _workspace.WriteSceneAsync(_chapterGuid, _sceneId,
            "<p><strong>One</strong> two.</p>", "One two.");

        var report = await _rpc.SceneAsync(_chapterGuid, _sceneId);

        Assert.Equal(2, report.WordCount);
    }

    [Fact]
    public async Task Book_CoversEveryChapterWhenNoFilterGiven()
    {
        var second = await _workspace.Projects.CreateChapterAsync("Two");
        var scene = await _workspace.Projects.CreateSceneAsync(second.Guid, "S2");
        await _workspace.WriteSceneAsync(second.Guid, scene.Id,
            "<p>He moved quickly and quietly.</p>", "He moved quickly and quietly.");

        var report = await _rpc.BookAsync();

        // One adverb from chapter one, two from chapter two.
        Assert.Equal(3, Find(report, "adverbs").Count);
    }

    [Fact]
    public async Task Book_WithChapterFilter_CoversOnlyThatChapter()
    {
        var second = await _workspace.Projects.CreateChapterAsync("Two");
        var scene = await _workspace.Projects.CreateSceneAsync(second.Guid, "S2");
        await _workspace.WriteSceneAsync(second.Guid, scene.Id,
            "<p>He moved quickly and quietly.</p>", "He moved quickly and quietly.");

        var report = await _rpc.BookAsync(second.Guid);

        Assert.Equal(2, Find(report, "adverbs").Count);
    }

    [Fact]
    public async Task Book_LanguageFollowsTheWritingLanguage()
    {
        _workspace.Settings.Settings.AutoReplacementLanguage = "de-low";

        var report = await _rpc.BookAsync();

        Assert.Equal("de", report.Language);
        // German has no adverb suffix list, so that report is unsupported
        // rather than silently zero.
        Assert.False(Find(report, "adverbs").Supported);
    }

    [Fact]
    public async Task Book_ExamplesCarryContext()
    {
        var report = await _rpc.BookAsync();
        var adverbs = Find(report, "adverbs");

        Assert.NotEmpty(adverbs.Examples);
        Assert.False(string.IsNullOrWhiteSpace(adverbs.Examples[0].Context));
    }
}
