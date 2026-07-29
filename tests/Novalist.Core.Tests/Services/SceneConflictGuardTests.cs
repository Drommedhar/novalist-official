using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// The check that stops a scene save from destroying an edit that arrived from
/// somewhere else — the case a synced project folder produces constantly and
/// that the write path used to lose silently.
/// </summary>
public class SceneConflictGuardTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProjectService _projects = new(new FileService());

    public void Dispose() => _dir.Dispose();

    private async Task<(ChapterData Chapter, SceneData Scene)> SceneAsync()
    {
        await _projects.CreateProjectAsync(_dir.Path, "P", "Book");
        var chapter = await _projects.CreateChapterAsync("One");
        var scene = await _projects.CreateSceneAsync(chapter.Guid, "A");
        return (chapter, scene);
    }

    private SceneConflictGuard Guard(ISnapshotService? snapshots = null)
        => new(_projects, snapshots);

    /// <summary>Simulates the other machine's save landing between our read and
    /// our write.</summary>
    private Task WriteOnDiskAsync(ChapterData chapter, SceneData scene, string html)
        => _projects.WriteSceneContentAsync(chapter, scene, html);

    // ── The check ──

    [Fact]
    public async Task ASaveThatKnowsWhatItIsOverwritingGoesThrough()
    {
        var (chapter, scene) = await SceneAsync();
        await WriteOnDiskAsync(chapter, scene, "<p>first</p>");
        var guard = Guard();
        var hash = await guard.DiskHashAsync(chapter, scene);

        var outcome = await guard.SaveAsync(chapter, scene, "<p>second</p>", hash);

        Assert.False(outcome.Conflicted);
        Assert.Null(outcome.DiskHtml);
        Assert.Equal("<p>second</p>", await _projects.ReadSceneContentAsync(chapter, scene));
    }

    [Fact]
    public async Task ASaveCarryingAStaleHashIsRefused()
    {
        var (chapter, scene) = await SceneAsync();
        await WriteOnDiskAsync(chapter, scene, "<p>as we read it</p>");
        var guard = Guard();
        var stale = await guard.DiskHashAsync(chapter, scene);

        // The other machine's save lands.
        await WriteOnDiskAsync(chapter, scene, "<p>from the other machine</p>");

        var outcome = await guard.SaveAsync(chapter, scene, "<p>mine</p>", stale);

        Assert.True(outcome.Conflicted);
        // Nothing was written: the other machine's work is still there.
        Assert.Equal(
            "<p>from the other machine</p>",
            await _projects.ReadSceneContentAsync(chapter, scene));
    }

    [Fact]
    public async Task ARefusedSaveHandsBackWhatIsActuallyOnDisk()
    {
        // Without this the writer is told there is a conflict and shown nothing
        // to compare against, which is worse than not telling them.
        var (chapter, scene) = await SceneAsync();
        var guard = Guard();
        var stale = await guard.DiskHashAsync(chapter, scene);
        await WriteOnDiskAsync(chapter, scene, "<p>theirs</p>");

        var outcome = await guard.SaveAsync(chapter, scene, "<p>mine</p>", stale);

        Assert.Equal("<p>theirs</p>", outcome.DiskHtml);
        Assert.Equal(await guard.DiskHashAsync(chapter, scene), outcome.Hash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task NoExpectedHashSkipsTheCheckEntirely(string? expected)
    {
        // Imports, restores and bulk operations have no editor behind them and
        // must keep working exactly as before.
        var (chapter, scene) = await SceneAsync();
        await WriteOnDiskAsync(chapter, scene, "<p>whatever</p>");

        var outcome = await Guard().SaveAsync(chapter, scene, "<p>forced</p>", expected);

        Assert.False(outcome.Conflicted);
        Assert.Equal("<p>forced</p>", await _projects.ReadSceneContentAsync(chapter, scene));
    }

    [Fact]
    public async Task ANewSceneWithNoFileYetIsNotAConflict()
    {
        var (chapter, scene) = await SceneAsync();
        var guard = Guard();

        // A scene created but never written hashes as empty on both sides.
        var outcome = await guard.SaveAsync(
            chapter, scene, "<p>first words</p>", await guard.DiskHashAsync(chapter, scene));

        Assert.False(outcome.Conflicted);
    }

    [Fact]
    public async Task TheIdentityStampDoesNotCountAsAChange()
    {
        // Every write re-stamps the scene id into the file. If that counted, a
        // second save would always conflict with the first.
        var (chapter, scene) = await SceneAsync();
        var guard = Guard();
        await guard.SaveAsync(chapter, scene, "<p>one</p>", null);

        var outcome = await guard.SaveAsync(
            chapter, scene, "<p>two</p>", await guard.DiskHashAsync(chapter, scene));

        Assert.False(outcome.Conflicted);
    }

    [Fact]
    public async Task TheHashOfASuccessfulSaveMatchesWhatIsOnDisk()
    {
        // The caller stores this hash and sends it with the next save, so it has
        // to agree with what a fresh read would compute.
        var (chapter, scene) = await SceneAsync();
        var guard = Guard();

        var outcome = await guard.SaveAsync(chapter, scene, "<p>text</p>", null);

        Assert.Equal(await guard.DiskHashAsync(chapter, scene), outcome.Hash);
    }

    // ── Resolving ──

    [Fact]
    public async Task ResolvingWritesTheChosenText()
    {
        var (chapter, scene) = await SceneAsync();

        var hash = await Guard().ResolveAsync(chapter, scene, "<p>merged</p>");

        Assert.Equal("<p>merged</p>", await _projects.ReadSceneContentAsync(chapter, scene));
        Assert.Equal(await Guard().DiskHashAsync(chapter, scene), hash);
    }

    [Fact]
    public async Task ResolvingKeepsBothVersionsAsSnapshots()
    {
        // The merge dialog is the one place a click can discard a paragraph
        // someone wrote, so neither side may be lost to a wrong choice.
        var (chapter, scene) = await SceneAsync();
        await WriteOnDiskAsync(chapter, scene, "<p>theirs</p>");
        var snapshots = new SnapshotService(_projects, new FileService());

        await new SceneConflictGuard(_projects, snapshots)
            .ResolveAsync(chapter, scene, "<p>merged</p>");

        var taken = await snapshots.ListAsync(scene);
        Assert.Equal(2, taken.Count);
        var contents = new List<string>();
        foreach (var snap in taken)
            contents.Add((await snapshots.LoadAsync(scene, snap.Id))!.Content);
        Assert.Contains("<p>theirs</p>", contents);
        Assert.Contains("<p>merged</p>", contents);
    }

    [Fact]
    public async Task ResolvingWithoutASnapshotServiceStillWrites()
    {
        var (chapter, scene) = await SceneAsync();

        await Guard().ResolveAsync(chapter, scene, "<p>merged</p>");

        Assert.Equal("<p>merged</p>", await _projects.ReadSceneContentAsync(chapter, scene));
    }

    // ── The merge view ──

    [Fact]
    public void Rows_MarkAgreementAsEqual()
    {
        var rows = SceneConflictGuard.Rows("<p>same line</p>", "<p>same line</p>");

        Assert.All(rows, row => Assert.Equal("equal", row.State));
    }

    [Fact]
    public void Rows_PairADivergedLineSideBySide()
    {
        var rows = SceneConflictGuard.Rows("<p>mine</p>", "<p>theirs</p>");

        var changed = rows.Single(r => r.State == "changed");
        Assert.Equal("mine", changed.Mine);
        Assert.Equal("theirs", changed.Theirs);
    }

    [Fact]
    public void Rows_MarkALineOnlyOneSideHas()
    {
        var rows = SceneConflictGuard.Rows("<p>shared</p><p>only mine</p>", "<p>shared</p>");

        var onlyMine = rows.Single(r => r.State == "mine");
        Assert.Equal("only mine", onlyMine.Mine);
        Assert.Null(onlyMine.Theirs);
    }

    [Fact]
    public void Rows_MarkALineOnlyTheFileHas()
    {
        var rows = SceneConflictGuard.Rows("<p>shared</p>", "<p>shared</p><p>only theirs</p>");

        var onlyTheirs = rows.Single(r => r.State == "theirs");
        Assert.Equal("only theirs", onlyTheirs.Theirs);
        Assert.Null(onlyTheirs.Mine);
    }

    [Fact]
    public void Rows_ReadAsProseRatherThanMarkup()
    {
        // A writer choosing between two drafts is reading sentences; a tag-level
        // diff would bury the difference under identical markup.
        var rows = SceneConflictGuard.Rows(
            "<p><strong>She</strong> left.</p>", "<p><em>She</em> stayed.</p>");

        var changed = rows.Single(r => r.State == "changed");
        Assert.Equal("She left.", changed.Mine);
        Assert.Equal("She stayed.", changed.Theirs);
    }
}
