using Avalonia.Input;
using NSubstitute;
using Novalist.Desktop.Services;
using Novalist.Desktop.ViewModels;
using Novalist.Sdk.Models;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

[Collection("Avalonia")]
public class HotkeySettingsViewModelTests
{
    private static HotkeyDescriptor Desc(string id, string name, string cat, string def)
        => new() { ActionId = id, DisplayName = name, Category = cat, DefaultGesture = def };

    private static (HotkeySettingsViewModel Vm, IHotkeyService Svc) Build(params HotkeyDescriptor[] descs)
    {
        var svc = Substitute.For<IHotkeyService>();
        svc.GetAllDescriptors().Returns(descs);
        svc.GetGesture(Arg.Any<string>()).Returns(ci => descs.First(d => d.ActionId == ci.Arg<string>()).DefaultGesture);
        return (new HotkeySettingsViewModel(svc), svc);
    }

    private static HotkeyBindingItem ItemFor(HotkeySettingsViewModel vm, string id)
        => vm.AllItems.First(i => i.ActionId == id);

    [AvaloniaFact]
    public void Ctor_Refresh_BuildsItemsAndHeaders()
    {
        var (vm, _) = Build(
            Desc("a", "Alpha", "Edit", "Ctrl+A"),
            Desc("b", "Bravo", "Edit", "Ctrl+B"),
            Desc("c", "Charlie", "View", "Ctrl+C"));

        Assert.Equal(3, vm.AllItems.Count);
        // 2 category headers + 3 items
        Assert.Equal(5, vm.DisplayItems.Count);
        Assert.IsType<HotkeyGroupHeader>(vm.DisplayItems[0]);
        Assert.Equal("Edit", ((HotkeyGroupHeader)vm.DisplayItems[0]).Category);
    }

    [AvaloniaFact]
    public void Filter_NarrowsItems_ByNameCategoryGesture()
    {
        var (vm, _) = Build(
            Desc("a", "Alpha", "Edit", "Ctrl+A"),
            Desc("b", "Bravo", "View", "Ctrl+B"));

        vm.FilterText = "alph"; // name match
        Assert.Contains(vm.DisplayItems, o => o is HotkeyBindingItem h && h.ActionId == "a");
        Assert.DoesNotContain(vm.DisplayItems, o => o is HotkeyBindingItem h && h.ActionId == "b");

        vm.FilterText = "view"; // category match -> b
        Assert.Contains(vm.DisplayItems, o => o is HotkeyBindingItem h && h.ActionId == "b");

        vm.FilterText = "ctrl+a"; // gesture match -> a
        Assert.Contains(vm.DisplayItems, o => o is HotkeyBindingItem h && h.ActionId == "a");

        vm.FilterText = "";
        Assert.Equal(4, vm.DisplayItems.Count); // 2 headers + 2 items
    }

    [AvaloniaFact]
    public void IsModified_TrueWhenGestureDiffersFromDefault()
    {
        var (vm, _) = Build(Desc("a", "Alpha", "Edit", "Ctrl+A"));
        var item = ItemFor(vm, "a");
        Assert.False(item.IsModified);
        item.CurrentGesture = "Ctrl+X";
        item.NotifyIsModifiedChanged();
        Assert.True(item.IsModified);
    }

    [AvaloniaFact]
    public void StartRecording_SecondCancelsFirst()
    {
        var (vm, _) = Build(Desc("a", "A", "E", "Ctrl+A"), Desc("b", "B", "E", "Ctrl+B"));
        var a = ItemFor(vm, "a");
        var b = ItemFor(vm, "b");

        vm.StartRecordingCommand.Execute(a);
        Assert.True(a.IsRecording);
        Assert.True(vm.IsRecording);

        vm.StartRecordingCommand.Execute(b);
        Assert.False(a.IsRecording);
        Assert.True(b.IsRecording);
    }

    [AvaloniaFact]
    public void CancelRecording_ClearsState()
    {
        var (vm, _) = Build(Desc("a", "A", "E", "Ctrl+A"));
        vm.StartRecordingCommand.Execute(ItemFor(vm, "a"));
        vm.CancelRecordingCommand.Execute(null);
        Assert.False(vm.IsRecording);
        Assert.False(ItemFor(vm, "a").IsRecording);
    }

    [AvaloniaFact]
    public void HandleRecordingKeyDown_NoRecording_ReturnsFalse()
    {
        var (vm, _) = Build(Desc("a", "A", "E", "Ctrl+A"));
        Assert.False(vm.HandleRecordingKeyDown(new KeyEventArgs { Key = Key.X }));
    }

