using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Sprint history from the RPC the status-bar timer calls.</summary>
public sealed class SprintRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly SprintRpc _rpc;

    public SprintRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-sprint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "SprintNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new SprintRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void AFreshProjectHasNoSprints()
    {
        var history = _rpc.History();

        Assert.Empty(history.Sprints);
        Assert.Equal(0, history.Summary.Count);
    }

    [Fact]
    public async Task ASprintRoundTripsWithItsPace()
    {
        var history = await _rpc.RecordAsync(600, 10, 500, "2026-03-01T09:00:00Z");

        Assert.Single(history.Sprints);
        Assert.Equal(500, history.Sprints[0].Words);
        Assert.Equal(50, history.Sprints[0].WordsPerMinute);
        Assert.Equal(1, history.Summary.Count);
    }

    [Fact]
    public async Task ABadTimestampStillRecordsTheSprint()
    {
        // Losing the sitting over a malformed stamp would be the worse failure.
        var history = await _rpc.RecordAsync(600, 10, 500, "not a date");

        Assert.Single(history.Sprints);
    }

    [Fact]
    public async Task ClearingEmptiesTheHistory()
    {
        await _rpc.RecordAsync(600, 10, 500, "2026-03-01T09:00:00Z");

        var history = await _rpc.ClearAsync();

        Assert.Empty(history.Sprints);
        Assert.Equal(0, history.Summary.Count);
    }

    [Fact]
    public async Task HistorySurvivesAReload()
    {
        await _rpc.RecordAsync(600, 10, 500, "2026-03-01T09:00:00Z");

        await _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!);

        Assert.Single(new SprintRpc(_workspace).History().Sprints);
    }
}
