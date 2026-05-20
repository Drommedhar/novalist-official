using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

public class SmartListItemViewModelTests
{
    private static (ChapterData, SceneData) Pair(string ct, string st)
        => (new ChapterData { Title = ct }, new SceneData { Title = st });

    private static (SmartListItemViewModel Vm, ISmartListService Svc) Build(
        params (ChapterData, SceneData)[] matches)
    {
        var svc = Substitute.For<ISmartListService>();
        svc.EvaluateAsync(Arg.Any<SmartList>())
           .Returns((IReadOnlyList<(ChapterData Chapter, SceneData Scene)>)matches.ToList());
        return (new SmartListItemViewModel(new SmartList { Name = "Drafts" }, svc, null), svc);
    }

    [Fact]
    public void Name_ComesFromSource()
    {
        var (vm, _) = Build();
        Assert.Equal("Drafts", vm.Name);
    }

    [Fact]
    public async Task Expand_EvaluatesOnce()
    {
        var (vm, svc) = Build(Pair("Ch1", "Sc1"), Pair("Ch1", "Sc2"));
        vm.IsExpanded = true;
        await Task.Delay(20);
        Assert.Equal(2, vm.MatchCount);
        Assert.Equal(2, vm.Matches.Count);
        Assert.False(vm.IsLoading);

        // Collapse + re-expand: already evaluated -> no second evaluation.
        vm.IsExpanded = false;
        vm.IsExpanded = true;
        await Task.Delay(20);
        await svc.Received(1).EvaluateAsync(Arg.Any<SmartList>());
    }

    [Fact]
    public async Task Refresh_ReEvaluates()
    {
        var (vm, svc) = Build(Pair("Ch1", "Sc1"));
        vm.IsExpanded = true;
        await Task.Delay(20);
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.MatchCount);
        await svc.Received(2).EvaluateAsync(Arg.Any<SmartList>());
    }

    [Fact]
    public void SceneEntry_DisplayLabel_AndOpenCallback()
    {
        ChapterData? oc = null; SceneData? os = null;
        var ch = new ChapterData { Title = "Chapter" };
        var sc = new SceneData { Title = "Scene" };
        var entry = new SmartListSceneEntryViewModel(ch, sc, (c, s) => { oc = c; os = s; });
        Assert.Equal("Chapter → Scene", entry.DisplayLabel);
        entry.OpenCommand.Execute(null);
        Assert.Same(ch, oc);
        Assert.Same(sc, os);
    }

    [Fact]
    public void SceneEntry_NullCallback_NoThrow()
    {
        var entry = new SmartListSceneEntryViewModel(new ChapterData(), new SceneData(), null);
        entry.OpenCommand.Execute(null);
    }
}
