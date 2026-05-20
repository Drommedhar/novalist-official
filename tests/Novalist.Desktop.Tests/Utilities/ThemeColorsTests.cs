using Avalonia;
using Avalonia.Media;
using Novalist.Desktop.Utilities;
using Xunit;

namespace Novalist.Desktop.Tests.Utilities;

[Collection("Avalonia")]
public class ThemeColorsTests
{
    [AvaloniaFact]
    public void Format_ProducesHex()
        => Assert.Equal("#FF8800", ThemeColors.Format(Color.FromRgb(0xFF, 0x88, 0x00)));

    [AvaloniaFact]
    public void Resolve_KnownBrush_ReturnsHex()
    {
        Application.Current!.Resources["TC_Test"] = new SolidColorBrush(Color.FromRgb(0x12, 0x34, 0x56));
        try
        {
            Assert.Equal("#123456", ThemeColors.Resolve("TC_Test", "#000000"));
        }
        finally { Application.Current.Resources.Remove("TC_Test"); }
    }

    [AvaloniaFact]
    public void Resolve_MissingBrush_ReturnsFallback()
        => Assert.Equal("#ABCDEF", ThemeColors.Resolve("NoSuchBrushKey", "#ABCDEF"));
}
