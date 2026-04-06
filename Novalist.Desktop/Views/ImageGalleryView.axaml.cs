using Avalonia.Controls;
using Avalonia.Input;
using Novalist.Desktop.ViewModels;

namespace Novalist.Desktop.Views;

public partial class ImageGalleryView : UserControl
{
    public ImageGalleryView()
    {
        InitializeComponent();
    }

    private void OnCardTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: ImageGalleryItem item }
            && DataContext is ImageGalleryViewModel vm)
        {
            vm.PreviewImagePath = item.RelativePath;
            vm.PreviewImageName = item.Name;
            vm.IsPreviewOpen = true;
        }
    }

    private void OnPreviewOverlayPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ImageGalleryViewModel vm)
        {
            vm.IsPreviewOpen = false;
            e.Handled = true;
        }
    }

    private void OnPreviewContentPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }
}
