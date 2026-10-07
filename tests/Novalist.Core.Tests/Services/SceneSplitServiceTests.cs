using NSubstitute;
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

    [Fact]
    public void AnnotationTransfersLeaveProseAndOtherAttributeValuesUntouched()
    {
        var first = new SceneData { Comments = [new() { Id = "same", Text = "First" }] };
        var second = new SceneData { Comments = [new() { Id = "same", Text = "Second" }] };
        const string untouched = "<p title=\"data-comment-id='same'\">data-comment-id='same'</p>";
        var result = SceneAnnotationTransfer.Merge(first, second, untouched + "<p data-comment-id='same'>Anchor</p>");
        Assert.StartsWith(untouched, result);
        Assert.EndsWith("<p data-comment-id=\"" + first.Comments![1].Id + "\">Anchor</p>", result);

        var created = new SceneData();
        SceneAnnotationTransfer.Split(second, created, "<p>Original</p>", untouched);
        Assert.Empty(created.Comments!);
        Assert.Single(second.Comments!);
    }

    [Fact]
    public void RejoiningSharedAnnotationsKeepsOneThreadWhenBothCopiesAreIdentical()
    {
        var original = new SceneData { Comments = [new() { Id = "shared", Text = "Feedback" }] };
        var created = new SceneData();
        const string html = "<p data-comment-id='shared'>Anchor</p>";
        SceneAnnotationTransfer.Split(original, created, html, html);

        Assert.Equal(html, SceneAnnotationTransfer.Merge(original, created, html));
        Assert.Single(original.Comments!);
    }

    [Fact]
    public async Task SplitMovesAnnotationsWithTheirMarkersAndPreservesPlanningFlags()
    {
        var (chapter, original) = await SceneAsync();
        original.Inactive = true;
        original.ExcludeFromExport = true;
        original.LabelKey = "review";
        original.Notes = "Shared instructions";
        original.Goal = "Leave";
        original.Outcome = "Stayed";
        original.NarrativeMode = "memory";
        original.Strand = "past";
        original.FocusEntityId = "hero";
        original.Cast = ["hero"];
        original.Properties = new() { ["tone"] = "quiet" };
        original.Footnotes = [new() { Id = "fn", Number = 1, Text = "Citation" }];
        original.Comments = [new() { Id = "shared", Text = "Feedback", Replies = [new() { Text = "Reply" }] },
            new() { Id = "orphan", Text = "Unanchored note" }];
        const string before = "<p><span data-comment-id='shared'>First</span></p>";
        const string after = "<p><span data-comment-id='shared'>Second</span><sup data-fn-id='fn'>1</sup></p>";

        var created = (await _sut.SplitAsync(chapter.Guid, original.Id, before, after, "Second"))!;

        Assert.True(created.Inactive);
        Assert.True(created.ExcludeFromExport);
        Assert.Equal("review", created.LabelKey);
        Assert.Equal("Shared instructions", created.Notes);
        Assert.Equal(original.Goal, created.Goal);
        Assert.Equal(original.Outcome, created.Outcome);
        Assert.Equal(original.NarrativeMode, created.NarrativeMode);
        Assert.Equal(original.Strand, created.Strand);
        Assert.Equal(original.FocusEntityId, created.FocusEntityId);
        Assert.Equal(original.Cast, created.Cast);
        Assert.Equal(original.Properties, created.Properties);
        Assert.Empty(original.Footnotes!);
        Assert.Equal("Citation", Assert.Single(created.Footnotes!).Text);
        Assert.Equal(2, original.Comments!.Count);
        var comment = Assert.Single(created.Comments!);
        comment.Replies![0].Text = "Changed reply";
        Assert.Equal("Reply", original.Comments[0].Replies![0].Text);
        await _projects.LoadProjectAsync(_projects.ProjectRoot!);
        var reloaded = _projects.GetScenesForChapter(chapter.Guid).Single(scene => scene.Id == created.Id);
        Assert.Equal("Citation", Assert.Single(reloaded.Footnotes!).Text);
        Assert.True(reloaded.Inactive);
    }

    [Fact]
    public async Task MergePreservesBothAnnotationsWhenTheirIdsCollide()
    {
        var (chapter, first) = await SceneAsync();
        var second = await _projects.CreateSceneAsync(chapter.Guid, "Second");
        first.Footnotes = [new() { Id = "same", Text = "First citation" }];
        second.Footnotes = [new() { Id = "same", Text = "Second citation" }];
        first.Comments = [new() { Id = "same", Text = "First comment" }];
        second.Comments = [new() { Id = "same", Text = "Second comment" }, new() { Id = "unique", Text = "Other comment" }];
        const string html = "<p><span data-comment-id='same'>Text</span><sup data-fn-id='same'>1</sup><span data-comment-id='unique'>Other</span></p>";
        await _projects.WriteSceneContentAsync(chapter, first, html);
        await _projects.WriteSceneContentAsync(chapter, second, html);

        Assert.True(await _sut.MergeAsync(chapter.Guid, first.Id, second.Id));

        Assert.Equal(2, first.Footnotes!.Count);
        Assert.Equal(3, first.Comments!.Count);
        Assert.Equal(2, first.Footnotes.Select(note => note.Id).Distinct().Count());
        Assert.Equal(3, first.Comments.Select(comment => comment.Id).Distinct().Count());
        var merged = await _projects.ReadSceneContentAsync(chapter, first);
        Assert.Contains("data-fn-id=\"" + first.Footnotes[1].Id + "\"", merged);
        Assert.Contains("data-comment-id=\"" + first.Comments[1].Id + "\"", merged);
        Assert.Equal("same", second.Footnotes![0].Id);
        Assert.Equal("same", second.Comments![0].Id);
    }

    [Fact]
    public async Task MergeDeletionManifestFailureKeepsRemappedAnnotationsOnDisk()
    {
        var files = new FailingDeletionManifestFiles();
        var projects = new ProjectService(files);
        await projects.CreateProjectAsync(_dir.Path, "Failure", "Book");
        var chapter = await projects.CreateChapterAsync("Chapter");
        var first = await projects.CreateSceneAsync(chapter.Guid, "First");
        var second = await projects.CreateSceneAsync(chapter.Guid, "Second");
        first.Comments = [new() { Id = "same", Text = "First comment" }];
        second.Comments = [new() { Id = "same", Text = "Second comment" }];
        first.Footnotes = [new() { Id = "same", Text = "First citation" }];
        second.Footnotes = [new() { Id = "same", Text = "Second citation" }];
        await projects.WriteSceneContentAsync(chapter, first, "<p>First half</p>");
        await projects.WriteSceneContentAsync(chapter, second, "<p data-comment-id='same'>Second half<sup data-fn-id='same'>1</sup></p>");
        await projects.SaveScenesAsync();
        files.DeletedScenePath = projects.GetSceneFilePath(chapter, second);

        await Assert.ThrowsAsync<IOException>(() => new SceneSplitService(projects).MergeAsync(chapter.Guid, first.Id, second.Id));

        Assert.False(File.Exists(files.DeletedScenePath));
        var reopened = new ProjectService(new FileService());
        await reopened.LoadProjectAsync(projects.ProjectRoot!);
        var merged = reopened.GetScenesForChapter(chapter.Guid).Single(scene => scene.Id == first.Id);
        var comments = Assert.IsType<List<SceneComment>>(merged.Comments);
        var footnotes = Assert.IsType<List<SceneFootnote>>(merged.Footnotes);
        Assert.Equal(["First comment", "Second comment"], comments.Select(comment => comment.Text));
        Assert.Equal(["First citation", "Second citation"], footnotes.Select(note => note.Text));
        var html = await reopened.ReadSceneContentAsync(chapter, merged);
        Assert.Contains("First half", html);
        Assert.Contains("Second half", html);
        Assert.Contains("data-comment-id=\"" + comments[1].Id + "\"", html);
        Assert.Contains("data-fn-id=\"" + footnotes[1].Id + "\"", html);
    }

    private sealed class FailingDeletionManifestFiles : FileService, IFileService
    {
        public string? DeletedScenePath { get; set; }

        Task IFileService.WriteTextAsync(string path, string content)
            => Path.GetFileName(path) == "scenes.json" && DeletedScenePath != null && !File.Exists(DeletedScenePath)
                ? Task.FromException(new IOException("Injected manifest failure after scene deletion."))
                : WriteTextAsync(path, content);
    }

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

    [Theory]
    [InlineData("metadata")]
    [InlineData("newHalf")]
    [InlineData("original")]
    [InlineData("finalMetadata")]
    public async Task FailedSplitKeepsEveryWordAndAnnotationRecoverable(string failure)
    {
        var (chapter, original) = await SceneAsync();
        const string before = "<p>First half</p>";
        const string after = "<p data-comment-id='comment'>Second half<sup data-fn-id='note'>1</sup></p>";
        original.Comments = [new() { Id = "comment", Text = "Feedback" }];
        original.Footnotes = [new() { Id = "note", Text = "Citation" }];
        original.ExcludeFromExport = true;
        original.Inactive = true;
        await _projects.WriteSceneContentAsync(chapter, original, before + after);
        await _projects.SaveScenesAsync();
        var project = FailingSplitProject(failure, original.Id);

        await Assert.ThrowsAsync<IOException>(() => new SceneSplitService(project)
            .SplitAsync(chapter.Guid, original.Id, before, after, "Second"));

        if (failure != "finalMetadata")
        {
            Assert.Equal(before + after, await _projects.ReadSceneContentAsync(chapter, original));
            Assert.Single(original.Comments);
            Assert.Single(original.Footnotes);
        }
        await _projects.LoadProjectAsync(_projects.ProjectRoot!);
        var persisted = _projects.GetScenesForChapter(chapter.Guid);
        var survivingText = string.Concat(await Task.WhenAll(persisted.Select(scene => _projects.ReadSceneContentAsync(chapter, scene))));
        Assert.Contains(before, survivingText);
        Assert.Contains(after, survivingText);
        Assert.Contains(persisted, scene => scene.Comments?.Any(comment => comment.Id == "comment") == true);
        Assert.Contains(persisted, scene => scene.Footnotes?.Any(note => note.Id == "note") == true);
        if (failure != "metadata") Assert.All(persisted, scene => { Assert.True(scene.ExcludeFromExport); Assert.True(scene.Inactive); });
    }

    private IProjectService FailingSplitProject(string failure, string originalId)
    {
        var project = Substitute.For<IProjectService>();
        project.GetChaptersOrdered().Returns(_ => _projects.GetChaptersOrdered());
        project.GetScenesForChapter(Arg.Any<string>()).Returns(call => _projects.GetScenesForChapter(call.Arg<string>()));
        project.CreateSceneAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(call => _projects.CreateSceneAsync(call.ArgAt<string>(0), call.ArgAt<string>(1)));
        project.WriteSceneContentAsync(Arg.Any<ChapterData>(), Arg.Any<SceneData>(), Arg.Any<string>()).Returns(call =>
            (call.Arg<SceneData>().Id == originalId ? failure == "original" : failure == "newHalf")
                ? Task.FromException(new IOException("Injected scene write failure."))
                : _projects.WriteSceneContentAsync(call.Arg<ChapterData>(), call.Arg<SceneData>(), call.Arg<string>()));
        var saves = 0;
        project.SaveScenesAsync().Returns(_ => ++saves == (failure == "finalMetadata" ? 2 : 1) && failure is "metadata" or "finalMetadata"
            ? Task.FromException(new IOException("Injected manifest write failure.")) : _projects.SaveScenesAsync());
        return project;
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
