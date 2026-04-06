using Avalonia.Controls;

namespace Novalist.Desktop;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    public void SetStatus(string text)
    {
        if (this.FindControl<TextBlock>("StatusText") is { } tb)
            tb.Text = text;
    }
}
