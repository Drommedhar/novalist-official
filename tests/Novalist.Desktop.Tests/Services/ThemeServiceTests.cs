using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using NSubstitute;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Novalist.Desktop.Services;
using Novalist.Sdk;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;
using Novalist.Sdk.Services;
using Xunit;

namespace Novalist.Desktop.Tests.Services;

[Collection("Avalonia")]
public class ThemeServiceTests
{
    private const string MinimalThemeXaml =
        """
        <ResourceDictionary xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <SolidColorBrush x:Key="RibbonBackground" Color="#222222"/>
        </ResourceDictionary>
        """;

    [AvaloniaFact]
    public void Ctor_HasDefaultTheme()
    {
        var sut = new ThemeService();
        Assert.Contains(sut.AvailableThemes, t => t.Name == "Default");
        Assert.Equal("Default", sut.ActiveThemeName);
        Assert.Equal("#007ACC", sut.GetActiveThemeDefaultAccentColor());
    }

    [AvaloniaFact]
    public void RegisterFolderThemes_AddsNonReserved_StripsThemeSuffix()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "Custom.axaml"), MinimalThemeXaml);
        File.WriteAllText(Path.Combine(dir.Path, "DiscordTheme.axaml"), MinimalThemeXaml); // -> "Discord"
        File.WriteAllText(Path.Combine(dir.Path, "NovalistTheme.axaml"), MinimalThemeXaml); // reserved -> skip
        File.WriteAllText(Path.Combine(dir.Path, "DesignTokens.axaml"), MinimalThemeXaml);  // reserved -> skip

        var sut = new ThemeService();
        sut.RegisterFolderThemes(dir.Path);

        Assert.Contains(sut.AvailableThemes, t => t.Name == "Custom");
        Assert.Contains(sut.AvailableThemes, t => t.Name == "Discord");
        Assert.DoesNotContain(sut.AvailableThemes, t => t.Name == "NovalistTheme");
    }

    [AvaloniaFact]
    public void RegisterFolderThemes_DuplicateName_Skipped()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "Custom.axaml"), MinimalThemeXaml);
        var sut = new ThemeService();
        sut.RegisterFolderThemes(dir.Path);
        sut.RegisterFolderThemes(dir.Path); // second pass -> duplicate, skipped
        Assert.Single(sut.AvailableThemes, t => t.Name == "Custom");
    }

    [AvaloniaFact]
    public void RegisterFolderThemes_MissingDir_NoOp()
    {
        var sut = new ThemeService();
        var before = sut.AvailableThemes.Count;
        sut.RegisterFolderThemes(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        Assert.Equal(before, sut.AvailableThemes.Count);
    }

    [AvaloniaFact]
    public void RegisterBuiltInTheme_AddsTheme()
    {
        var sut = new ThemeService();
        sut.RegisterBuiltInTheme("MyBuiltIn", "avares://Novalist.Desktop/Assets/Themes/Nope.axaml", "#FF0000");
        Assert.Contains(sut.AvailableThemes, t => t.Name == "MyBuiltIn");
    }

    [AvaloniaFact]
    public void ApplyTheme_Default_RaisesChanged()
    {
        var sut = new ThemeService();
        var raised = false;
        sut.ThemeChanged += () => raised = true;
        sut.ApplyTheme("Default");
        Assert.Equal("Default", sut.ActiveThemeName);
        Assert.True(raised);
    }

    [AvaloniaFact]
    public void ApplyTheme_UnknownName_NoOp()
    {
        var sut = new ThemeService();
        sut.ApplyTheme("DoesNotExist"); // themeInfo null -> returns
        Assert.Equal("Default", sut.ActiveThemeName);
    }

    [AvaloniaFact]
    public void ApplyTheme_FileTheme_AppliesThenRestores()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "Custom.axaml"), MinimalThemeXaml);
        var sut = new ThemeService();
        sut.RegisterFolderThemes(dir.Path);

        sut.ApplyTheme("Custom");
        Assert.Equal("Custom", sut.ActiveThemeName);

        sut.ApplyTheme("Default"); // removes the override
        Assert.Equal("Default", sut.ActiveThemeName);
    }

    [AvaloniaFact]
    public void ApplyTheme_CorruptFile_FallsBackToDefault()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "Broken.axaml"), "<not valid xaml");
        var sut = new ThemeService();
        sut.RegisterFolderThemes(dir.Path);
        sut.ApplyTheme("Broken"); // parse throws -> catch -> Default
        Assert.Equal("Default", sut.ActiveThemeName);
    }

    [AvaloniaFact]
    public void ApplyAccentColor_ValidClearsInvalid()
    {
        var sut = new ThemeService();
        var raised = 0;
        sut.ThemeChanged += () => raised++;

        sut.ApplyAccentColor("#3498db");  // valid -> adds override + raises
        sut.ApplyAccentColor("#e74c3c");  // replaces previous
        sut.ApplyAccentColor(null);       // clears, returns (no raise)
        sut.ApplyAccentColor("not-a-color"); // invalid -> returns
        Assert.Equal(2, raised);
    }

    [AvaloniaFact]
    public void RaiseThemeChanged_SwallowsHandlerExceptions()
    {
        var sut = new ThemeService();
        sut.ThemeChanged += () => throw new InvalidOperationException("boom");
        // Must not propagate.
        sut.ApplyTheme("Default");
    }

    [AvaloniaFact]
    public void ApplyTheme_RemovesAccentOverrideFirst()
    {
        var sut = new ThemeService();
        sut.ApplyAccentColor("#3498db"); // sets _accentOverride
        sut.ApplyTheme("Default");        // must remove the accent override
        Assert.Equal("Default", sut.ActiveThemeName);
    }

    [AvaloniaFact]
    public void BuiltInTheme_FromFallbackFile_AppliesCachedXaml()
    {
        // RegisterBuiltInTheme falls back to AppContext.BaseDirectory when the
        // avares URI can't be resolved; drop a real theme file there.
        var rel = Path.Combine("Assets", "Themes", "TBI.axaml");
        var full = Path.Combine(AppContext.BaseDirectory, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, MinimalThemeXaml);
        try
        {
            var sut = new ThemeService();
            sut.RegisterBuiltInTheme("TBI", "avares://Novalist.Desktop/Assets/Themes/TBI.axaml");
            sut.ApplyTheme("TBI"); // parses CachedXaml -> success path
            Assert.Equal("TBI", sut.ActiveThemeName);
        }
        finally { File.Delete(full); }
    }

    [AvaloniaFact]
    public void BuiltInTheme_NoCachedXaml_FailsToDefault()
    {
        var sut = new ThemeService();
        // avares fails, no fallback file -> CachedXaml null -> ApplyTheme throws -> caught -> Default.
        sut.RegisterBuiltInTheme("Missing", "avares://Novalist.Desktop/Assets/Themes/DoesNotExistAnywhere.axaml");
        sut.ApplyTheme("Missing");
        Assert.Equal("Default", sut.ActiveThemeName);
    }

    [AvaloniaFact]
    public void RegisterBuiltInTheme_FromAvaresResource_ReadsContent()
    {
        // splash.png is a real AvaloniaResource in Novalist.Desktop, so the
        // avares AssetLoader.Open path succeeds (content is irrelevant here).
        var sut = new ThemeService();
        sut.RegisterBuiltInTheme("PngTheme", "avares://Novalist.Desktop/splash.png");
        Assert.Contains(sut.AvailableThemes, t => t.Name == "PngTheme");
    }

    [AvaloniaFact]
    public void ThemeInfo_ToString_IsName()
        => Assert.Equal("Default", new ThemeService().AvailableThemes.First(t => t.Name == "Default").ToString());

    // Runs an action with a fake NovalistTheme dictionary present so the
    // _originalInclude capture/restore branches are reachable.
    private static void WithOriginalInclude(Action<ThemeService> body)
    {
        var themeDict = new ResourceDictionary { { "RibbonBackground", new SolidColorBrush(Colors.Black) } };
        var merged = Application.Current!.Resources.MergedDictionaries;
        merged.Add(themeDict);
        try { body(new ThemeService()); }
        finally { merged.Remove(themeDict); }
    }

    [AvaloniaFact]
    public void ApplyTheme_OriginalInclude_RestoredOnDefault()
        => WithOriginalInclude(sut =>
        {
            using var dir = new TempDir();
            File.WriteAllText(Path.Combine(dir.Path, "Custom.axaml"), MinimalThemeXaml);
            sut.RegisterFolderThemes(dir.Path);
            sut.ApplyTheme("Custom");  // captures + removes original
            sut.ApplyTheme("Default"); // original absent -> re-added (line 195)
            Assert.Equal("Default", sut.ActiveThemeName);
        });

    [AvaloniaFact]
    public void ApplyTheme_OriginalInclude_RestoredOnFileParseFailure()
        => WithOriginalInclude(sut =>
        {
            using var dir = new TempDir();
            File.WriteAllText(Path.Combine(dir.Path, "Custom.axaml"), MinimalThemeXaml);
            File.WriteAllText(Path.Combine(dir.Path, "Broken.axaml"), "<bad xaml");
            sut.RegisterFolderThemes(dir.Path);
            sut.ApplyTheme("Custom");  // removes original
            sut.ApplyTheme("Broken");  // parse fails, original absent -> re-added (line 249)
            Assert.Equal("Default", sut.ActiveThemeName);
        });

    [AvaloniaFact]
    public void ApplyTheme_OriginalInclude_BuiltInSuccessAndFailure()
        => WithOriginalInclude(sut =>
        {
            var rel = Path.Combine(AppContext.BaseDirectory, "Assets", "Themes", "TBI2.axaml");
            Directory.CreateDirectory(Path.GetDirectoryName(rel)!);
            File.WriteAllText(rel, MinimalThemeXaml);
            try
            {
                sut.RegisterBuiltInTheme("TBI2", "avares://Novalist.Desktop/Assets/Themes/TBI2.axaml");
                sut.RegisterBuiltInTheme("NoXaml", "avares://Novalist.Desktop/Assets/Themes/None.axaml");
                sut.ApplyTheme("TBI2");   // built-in success with original present (remove original, line 213)
                sut.ApplyTheme("NoXaml"); // built-in fail -> restore original (line 223)
                Assert.Equal("Default", sut.ActiveThemeName);
            }
            finally { File.Delete(rel); }
        });

    private sealed class FakeThemeExtension : IExtension, IThemeContributor
    {
        private readonly ThemeOverride _ov;
        public FakeThemeExtension(ThemeOverride ov) => _ov = ov;
        public string Id => "fake.theme";
        public string DisplayName => "Fake";
        public string Description => "";
        public string Version => "1.0.0";
        public string Author => "";
        public void Initialize(IHostServices host) { }
        public void Shutdown() { }
        public IReadOnlyList<ThemeOverride> GetThemeOverrides() => new[] { _ov };
    }

    [AvaloniaFact]
    public void RegisterExtensionThemes_AddsThemeFromOwningExtension()
    {
        using var extDir = new TempDir();
        File.WriteAllText(Path.Combine(extDir.Path, "ext-theme.axaml"), MinimalThemeXaml);
        var ov = new ThemeOverride { Name = "ExtTheme", ResourcePath = "ext-theme.axaml" };

        var settings = Substitute.For<ISettingsService>();
        var host = new HostServices(Substitute.For<IFileService>(), Substitute.For<IProjectService>(),
            Substitute.For<IEntityService>(), settings);
        var manager = new ExtensionManager(settings, host);
        manager.Extensions.Add(new ExtensionInfo
        {
            Manifest = new ExtensionManifest { Id = "fake.theme" },
            FolderPath = extDir.Path,
            Instance = new FakeThemeExtension(ov)
        });

        var sut = new ThemeService();
        sut.RegisterExtensionThemes(new[] { ov, new ThemeOverride { Name = "NoPath" } }, manager);

        Assert.Contains(sut.AvailableThemes, t => t.Name == "ExtTheme"); // resolved + file exists
    }
}
