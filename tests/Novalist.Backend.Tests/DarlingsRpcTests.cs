using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Prose the writer cut and kept, over the wire.
///
/// Deleted text was recoverable only by opening a snapshot of the whole scene
/// and reading it for the paragraph that used to be there.
/// </summary>
public sealed class DarlingsRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly DarlingsRpc _rpc;

    public DarlingsRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-darl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "DarlNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new DarlingsRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task NothingIsKeptToStartWith()
        => Assert.Empty(await _rpc.ListAsync());

    [Fact]
    public async Task ACutIsKeptWithTheSceneItCameFrom()
    {
        var kept = await _rpc.KeepAsync("She had never once looked back.", "Chapter One - Arrival");

        var one = Assert.Single(kept);
        Assert.Equal("She had never once looked back.", one.Text);
        Assert.Equal("Chapter One - Arrival", one.Source);
    }

    [Fact]
    public async Task ACutCanBeNotedAndThrownAway()
    {
        var kept = await _rpc.KeepAsync("kept prose");

        var noted = await _rpc.SetNoteAsync(kept[0].Id, "for chapter nine");
        Assert.Equal("for chapter nine", Assert.Single(noted).Note);

        Assert.Empty(await _rpc.RemoveAsync(kept[0].Id));
    }

    [Fact]
    public async Task CutsSurviveReopeningTheProject()
    {
        await _rpc.KeepAsync("She had never once looked back.", "Chapter One");

        await _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!);

        // Cut prose belongs to the project rather than the machine.
        Assert.Single(await _rpc.ListAsync());
    }

    [Fact]
    public async Task TheDiagnosticLogNeverSeesTheProse()
    {
        // The whole payload of this call is the writer's writing, so this path
        // logs nothing at any level. Asserted here because a Log line added
        // later would look harmless in review.
        var source = await System.IO.File.ReadAllTextAsync(
            Path.Combine(RepoRoot(), "Novalist.Backend", "Rpc", "DarlingsRpc.cs"));

        Assert.DoesNotContain("Log.", source, StringComparison.Ordinal);
    }

    /// <summary>Walks up to the folder holding the solution.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("*.sln").Any()) dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
