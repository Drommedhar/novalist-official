using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Where this book has been sent and what came back.
///
/// Novalist produced submission-ready material and recorded nothing about
/// where any of it went, so the one thing a writer must not do - send the same
/// manuscript to the same agent twice - was the one thing it could not help
/// with.
/// </summary>
public sealed class SubmissionsRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly SubmissionsRpc _rpc;

    public SubmissionsRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-subs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "SubNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new SubmissionsRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void ABookStartsHavingBeenNowhere() => Assert.Empty(_rpc.List());

    [Fact]
    public async Task ASendIsRecorded()
    {
        var all = await _rpc.SaveAsync(null, "  Vane Literary  ", "Query and three chapters", "March");

        var one = Assert.Single(all);
        Assert.Equal("Vane Literary", one.Recipient);
        Assert.Equal("Query and three chapters", one.Material);
        Assert.Equal("March", one.SentOn);
        Assert.True(one.IsOpen);
    }

    [Fact]
    public async Task ASubmissionNeedsSomebodyItWentTo()
        => await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.SaveAsync(null, "  "));

    [Fact]
    public async Task AnAnswerIsRecordedAgainstTheSameRow()
    {
        var saved = await _rpc.SaveAsync(null, "Vane Literary");

        var all = await _rpc.SaveAsync(saved[0].Id, "Vane Literary", null, "March", "rejected", "May");

        var one = Assert.Single(all);
        Assert.Equal("rejected", one.Status);
        Assert.Equal("May", one.RespondedOn);
        Assert.False(one.IsOpen);
    }

    [Fact]
    public async Task AStatusNobodyKnowsReadsAsStillOut()
    {
        var all = await _rpc.SaveAsync(null, "Vane Literary", null, null, "on the moon");

        // Reading an unknown status as resolved would quietly drop a live
        // submission out of the list a writer is watching.
        Assert.True(Assert.Single(all).IsOpen);
    }

    [Fact]
    public async Task TheOnesStillOutComeFirst()
    {
        await _rpc.SaveAsync(null, "Rejected Agency", null, "2020", "rejected");
        await _rpc.SaveAsync(null, "Waiting Agency", null, "2019");

        // A list ordered by date buries the ones being waited on under a year
        // of rejections.
        Assert.Equal("Waiting Agency", _rpc.List()[0].Recipient);
    }

    // ─── The duplicate send ──────────────────────────────────────────

    [Fact]
    public async Task ASecondSendToTheSamePlaceIsReported()
    {
        await _rpc.SaveAsync(null, "Vane Literary", null, "March");

        Assert.Equal(["March"], _rpc.OpenWith("vane literary"));
    }

    [Fact]
    public async Task ASendAfterARejectionIsNotADuplicate()
    {
        var saved = await _rpc.SaveAsync(null, "Vane Literary", null, "March");
        await _rpc.SaveAsync(saved[0].Id, "Vane Literary", null, "March", "rejected");

        // Sending again after a rejection is a new attempt, not a mistake.
        Assert.Empty(_rpc.OpenWith("Vane Literary"));
    }

    [Fact]
    public void NobodyIsNotADuplicate()
        => Assert.Empty(_rpc.OpenWith("   "));

    [Fact]
    public async Task ARecordCanBeRemoved()
    {
        var saved = await _rpc.SaveAsync(null, "Vane Literary");

        Assert.Empty(await _rpc.RemoveAsync(saved[0].Id));
    }

    [Fact]
    public async Task RemovingSomethingThatIsGoneIsQuiet()
    {
        await _rpc.SaveAsync(null, "Vane Literary");

        Assert.Single(await _rpc.RemoveAsync("no-such-record"));
    }

    [Fact]
    public async Task SubmissionsSurviveReopeningTheProject()
    {
        await _rpc.SaveAsync(null, "Vane Literary", null, "March");

        await _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!);

        Assert.Single(_rpc.List());
    }
}
