namespace Novalist.Sdk.Models;

/// <summary>
/// Describes an item to display in the application status bar.
/// </summary>
public sealed class StatusBarItem
{
    /// <summary>Unique item ID.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>"Left", "Center", or "Right" alignment in the status bar.</summary>
    public string Alignment { get; init; } = "Right";

    /// <summary>Priority within the alignment group (lower = further left).</summary>
    public int Order { get; init; } = 100;

    /// <summary>Function returning current display text.</summary>
    public Func<string> GetText { get; init; } = () => string.Empty;

    /// <summary>Tooltip text function.</summary>
    public Func<string>? GetTooltip { get; init; }

    /// <summary>Click handler (optional).</summary>
    public Action? OnClick { get; init; }

    /// <summary>
    /// Called by the host when the status bar refreshes.
    /// Extension should update its internal state here.
    /// </summary>
    public Action? OnRefresh { get; init; }
}
