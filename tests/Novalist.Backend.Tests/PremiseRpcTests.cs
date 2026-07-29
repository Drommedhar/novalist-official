using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// The book's premise ladder, and laying a book out from it. Novalist shipped
/// a Snowflake-shaped wizard nothing called and nowhere for its answers to go.
/// </summary>
public sealed class PremiseRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly PremiseRpc _rpc;

    public PremiseRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-premise-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "PremiseNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new PremiseRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task SaveAndGet_RoundTrip()
    {
        var saved = await _rpc.SaveAsync("  A thief robs her framer.  ", " The world. ", [
            new PremiseActDto("Act One", " Status quo. "),
            new PremiseActDto("Act Two", "  "),
            new PremiseActDto("   ", "orphan")
        ]);

        Assert.Equal("A thief robs her framer.", saved.Logline);
        Assert.Equal("The world.", saved.Paragraph);
        // An act with nothing said about it, and a summary with no act, are
        // both nothing rather than an empty entry that has to be explained.
        Assert.Equal("Act One", Assert.Single(saved.Acts).Act);
        Assert.Equal("Status quo.", saved.Acts[0].Summary);
        Assert.Equal("A thief robs her framer.", _rpc.Get().Logline);
    }

    [Fact]
    public async Task Get_OffersABoxForEveryActTheChaptersUse()
    {
        var one = await _workspace.Projects.CreateChapterAsync("C1");
        one.Act = "Act One";
        var two = await _workspace.Projects.CreateChapterAsync("C2");
        two.Act = "The long dark";
        await _workspace.Projects.SaveProjectAsync();
        await _rpc.SaveAsync("l", "p", [new PremiseActDto("Act One", "Setup")]);

        var premise = _rpc.Get();

        // Acts come from the chapters, so one added after the premise was
        // written still gets somewhere to be summarised.
        Assert.Equal(["Act One", "The long dark"], premise.Acts.Select(a => a.Act));
        Assert.Equal("Setup", premise.Acts[0].Summary);
        Assert.Equal(string.Empty, premise.Acts[1].Summary);
    }

    [Fact]
    public async Task Get_KeepsAnActThatOnlyThePremiseKnowsAbout()
    {
        await _rpc.SaveAsync("l", "p", [new PremiseActDto("Act Nine", "Written before any chapter")]);

        // The ladder is written before the chapters exist; losing it because
        // no chapter mentions that act yet would defeat the point.
        Assert.Equal("Act Nine", Assert.Single(_rpc.Get().Acts).Act);
    }

    [Fact]
    public void Get_WithNoProjectOpen_IsEmptyRatherThanAnError()
    {
        var bare = new PremiseRpc(new Workspace(Path.Combine(_root, "settings2")));

        var premise = bare.Get();

        Assert.Equal(string.Empty, premise.Logline);
        Assert.Empty(premise.Acts);
    }

    [Fact]
    public async Task SaveAndScaffold_WithoutABook_Throw()
    {
        var bare = new PremiseRpc(new Workspace(Path.Combine(_root, "settings3")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => bare.SaveAsync("l", "p", []));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bare.ScaffoldAsync([], 3));
    }

    [Fact]
    public async Task Scaffold_MakesPlaceholderChaptersUnderEachAct()
    {
        var created = await _rpc.ScaffoldAsync([
            new PremiseActDto("Act One", "Setup"),
            new PremiseActDto("Act Two", "Trouble"),
            new PremiseActDto("  ", "ignored")
        ], 2);

        Assert.Equal(4, created);
        var chapters = _workspace.Projects.ActiveBook!.Chapters;
        Assert.Equal(
            ["Act One - 1", "Act One - 2", "Act Two - 1", "Act Two - 2"],
            chapters.Select(c => c.Title));
        Assert.Equal(["Act One", "Act One", "Act Two", "Act Two"], chapters.Select(c => c.Act));
    }

    [Fact]
    public async Task Scaffold_ClampsAnAbsurdChapterCount()
    {
        // A typo in a number box should not make three hundred chapters.
        Assert.Equal(30, await _rpc.ScaffoldAsync([new PremiseActDto("Act One", "x")], 300));
    }

    [Fact]
    public async Task Scaffold_WithNoActs_ChangesNothing()
    {
        Assert.Equal(0, await _rpc.ScaffoldAsync([], 5));
        Assert.Empty(_workspace.Projects.ActiveBook!.Chapters);
    }
}
