using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class PlotlineServiceTests
{
    private static (PlotlineService Sut, IProjectService Project, BookData Book) Build()
    {
        var project = Substitute.For<IProjectService>();
        var book = new BookData();
        project.ActiveBook.Returns(book);
        return (new PlotlineService(project), project, book);
    }

    [Fact]
    public void GetPlotlines_NoBook_ReturnsEmpty()
    {
        var project = Substitute.For<IProjectService>();
        project.ActiveBook.Returns((BookData?)null);
        Assert.Empty(new PlotlineService(project).GetPlotlines());
    }

    [Fact]
    public void GetPlotlines_OrdersByOrder()
    {
        var (sut, _, book) = Build();
        book.Plotlines.Add(new PlotlineData { Id = "b", Order = 2 });
        book.Plotlines.Add(new PlotlineData { Id = "a", Order = 1 });
        var result = sut.GetPlotlines();
        Assert.Equal("a", result[0].Id);
        Assert.Equal("b", result[1].Id);
    }

    [Fact]
    public async Task CreateAsync_AddsWithIncrementingOrder_AndSaves()
    {
        var (sut, project, book) = Build();
        var p = await sut.CreateAsync("Romance", "#fff");
        Assert.Equal("Romance", p.Name);
        Assert.Equal("#fff", p.Color);
        Assert.Equal(0, p.Order);
        Assert.Single(book.Plotlines);
        await project.Received(1).SaveProjectAsync();
    }

    [Fact]
    public async Task CreateAsync_NoBook_Throws()
    {
        var project = Substitute.For<IProjectService>();
        project.ActiveBook.Returns((BookData?)null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PlotlineService(project).CreateAsync("x"));
    }

    [Fact]
    public async Task UpdateAsync_ReplacesExisting()
    {
        var (sut, project, book) = Build();
        book.Plotlines.Add(new PlotlineData { Id = "p1", Name = "old" });
        await sut.UpdateAsync(new PlotlineData { Id = "p1", Name = "new" });
        Assert.Equal("new", book.Plotlines[0].Name);
        await project.Received(1).SaveProjectAsync();
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_NoOp()
    {
        var (sut, project, book) = Build();
        book.Plotlines.Add(new PlotlineData { Id = "p1" });
        await sut.UpdateAsync(new PlotlineData { Id = "other" });
        await project.DidNotReceive().SaveProjectAsync();
    }

    [Fact]
    public async Task DeleteAsync_RemovesPlotlineAndSceneAssignments()
    {
        var (sut, project, book) = Build();
        book.Plotlines.Add(new PlotlineData { Id = "p1" });
        var ch = new ChapterData { Guid = "c1" };
        var sceneWith = new SceneData { Id = "s1", PlotlineIds = new() { "p1" } };
        var sceneMulti = new SceneData { Id = "s2", PlotlineIds = new() { "p1", "p2" } };
        var sceneNone = new SceneData { Id = "s3" };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { sceneWith, sceneMulti, sceneNone });

        await sut.DeleteAsync("p1");

        Assert.Empty(book.Plotlines);
        Assert.Null(sceneWith.PlotlineIds);          // emptied -> nulled
        Assert.Equal(new[] { "p2" }, sceneMulti.PlotlineIds);
        await project.Received(1).SaveScenesAsync();
        await project.Received(1).SaveProjectAsync();
    }

    [Fact]
    public async Task ReorderAsync_AssignsSequentialOrder()
    {
        var (sut, project, book) = Build();
        book.Plotlines.Add(new PlotlineData { Id = "a", Order = 5 });
        book.Plotlines.Add(new PlotlineData { Id = "b", Order = 9 });
        await sut.ReorderAsync(new[] { "b", "a", "ghost" });
        Assert.Equal(0, book.Plotlines.First(p => p.Id == "b").Order);
        Assert.Equal(1, book.Plotlines.First(p => p.Id == "a").Order);
        await project.Received(1).SaveProjectAsync();
    }

    [Fact]
    public async Task ToggleSceneAsync_AddsThenRemoves()
    {
        var (sut, project, _) = Build();
        var ch = new ChapterData { Guid = "c1" };
        var sc = new SceneData { Id = "s1" };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { sc });

        await sut.ToggleSceneAsync("c1", "s1", "p1");
        Assert.Contains("p1", sc.PlotlineIds!);

        await sut.ToggleSceneAsync("c1", "s1", "p1");
        Assert.Null(sc.PlotlineIds); // last one removed -> nulled
    }

    [Fact]
    public async Task SetCellNoteAsync_SetsUpdatesAndClears()
    {
        var (sut, project, _) = Build();
        var ch = new ChapterData { Guid = "c1" };
        var sc = new SceneData { Id = "s1" };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { sc });

        await sut.SetCellNoteAsync("c1", "s1", "p1", "  Sets up the betrayal.  ");
        Assert.Equal("Sets up the betrayal.", sc.PlotlineNotes!["p1"]);   // trimmed

        await sut.SetCellNoteAsync("c1", "s1", "p2", "Pays it off.");
        Assert.Equal(2, sc.PlotlineNotes!.Count);

        await sut.SetCellNoteAsync("c1", "s1", "p1", "   ");
        Assert.False(sc.PlotlineNotes!.ContainsKey("p1"));               // blank clears

        await sut.SetCellNoteAsync("c1", "s1", "p2", "");
        Assert.Null(sc.PlotlineNotes);                                    // last one removed -> nulled
    }

    [Fact]
    public async Task SetCellNoteAsync_ClearingWhenNoNotes_IsNoOp()
    {
        var (sut, project, _) = Build();
        var ch = new ChapterData { Guid = "c1" };
        var sc = new SceneData { Id = "s1" };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { sc });

        await sut.SetCellNoteAsync("c1", "s1", "p1", null);
        Assert.Null(sc.PlotlineNotes);
        await project.DidNotReceive().SaveScenesAsync();
    }

    [Fact]
    public async Task SetCellNoteAsync_MissingChapterOrScene_NoOp()
    {
        var (sut, project, _) = Build();
        project.GetChaptersOrdered().Returns(new List<ChapterData>());
        await sut.SetCellNoteAsync("nope", "s1", "p1", "x");

        var ch = new ChapterData { Guid = "c1" };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData>());
        await sut.SetCellNoteAsync("c1", "missing", "p1", "x");

        await project.DidNotReceive().SaveScenesAsync();
    }

    [Fact]
    public async Task ToggleSceneAsync_MissingChapter_NoOp()
    {
        var (sut, project, _) = Build();
        project.GetChaptersOrdered().Returns(new List<ChapterData>());
        await sut.ToggleSceneAsync("nope", "s1", "p1");
        await project.DidNotReceive().SaveScenesAsync();
    }

    [Fact]
    public async Task ToggleSceneAsync_MissingScene_NoOp()
    {
        var (sut, project, _) = Build();
        var ch = new ChapterData { Guid = "c1" };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData>());
        await sut.ToggleSceneAsync("c1", "nope", "p1");
        await project.DidNotReceive().SaveScenesAsync();
    }

    [Fact]
    public async Task ToggleSceneAsync_KeepsOtherIds_WhenRemovingOne()
    {
        var (sut, project, _) = Build();
        var ch = new ChapterData { Guid = "c1" };
        var sc = new SceneData { Id = "s1", PlotlineIds = new() { "p1", "p2" } };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { sc });
        await sut.ToggleSceneAsync("c1", "s1", "p1");
        Assert.Equal(new[] { "p2" }, sc.PlotlineIds);
    }

    [Theory]
    [InlineData(new[] { "p1" }, "p1", true)]
    [InlineData(new[] { "p2" }, "p1", false)]
    public void IsSceneInPlotline(string[] ids, string query, bool expected)
    {
        var (sut, _, _) = Build();
        var sc = new SceneData { PlotlineIds = ids.ToList() };
        Assert.Equal(expected, sut.IsSceneInPlotline(sc, query));
    }

    [Fact]
    public void IsSceneInPlotline_NullIds_False()
    {
        var (sut, _, _) = Build();
        Assert.False(sut.IsSceneInPlotline(new SceneData(), "p1"));
    }

    // Every thread used to be the same blue, so a grid of coloured cells and a
    // lane view of coloured tracks both said nothing about which was which.
    [Fact]
    public async Task EachNewThreadTakesTheNextColour()
    {
        var (sut, _, _) = Build();

        var first = await sut.CreateAsync("The debt");
        var second = await sut.CreateAsync("The romance");
        var third = await sut.CreateAsync("The joke");

        Assert.Equal(PlotlineService.Palette[0], first.Color);
        Assert.Equal(PlotlineService.Palette[1], second.Color);
        Assert.Equal(PlotlineService.Palette[2], third.Color);
    }

    [Fact]
    public async Task TheColoursComeRoundAgainRatherThanRunningOut()
    {
        var (sut, _, _) = Build();

        for (var i = 0; i < PlotlineService.Palette.Count; i++) await sut.CreateAsync($"T{i}");
        var wrapped = await sut.CreateAsync("One more");

        Assert.Equal(PlotlineService.Palette[0], wrapped.Color);
    }

    [Fact]
    public async Task AColourAskedForIsHonoured()
    {
        var (sut, _, _) = Build();

        Assert.Equal("#ff0000", (await sut.CreateAsync("Mine", "#ff0000")).Color);
    }
}
