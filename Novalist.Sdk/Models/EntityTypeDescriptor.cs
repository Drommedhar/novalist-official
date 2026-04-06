using Avalonia.Controls;

namespace Novalist.Sdk.Models;

/// <summary>
/// Describes a custom entity type contributed by an extension.
/// </summary>
public sealed class EntityTypeDescriptor
{
    /// <summary>Unique type key (e.g. "faction", "magic_system").</summary>
    public string TypeKey { get; init; } = string.Empty;

    /// <summary>Display name (e.g. "Faction").</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Plural display name (e.g. "Factions").</summary>
    public string DisplayNamePlural { get; init; } = string.Empty;

    /// <summary>Icon emoji.</summary>
    public string Icon { get; init; } = "🧩";

    /// <summary>Folder name within the book directory.</summary>
    public string FolderName { get; init; } = string.Empty;

    /// <summary>Factory for the entity editor view.</summary>
    public Func<object, Control>? CreateEditorView { get; init; }

    /// <summary>Factory for creating a new empty entity of this type.</summary>
    public Func<object>? CreateNew { get; init; }
}
