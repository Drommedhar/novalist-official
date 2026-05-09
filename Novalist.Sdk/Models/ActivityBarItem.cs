namespace Novalist.Sdk.Models;

/// <summary>
/// Describes a button to add to the application activity bar (left icon column).
/// </summary>
public sealed class ActivityBarItem
{
    /// <summary>Button label (used as tooltip).</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Tooltip text (falls back to Label if empty).</summary>
    public string Tooltip { get; init; } = string.Empty;

    /// <summary>Optional SVG path geometry data for a vector icon.</summary>
    public string? IconPath { get; init; }

    /// <summary>Emoji or text fallback icon when IconPath is not set.</summary>
    public string Icon { get; init; } = "🧩";

    /// <summary>Click handler.</summary>
    public Action? OnClick { get; init; }
}
