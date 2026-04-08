using System;
using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// Defines a custom entity type created by the user or contributed by an extension.
/// Stored in ProjectMetadata.CustomEntityTypes.
/// </summary>
public class CustomEntityTypeDefinition
{
    /// <summary>
    /// Unique key for this entity type (e.g. "faction", "magic_system").
    /// User-defined keys use lowercase_snake. Extension keys are prefixed with "ext.{extensionId}.".
    /// </summary>
    [JsonPropertyName("typeKey")]
    public string TypeKey { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("displayNamePlural")]
    public string DisplayNamePlural { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "📋";

    /// <summary>
    /// Folder name within the book/world-bible directory for storing entities of this type.
    /// </summary>
    [JsonPropertyName("folderName")]
    public string FolderName { get; set; } = string.Empty;

    /// <summary>
    /// "user" for user-created types, or the extension ID for extension-contributed types.
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "user";

    /// <summary>True when the user created this type (not from an extension).</summary>
    [JsonIgnore]
    public bool IsUserSource => string.Equals(Source, "user", StringComparison.Ordinal);

    /// <summary>
    /// Default field definitions for entities of this type.
    /// </summary>
    [JsonPropertyName("defaultFields")]
    public List<CustomEntityFieldDefinition> DefaultFields { get; set; } = [];

    /// <summary>
    /// Feature toggles controlling which sections are available.
    /// </summary>
    [JsonPropertyName("features")]
    public CustomEntityFeatures Features { get; set; } = new();
}

/// <summary>
/// Defines a single field on a custom entity type.
/// </summary>
public class CustomEntityFieldDefinition
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public CustomPropertyType Type { get; set; } = CustomPropertyType.String;

    [JsonPropertyName("typeKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TypeKey { get; set; }

    [JsonPropertyName("defaultValue")]
    public string DefaultValue { get; set; } = string.Empty;

    [JsonPropertyName("enumOptions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? EnumOptions { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

/// <summary>
/// Feature toggles for a custom entity type.
/// </summary>
public class CustomEntityFeatures
{
    [JsonPropertyName("includeImages")]
    public bool IncludeImages { get; set; } = true;

    [JsonPropertyName("includeRelationships")]
    public bool IncludeRelationships { get; set; }

    [JsonPropertyName("includeSections")]
    public bool IncludeSections { get; set; } = true;
}
