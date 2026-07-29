using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// A character's arc. Per-scope overrides could say what a character is like at
/// a point in the book; nothing could say what the change was for, or which
/// scene was the one where it happened.
/// </summary>
public sealed class ArcRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly ArcRpc _rpc;
    private readonly CharacterData _mira = new() { Name = "Mira" };
    private readonly string _first;
    private readonly string _second;

    public ArcRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-arc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "ArcNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        var chapter = _workspace.Projects.CreateChapterAsync("One").GetAwaiter().GetResult();
        _first = _workspace.Projects.CreateSceneAsync(chapter.Guid, "Opening")
            .GetAwaiter().GetResult().Id;
        _second = _workspace.Projects.CreateSceneAsync(chapter.Guid, "The turn")
            .GetAwaiter().GetResult().Id;
        new EntityService(_workspace.Projects).SaveCharacterAsync(_mira).GetAwaiter().GetResult();
        _rpc = new ArcRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task SaveAndGet_RoundTrip()
    {
        var saved = await _rpc.SaveAsync(_mira.Id, "  A liar.  ", " Honest. ", [
            new ArcPointDto(null, _second, "  stops lying to herself  "),
            new ArcPointDto(null, "", "   ")
        ]);

        Assert.Equal("A liar.", saved.Start);
        Assert.Equal("Honest.", saved.End);
        // A point with no words is nothing; a point with no scene is a plan.
        var point = Assert.Single(saved.Points);
        Assert.Equal("stops lying to herself", point.Label);
        Assert.NotNull(point.Id);
        Assert.Equal("A liar.", (await _rpc.GetAsync(_mira.Id)).Start);
    }

    [Fact]
    public async Task APointCanExistBeforeItHasAScene()
    {
        var saved = await _rpc.SaveAsync(_mira.Id, "", "", [
            new ArcPointDto(null, null, "she finally asks")
        ]);

        Assert.Equal(string.Empty, Assert.Single(saved.Points).SceneId);
    }

    [Fact]
    public async Task AnEmptyArcIsNoArc()
    {
        await _rpc.SaveAsync(_mira.Id, "A liar.", "Honest.", []);

        await _rpc.SaveAsync(_mira.Id, "  ", "  ", []);

        Assert.Empty(await _rpc.AllAsync());
    }

    [Fact]
    public async Task All_PlacesPointsInReadingOrder_AndKeepsUnplacedOnesLast()
    {
        await _rpc.SaveAsync(_mira.Id, "A liar.", "Honest.", [
            new ArcPointDto(null, null, "unplaced"),
            new ArcPointDto(null, _second, "the turn"),
            new ArcPointDto(null, _first, "the lie")
        ]);

        var arc = Assert.Single(await _rpc.AllAsync());

        Assert.Equal("Mira", arc.Name);
        Assert.Equal(["the lie", "the turn", "unplaced"], arc.Points.Select(p => p.Label));
        Assert.Equal("Opening", arc.Points[0].SceneTitle);
        // Not placed yet reads as -1 rather than as position zero, which would
        // put every unplanned turn at the start of the book.
        Assert.Equal(-1, arc.Points[2].ReadingIndex);
    }

    [Fact]
    public async Task AnUnknownCharacterThrows()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.GetAsync("nobody"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.SaveAsync("nobody", "a", "b", null));
    }
}
