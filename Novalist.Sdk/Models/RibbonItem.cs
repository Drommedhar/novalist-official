namespace Novalist.Sdk.Models;

/// <summary>
/// Describes a button to add to the application ribbon bar.
/// </summary>
public sealed class RibbonItem
{
    /// <summary>Target ribbon tab: "Edit", "View", or "Extensions" (new tab).</summary>
    public string Tab { get; init; } = "Extensions";

    /// <summary>Group label within the tab.</summary>
    public string Group { get; init; } = string.Empty;

    /// <summary>Button label text.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Emoji or icon text for the button.</summary>
    public string Icon { get; init; } = "🧩";

    /// <summary>Optional SVG path geometry data for a vector icon. When set, rendered as a stroked Path instead of the emoji Icon.</summary>
    public string? IconPath { get; init; }

    /// <summary>Tooltip text.</summary>
    public string Tooltip { get; init; } = string.Empty;

    /// <summary>Whether this is a toggle button (has active state).</summary>
    public bool IsToggle { get; init; }

    /// <summary>For toggle buttons: function returning current active state.</summary>
    public Func<bool>? IsActive { get; init; }

    /// <summary>Click handler.</summary>
    public Action? OnClick { get; init; }

    /// <summary>Button size: "Small" or "Large".</summary>
    public string Size { get; init; } = "Large";
}
