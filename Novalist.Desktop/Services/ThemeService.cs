using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Novalist.Sdk.Models;

namespace Novalist.Desktop.Services;

/// <summary>
/// Manages runtime theme switching by replacing the Novalist theme resource dictionary.
/// </summary>
public sealed class ThemeService
{
    private IResourceProvider? _originalInclude;
    private ResourceDictionary? _activeOverride;
    private readonly List<ThemeInfo> _availableThemes = [];

    public IReadOnlyList<ThemeInfo> AvailableThemes => _availableThemes;
    public string ActiveThemeName { get; private set; } = "Default";

    public ThemeService()
    {
        _availableThemes.Add(new ThemeInfo("Default", null, null));
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
    /// Applies a theme by name. Pass "Default" to restore the built-in theme.
    /// </summary>
    public void ApplyTheme(string themeName)
    {
        var app = Application.Current;
        if (app == null) return;

        var mergedDicts = app.Resources.MergedDictionaries;

        // Lazily capture the original ResourceInclude from App.axaml on first call
        _originalInclude ??= mergedDicts.OfType<ResourceInclude>()
            .FirstOrDefault(r => r.Source?.ToString().Contains("NovalistTheme") == true);

        // Remove previous override
        if (_activeOverride != null)
        {
            mergedDicts.Remove(_activeOverride);
            _activeOverride = null;
        }

        if (themeName == "Default" || string.IsNullOrEmpty(themeName))
        {
            // Restore the original ResourceInclude
            if (_originalInclude != null && !mergedDicts.Contains(_originalInclude))
                mergedDicts.Add(_originalInclude);
            ActiveThemeName = "Default";
            return;
        }

        var themeInfo = _availableThemes.FirstOrDefault(t => t.Name == themeName);
        if (themeInfo?.FilePath == null) return;

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

    public override string ToString() => Name;
}
