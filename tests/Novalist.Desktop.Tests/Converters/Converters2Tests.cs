using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Novalist.Desktop.Converters;
using Novalist.Desktop.Services;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.Converters;

[Collection("Avalonia")]
public class Converters2Tests
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    // ── BoolToExpanderIconConverter ──

    [AvaloniaFact]
    public void ExpanderIcon_TrueFalse()
    {
        var sut = new BoolToExpanderIconConverter();
        Assert.Equal("▾", sut.Convert(true, typeof(string), null, Ci));
        Assert.Equal("▸", sut.Convert(false, typeof(string), null, Ci));
    }

    [AvaloniaFact]
    public void ExpanderIcon_ConvertBack_Throws()
        => Assert.Throws<NotSupportedException>(() => new BoolToExpanderIconConverter().ConvertBack(null, typeof(bool), null, Ci));

    // ── BoolToSelectedBrushConverter ──

    [AvaloniaFact]
    public void SelectedBrush_False_Transparent()
        => Assert.Same(Brushes.Transparent, new BoolToSelectedBrushConverter().Convert(false, typeof(IBrush), null, Ci));

    [AvaloniaFact]
    public void SelectedBrush_True_ResourceMissing_FallbackColor()
    {
        // TreeItemSelected is not registered -> fallback SolidColorBrush.
        var brush = Assert.IsType<SolidColorBrush>(new BoolToSelectedBrushConverter().Convert(true, typeof(IBrush), null, Ci));
        Assert.Equal(Color.Parse("#45475A"), brush.Color);
    }

    [AvaloniaFact]
    public void SelectedBrush_True_ResourcePresent_UsesResource()
    {
        var expected = new SolidColorBrush(Colors.Magenta);
        Application.Current!.Resources["TreeItemSelected"] = expected;
        try
        {
            Assert.Same(expected, new BoolToSelectedBrushConverter().Convert(true, typeof(IBrush), null, Ci));
        }
        finally { Application.Current.Resources.Remove("TreeItemSelected"); }
    }

    [AvaloniaFact]
    public void SelectedBrush_ConvertBack_Throws()
        => Assert.Throws<NotSupportedException>(() => new BoolToSelectedBrushConverter().ConvertBack(null, typeof(bool), null, Ci));

    // ── Store converters ──

    [AvaloniaFact]
    public void TabVisibility()
    {
        var sut = TabVisibilityConverter.Instance;
        Assert.True((bool)sut.Convert(2, typeof(bool), "2", Ci));
        Assert.False((bool)sut.Convert(2, typeof(bool), "3", Ci));
        Assert.False((bool)sut.Convert("x", typeof(bool), "2", Ci)); // wrong types
        Assert.Throws<NotSupportedException>(() => sut.ConvertBack(null, typeof(int), null, Ci));
    }

    [AvaloniaFact]
    public void TabFontWeight()
    {
        var sut = TabFontWeightConverter.Instance;
        Assert.Equal(FontWeight.SemiBold, sut.Convert(1, typeof(FontWeight), "1", Ci));
        Assert.Equal(FontWeight.Normal, sut.Convert(1, typeof(FontWeight), "2", Ci));
        Assert.Equal(FontWeight.Normal, sut.Convert(null, typeof(FontWeight), "1", Ci));
        Assert.Throws<NotSupportedException>(() => sut.ConvertBack(null, typeof(int), null, Ci));
    }

    [AvaloniaFact]
    public void TagsJoin()
    {
        var sut = TagsJoinConverter.Instance;
        Assert.Equal("a, b", sut.Convert(new List<string> { "a", "b" }, typeof(string), null, Ci));
        Assert.Equal(string.Empty, sut.Convert(42, typeof(string), null, Ci));
        Assert.Throws<NotSupportedException>(() => sut.ConvertBack(null, typeof(object), null, Ci));
    }

    // ── Image converters ──

    [AvaloniaFact]
    public void AbsolutePath_NullEmptyMissing_ReturnsNull()
    {
        var sut = AbsolutePathToBitmapConverter.Instance;
        Assert.Null(sut.Convert(null, typeof(Bitmap), null, Ci));
        Assert.Null(sut.Convert("", typeof(Bitmap), null, Ci));
        Assert.Null(sut.Convert(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png"), typeof(Bitmap), null, Ci));
        Assert.Throws<NotSupportedException>(() => sut.ConvertBack(null, typeof(string), null, Ci));
    }

    [AvaloniaFact]
    public void AbsolutePath_ExistingFile_ReturnsBitmap()
    {
        // The headless image loader is lenient and returns a stub bitmap for any
        // existing file, so this exercises the success path.
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        File.WriteAllBytes(path, new byte[] { 0, 1, 2, 3 });
        try
        {
            Assert.NotNull(AbsolutePathToBitmapConverter.Instance.Convert(path, typeof(Bitmap), null, Ci));
        }
        finally { File.Delete(path); }
    }

    // Note: RelativePathToBitmapConverter / AbsolutePathToBitmapConverter are
    // excluded from coverage — they are thin disk->Bitmap adapters whose
    // behaviour depends on the platform image stack (the headless loader is too
    // lenient to exercise the failure branch deterministically) and on the
    // global App.EntityService static.

    // ── Resource-missing fallbacks (mutate global resources) ──

    [AvaloniaFact]
    public void Toast_ResourceMissing_FallsBackToGray()
    {
        var app = Application.Current!;
        var saved = app.Resources["AccentBrush"];
        app.Resources.Remove("AccentBrush");
        try
        {
            Assert.Same(Brushes.Gray, ToastSeverityToBrushConverter.Instance.Convert((ToastSeverity)999, typeof(IBrush), null, Ci));
        }
        finally { app.Resources["AccentBrush"] = saved; }
    }

    [AvaloniaFact]
    public void SceneRow_Selected_ResourceMissing_FallbackBrush()
    {
        var app = Application.Current!;
        var saved = app.Resources["ListBoxItemSelectedBackground"];
        app.Resources.Remove("ListBoxItemSelectedBackground");
        try
        {
            var brush = Assert.IsType<SolidColorBrush>(
                new SceneRowBackgroundConverter().Convert(new object?[] { true, null }, typeof(IBrush), null, Ci));
            Assert.Equal(Color.FromArgb(120, 100, 150, 200), brush.Color);
        }
        finally { app.Resources["ListBoxItemSelectedBackground"] = saved; }
    }
}
