using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Novalist.Desktop.ViewModels;

namespace Novalist.Desktop.Views;

public partial class CalendarView : UserControl
{
    private static readonly DataFormat<CalendarSceneEvent> SceneDragFormat =
        DataFormat.CreateInProcessFormat<CalendarSceneEvent>("novalist/calendar-scene");

    public CalendarView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnMonthDayDrop);
        AddHandler(DragDrop.DragOverEvent, OnMonthDayDragOver);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async void OnMonthEventPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control) return;
        if (control.Tag is not CalendarSceneEvent ev) return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) return;

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(SceneDragFormat, ev));
        await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
    }

    private void OnMonthDayDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer?.Contains(SceneDragFormat) == true)
            e.DragEffects = DragDropEffects.Move;
        else
            e.DragEffects = DragDropEffects.None;
    }

    private async void OnMonthDayDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not CalendarViewModel vm) return;
        var ev = e.DataTransfer?.TryGetValue(SceneDragFormat);
        if (ev == null) return;

        var target = FindMonthDay(e.Source as Visual);
        if (target == null) return;

        await vm.RescheduleSceneAsync(ev.ChapterGuid, ev.SceneId, target.Day);
    }

    private static CalendarMonthDay? FindMonthDay(Visual? source)
    {
        var current = source;
        while (current != null)
        {
            if (current is Control c && c.Tag is CalendarMonthDay d)
                return d;
            current = current.GetVisualParent();
        }
        return null;
    }
}
