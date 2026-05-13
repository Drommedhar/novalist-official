using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// Generic entity data for user-defined or extension-defined entity types.
/// All fields are stored dynamically (unlike CharacterData etc. which have typed properties).
/// </summary>
public class CustomEntityData : IEntityData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonIgnore]
    public bool IsWorldBible { get; set; }

    /// <summary>
    /// The type key that identifies which custom entity type this belongs to (e.g. "faction").
    /// </summary>
    [JsonPropertyName("entityTypeKey")]
    public string EntityTypeKey { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("aliases")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string> Aliases { get; set; } = [];

    /// <summary>
    /// Known fields defined by the entity type definition, stored as key-value pairs.
    /// </summary>
    [JsonPropertyName("fields")]
    public Dictionary<string, string> Fields { get; set; } = [];

    /// <summary>
    /// Additional custom properties added via templates.
    /// </summary>
    [JsonPropertyName("customProperties")]
    public Dictionary<string, string> CustomProperties { get; set; } = [];

    [JsonPropertyName("images")]
    public List<EntityImage> Images { get; set; } = [];

    [JsonPropertyName("sections")]
    public List<EntitySection> Sections { get; set; } = [];

    [JsonPropertyName("relationships")]
    public List<EntityRelationship> Relationships { get; set; } = [];

    [JsonPropertyName("templateId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TemplateId { get; set; }
}
