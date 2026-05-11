using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Novalist.Desktop.Views;

public partial class FootnotesPanelView : UserControl
{
    public FootnotesPanelView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
