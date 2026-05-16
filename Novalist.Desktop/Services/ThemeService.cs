using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Platform;
using Novalist.Desktop.Utilities;
using Novalist.Sdk.Models;

namespace Novalist.Desktop.Services;

/// <summary>
/// Manages runtime theme switching by replacing the Novalist theme resource dictionary.
/// </summary>
public sealed class ThemeService
{
    private IResourceProvider? _originalInclude;
    private ResourceDictionary? _activeOverride;
    private ResourceDictionary? _accentOverride;
    private readonly List<ThemeInfo> _availableThemes = [];
    private readonly HashSet<string> _builtInFileNames = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ThemeInfo> AvailableThemes => _availableThemes;
    public string ActiveThemeName { get; private set; } = "Default";

    /// <summary>
    /// Fires whenever the active theme dictionary changes — either a full
    /// <see cref="ApplyTheme"/> swap or an <see cref="ApplyAccentColor"/>
    /// override. WebView-bridged views (EditorView, ManuscriptView, MapView)
    /// subscribe to this to push the new palette into their embedded HTML.
    /// </summary>
    public event Action? ThemeChanged;

    private void RaiseThemeChanged()
    {
        try { ThemeChanged?.Invoke(); }
        catch (Exception ex) { Log.Error("ThemeChanged handler threw", ex); }
    }

    public ThemeService()
    {
        _availableThemes.Add(new ThemeInfo("Default", null,
            new ThemeOverride { Name = "Default", AccentColor = "#007ACC" }));
    }

    /// <summary>
    /// Registers a built-in theme from an embedded avares:// resource path.
    /// </summary>
    public void RegisterBuiltInTheme(string name, string avaresPath, string? defaultAccentColor = null)
    {
        var source = new ThemeOverride { Name = name, AccentColor = defaultAccentColor };
        // Read the XAML content at registration time
        string? xamlContent = null;
        try
        {
            using var stream = AssetLoader.Open(new Uri(avaresPath));
            using var reader = new StreamReader(stream);
            xamlContent = reader.ReadToEnd();
        }
        catch
        {
            // Fallback when AssetLoader cannot resolve the avares:// URI
            // (e.g. theme included as Content rather than AvaloniaResource).
            // Use AppContext.BaseDirectory — Assembly.Location is empty under
            // single-file publish, so it cannot be relied on.
            var asmDir = AppContext.BaseDirectory;
            var relPath = avaresPath.Replace("avares://Novalist.Desktop/", "").Replace('/', Path.DirectorySeparatorChar);
            var filePath = !string.IsNullOrEmpty(asmDir) ? Path.Combine(asmDir, relPath) : null;
            if (filePath != null && File.Exists(filePath))
                xamlContent = File.ReadAllText(filePath);
        }
        _availableThemes.Add(new ThemeInfo(name, null, source) { AvaresPath = avaresPath, CachedXaml = xamlContent });
        _builtInFileNames.Add(Path.GetFileNameWithoutExtension(avaresPath));
    }

    /// <summary>
    /// Scans a folder for user-supplied theme .axaml files and registers each as
    /// an available theme. Skips core resource dictionaries (NovalistTheme,
    /// DesignTokens) and any file already registered as a built-in theme.
    /// Theme display name is the filename without extension and without a
    /// trailing "Theme" suffix.
    /// </summary>
    public void RegisterFolderThemes(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            return;

        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "NovalistTheme", "DesignTokens"
        };

