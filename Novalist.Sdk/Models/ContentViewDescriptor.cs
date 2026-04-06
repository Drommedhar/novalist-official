using Avalonia.Controls;

namespace Novalist.Sdk.Models;

/// <summary>
/// Describes a full content area view contributed by an extension.
/// </summary>
public sealed class ContentViewDescriptor
{
    /// <summary>Unique view key (used as ActiveContentView value).</summary>
    public string ViewKey { get; init; } = string.Empty;

    /// <summary>Display name.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Icon emoji.</summary>
    public string Icon { get; init; } = "🧩";

    /// <summary>Optional SVG path geometry data for a vector icon.</summary>
    public string? IconPath { get; init; }

    /// <summary>Factory that creates the content view Control.</summary>
    public Func<Control> CreateView { get; init; } = null!;

    /// <summary>Called when the view becomes active.</summary>
    public Action? OnActivated { get; init; }

    /// <summary>Called when the view is deactivated.</summary>
    public Action? OnDeactivated { get; init; }
}
