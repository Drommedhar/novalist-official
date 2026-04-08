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

    /// <summary>Default field definitions for entities of this type.</summary>
    public IReadOnlyList<EntityFieldDescriptor> DefaultFields { get; init; } = [];

    /// <summary>Feature toggles (images, relationships, sections).</summary>
    public EntityTypeFeatures Features { get; init; } = new();

    /// <summary>Optional: custom editor view (overrides the generic editor).</summary>
    public Func<object, Control>? CreateEditorView { get; init; }

    /// <summary>Optional: factory for creating a new empty entity of this type.</summary>
    public Func<object>? CreateNew { get; init; }
}

/// <summary>
/// Describes a default field on a custom entity type.
/// </summary>
public sealed class EntityFieldDescriptor
{
    /// <summary>Field key (e.g. "alignment", "power_level").</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Display name for the field.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Property type key: a built-in type name or extension type key.</summary>
    public string TypeKey { get; init; } = "String";

    /// <summary>Default value for new entities.</summary>
    public string DefaultValue { get; init; } = string.Empty;

    /// <summary>Options for Enum-type fields.</summary>
    public List<string>? EnumOptions { get; init; }

    /// <summary>Whether the field is required.</summary>
    public bool Required { get; init; }
}

/// <summary>
/// Feature toggles for a custom entity type.
/// </summary>
public sealed class EntityTypeFeatures
{
    /// <summary>Whether entities of this type can have images.</summary>
    public bool IncludeImages { get; init; } = true;

    /// <summary>Whether entities of this type can have relationships.</summary>
    public bool IncludeRelationships { get; init; }

    /// <summary>Whether entities of this type can have free-form sections.</summary>
    public bool IncludeSections { get; init; } = true;
}
