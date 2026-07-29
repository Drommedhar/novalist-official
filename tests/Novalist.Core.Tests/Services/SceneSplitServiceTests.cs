using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Splitting a scene in two and merging two into one.
///
/// The text is the easy part. What the writer used to lose doing this by hand -
/// order, date, stage, plotlines, analysis overrides - is what these assert.
/// </summary>
public class SceneSplitServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProjectService _projects = new(new FileService());
    private readonly SceneSplitService _sut;

    public SceneSplitServiceTests()
    {
        _sut = new SceneSplitService(_projects);
    }

    public void Dispose() => _dir.Dispose();

    private async Task<(ChapterData Chapter, SceneData Scene)> SceneAsync(string title = "Arrival")
    {
        await _projects.CreateProjectAsync(_dir.Path, "P", "Book");
        var chapter = await _projects.CreateChapterAsync("One");
        var scene = await _projects.CreateSceneAsync(chapter.Guid, title);
        return (chapter, scene);
    }

    private List<SceneData> Scenes(string chapterGuid)
        => [.. _projects.GetScenesForChapter(chapterGuid).OrderBy(s => s.Order)];

    // ── Splitting ──

    [Fact]
    public async Task SplitLeavesEachHalfWithItsOwnText()
    {
        var (chapter, scene) = await SceneAsync();
        await _projects.WriteSceneContentAsync(chapter, scene, "<p>One</p><p>Two</p>");

        var created = await _sut.SplitAsync(
            chapter.Guid, scene.Id, "<p>One</p>", "<p>Two</p>", newTitle: "");

        Assert.NotNull(created);
        Assert.Equal("<p>One</p>", await _projects.ReadSceneContentAsync(chapter, scene));
        Assert.Equal("<p>Two</p>", await _projects.ReadSceneContentAsync(chapter, created!));
    }

    [Fact]
    public async Task TheNewHalfSitsImmediatelyAfterTheOriginal()
    {
        // CreateSceneAsync appends, so without the reorder the second half would
        // land at the end of the chapter.
        var (chapter, scene) = await SceneAsync();
        await _projects.CreateSceneAsync(chapter.Guid, "Later scene");

        var created = await _sut.SplitAsync(chapter.Guid, scene.Id, "<p>a</p>", "<p>b</p>", "");

        Assert.Equal(
            [scene.Id, created!.Id],
            Scenes(chapter.Guid).Take(2).Select(s => s.Id));
    }

    [Fact]
    public async Task OrderIsContiguousAfterASplit()
    {
        var (chapter, scene) = await SceneAsync();
        await _projects.CreateSceneAsync(chapter.Guid, "Later");

        await _sut.SplitAsync(chapter.Guid, scene.Id, "<p>a</p>", "<p>b</p>", "");

        Assert.Equal([1, 2, 3], Scenes(chapter.Guid).Select(s => s.Order));
    }

    [Fact]
    public async Task TheOriginalKeepsItsIdAndSoItsHistory()
    {
        var (chapter, scene) = await SceneAsync();

        var created = await _sut.SplitAsync(chapter.Guid, scene.Id, "<p>a</p>", "<p>b</p>", "");

        Assert.Contains(Scenes(chapter.Guid), s => s.Id == scene.Id);
        Assert.NotEqual(scene.Id, created!.Id);
    }

    [Fact]
    public async Task MetadataThatStillDescribesBothHalvesIsCarried()
    {
        var (chapter, scene) = await SceneAsync();
        scene.Date = "2026-03-01";
        scene.DateRange = new StoryDateRange { Start = "2026-03-01", End = "2026-03-02" };
        scene.Stage = "revised";
        scene.LabelColor = "#ff0000";
        scene.PlotlineIds = ["plot-a"];
        scene.AnalysisOverrides = new SceneAnalysisOverrides { Pov = "Rose" };

        var created = await _sut.SplitAsync(chapter.Guid, scene.Id, "<p>a</p>", "<p>b</p>", "");

        Assert.Equal("2026-03-01", created!.Date);
        Assert.Equal("2026-03-02", created.DateRange!.End);
        Assert.Equal("revised", created.Stage);
        Assert.Equal("#ff0000", created.LabelColor);
        Assert.Equal(["plot-a"], created.PlotlineIds);
        Assert.Equal("Rose", created.AnalysisOverrides!.Pov);
    }

    [Fact]
    public async Task TheSynopsisIsNotCarried()
    {
        // It described the whole scene; leaving a copy on both halves would make
        // two scenes claim to be about the same thing.
        var (chapter, scene) = await SceneAsync();
        scene.Synopsis = "She arrives and everything changes.";

        var created = await _sut.SplitAsync(chapter.Guid, scene.Id, "<p>a</p>", "<p>b</p>", "");

        Assert.True(string.IsNullOrEmpty(created!.Synopsis));
    }

    [Fact]
    public async Task CarriedPlotlinesAreACopyRatherThanTheSameList()
    {
        // Sharing the list would make editing one half's plotlines edit both.
        var (chapter, scene) = await SceneAsync();
        scene.PlotlineIds = ["plot-a"];

        var created = await _sut.SplitAsync(chapter.Guid, scene.Id, "<p>a</p>", "<p>b</p>", "");
        created!.PlotlineIds!.Add("plot-b");

        Assert.Equal(["plot-a"], scene.PlotlineIds);
    }

    [Theory]
    [InlineData("Arrival", "Arrival (2)")]
    [InlineData("Arrival (2)", "Arrival (3)")]
    [InlineData("Arrival (9)", "Arrival (10)")]
    [InlineData("", "(2)")]
    [InlineData("Ending (final)", "Ending (final) (2)")]
    public void TheDefaultTitleCountsUpRatherThanNesting(string title, string expected)
    {
        Assert.Equal(expected, SceneSplitService.ContinuationTitle(title));
    }

    [Fact]
    public async Task AGivenTitleWins()
    {
        var (chapter, scene) = await SceneAsync();

        var created = await _sut.SplitAsync(
            chapter.Guid, scene.Id, "<p>a</p>", "<p>b</p>", "  The Inn  ");

        Assert.Equal("The Inn", created!.Title);
    }

    [Fact]
    public async Task SplittingASceneThatIsGoneDoesNothing()
    {
        var (chapter, _) = await SceneAsync();

        Assert.Null(await _sut.SplitAsync(chapter.Guid, "no-such-scene", "<p>a</p>", "<p>b</p>", ""));
    }

    [Fact]
    public async Task SplittingInAChapterThatIsGoneDoesNothing()
    {
        var (_, scene) = await SceneAsync();

        Assert.Null(await _sut.SplitAsync("no-such-chapter", scene.Id, "<p>a</p>", "<p>b</p>", ""));
    }

    // ── Merging ──

    private async Task<(ChapterData Chapter, SceneData First, SceneData Second)> TwoScenesAsync()
    {
        await _projects.CreateProjectAsync(_dir.Path, "P", "Book");
        var chapter = await _projects.CreateChapterAsync("One");
        var first = await _projects.CreateSceneAsync(chapter.Guid, "A");
        var second = await _projects.CreateSceneAsync(chapter.Guid, "B");
        await _projects.WriteSceneContentAsync(chapter, first, "<p>First half.</p>");
        await _projects.WriteSceneContentAsync(chapter, second, "<p>Second half.</p>");
        return (chapter, first, second);
    }

    [Fact]
    public async Task MergeJoinsTheTextAndRemovesTheSecondScene()
    {
        var (chapter, first, second) = await TwoScenesAsync();

        Assert.True(await _sut.MergeAsync(chapter.Guid, first.Id, second.Id));

        Assert.Equal(
            "<p>First half.</p><p>Second half.</p>",
            await _projects.ReadSceneContentAsync(chapter, first));
        Assert.Equal([first.Id], Scenes(chapter.Guid).Select(s => s.Id));
    }

    [Fact]
    public async Task MergeAddsUpTheWordCounts()
    {
        var (chapter, first, second) = await TwoScenesAsync();
        first.WordCount = 100;
        second.WordCount = 250;

        await _sut.MergeAsync(chapter.Guid, first.Id, second.Id);

        Assert.Equal(350, Scenes(chapter.Guid).Single().WordCount);
    }

    [Fact]
    public async Task TheSurvivingScenesMetadataWins()
    {
        var (chapter, first, second) = await TwoScenesAsync();
        first.Synopsis = "Kept";
        second.Synopsis = "Discarded";

        await _sut.MergeAsync(chapter.Guid, first.Id, second.Id);

        Assert.Equal("Kept", Scenes(chapter.Guid).Single().Synopsis);
    }

    [Fact]
    public async Task ASynopsisOnlyTheSecondHadIsKeptRatherThanLost()
    {
        var (chapter, first, second) = await TwoScenesAsync();
        second.Synopsis = "The only one there is";
        second.Notes = "And the notes";

        await _sut.MergeAsync(chapter.Guid, first.Id, second.Id);

        var merged = Scenes(chapter.Guid).Single();
        Assert.Equal("The only one there is", merged.Synopsis);
        Assert.Equal("And the notes", merged.Notes);
    }

    [Fact]
    public async Task PlotlinesAreUnionedBecauseAMergedSceneServesBothThreads()
    {
        var (chapter, first, second) = await TwoScenesAsync();
        first.PlotlineIds = ["a"];
        second.PlotlineIds = ["b", "a"];

        await _sut.MergeAsync(chapter.Guid, first.Id, second.Id);

        Assert.Equal(["a", "b"], Scenes(chapter.Guid).Single().PlotlineIds);
    }

    [Fact]
    public async Task PlotlinesTheFirstAlreadyHadSurviveASecondWithNone()
    {
        var (chapter, first, second) = await TwoScenesAsync();
        first.PlotlineIds = ["a"];

        await _sut.MergeAsync(chapter.Guid, first.Id, second.Id);

        Assert.Equal(["a"], Scenes(chapter.Guid).Single().PlotlineIds);
    }

    [Fact]
    public async Task MergingASceneWithItselfIsRefused()
    {
        // It would concatenate the scene onto itself and then delete it.
        var (chapter, first, _) = await TwoScenesAsync();

        Assert.False(await _sut.MergeAsync(chapter.Guid, first.Id, first.Id));
        Assert.Equal(2, Scenes(chapter.Guid).Count);
    }

    [Fact]
    public async Task MergingASceneThatIsGoneDoesNothing()
    {
        var (chapter, first, _) = await TwoScenesAsync();

        Assert.False(await _sut.MergeAsync(chapter.Guid, first.Id, "no-such-scene"));
        Assert.Equal(2, Scenes(chapter.Guid).Count);
    }

    [Fact]
    public async Task MergingInAChapterThatIsGoneDoesNothing()
    {
        var (_, first, second) = await TwoScenesAsync();

        Assert.False(await _sut.MergeAsync("no-such-chapter", first.Id, second.Id));
    }

    // ── Round trip ──

    [Fact]
    public async Task SplittingThenMergingGetsTheTextBack()
    {
        var (chapter, scene) = await SceneAsync();
        await _projects.WriteSceneContentAsync(chapter, scene, "<p>One</p><p>Two</p>");

        var created = await _sut.SplitAsync(chapter.Guid, scene.Id, "<p>One</p>", "<p>Two</p>", "");
        await _sut.MergeAsync(chapter.Guid, scene.Id, created!.Id);

        Assert.Equal("<p>One</p><p>Two</p>", await _projects.ReadSceneContentAsync(chapter, scene));
        Assert.Single(Scenes(chapter.Guid));
    }
}
