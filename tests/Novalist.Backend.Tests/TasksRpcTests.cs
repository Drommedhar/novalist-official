using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Things to do before the book is finished.
///
/// Novalist had todo comments, which are anchored to a passage and belong to
/// the scene they sit in. "Read the whole thing aloud" belongs to no passage
/// and to no scene, so it was kept on paper.
/// </summary>
public sealed class TasksRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly TasksRpc _rpc;

    public TasksRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-tasks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "TaskNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new TasksRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void AProjectStartsWithNothingToDo() => Assert.Empty(_rpc.List());

    [Fact]
    public async Task ATaskIsRecorded()
    {
        var all = await _rpc.SaveAsync(null, "  Read the whole thing aloud  ");

        var one = Assert.Single(all);
        Assert.Equal("Read the whole thing aloud", one.Text);
        Assert.False(one.Done);
        Assert.Null(one.DoneAt);
    }

    [Fact]
    public async Task ATaskNeedsSomethingToDo()
        => await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.SaveAsync(null, "  "));

    [Fact]
    public async Task TickingRecordsWhen()
    {
        var saved = await _rpc.SaveAsync(null, "Read it aloud");

        var ticked = await _rpc.SetDoneAsync(saved[0].Id, true);

        var one = Assert.Single(ticked);
        Assert.True(one.Done);
        Assert.NotNull(one.DoneAt);
    }

    [Fact]
    public async Task UntickingClearsTheDate()
    {
        var saved = await _rpc.SaveAsync(null, "Read it aloud");
        await _rpc.SetDoneAsync(saved[0].Id, true);

        var unticked = await _rpc.SetDoneAsync(saved[0].Id, false);

        // A date saying it was finished, on a row that is not, is worse than
        // no date at all.
        Assert.Null(Assert.Single(unticked).DoneAt);
    }

    [Fact]
    public async Task ATickedTaskStaysInTheList()
    {
        var saved = await _rpc.SaveAsync(null, "Read it aloud");

        var after = await _rpc.SetDoneAsync(saved[0].Id, true);

        // A checklist that empties as it is worked reads as though nothing was
        // done, which is the opposite of what a revision pass is for.
        Assert.Single(after);
    }

    [Fact]
    public async Task TickingSomethingThatIsGoneIsQuiet()
    {
        await _rpc.SaveAsync(null, "Read it aloud");

        Assert.Single(await _rpc.SetDoneAsync("no-such-task", true));
    }

    [Fact]
    public async Task UnfinishedComesFirstWithinAList()
    {
        var first = await _rpc.SaveAsync(null, "One", "Revision");
        await _rpc.SaveAsync(null, "Two", "Revision");
        await _rpc.SetDoneAsync(first[0].Id, true);

        Assert.Equal("Two", _rpc.List()[0].Text);
    }

    [Fact]
    public async Task AListCanBeRunAgain()
    {
        var first = await _rpc.SaveAsync(null, "One", "Revision");
        var second = await _rpc.SaveAsync(null, "Two", "Revision");
        await _rpc.SetDoneAsync(first[0].Id, true);
        await _rpc.SetDoneAsync(second.Single(t => t.Text == "Two").Id, true);

        var reset = await _rpc.ResetListAsync("revision");

        // A checklist is run once per pass. Retyping it every time is how it
        // stops being used. Matched without regard to case, because the writer
        // typing it back is the point.
        Assert.All(reset, t => Assert.False(t.Done));
        Assert.All(reset, t => Assert.Null(t.DoneAt));
    }

    [Fact]
    public async Task ResettingLeavesOtherListsAlone()
    {
        var revision = await _rpc.SaveAsync(null, "One", "Revision");
        var other = await _rpc.SaveAsync(null, "Two", "Submitting");
        await _rpc.SetDoneAsync(revision[0].Id, true);
        await _rpc.SetDoneAsync(other.Single(t => t.Text == "Two").Id, true);

        await _rpc.ResetListAsync("Revision");

        Assert.True(_rpc.List().Single(t => t.Text == "Two").Done);
    }

    [Fact]
    public async Task ResettingAListWithNothingTickedChangesNothing()
    {
        await _rpc.SaveAsync(null, "One", "Revision");

        Assert.Single(await _rpc.ResetListAsync("Revision"));
    }

    [Fact]
    public async Task ATaskCanBeAboutAScene()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Arrival");

        var all = await _rpc.SaveAsync(null, "Check the dates", null, chapter.Guid, scene.Id);

        Assert.Equal(scene.Id, Assert.Single(all).SceneId);
    }

    [Fact]
    public async Task ATaskCanBeRemoved()
    {
        var saved = await _rpc.SaveAsync(null, "Read it aloud");

        Assert.Empty(await _rpc.RemoveAsync(saved[0].Id));
    }

    [Fact]
    public async Task RemovingSomethingThatIsGoneIsQuiet()
    {
        await _rpc.SaveAsync(null, "Read it aloud");

        Assert.Single(await _rpc.RemoveAsync("no-such-task"));
    }

    [Fact]
    public async Task TasksSurviveReopeningTheProject()
    {
        await _rpc.SaveAsync(null, "Read it aloud", "Revision");

        await _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!);

        Assert.Single(_rpc.List());
    }
}
