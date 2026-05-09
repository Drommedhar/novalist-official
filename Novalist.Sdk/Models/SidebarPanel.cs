using Avalonia.Controls;

namespace Novalist.Sdk.Models;

/// <summary>
/// Describes a panel to add to the application sidebar.
/// </summary>
public sealed class SidebarPanel
{
    /// <summary>Unique panel ID.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display label for the sidebar tab.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Icon emoji/text.</summary>
    public string Icon { get; init; } = "🧩";

    /// <summary>Optional SVG path geometry data for a vector icon.</summary>
    public string? IconPath { get; init; }

    /// <summary>"Left", "Right", or "Context" sidebar placement. "Context" integrates as a tab inside the context sidebar.</summary>
    public string Side { get; init; } = "Right";

    /// <summary>Factory that creates the Avalonia Control for this panel.</summary>
    public Func<Control> CreateView { get; init; } = null!;

    /// <summary>Tooltip text.</summary>
    public string Tooltip { get; init; } = string.Empty;
}
