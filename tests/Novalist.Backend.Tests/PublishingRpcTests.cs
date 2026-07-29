using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Publishing metadata from the RPC the Export view calls.</summary>
public sealed class PublishingRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly PublishingRpc _rpc;

    public PublishingRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-pub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "PubNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new PublishingRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static PublishingDto Dto(
        string isbn = "", string publisher = "", string series = "", string position = "",
        params string[] subjects)
        => new(isbn, publisher, "", "", "", series, position, subjects, "");

    [Fact]
    public void AFreshBookHasNothingSet()
    {
        var meta = _rpc.Get();

        Assert.Empty(meta.Isbn);
        Assert.Empty(meta.Subjects);
    }

    [Fact]
    public async Task MetadataRoundTrips()
    {
        await _rpc.SetAsync(Dto(isbn: "978-3-16-148410-0", publisher: "Raven Press"));

        var meta = _rpc.Get();
        Assert.Equal("978-3-16-148410-0", meta.Isbn);
        Assert.Equal("Raven Press", meta.Publisher);
    }

    [Fact]
    public async Task TheResolvedIsbnComesBackSoThePanelCanShowIt()
    {
        var meta = await _rpc.SetAsync(Dto(isbn: "978-3-16-148410-0"));

        Assert.Equal("9783161484100", meta.NormalizedIsbn);
    }

    [Fact]
    public async Task AnUnusableIsbnResolvesToNothingWithoutLosingWhatWasTyped()
    {
        // The writer's copy is what they will check against their registration,
        // so it is kept even while nothing is exportable from it.
        var meta = await _rpc.SetAsync(Dto(isbn: "coming soon"));

        Assert.Equal("coming soon", meta.Isbn);
        Assert.Empty(meta.NormalizedIsbn);
    }

    [Fact]
    public async Task BlankAndDuplicateSubjectsAreDropped()
    {
        var meta = await _rpc.SetAsync(Dto(subjects: ["Fantasy", "  ", "fantasy", "Epic", ""]));

        Assert.Equal(["Fantasy", "Epic"], meta.Subjects);
    }

    [Fact]
    public async Task SeriesAndPositionRoundTrip()
    {
        var meta = await _rpc.SetAsync(Dto(series: "The Ravens", position: "2"));

        Assert.Equal("The Ravens", meta.SeriesName);
        Assert.Equal("2", meta.SeriesPosition);
    }

    [Fact]
    public async Task MetadataSurvivesAReload()
    {
        await _rpc.SetAsync(Dto(publisher: "Raven Press"));

        await _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!);

        Assert.Equal("Raven Press", new PublishingRpc(_workspace).Get().Publisher);
    }
}
