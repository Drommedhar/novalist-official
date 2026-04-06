using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Novalist.Desktop.ViewModels;

namespace Novalist.Desktop.Views;

public partial class ExtensionsView : UserControl
{
    public ExtensionsView()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();
    }

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        Close();
    }

    private void OnPanelPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnExtensionSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string settingsKey)
        {
            var mainVm = Avalonia.VisualTree.VisualExtensions
                .FindAncestorOfType<Window>(this)?.DataContext as MainWindowViewModel;
            if (mainVm != null)
            {
                mainVm.IsExtensionsOpen = false;
                mainVm.OpenSettingsToCategory(settingsKey);
            }
        }
    }

    private void Close()
    {
        var mainVm = (this.Parent as Control)?.DataContext as MainWindowViewModel
                  ?? Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Window>(this)?.DataContext as MainWindowViewModel;
        if (mainVm != null)
            mainVm.IsExtensionsOpen = false;
    }
}
