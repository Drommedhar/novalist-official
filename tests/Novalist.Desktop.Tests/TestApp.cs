using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Novalist.Desktop.Tests;

// Avalonia.Headless.XUnit runs [AvaloniaFact]/[AvaloniaTheory] on a shared headless UI
// thread, so view/visual construction always happens on the dispatcher-owner thread.
// Tests that don't need the app (pure VM/service logic) stay [Fact].
[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

// The headless session dispatches one test at a time; parallel collections would dispatch
// concurrently and fail with "Cannot get KeyValueStorage on the idle test context".
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Novalist.Desktop.Tests;

/// <summary>
/// Minimal headless Avalonia application for Desktop unit tests. Registers the
/// brush resources converters look up so both the resource-found and fallback
/// paths are exercisable.
/// </summary>
public sealed class TestApp : Application
{
    public override void Initialize()
    {
        // Merge the real DesignTokens so views that bind StaticResource sizes/radii load.
        Resources.MergedDictionaries.Add(new ResourceInclude((Uri?)null)
        {
            Source = new Uri("avares://Novalist.Desktop/Assets/Themes/DesignTokens.axaml")
        });

        Resources["SuccessBrush"] = new SolidColorBrush(Colors.Green);
        Resources["WarningBrush"] = new SolidColorBrush(Colors.Orange);
        Resources["DangerBrush"] = new SolidColorBrush(Colors.Red);
        Resources["AccentBrush"] = new SolidColorBrush(Colors.Blue);
        Resources["ListBoxItemSelectedBackground"] = new SolidColorBrush(Colors.SteelBlue);
        // Brushes the snapshot-compare diff view looks up via FindResource.
        Resources["SubtleText"] = new SolidColorBrush(Colors.Gray);
        Resources["NormalText"] = new SolidColorBrush(Colors.Black);
        Resources["ExplorerBorder"] = new SolidColorBrush(Colors.DimGray);
        Resources["CardBackground"] = new SolidColorBrush(Colors.DarkSlateGray);
    }
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
