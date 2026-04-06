using Avalonia.Controls;

namespace Novalist.Sdk.Models;

/// <summary>
/// Describes a settings page to add to the Settings view.
/// </summary>
public sealed class SettingsPage
{
    /// <summary>Category label shown in the settings sidebar.</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Icon emoji/text.</summary>
    public string Icon { get; init; } = "🧩";

    /// <summary>Optional SVG path geometry data for a vector icon.</summary>
    public string? IconPath { get; init; }

    /// <summary>Factory that creates the settings panel Control.</summary>
    public Func<Control> CreateView { get; init; } = null!;

    /// <summary>Called when the user leaves the settings page. Persist settings here.</summary>
    public Action? OnSave { get; init; }
}
