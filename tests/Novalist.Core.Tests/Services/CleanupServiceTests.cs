using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// A cleanup pass walking the manuscript.
///
/// It rewrites the prose itself, which is why the preview exists and why every
/// scene it changes is snapshotted first.
/// </summary>
public class CleanupServiceTests
{
    private readonly IProjectService _project = Substitute.For<IProjectService>();
    private readonly ChapterData _first = new() { Title = "One", Order = 1 };
    private readonly ChapterData _second = new() { Title = "Two", Order = 2 };
    private readonly Dictionary<string, string> _written = [];

    private CleanupService Service() => new(_project);

    private static CleanupOptions Quotes() => new()
    {
        Rules = [CleanupRule.SmartenQuotes],
        Language = "en"
    };

    /// <summary>Two chapters, so a scope that covers everything cannot pass by luck.</summary>
    private void Setup(string firstHtml, string secondHtml)
    {
        var one = new SceneData { Title = "Arrival", Order = 1, ChapterGuid = _first.Guid };
        var two = new SceneData { Title = "Departure", Order = 1, ChapterGuid = _second.Guid };
        _project.GetChaptersOrdered().Returns([_first, _second]);
        _project.GetScenesForChapter(_first.Guid).Returns([one]);
        _project.GetScenesForChapter(_second.Guid).Returns([two]);
        _project.ReadSceneContentAsync(_first, one).Returns(firstHtml);
        _project.ReadSceneContentAsync(_second, two).Returns(secondHtml);
        _project.WriteSceneContentAsync(
                Arg.Any<ChapterData>(), Arg.Any<SceneData>(), Arg.Any<string>())
            .Returns(callInfo =>
            {
                _written[callInfo.Arg<SceneData>().Title] = callInfo.ArgAt<string>(2);
                return Task.CompletedTask;
            });
    }

    [Fact]
    public async Task NoRulesIsNoPass()
    {
        Setup("<p>\"Hi\"</p>", "<p>\"Bye\"</p>");

        var report = await Service().RunAsync(new CleanupOptions());

        // Not "nothing changed" - nothing was even read.
        Assert.Equal(0, report.ScenesConsidered);
        Assert.Empty(_written);
    }

    [Fact]
    public async Task ThePreviewChangesNothing()
    {
        Setup("<p>\"Hi\"</p>", "<p>Already clean.</p>");

        var report = await Service().PreviewAsync(Quotes());

        // A pass that rewrites every scene in a book is not something to find
        // out about afterwards.
        Assert.Equal(2, report.ScenesConsidered);
        Assert.Equal(1, report.ScenesChanged);
        Assert.Equal(["Arrival"], report.ChangedTitles);
        Assert.Empty(_written);
        await _project.DidNotReceive().SaveScenesAsync();
    }

    [Fact]
    public async Task TheRunWritesOnlyWhatChanged()
    {
        Setup("<p>\"Hi\"</p>", "<p>Already clean.</p>");

        var report = await Service().RunAsync(Quotes());

        Assert.Equal(1, report.ScenesChanged);
        Assert.Equal("<p>“Hi”</p>", Assert.Contains("Arrival", _written));
        Assert.DoesNotContain("Departure", _written);
        await _project.Received(1).SaveScenesAsync();
    }

    [Fact]
    public async Task NothingToDoWritesNothingAtAll()
    {
        Setup("<p>Already clean.</p>", "<p>So is this.</p>");

        var report = await Service().RunAsync(Quotes());

        Assert.Equal(0, report.ScenesChanged);
        // Saving the manifest when no scene changed touches the file for no
        // reason, and every write is a chance to lose something.
        await _project.DidNotReceive().SaveScenesAsync();
    }

    [Fact]
    public async Task NamingChaptersNarrowsThePass()
    {
        Setup("<p>\"Hi\"</p>", "<p>\"Bye\"</p>");

        var report = await Service().RunAsync(Quotes(), [_second.Guid]);

        Assert.Equal(1, report.ScenesConsidered);
        Assert.Equal(["Departure"], report.ChangedTitles);
    }

    [Fact]
    public async Task NamingNoChapterMeansTheWholeBook()
    {
        Setup("<p>\"Hi\"</p>", "<p>\"Bye\"</p>");

        // A pass called "clean up the manuscript" has to mean the manuscript.
        Assert.Equal(2, (await Service().RunAsync(Quotes(), [])).ScenesChanged);
    }

    [Fact]
    public async Task EveryChangedSceneIsSnapshottedFirst()
    {
        Setup("<p>\"Hi\"</p>", "<p>Already clean.</p>");
        var snapshots = Substitute.For<ISnapshotService>();

        await Service().RunAsync(Quotes(), null, snapshots);

        // This rewrites the prose itself; the writer has to be able to get it
        // back. The untouched scene is not snapshotted - a snapshot of a file
        // nothing happened to is noise in the history.
        await snapshots.Received(1).TakeAsync(
            _first, Arg.Is<SceneData>(s => s.Title == "Arrival"), Arg.Any<string>());
        await snapshots.Received(1).TakeAsync(
            Arg.Any<ChapterData>(), Arg.Any<SceneData>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ARunWithoutSnapshotsStillWrites()
    {
        Setup("<p>\"Hi\"</p>", "<p>Already clean.</p>");

        await Service().RunAsync(Quotes(), null, null);

        Assert.Contains("Arrival", _written);
    }

    [Fact]
    public async Task ThePassCanBeCalledOff()
    {
        Setup("<p>\"Hi\"</p>", "<p>\"Bye\"</p>");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Service().RunAsync(Quotes(), null, null, cancelled.Token));
    }
}
