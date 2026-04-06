namespace Novalist.Sdk.Models;

/// <summary>
/// Describes a context menu item to add to explorer or entity context menus.
/// </summary>
public sealed class ContextMenuItem
{
    /// <summary>Menu item label.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Icon emoji.</summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>Optional SVG path geometry data for a vector icon.</summary>
    public string? IconPath { get; init; }

    /// <summary>
    /// Context where this item appears:
    /// "Chapter", "Scene", "Character", "Location", "Item", "Lore", "Editor"
    /// </summary>
    public string Context { get; init; } = string.Empty;

    /// <summary>Click handler. Receives the context object (ChapterData, SceneData, etc.).</summary>
    public Action<object?>? OnClick { get; init; }

    /// <summary>Optional condition function controlling visibility.</summary>
    public Func<object?, bool>? IsVisible { get; init; }
}
