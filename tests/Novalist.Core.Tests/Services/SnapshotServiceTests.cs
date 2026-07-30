using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class SnapshotServiceTests
{
    private const string Root = "/draft";

    private static (SnapshotService Sut, IProjectService Project, InMemoryFileService Files) Build(bool withBook = true)
    {
        var project = Substitute.For<IProjectService>();
        var files = new InMemoryFileService();
        if (withBook)
        {
            project.ActiveBook.Returns(new BookData { SnapshotFolder = "Snapshots" });
            project.ActiveDraftRoot.Returns(Root);
        }
        return (new SnapshotService(project, files), project, files);
    }

    private static ChapterData Ch() => new() { Guid = "c1" };
    private static SceneData Sc() => new() { Id = "s1", WordCount = 42 };

    [Fact]
    public async Task TakeAsync_WritesSnapshotFile()
    {
        var (sut, project, files) = Build();
        var ch = Ch();
        var sc = Sc();
        project.ReadSceneContentAsync(ch, sc).Returns("<p>text</p>");

        var snap = await sut.TakeAsync(ch, sc, "manual");

        Assert.Equal("s1", snap.SceneId);
        Assert.Equal("manual", snap.Label);
        Assert.Equal(42, snap.WordCount);
        Assert.Single(files.Files);
    }

    [Fact]
    public async Task TakeAsync_NullLabel_BecomesEmpty()
    {
        var (sut, project, _) = Build();
        var ch = Ch();
        var sc = Sc();
        project.ReadSceneContentAsync(ch, sc).Returns("x");
        var snap = await sut.TakeAsync(ch, sc, null!);
        Assert.Equal(string.Empty, snap.Label);
    }

    [Fact]
    public async Task TakeAsync_NoActiveBook_Throws()
    {
        var (sut, project, _) = Build(withBook: false);
        var ch = Ch();
        var sc = Sc();
        project.ReadSceneContentAsync(ch, sc).Returns("x");
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.TakeAsync(ch, sc, "l"));
    }

    [Fact]
    public async Task ListAsync_NoDir_ReturnsEmpty()
    {
        var (sut, _, _) = Build();
        Assert.Empty(await sut.ListAsync(Sc()));
    }

    [Fact]
    public async Task ListAsync_NoBook_ReturnsEmpty()
    {
        var (sut, _, _) = Build(withBook: false);
        Assert.Empty(await sut.ListAsync(Sc()));
    }

    [Fact]
    public async Task ListAsync_ReturnsSnapshotsNewestFirst_SkipsCorrupt()
    {
        var (sut, project, files) = Build();
        var ch = Ch();
        var sc = Sc();
        project.ReadSceneContentAsync(ch, sc).Returns("a");
        await sut.TakeAsync(ch, sc, "first");
        await Task.Delay(5);
        project.ReadSceneContentAsync(ch, sc).Returns("b");
        await sut.TakeAsync(ch, sc, "second");

        // Inject a corrupt snapshot file in the same dir.
        var dir = Path.Combine(Root, "Snapshots", "s1");
        files.Files[Path.Combine(dir, "corrupt.json")] = "{ not json";

        var list = await sut.ListAsync(sc);
        Assert.Equal(2, list.Count);
        Assert.True(list[0].CreatedAt >= list[1].CreatedAt);
    }

    [Fact]
    public async Task LoadAsync_FoundAndNotFound()
    {
        var (sut, project, _) = Build();
        var ch = Ch();
        var sc = Sc();
        project.ReadSceneContentAsync(ch, sc).Returns("a");
        var snap = await sut.TakeAsync(ch, sc, "l");

        Assert.NotNull(await sut.LoadAsync(sc, snap.Id));
        Assert.Null(await sut.LoadAsync(sc, "missing"));
    }

    [Fact]
    public async Task RestoreAsync_UnknownSnapshot_ReturnsFalse()
    {
        var (sut, _, _) = Build();
        Assert.False(await sut.RestoreAsync(Ch(), Sc(), "nope"));
    }

    [Fact]
    public async Task RestoreAsync_RestoresContentAndSaves()
    {
        var (sut, project, _) = Build();
        var ch = Ch();
        var sc = Sc();
        project.ReadSceneContentAsync(ch, sc).Returns("original");
        var snap = await sut.TakeAsync(ch, sc, "l");

        sc.WordCount = 999; // simulate later edits
        var ok = await sut.RestoreAsync(ch, sc, snap.Id);

        Assert.True(ok);
        Assert.Equal(42, sc.WordCount); // restored
        await project.Received(1).WriteSceneContentAsync(ch, sc, "original");
        await project.Received(1).SaveScenesAsync();
    }

    [Fact]
    public async Task DeleteAsync_NoDir_NoOp()
    {
        var (sut, _, files) = Build();
        await sut.DeleteAsync(Sc(), "x");
        Assert.Empty(files.Files);
    }

    [Fact]
    public async Task DeleteAsync_NoBook_NoOp()
    {
        var (sut, _, _) = Build(withBook: false);
        await sut.DeleteAsync(Sc(), "x"); // no throw
    }

    [Fact]
    public async Task DeleteAsync_RemovesMatchingSnapshot()
    {
        var (sut, project, files) = Build();
        var ch = Ch();
        var sc = Sc();
        project.ReadSceneContentAsync(ch, sc).Returns("a");
        var snap = await sut.TakeAsync(ch, sc, "l");
        Assert.Single(files.Files);

        await sut.DeleteAsync(sc, snap.Id);
        Assert.Empty(files.Files);
    }

    [Fact]
    public async Task DeleteAsync_NoMatch_KeepsFiles()
    {
        var (sut, project, files) = Build();
        var ch = Ch();
        var sc = Sc();
        project.ReadSceneContentAsync(ch, sc).Returns("a");
        await sut.TakeAsync(ch, sc, "l");

        await sut.DeleteAsync(sc, "does-not-match");
        Assert.Single(files.Files);
    }

    [Fact]
    public async Task GetSceneDir_FallsBackToBookRoot_WhenNoDraftRoot()
    {
        var project = Substitute.For<IProjectService>();
        var files = new InMemoryFileService();
        project.ActiveBook.Returns(new BookData { SnapshotFolder = "Snaps" });
        project.ActiveDraftRoot.Returns((string?)null);
        project.ActiveBookRoot.Returns("/book");
        var sut = new SnapshotService(project, files);
        var ch = Ch();
        var sc = Sc();
        project.ReadSceneContentAsync(ch, sc).Returns("a");

        await sut.TakeAsync(ch, sc, "l");
        Assert.Contains(files.Files.Keys, k => k.Contains(Path.Combine("/book", "Snaps", "s1")) || k.Contains("Snaps"));
    }

    // ── The scene around the prose ──

    [Fact]
    public async Task TakeAsync_CapturesTheSceneNotOnlyItsWords()
    {
        var (sut, project, _) = Build();
        var ch = Ch();
        var sc = Sc();
        sc.Synopsis = "She leaves";
        sc.Notes = "check the bell";
        sc.Stage = "draft";
        sc.LabelKey = "red";
        sc.Date = "1893-04-02";
        sc.PlotlineIds = ["p1"];
        sc.AnalysisOverrides = new SceneAnalysisOverrides { Pov = "Mira", Tags = ["night"] };
        project.ReadSceneContentAsync(ch, sc).Returns("<p>text</p>");

        var snap = await sut.TakeAsync(ch, sc, "manual");

        Assert.Equal("She leaves", snap.Meta!.Synopsis);
        Assert.Equal("check the bell", snap.Meta.Notes);
        Assert.Equal("draft", snap.Meta.Stage);
        Assert.Equal("red", snap.Meta.LabelKey);
        Assert.Equal("1893-04-02", snap.Meta.StoryDate);
        Assert.Equal(["p1"], snap.Meta.PlotlineIds);
        Assert.Equal("Mira", snap.Meta.Pov);
        Assert.Equal(["night"], snap.Meta.Tags);
    }

    [Fact]
    public async Task TakeAsync_CopiesTheListsRatherThanSharingThem()
    {
        var (sut, project, _) = Build();
        var ch = Ch();
        var sc = Sc();
        sc.PlotlineIds = ["p1"];
        project.ReadSceneContentAsync(ch, sc).Returns("<p>text</p>");

        var snap = await sut.TakeAsync(ch, sc, "manual");
        sc.PlotlineIds.Add("p2");

        Assert.Equal(["p1"], snap.Meta!.PlotlineIds);
    }

    [Fact]
    public async Task RestoreAsync_PutsTheScenesOwnFieldsBack()
    {
        var (sut, project, _) = Build();
        var ch = Ch();
        var sc = Sc();
        sc.Synopsis = "She leaves";
        sc.Stage = "draft";
        sc.AnalysisOverrides = new SceneAnalysisOverrides { Pov = "Mira" };
        project.ReadSceneContentAsync(ch, sc).Returns("<p>first</p>");
        var snap = await sut.TakeAsync(ch, sc, "before");

        sc.Synopsis = "She stays";
        sc.Stage = "final";
        sc.AnalysisOverrides.Pov = "Halden";

        Assert.True(await sut.RestoreAsync(ch, sc, snap.Id));
        Assert.Equal("She leaves", sc.Synopsis);
        Assert.Equal("draft", sc.Stage);
        Assert.Equal("Mira", sc.AnalysisOverrides.Pov);
    }

    [Fact]
    public async Task RestoreAsync_ASnapshotWithNoMetaLeavesTheSceneAlone()
    {
        var (sut, project, files) = Build();
        var ch = Ch();
        var sc = Sc();
        project.ReadSceneContentAsync(ch, sc).Returns("<p>first</p>");
        var snap = await sut.TakeAsync(ch, sc, "before");

        // Every snapshot taken before this shipped looks exactly like this one.
        var path = files.Files.Keys.First();
        files.Files[path] = files.Files[path].Replace(
            System.Text.Json.JsonSerializer.Serialize(snap.Meta), "null");
        sc.Synopsis = "written since";

        Assert.True(await sut.RestoreAsync(ch, sc, snap.Id));
        Assert.Equal("written since", sc.Synopsis);
    }

    // ── The project-wide list ──

    [Fact]
    public async Task ListAllAsync_ReturnsEverySnapshotNewestFirst()
    {
        var (sut, project, _) = Build();
        var ch = Ch();
        var one = new SceneData { Id = "s1", Title = "One" };
        var two = new SceneData { Id = "s2", Title = "Two" };
        project.GetChaptersOrdered().Returns([ch]);
        project.GetScenesForChapter("c1").Returns([one, two]);
        project.ReadSceneContentAsync(ch, one).Returns("<p>a</p>");
        project.ReadSceneContentAsync(ch, two).Returns("<p>b</p>");
        await sut.TakeAsync(ch, one, "first");
        await sut.TakeAsync(ch, two, "second");

        var all = await sut.ListAllAsync();

        Assert.Equal(2, all.Count);
        Assert.All(all, row => Assert.Equal("c1", row.ChapterGuid));
        Assert.Contains(all, row => row.SceneTitle == "Two");
    }

    [Fact]
    public async Task RenameAsync_ChangesTheLabelOnDisk()
    {
        var (sut, project, _) = Build();
        var ch = Ch();
        var sc = Sc();
        project.ReadSceneContentAsync(ch, sc).Returns("<p>a</p>");
        var snap = await sut.TakeAsync(ch, sc, "before");

        Assert.True(await sut.RenameAsync(sc, snap.Id, "sent to the agent"));
        Assert.Equal("sent to the agent", (await sut.LoadAsync(sc, snap.Id))!.Label);
    }

    [Fact]
    public async Task RenameAsync_UnknownSnapshotIsFalse()
    {
        var (sut, project, _) = Build();
        var ch = Ch();
        var sc = Sc();
        project.ReadSceneContentAsync(ch, sc).Returns("<p>a</p>");
        await sut.TakeAsync(ch, sc, "before");

        Assert.False(await sut.RenameAsync(sc, "nope", "x"));
    }

    [Fact]
    public async Task RenameAsync_ACorruptSnapshotFileIsFalse()
    {
        var (sut, project, files) = Build();
        var ch = Ch();
        var sc = Sc();
        project.ReadSceneContentAsync(ch, sc).Returns("<p>a</p>");
        var snap = await sut.TakeAsync(ch, sc, "before");
        files.Files[files.Files.Keys.First()] = "{ not json";

        Assert.False(await sut.RenameAsync(sc, snap.Id, "x"));
    }

    [Fact]
    public async Task RenameAsync_NoSceneFolderIsFalse()
    {
        var (sut, _, _) = Build();
        Assert.False(await sut.RenameAsync(Sc(), "any", "x"));
    }

    // ── Pruning ──

    [Fact]
    public async Task PruneAsync_KeepsTheNewestFewPerScene()
    {
        var (sut, project, files) = Build();
        var ch = Ch();
        var sc = Sc();
        project.GetChaptersOrdered().Returns([ch]);
        project.GetScenesForChapter("c1").Returns([sc]);
        project.ReadSceneContentAsync(ch, sc).Returns("<p>a</p>");
        for (var i = 0; i < 4; i++) await sut.TakeAsync(ch, sc, $"take-{i}");

        var removed = await sut.PruneAsync(keepPerScene: 2, olderThanDays: 0, dropOrphans: false);

        Assert.Equal(2, removed);
        Assert.Equal(2, (await sut.ListAsync(sc)).Count);
        Assert.Equal(2, files.Files.Count);
    }

    // One Replace All labels every snapshot it takes the same way, so the run
    // can be cleared without touching the ones the writer took deliberately.
    [Fact]
    public async Task DeleteByLabelAsync_RemovesOneRunAndLeavesTheRest()
    {
        var (sut, project, files) = Build();
        var ch = Ch();
        var sc = Sc();
        project.GetChaptersOrdered().Returns([ch]);
        project.GetScenesForChapter("c1").Returns([sc]);
        project.ReadSceneContentAsync(ch, sc).Returns("<p>a</p>");
        await sut.TakeAsync(ch, sc, "Before find/replace 2026-07-31 01:00:00");
        await sut.TakeAsync(ch, sc, "Before find/replace 2026-07-31 01:00:00");
        await sut.TakeAsync(ch, sc, "Before find/replace 2026-07-31 02:00:00");
        await sut.TakeAsync(ch, sc, "Mine");

        var removed = await sut.DeleteByLabelAsync("Before find/replace 2026-07-31 01:00:00");

        Assert.Equal(2, removed);
        var left = await sut.ListAsync(sc);
        Assert.Equal(2, left.Count);
        Assert.Contains(left, s => s.Label == "Mine");
        Assert.Contains(left, s => s.Label == "Before find/replace 2026-07-31 02:00:00");
        Assert.Equal(2, files.Files.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DeleteByLabelAsync_RefusesABlankLabel(string label)
    {
        var (sut, project, _) = Build();
        var ch = Ch();
        var sc = Sc();
        project.GetChaptersOrdered().Returns([ch]);
        project.GetScenesForChapter("c1").Returns([sc]);
        project.ReadSceneContentAsync(ch, sc).Returns("<p>a</p>");
        // A snapshot with no label at all must not be swept up by a blank one.
        await sut.TakeAsync(ch, sc, string.Empty);

        Assert.Equal(0, await sut.DeleteByLabelAsync(label));
        Assert.Single(await sut.ListAsync(sc));
    }

    [Fact]
    public async Task PruneAsync_DropsFoldersLeftBehindByDeletedScenes()
    {
        var (sut, project, files) = Build();
        var ch = Ch();
        var gone = new SceneData { Id = "deleted" };
        project.GetChaptersOrdered().Returns([ch]);
        project.GetScenesForChapter("c1").Returns([]);
        project.ReadSceneContentAsync(ch, gone).Returns("<p>a</p>");
        await sut.TakeAsync(ch, gone, "orphan");

        Assert.Equal(1, await sut.PruneAsync(0, 0, dropOrphans: true));
        Assert.Empty(files.Files);
    }

    [Fact]
    public async Task PruneAsync_LeavesOrphansAloneWhenNotAsked()
    {
        var (sut, project, files) = Build();
        var ch = Ch();
        var gone = new SceneData { Id = "deleted" };
        project.GetChaptersOrdered().Returns([ch]);
        project.GetScenesForChapter("c1").Returns([]);
        project.ReadSceneContentAsync(ch, gone).Returns("<p>a</p>");
        await sut.TakeAsync(ch, gone, "orphan");

        Assert.Equal(0, await sut.PruneAsync(0, 0, dropOrphans: false));
        Assert.Single(files.Files);
    }

    [Fact]
    public async Task PruneAsync_NoProjectIsNothingToDo()
    {
        var (sut, _, _) = Build(withBook: false);
        Assert.Equal(0, await sut.PruneAsync(1, 1, true));
    }
}
