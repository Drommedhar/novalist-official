using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Novalist.Desktop.Views;

public partial class RelationshipsGraphView : UserControl
{
    public RelationshipsGraphView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
