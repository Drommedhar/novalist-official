namespace Novalist.Sdk.Models;

/// <summary>
/// Describes a settings category an extension contributes. The host surfaces it
/// in the Extensions view; the editable form itself comes from
/// <see cref="Hooks.ISettingsSchemaContributor"/>.
/// </summary>
public sealed class SettingsPage
{
    /// <summary>Category label shown in the settings list.</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Optional icon text.</summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>Optional SVG path geometry data for a vector icon.</summary>
    public string? IconPath { get; init; }

    /// <summary>Called when the user leaves the settings page. Persist settings here.</summary>
    public Action? OnSave { get; init; }
}