        foreach (var file in Directory.EnumerateFiles(folderPath, "*.axaml", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (reserved.Contains(fileName)) continue;
            if (_builtInFileNames.Contains(fileName)) continue;

            var name = fileName.EndsWith("Theme", StringComparison.OrdinalIgnoreCase) && fileName.Length > 5
                ? fileName[..^5]
                : fileName;

            if (_availableThemes.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                continue;

            _availableThemes.Add(new ThemeInfo(name, file, new ThemeOverride { Name = name }));
        }
    }

    /// <summary>
    /// Registers extension-contributed themes. Call after extensions are loaded.
    /// </summary>
    public void RegisterExtensionThemes(IReadOnlyList<ThemeOverride> overrides, ExtensionManager manager)
    {
        foreach (var themeOverride in overrides)
        {
            if (string.IsNullOrWhiteSpace(themeOverride.ResourcePath))
                continue;

            // Find the extension folder that owns this theme
            var ownerExt = manager.Extensions
                .FirstOrDefault(e => e.Instance is Novalist.Sdk.Hooks.IThemeContributor tc &&
                    tc.GetThemeOverrides().Any(t => t.Name == themeOverride.Name));

            if (ownerExt == null) continue;

            var fullPath = Path.Combine(ownerExt.FolderPath, themeOverride.ResourcePath);
            if (!File.Exists(fullPath)) continue;

            _availableThemes.Add(new ThemeInfo(themeOverride.Name, fullPath, themeOverride));
        }
    }

    /// <summary>
    /// Gets the default accent color for the currently active theme, or null.
    /// </summary>
    public string? GetActiveThemeDefaultAccentColor()
    {
        var theme = _availableThemes.FirstOrDefault(t => t.Name == ActiveThemeName);
        return theme?.Source?.AccentColor;
    }

    /// <summary>
    /// Applies a theme by name. Pass "Default" to restore the built-in theme.
    /// </summary>
    public void ApplyTheme(string themeName)
    {
        var app = Application.Current;
        if (app == null) return;

        var mergedDicts = app.Resources.MergedDictionaries;

        // Debug: log what's in MergedDictionaries
        Log.Debug($"[ThemeService] ApplyTheme({themeName}), MergedDictionaries count: {mergedDicts.Count}");
        foreach (var d in mergedDicts)
            Log.Debug($"[ThemeService]   Entry: {d.GetType().FullName}, IsResourceInclude={d is ResourceInclude}, Source={(d as ResourceInclude)?.Source}");

        // Lazily capture the original theme dictionary from App.axaml on first call
        if (_originalInclude == null)
        {
            // Try ResourceInclude first
            _originalInclude = mergedDicts.OfType<ResourceInclude>()
                .FirstOrDefault(r => r.Source?.ToString().Contains("NovalistTheme") == true);
            // If not found (Avalonia 12 may compile it as a plain ResourceDictionary),
            // look for the first non-accent ResourceDictionary that contains our theme keys
            _originalInclude ??= mergedDicts.OfType<ResourceDictionary>()
                .FirstOrDefault(d => d != _activeOverride && d != _accentOverride
                    && d.ContainsKey("RibbonBackground"));
        }
        Log.Debug($"[ThemeService] _originalInclude is {(_originalInclude == null ? "NULL" : "found (" + _originalInclude.GetType().Name + ")")}");


        // Remove previous override
        if (_activeOverride != null)
        {
            mergedDicts.Remove(_activeOverride);
            _activeOverride = null;
        }

        // Remove accent override — will be re-applied after theme switch if needed
        if (_accentOverride != null)
        {
            mergedDicts.Remove(_accentOverride);
            _accentOverride = null;
        }

        if (themeName == "Default" || string.IsNullOrEmpty(themeName))
        {
            // Restore the original ResourceInclude
            if (_originalInclude != null && !mergedDicts.Contains(_originalInclude))
                mergedDicts.Add(_originalInclude);
            ActiveThemeName = "Default";
            RaiseThemeChanged();
            return;
        }

        var themeInfo = _availableThemes.FirstOrDefault(t => t.Name == themeName);
        if (themeInfo == null) return;

        // Built-in theme with avares:// path
        if (themeInfo.AvaresPath != null)
        {
            try
            {
                var xaml = themeInfo.CachedXaml
                    ?? throw new InvalidOperationException($"No cached XAML for built-in theme '{themeName}'");
                var overrideDict = (ResourceDictionary)AvaloniaRuntimeXamlLoader.Parse(xaml);
                if (_originalInclude != null)
                    mergedDicts.Remove(_originalInclude);
                mergedDicts.Add(overrideDict);
                _activeOverride = overrideDict;
                ActiveThemeName = themeName;
                Log.Debug($"[ThemeService] Successfully applied built-in theme '{themeName}', overrideDict has {overrideDict.Count} entries");
            }
            catch (Exception ex)
            {
                Log.Error($"[ThemeService] Failed to load built-in theme '{themeName}'", ex);
                if (_originalInclude != null && !mergedDicts.Contains(_originalInclude))
                    mergedDicts.Add(_originalInclude);
                ActiveThemeName = "Default";
            }
            RaiseThemeChanged();
            return;
        }

        if (themeInfo.FilePath == null) return;

        // Load the override dictionary from the AXAML file
        try
        {
            var xaml = File.ReadAllText(themeInfo.FilePath);
            var overrideDict = (ResourceDictionary)AvaloniaRuntimeXamlLoader.Parse(xaml);

            // Remove the original theme, add override (which has the same keys)
            if (_originalInclude != null)
                mergedDicts.Remove(_originalInclude);
            mergedDicts.Add(overrideDict);
            _activeOverride = overrideDict;
            ActiveThemeName = themeName;
        }
        catch
        {
            // On failure, restore original
            if (_originalInclude != null && !mergedDicts.Contains(_originalInclude))
                mergedDicts.Add(_originalInclude);
            ActiveThemeName = "Default";
        }
        RaiseThemeChanged();
    }

    /// <summary>
    /// Applies a user-chosen accent color override on top of the current theme.
    /// Pass null to clear the override (revert to theme default).
    /// </summary>
    public void ApplyAccentColor(string? hexColor)
    {
        var app = Application.Current;
        if (app == null) return;

        var mergedDicts = app.Resources.MergedDictionaries;

        // Remove previous accent override
        if (_accentOverride != null)
        {
            mergedDicts.Remove(_accentOverride);
            _accentOverride = null;
        }

        if (string.IsNullOrWhiteSpace(hexColor))
            return;

        if (!Color.TryParse(hexColor, out var color))
            return;

        // Compute a lighter hover variant
        var hoverColor = LightenColor(color, 0.12);

        var dict = new ResourceDictionary
        {
            { "AccentBrush", new SolidColorBrush(color) },
            { "AccentBrushHover", new SolidColorBrush(hoverColor) },
            { "StatusBarBackground", new SolidColorBrush(color) },
        };

        mergedDicts.Add(dict);
        _accentOverride = dict;
        RaiseThemeChanged();
    }

    private static Color LightenColor(Color c, double amount)
    {
        var r = (byte)Math.Min(255, c.R + (255 - c.R) * amount);
        var g = (byte)Math.Min(255, c.G + (255 - c.G) * amount);
        var b = (byte)Math.Min(255, c.B + (255 - c.B) * amount);
        return Color.FromArgb(c.A, r, g, b);
    }
}

public sealed class ThemeInfo
{
    public ThemeInfo(string name, string? filePath, ThemeOverride? source)
    {
        Name = name;
        FilePath = filePath;
        Source = source;
    }

    public string Name { get; }
    public string? FilePath { get; }
    public ThemeOverride? Source { get; }

    /// <summary>For built-in themes: avares:// URI to the .axaml resource.</summary>
    public string? AvaresPath { get; init; }

    /// <summary>Cached XAML content for built-in themes.</summary>
    public string? CachedXaml { get; init; }

    public override string ToString() => Name;
}
