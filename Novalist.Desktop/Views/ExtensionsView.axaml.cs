using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Novalist.Desktop.Services;
using Novalist.Desktop.ViewModels;

namespace Novalist.Desktop.Views;

public partial class ExtensionsView : UserControl
{
    private AvaloniaWebView.WebView? _readmeWebView;

    public ExtensionsView()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ExtensionsViewModel vm && vm.Store is not null)
        {
            vm.Store.PropertyChanged += OnStorePropertyChanged;
        }
    }

    private void OnStorePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExtensionStoreViewModel.DetailHtml))
        {
            var store = sender as ExtensionStoreViewModel;
            var html = store?.DetailHtml;
            if (string.IsNullOrEmpty(html))
                return;

            EnsureReadmeWebView();
            if (_readmeWebView != null)
                _readmeWebView.HtmlContent = html;
        }
        else if (e.PropertyName == nameof(ExtensionStoreViewModel.IsDetailVisible))
        {
            var store = sender as ExtensionStoreViewModel;
            if (store is { IsDetailVisible: false } && _readmeWebView != null)
            {
                _readmeWebView.HtmlContent = "<html><body></body></html>";
            }
        }
    }

    private void EnsureReadmeWebView()
    {
        if (_readmeWebView != null)
            return;

        try
        {
            _readmeWebView = new AvaloniaWebView.WebView
            {
                MinHeight = 200,
            };
            ReadmeWebViewHost.Children.Add(_readmeWebView);
        }
        catch (Exception ex)
        {
            _readmeWebView = null;
            Console.Error.WriteLine($"[ExtensionsView] WebView init failed: {ex.Message}");
        }
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

    // ── Store card interactions ──

    private void OnStoreCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: StoreExtensionItemViewModel item } &&
            DataContext is ExtensionsViewModel { Store: { } store })
        {
            e.Handled = true;
            _ = store.ShowDetailAsync(item);
        }
    }

    private void OnInstallClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: StoreExtensionItemViewModel item } &&
            DataContext is ExtensionsViewModel { Store: { } store })
        {
            e.Handled = true;
            _ = store.InstallAsync(item);
        }
    }

    private void OnDetailInstallClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ExtensionsViewModel { Store: { SelectedItem: { } item } store })
        {
            _ = store.InstallAsync(item);
        }
    }

    private void OnDetailUninstallClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ExtensionsViewModel { Store: { SelectedItem: { } item } store })
        {
            _ = store.UninstallAsync(item);
        }
    }

    private void OnDetailUpdateClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ExtensionsViewModel { Store: { SelectedItem: { } item } store })
        {
            _ = store.UpdateAsync(item);
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