    [AvaloniaFact]
    public void HandleRecordingKeyDown_ModifierOnly_Ignored()
    {
        var (vm, _) = Build(Desc("a", "A", "E", "Ctrl+A"));
        vm.StartRecordingCommand.Execute(ItemFor(vm, "a"));
        Assert.True(vm.HandleRecordingKeyDown(new KeyEventArgs { Key = Key.LeftCtrl }));
        Assert.True(vm.IsRecording); // still recording
    }

    [AvaloniaFact]
    public void HandleRecordingKeyDown_Escape_Cancels()
    {
        var (vm, _) = Build(Desc("a", "A", "E", "Ctrl+A"));
        vm.StartRecordingCommand.Execute(ItemFor(vm, "a"));
        Assert.True(vm.HandleRecordingKeyDown(new KeyEventArgs { Key = Key.Escape }));
        Assert.False(vm.IsRecording);
    }

    [AvaloniaFact]
    public void HandleRecordingKeyDown_NoConflict_AppliesGesture()
    {
        var (vm, svc) = Build(Desc("a", "A", "E", "Ctrl+A"));
        svc.FindConflict(Arg.Any<string>(), Arg.Any<string>()).Returns((string?)null);
        var a = ItemFor(vm, "a");
        vm.StartRecordingCommand.Execute(a);

        var handled = vm.HandleRecordingKeyDown(new KeyEventArgs { Key = Key.X, KeyModifiers = KeyModifiers.Control });
        Assert.True(handled);
        Assert.False(a.HasConflict);
        Assert.False(a.IsRecording);
        Assert.False(vm.IsRecording);
        svc.Received().SetGesture("a", Arg.Any<string>());
        Assert.NotEqual("Ctrl+A", a.CurrentGesture);
    }

    [AvaloniaFact]
    public void HandleRecordingKeyDown_Conflict_SetsConflictDescription()
    {
        var (vm, svc) = Build(Desc("a", "A", "E", "Ctrl+A"), Desc("b", "Bravo", "E", "Ctrl+B"));
        svc.FindConflict("a", Arg.Any<string>()).Returns("b"); // collides with b
        var a = ItemFor(vm, "a");
        vm.StartRecordingCommand.Execute(a);

        vm.HandleRecordingKeyDown(new KeyEventArgs { Key = Key.B, KeyModifiers = KeyModifiers.Control });
        Assert.True(a.HasConflict);
        Assert.False(string.IsNullOrEmpty(a.ConflictDescription));
    }

    [AvaloniaFact]
    public void HandleRecordingKeyDown_ConflictUnknownAction_GenericDescription()
    {
        var (vm, svc) = Build(Desc("a", "A", "E", "Ctrl+A"));
        svc.FindConflict("a", Arg.Any<string>()).Returns("ghost"); // not in AllItems
        var a = ItemFor(vm, "a");
        vm.StartRecordingCommand.Execute(a);
        vm.HandleRecordingKeyDown(new KeyEventArgs { Key = Key.Z, KeyModifiers = KeyModifiers.Control });
        Assert.True(a.HasConflict);
        Assert.False(string.IsNullOrEmpty(a.ConflictDescription));
    }

    [AvaloniaFact]
    public void ResetBinding_RevertsToDefault()
    {
        var (vm, svc) = Build(Desc("a", "A", "E", "Ctrl+A"));
        var a = ItemFor(vm, "a");
        a.CurrentGesture = "Ctrl+X";
        a.HasConflict = true;

        vm.ResetBindingCommand.Execute(a);
        svc.Received().ResetGesture("a");
        Assert.Equal("Ctrl+A", a.CurrentGesture);
        Assert.False(a.HasConflict);
        Assert.Equal(string.Empty, a.ConflictDescription);
    }

    [AvaloniaFact]
    public void ResetAll_RevertsEveryItem()
    {
        var (vm, svc) = Build(Desc("a", "A", "E", "Ctrl+A"), Desc("b", "B", "E", "Ctrl+B"));
        foreach (var i in vm.AllItems) { i.CurrentGesture = "Ctrl+Z"; i.HasConflict = true; }

        vm.ResetAllCommand.Execute(null);
        svc.Received().ResetAll();
        Assert.All(vm.AllItems, i => Assert.Equal(i.DefaultGesture, i.CurrentGesture));
        Assert.All(vm.AllItems, i => Assert.False(i.HasConflict));
    }
}
