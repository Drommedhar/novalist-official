using System.ComponentModel;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

public class SmallViewModelsTests
{
    [AvaloniaFact]
    public void Toast_StaticShow_Invokable()
    {
        string? msg = null;
        ToastSeverity sev = ToastSeverity.Info;
        Toast.Show = (m, s) => { msg = m; sev = s; };
        try
        {
            Toast.Show?.Invoke("hi", ToastSeverity.Error);
            Assert.Equal("hi", msg);
            Assert.Equal(ToastSeverity.Error, sev);
        }
        finally { Toast.Show = null; }
    }

    [AvaloniaFact]
    public void ToastNotification_HoldsMessageSeverity_DefaultsInfo()
    {
        var n = new ToastNotification("m", ToastSeverity.Warning);
        Assert.Equal("m", n.Message);
        Assert.Equal(ToastSeverity.Warning, n.Severity);
        Assert.NotEqual(Guid.Empty, n.Id);
        Assert.Equal(ToastSeverity.Info, new ToastNotification("x").Severity);
    }

    [AvaloniaFact]
    public void BusyProgressDialog_StatusChange_NotifiesHasStatus()
    {
        var vm = new BusyProgressDialogViewModel();
        Assert.False(vm.HasStatus);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.Status = "Working";
        Assert.True(vm.HasStatus);
        Assert.Contains(nameof(BusyProgressDialogViewModel.HasStatus), raised);
    }

    [AvaloniaFact]
    public void EditorTabDescriptor_ConstructsWithDefaultsAndInitProps()
    {
        var closed = false;
        var tab = new EditorTabDescriptor("id1", "key1", "Title", () => closed = true,
            badge: "3", minWidth: 200, tooltip: "tip")
        {
            ActivateAction = () => { },
            MoveToOtherPaneAction = () => { }
        };
        Assert.Equal("id1", tab.Id);
        Assert.Equal("key1", tab.ActivationKey);
        Assert.Equal("Title", tab.Title);
        Assert.Equal("3", tab.Badge);
        Assert.Equal(200, tab.MinWidth);
        Assert.Equal("tip", tab.Tooltip);
        Assert.NotNull(tab.ActivateAction);
        Assert.NotNull(tab.MoveToOtherPaneAction);
        tab.OnClose();
        Assert.True(closed);
    }

    [AvaloniaFact]
    public async Task SmartListItem_Evaluate_PopulatesMatches()
    {
        var service = Substitute.For<ISmartListService>();
        var ch = new ChapterData { Title = "Ch" };
        var sc = new SceneData { Title = "Sc" };
        service.EvaluateAsync(Arg.Any<SmartList>())
            .Returns(new List<(ChapterData, SceneData)> { (ch, sc) });

        var vm = new SmartListItemViewModel(new SmartList { Name = "List" }, service, null);
        Assert.Equal("List", vm.Name);

        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.MatchCount);
        Assert.Single(vm.Matches);
        Assert.False(vm.IsLoading);
    }

    [AvaloniaFact]
    public async Task SmartListItem_Expand_TriggersEvaluateOnce()
    {
        var service = Substitute.For<ISmartListService>();
        service.EvaluateAsync(Arg.Any<SmartList>())
            .Returns(new List<(ChapterData, SceneData)>());
        var vm = new SmartListItemViewModel(new SmartList(), service, null);

        vm.IsExpanded = true;                 // triggers fire-and-forget EvaluateAsync
        await vm.RefreshCommand.ExecuteAsync(null); // deterministic evaluate
        await service.Received().EvaluateAsync(Arg.Any<SmartList>());
    }

    [AvaloniaFact]
    public void SmartListSceneEntry_OpenInvokesCallback()
    {
        ChapterData? oc = null; SceneData? os = null;
        var ch = new ChapterData { Title = "C" };
        var sc = new SceneData { Title = "S" };
        var entry = new SmartListSceneEntryViewModel(ch, sc, (c, s) => { oc = c; os = s; });
        Assert.Equal("C → S", entry.DisplayLabel);
        entry.OpenCommand.Execute(null);
        Assert.Same(ch, oc);
        Assert.Same(sc, os);
    }
}

[Collection("Avalonia")]
public class MapConvertersTests
{
    private static readonly System.Globalization.CultureInfo Ci = System.Globalization.CultureInfo.InvariantCulture;

    [AvaloniaFact]
    public void Chevron_Lock_Eye_OpacityGlyphs()
    {
        Assert.Equal("▾", MapConverters.ChevronGlyph.Convert(true, typeof(string), null, Ci));
        Assert.Equal("▸", MapConverters.ChevronGlyph.Convert(false, typeof(string), null, Ci));
        Assert.Equal("▣", MapConverters.LockGlyph.Convert(true, typeof(string), null, Ci));
        Assert.Equal("▢", MapConverters.LockGlyph.Convert(false, typeof(string), null, Ci));
        Assert.Equal("○", MapConverters.EyeGlyph.Convert(true, typeof(string), null, Ci));
        Assert.Equal("●", MapConverters.EyeGlyph.Convert(false, typeof(string), null, Ci));
        Assert.Equal(50m, MapConverters.OpacityToPercent.Convert(0.5, typeof(decimal), null, Ci));
    }
}
