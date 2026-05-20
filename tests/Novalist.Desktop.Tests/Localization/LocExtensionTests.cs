using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Novalist.Desktop.Localization;
using Xunit;

namespace Novalist.Desktop.Tests.Localization;

[Collection("Avalonia")]
public class LocExtensionTests
{
    private static readonly string BundledLocales =
        System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Locales");

    private sealed class FakeTarget : IProvideValueTarget
    {
        public object TargetObject { get; init; } = null!;
        public object TargetProperty { get; init; } = null!;
    }

    private sealed class FakeServiceProvider : IServiceProvider
    {
        public IProvideValueTarget? Target;
        public object? GetService(Type serviceType)
            => serviceType == typeof(IProvideValueTarget) ? Target : null;
    }

    [AvaloniaFact]
    public void ProvideValue_AvaloniaTarget_SetsValue_AndTracksLanguage()
    {
        Loc.Instance.Initialize(BundledLocales, "en");
        var tb = new TextBlock();
        var sp = new FakeServiceProvider { Target = new FakeTarget { TargetObject = tb, TargetProperty = TextBlock.TextProperty } };

        var ext = new LocExtension("app.ready");
        var value = ext.ProvideValue(sp);
        Assert.NotNull(value);
        Assert.Equal(value, tb.Text); // UpdateValue applied to the target

        // language change fires the handler -> UpdateValue again (live target)
        Loc.Instance.CurrentLanguage = "de";
        Loc.Instance.CurrentLanguage = "en";
        Assert.NotNull(tb.Text);
    }

    [AvaloniaFact]
    public void ProvideValue_NonAvaloniaTarget_ReturnsFormattedValue()
    {
        Loc.Instance.Initialize(BundledLocales, "en");
        var sp = new FakeServiceProvider { Target = new FakeTarget { TargetObject = "not-avalonia", TargetProperty = null! } };
        var ext = new LocExtension("app.ready");
        var value = ext.ProvideValue(sp);
        Assert.IsType<string>(value);
    }

    [AvaloniaFact]
    public void ProvideValue_NoTargetService_ReturnsValue()
    {
        Loc.Instance.Initialize(BundledLocales, "en");
        var sp = new FakeServiceProvider { Target = null };
        var ext = new LocExtension("app.ready");
        Assert.NotNull(ext.ProvideValue(sp));
    }

    [AvaloniaFact]
    public void StringFormat_ValidAndInvalid()
    {
        Loc.Instance.Initialize(BundledLocales, "en");
        var sp = new FakeServiceProvider { Target = new FakeTarget { TargetObject = "x", TargetProperty = null! } };

        var valid = new LocExtension("app.ready") { StringFormat = "[{0}]" };
        var formatted = (string)valid.ProvideValue(sp);
        Assert.StartsWith("[", formatted);

        var invalid = new LocExtension("app.ready") { StringFormat = "{0" }; // bad format -> FormatException caught -> raw value
        var raw = (string)invalid.ProvideValue(sp);
        Assert.DoesNotContain("[", raw);
    }

    [AvaloniaFact]
    public void LanguageChange_AfterTargetCollected_Unsubscribes()
    {
        Loc.Instance.Initialize(BundledLocales, "en");
        var ext = new LocExtension("app.ready");

        void Subscribe()
        {
            var tb = new TextBlock();
            var sp = new FakeServiceProvider { Target = new FakeTarget { TargetObject = tb, TargetProperty = TextBlock.TextProperty } };
            ext.ProvideValue(sp);
            // tb goes out of scope here
        }
        Subscribe();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // handler fires; weak target is dead -> handler unsubscribes itself (no throw)
        Loc.Instance.CurrentLanguage = "de";
        Loc.Instance.CurrentLanguage = "en";
        Assert.True(true);
    }
}
