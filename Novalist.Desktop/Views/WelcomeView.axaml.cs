using Avalonia.Controls;
using Avalonia.Interactivity;
using Novalist.Desktop.ViewModels;

namespace Novalist.Desktop.Views;

public partial class WelcomeView : UserControl
{
    public WelcomeView()
    {
        InitializeComponent();
    }

    private void OnRemoveRecentClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item) return;
        if (item.Tag is not RecentProjectCard card) return;
        if (DataContext is not WelcomeViewModel vm) return;
        vm.RemoveRecentProjectCommand.Execute(card);
    }
}
