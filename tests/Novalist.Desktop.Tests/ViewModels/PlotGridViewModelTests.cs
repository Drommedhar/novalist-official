using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

public class PlotGridViewModelTests
{
    private static (PlotGridViewModel Vm, IProjectService Proj, IPlotlineService Plot) Build()
    {
        var proj = Substitute.For<IProjectService>();
        var plot = Substitute.For<IPlotlineService>();
        return (new PlotGridViewModel(proj, plot), proj, plot);
    }

    private static void Seed(IProjectService proj, IPlotlineService plot,
        out ChapterData ch, out SceneData sc, out PlotlineData pl)
    {
        ch = new ChapterData { Guid = "c1", Title = "Chap 1" };
        sc = new SceneData { Id = "s1", Title = "Scene 1", ChapterGuid = "c1" };
        pl = new PlotlineData { Id = "p1", Name = "Main" };
        proj.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        proj.GetScenesForChapter("c1").Returns(new List<SceneData> { sc });
        plot.GetPlotlines().Returns(new List<PlotlineData> { pl });
    }

    [Fact]
    public void Refresh_BuildsColumnsRowsCells_AndHasContent()
    {
        var (vm, proj, plot) = Build();
        Seed(proj, plot, out _, out var sc, out var pl);
        plot.IsSceneInPlotline(sc, "p1").Returns(true);

        vm.Refresh();

        Assert.Single(vm.Columns);
        Assert.Equal("Chap 1", vm.Columns[0].ChapterTitle);
        Assert.Equal("Scene 1", vm.Columns[0].SceneTitle);
        Assert.Single(vm.Rows);
        var row = vm.Rows[0];
        Assert.Same(pl, row.Plotline);
        Assert.Single(row.Cells);
        Assert.True(row.Cells[0].IsAssigned);
        Assert.Equal("Chap 1 → Scene 1", row.Cells[0].Tooltip);
        Assert.True(vm.HasContent);
    }

