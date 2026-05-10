using Avalonia.Controls;
using Avalonia.Interactivity;
using Novalist.Desktop.ViewModels;

namespace Novalist.Desktop.Views;

public partial class PlotGridView : UserControl
{
    public PlotGridView()
    {
        InitializeComponent();
    }

    private PlotGridViewModel? Vm => DataContext as PlotGridViewModel;

    private void OnRenamePlotlineClick(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        if (sender is MenuItem mi && mi.Tag is PlotGridRow row)
            Vm.RenamePlotlineCommand.Execute(row);
    }

    private void OnDeletePlotlineClick(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        if (sender is MenuItem mi && mi.Tag is PlotGridRow row)
            Vm.DeletePlotlineCommand.Execute(row);
    }
}
