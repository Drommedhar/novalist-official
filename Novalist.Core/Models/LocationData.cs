using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

public class LocationData : IEntityData
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



    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("aliases")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string> Aliases { get; set; } = [];

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("parent")]
    public string Parent { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("images")]
    public List<EntityImage> Images { get; set; } = [];

    [JsonPropertyName("customProperties")]
    public Dictionary<string, string> CustomProperties { get; set; } = [];

    /// <summary>Named links to other entities (owner, faction, origin, ...). The
    /// same shape characters use, so the Wiki and peek resolve them identically.</summary>
    [JsonPropertyName("relationships")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<EntityRelationship> Relationships { get; set; } = [];

    [JsonPropertyName("sections")]
    public List<EntitySection> Sections { get; set; } = [];

    [JsonPropertyName("templateId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TemplateId { get; set; }
}