    [Fact]
    public void Refresh_NoPlotlines_HasContentFalse()
    {
        var (vm, proj, plot) = Build();
        proj.GetChaptersOrdered().Returns(new List<ChapterData>());
        plot.GetPlotlines().Returns(new List<PlotlineData>());
        vm.Refresh();
        Assert.False(vm.HasContent);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task Cell_Toggle_CallsToggleScene()
    {
        var (vm, proj, plot) = Build();
        Seed(proj, plot, out _, out var sc, out var pl);
        vm.Refresh();
        var cell = vm.Rows[0].Cells[0];

        cell.IsAssigned = !cell.IsAssigned; // OnIsAssignedChanged -> OnCellToggleAsync
        await Task.Delay(20);

        await plot.Received(1).ToggleSceneAsync("c1", "s1", "p1");
    }

    [Fact]
    public async Task Row_NameChange_UpdatesPlotline_SkipsWhenUnchanged()
    {
        var (vm, proj, plot) = Build();
        Seed(proj, plot, out _, out _, out var pl);
        vm.Refresh();
        var row = vm.Rows[0];

        row.Name = "Main"; // same value -> skipped
        await Task.Delay(10);
        await plot.DidNotReceive().UpdateAsync(Arg.Any<PlotlineData>());

        proj.ClearReceivedCalls();
        row.Name = "Renamed"; // changed -> UpdateAsync, then _onDirty -> Refresh
        await Task.Delay(10);
        Assert.Equal("Renamed", pl.Name);
        await plot.Received(1).UpdateAsync(pl);
        proj.Received().GetChaptersOrdered(); // OnRowDirty triggered a Refresh
    }

    [Fact]
    public async Task AddPlotline_NullDialog_NoOp()
    {
        var (vm, _, plot) = Build();
        await vm.AddPlotlineCommand.ExecuteAsync(null);
        await plot.DidNotReceive().CreateAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task AddPlotline_Whitespace_NoCreate()
    {
        var (vm, _, plot) = Build();
        vm.ShowInputDialog = (_, _, _) => Task.FromResult<string?>("   ");
        await vm.AddPlotlineCommand.ExecuteAsync(null);
        await plot.DidNotReceive().CreateAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task AddPlotline_Valid_CreatesAndRefreshes()
    {
        var (vm, proj, plot) = Build();
        proj.GetChaptersOrdered().Returns(new List<ChapterData>());
        plot.GetPlotlines().Returns(new List<PlotlineData>());
        vm.ShowInputDialog = (_, _, _) => Task.FromResult<string?>("  New  ");
        await vm.AddPlotlineCommand.ExecuteAsync(null);
        await plot.Received(1).CreateAsync("New");
        plot.Received().GetPlotlines(); // refreshed
    }

    [Fact]
    public async Task RenamePlotline_NullRowOrDialog_NoOp()
    {
        var (vm, _, plot) = Build();
        await vm.RenamePlotlineCommand.ExecuteAsync(null); // null row
        var row = new PlotGridRow(new PlotlineData { Id = "p1", Name = "X" }, [], plot, () => { });
        await vm.RenamePlotlineCommand.ExecuteAsync(row); // null dialog
        await plot.DidNotReceive().UpdateAsync(Arg.Any<PlotlineData>());
    }

    [Fact]
    public async Task RenamePlotline_Whitespace_NoUpdate()
    {
        var (vm, _, plot) = Build();
        vm.ShowInputDialog = (_, _, _) => Task.FromResult<string?>(" ");
        var row = new PlotGridRow(new PlotlineData { Id = "p1", Name = "X" }, [], plot, () => { });
        await vm.RenamePlotlineCommand.ExecuteAsync(row);
        await plot.DidNotReceive().UpdateAsync(Arg.Any<PlotlineData>());
    }

    [Fact]
    public async Task RenamePlotline_Valid_UpdatesAndRefreshes()
    {
        var (vm, proj, plot) = Build();
        proj.GetChaptersOrdered().Returns(new List<ChapterData>());
        plot.GetPlotlines().Returns(new List<PlotlineData>());
        var pl = new PlotlineData { Id = "p1", Name = "Old" };
        var row = new PlotGridRow(pl, [], plot, () => { });
        vm.ShowInputDialog = (_, _, _) => Task.FromResult<string?>(" Fresh ");
        await vm.RenamePlotlineCommand.ExecuteAsync(row);
        Assert.Equal("Fresh", pl.Name);
        await plot.Received().UpdateAsync(pl);
    }

    [Fact]
    public async Task DeletePlotline_NullRow_NoOp()
    {
        var (vm, _, plot) = Build();
        await vm.DeletePlotlineCommand.ExecuteAsync(null);
        await plot.DidNotReceive().DeleteAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task DeletePlotline_ConfirmFalse_NoDelete()
    {
        var (vm, _, plot) = Build();
        vm.ShowConfirmDialog = (_, _) => Task.FromResult(false);
        var row = new PlotGridRow(new PlotlineData { Id = "p1", Name = "X" }, [], plot, () => { });
        await vm.DeletePlotlineCommand.ExecuteAsync(row);
        await plot.DidNotReceive().DeleteAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task DeletePlotline_ConfirmTrue_Deletes()
    {
        var (vm, proj, plot) = Build();
        proj.GetChaptersOrdered().Returns(new List<ChapterData>());
        plot.GetPlotlines().Returns(new List<PlotlineData>());
        vm.ShowConfirmDialog = (_, _) => Task.FromResult(true);
        var row = new PlotGridRow(new PlotlineData { Id = "p1", Name = "X" }, [], plot, () => { });
        await vm.DeletePlotlineCommand.ExecuteAsync(row);
        await plot.Received(1).DeleteAsync("p1");
    }

    [Fact]
    public async Task DeletePlotline_NoConfirmDialog_DeletesDirectly()
    {
        var (vm, proj, plot) = Build();
        proj.GetChaptersOrdered().Returns(new List<ChapterData>());
        plot.GetPlotlines().Returns(new List<PlotlineData>());
        var row = new PlotGridRow(new PlotlineData { Id = "p1", Name = "X" }, [], plot, () => { });
        await vm.DeletePlotlineCommand.ExecuteAsync(row); // ShowConfirmDialog null -> skips gate
        await plot.Received(1).DeleteAsync("p1");
    }
}
