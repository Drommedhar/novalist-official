using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// What a Codex entry said before it was overwritten. Snapshots covered scenes
/// and nothing else, so the wrong eye colour typed over the right one had no
/// answer inside the app.
/// </summary>
public sealed class EntityHistoryTests : IDisposable
{
    private readonly string _root;
    private readonly EntityHistory _sut;

    public EntityHistoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-hist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var projects = Substitute.For<IProjectService>();
        projects.ActiveBook.Returns(new BookData { SnapshotFolder = "Snapshots" });
        projects.ActiveDraftRoot.Returns(_root);
        _sut = new EntityHistory(projects);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task TheStateBeingReplacedIsWhatIsKept()
    {
        // Recording the new state would keep what is already on screen and be
        // worth nothing.
        await _sut.RecordAsync("mira", "{\"eyes\":\"green\"}", "{\"eyes\":\"brown\"}");

        var revision = Assert.Single(_sut.List("mira"));
        Assert.Equal("{\"eyes\":\"green\"}", await _sut.ReadAsync("mira", revision.Id));
    }

    [Fact]
    public async Task ASaveThatChangesNothingIsNotAVersion()
    {
        await _sut.RecordAsync("mira", "{\"a\":1}", "{\"a\":1}");

        Assert.Empty(_sut.List("mira"));
    }

    [Fact]
    public async Task TheFirstEverSaveHasNothingToKeep()
    {
        await _sut.RecordAsync("mira", string.Empty, "{\"a\":1}");

        Assert.Empty(_sut.List("mira"));
    }

    [Fact]
    public async Task TwoSavesInsideOneMillisecondBothSurvive()
    {
        // A script, or a paste over a whole field set, writes faster than the
        // timestamp's resolution. Sharing a name lost exactly the revision
        // somebody would want back. The clock is held still so the collision is
        // certain rather than a matter of how fast the machine is.
        var projects = Substitute.For<IProjectService>();
        projects.ActiveBook.Returns(new BookData { SnapshotFolder = "Snapshots" });
        projects.ActiveDraftRoot.Returns(_root);
        var frozen = new EntityHistory(projects, () => new DateTime(2026, 7, 31, 3, 0, 0, 0));

        await frozen.RecordAsync("mira", "first", "second");
        await frozen.RecordAsync("mira", "second", "third");
        await frozen.RecordAsync("mira", "third", "fourth");

        var revisions = frozen.List("mira");
        Assert.Equal(3, revisions.Count);
        var kept = new List<string?>();
        foreach (var revision in revisions) kept.Add(await frozen.ReadAsync("mira", revision.Id));
        Assert.Equal(["third", "second", "first"], kept);
    }

    [Fact]
    public async Task NewestFirst()
    {
        await _sut.RecordAsync("mira", "one", "two");
        await Task.Delay(5);
        await _sut.RecordAsync("mira", "two", "three");

        var revisions = _sut.List("mira");
        Assert.Equal(2, revisions.Count);
        Assert.Equal("two", await _sut.ReadAsync("mira", revisions[0].Id));
        Assert.Equal("one", await _sut.ReadAsync("mira", revisions[1].Id));
    }

    [Fact]
    public async Task OneEntrysHistoryIsItsOwn()
    {
        await _sut.RecordAsync("mira", "hers", "next");
        await _sut.RecordAsync("tomas", "his", "next");

        Assert.Equal("hers", await _sut.ReadAsync("mira", _sut.List("mira").Single().Id));
        Assert.Single(_sut.List("tomas"));
    }

    [Fact]
    public async Task AProjectEditedForYearsDoesNotAccumulateForever()
    {
        for (var i = 0; i < EntityHistory.KeepPerEntity + 8; i++)
        {
            await _sut.RecordAsync("mira", $"state {i}", $"state {i + 1}");
            await Task.Delay(2);
        }

        var revisions = _sut.List("mira");
        Assert.Equal(EntityHistory.KeepPerEntity, revisions.Count);
        // The newest survive; the oldest are the ones dropped.
        Assert.Equal($"state {EntityHistory.KeepPerEntity + 7}",
            await _sut.ReadAsync("mira", revisions[0].Id));
    }

    [Theory]
    [InlineData("../../../secret")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public async Task ARevisionIdCannotReachOutOfItsFolder(string revisionId)
    {
        // The id is a file name and the caller could hand over anything.
        Assert.Null(await _sut.ReadAsync("mira", revisionId));
    }

    [Fact]
    public async Task AnUnknownRevisionIsNothingRatherThanAnError()
        => Assert.Null(await _sut.ReadAsync("mira", "20260101-000000000"));

    [Fact]
    public void AnEntryWithNoHistoryListsNothing()
        => Assert.Empty(_sut.List("nobody"));

    [Fact]
    public async Task WithNoBookOpenNothingIsRecorded()
    {
        var projects = Substitute.For<IProjectService>();
        var orphan = new EntityHistory(projects);

        await orphan.RecordAsync("mira", "before", "after");

        Assert.Empty(orphan.List("mira"));
        Assert.Null(await orphan.ReadAsync("mira", "anything"));
    }

    [Fact]
    public async Task AFileRenamedByHandStillListsRatherThanThrowing()
    {
        await _sut.RecordAsync("mira", "kept", "next");
        var dir = Path.Combine(_root, "Snapshots", "Entities", "mira");
        File.Move(Directory.EnumerateFiles(dir).Single(), Path.Combine(dir, "not-a-date.json"));

        var revision = Assert.Single(_sut.List("mira"));
        Assert.Equal(default, revision.SavedAt);
        Assert.Equal("kept", await _sut.ReadAsync("mira", "not-a-date"));
    }

    [Fact]
    public async Task ARevisionKnowsHowBigItIs()
    {
        await _sut.RecordAsync("mira", "12345", "next");

        Assert.Equal(5, _sut.List("mira").Single().SizeBytes);
    }

    [Fact]
    public async Task ARevisionSomethingElseIsHoldingIsLeftForNextTime()
    {
        for (var i = 0; i < EntityHistory.KeepPerEntity + 1; i++)
        {
            await _sut.RecordAsync("mira", $"state {i}", $"state {i + 1}");
            await Task.Delay(2);
        }
        var dir = Path.Combine(_root, "Snapshots", "Entities", "mira");
        var oldest = Directory.EnumerateFiles(dir).OrderBy(f => f, StringComparer.Ordinal).First();

        // Held open with no sharing: pruning cannot delete it, and must carry on
        // rather than failing the save it is part of.
        using (File.Open(oldest, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await _sut.RecordAsync("mira", "one more", "and another");
        }

        Assert.True(File.Exists(oldest));
        Assert.NotEmpty(_sut.List("mira"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnEntryWithNoIdIsNotRecorded(string entityId)
    {
        await _sut.RecordAsync(entityId, "before", "after");

        Assert.False(Directory.Exists(Path.Combine(_root, "Snapshots", "Entities")));
    }
}
