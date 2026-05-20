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
}
