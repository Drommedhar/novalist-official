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

    /// <summary>How this entry's name is recognised in prose.</summary>
    [JsonPropertyName("match")]
    public EntityMatchSettings Match { get; set; } = new();

    /// <summary>Whether this entry may be sent to an AI model, and when.</summary>
    [JsonPropertyName("ai")]
    public AiInclusion Ai { get; set; } = AiInclusion.WhenMentioned;

    /// <summary>What this entry is like at particular points in the story - a
    /// razed city, an artefact that changed hands. Empty means unchanging.</summary>
    [JsonPropertyName("stateOverrides")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<EntityStateOverride> StateOverrides { get; set; } = [];
    /// <summary>
    /// Project-wide tags on this entry. The same vocabulary the scenes and the
    /// research notes use, so a tag means one thing across the project rather
    /// than three unrelated things that happen to be spelled alike.
    /// </summary>
    [JsonPropertyName("tags")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string> Tags { get; set; } = [];



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

    /// <summary>
    /// The group this entry belongs to, or empty for none. A faction spans
    /// types - the house, the ship and the crest all belong to it - which a
    /// character-only field could never say.
    /// </summary>
    [JsonPropertyName("group")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string Group { get; set; } = string.Empty;

    /// <summary>What this entry is called.</summary>
    [JsonIgnore]
    public string DisplayName => Name;


    [JsonPropertyName("templateId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TemplateId { get; set; }
}
