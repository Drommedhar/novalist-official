using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Links from a scene to another scene, a research item or a Codex entry, and
/// the same links read backwards.
///
/// Research items could already point at each other both ways. Scenes could
/// point at nothing: a scene that answers another scene, or leans on one
/// research note, could only say so as prose in its own notes - which nothing
/// could follow, and which the other end never knew about.
/// </summary>
public sealed class LinksRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly LinksRpc _rpc;
    private readonly string _chapterGuid;
    private readonly string _first;
    private readonly string _second;

    public LinksRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-links-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "LinkNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        var chapter = _workspace.Projects.CreateChapterAsync("One").GetAwaiter().GetResult();
        _chapterGuid = chapter.Guid;
        _first = _workspace.Projects.CreateSceneAsync(chapter.Guid, "The promise")
            .GetAwaiter().GetResult().Id;
        _second = _workspace.Projects.CreateSceneAsync(chapter.Guid, "The payoff")
            .GetAwaiter().GetResult().Id;
        _rpc = new LinksRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private Task<SceneLinkDto[]> LinkToSecondAsync(string? note = null)
        => _rpc.AddAsync(_chapterGuid, _first, "scene", _second, note);

    [Fact]
    public async Task ASceneStartsPointingAtNothing()
        => Assert.Empty(await _rpc.ListAsync(_chapterGuid, _first));

    [Fact]
    public async Task ASceneCanPointAtAnotherScene()
    {
        var link = Assert.Single(await LinkToSecondAsync(" pays this off "));

        Assert.Equal("scene", link.Kind);
        Assert.Equal(_second, link.TargetId);
        // Named well enough to open: a title alone does not locate a scene,
        // because three chapters can each have one called "Arrival".
        Assert.Equal("One - The payoff", link.TargetTitle);
        Assert.Equal("pays this off", link.Note);
    }

    [Fact]
    public async Task ASceneCannotPointAtItself()
        => await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.AddAsync(_chapterGuid, _first, "scene", _first));

    [Fact]
    public async Task TheSameTargetTwiceIsOneLink()
    {
        await LinkToSecondAsync("first reason");

        var links = await LinkToSecondAsync("a better reason");

        // A list that says "see The payoff" twice is a list nobody trusts.
        var link = Assert.Single(links);
        Assert.Equal("a better reason", link.Note);
    }

    [Fact]
    public async Task ALinkNeedsSomethingToPointAt()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.AddAsync(_chapterGuid, _first, "scene", "  "));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.AddAsync(_chapterGuid, _first, "postcard", _second));
    }

    [Fact]
    public async Task AResearchNoteCanBeLeanedOn()
    {
        var research = new ResearchService(_workspace.Projects, _workspace.FileService);
        var note = new ResearchItem { Title = "Coastal shipping, 1847" };
        await research.SaveAsync(note);

        var link = Assert.Single(
            await _rpc.AddAsync(_chapterGuid, _first, "research", note.Id));

        Assert.Equal("Coastal shipping, 1847", link.TargetTitle);
    }

    [Fact]
    public async Task ACodexEntryCanBePointedAt()
    {
        var entities = new EntityService(_workspace.Projects);
        var mira = new CharacterData { Name = "Mira", Surname = "Vane" };
        await entities.SaveCharacterAsync(mira);

        var link = Assert.Single(await _rpc.AddAsync(_chapterGuid, _first, "entity", mira.Id));

        Assert.Equal("Mira Vane", link.TargetTitle);
    }

    [Fact]
    public async Task ATargetThatIsGoneKeepsItsRow()
    {
        await _rpc.AddAsync(_chapterGuid, _first, "research", "no-such-note");

        // A link that disappears silently is a link the writer never finds out
        // they lost. The row stays and says nothing is there.
        var link = Assert.Single(await _rpc.ListAsync(_chapterGuid, _first));
        Assert.Equal(string.Empty, link.TargetTitle);
        Assert.Equal("no-such-note", link.TargetId);
    }

    // ─── Reading it backwards ────────────────────────────────────────

    [Fact]
    public async Task TheSceneAtTheOtherEndKnowsAboutIt()
    {
        await LinkToSecondAsync("pays this off");

        var back = Assert.Single(_rpc.Backlinks("scene", _second));

        // The half that makes a link worth making.
        Assert.Equal(_first, back.SceneId);
        Assert.Equal("The promise", back.SceneTitle);
        Assert.Equal("One", back.ChapterTitle);
        Assert.Equal("pays this off", back.Note);
    }

    [Fact]
    public async Task AResearchNoteKnowsWhichScenesLeanOnIt()
    {
        await _rpc.AddAsync(_chapterGuid, _first, "research", "note-1");
        await _rpc.AddAsync(_chapterGuid, _second, "research", "note-1");

        Assert.Equal(2, _rpc.Backlinks("research", "note-1").Length);
    }

    [Fact]
    public void NothingPointingHereIsNoBacklinks()
        => Assert.Empty(_rpc.Backlinks("scene", _second));

    [Fact]
    public async Task AKindIsPartOfTheMatch()
    {
        // An entity and a research item can hold the same id in a project that
        // was imported; matching on id alone would cross them.
        await _rpc.AddAsync(_chapterGuid, _first, "research", "shared-id");

        Assert.Empty(_rpc.Backlinks("entity", "shared-id"));
        Assert.Single(_rpc.Backlinks("research", "shared-id"));
    }

    // ─── Editing and removing ────────────────────────────────────────

    [Fact]
    public async Task TheReasonCanBeRewritten()
    {
        var saved = await LinkToSecondAsync("first go");

        var links = await _rpc.SetNoteAsync(_chapterGuid, _first, saved[0].Id, " better ");

        Assert.Equal("better", Assert.Single(links).Note);
    }

    [Fact]
    public async Task RewritingAReasonThatIsGoneIsQuiet()
    {
        await LinkToSecondAsync("a reason");

        Assert.Single(await _rpc.SetNoteAsync(_chapterGuid, _first, "no-such-link", "x"));
    }

    [Fact]
    public async Task ALinkCanBeTakenOff()
    {
        var saved = await LinkToSecondAsync();

        Assert.Empty(await _rpc.RemoveAsync(_chapterGuid, _first, saved[0].Id));
        Assert.Empty(_rpc.Backlinks("scene", _second));
    }

    [Fact]
    public async Task RemovingOneThatIsGoneIsQuiet()
    {
        await LinkToSecondAsync();

        Assert.Single(await _rpc.RemoveAsync(_chapterGuid, _first, "no-such-link"));
    }

    [Fact]
    public async Task LinksSurviveReopeningTheProject()
    {
        await LinkToSecondAsync("pays this off");

        await _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!);

        Assert.Single(await _rpc.ListAsync(_chapterGuid, _first));
        Assert.Single(_rpc.Backlinks("scene", _second));
    }
}
