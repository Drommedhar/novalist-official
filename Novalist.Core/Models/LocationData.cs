using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

public class LocationData : IEntityData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonIgnore]
    public bool IsWorldBible { get; set; }

    /// <summary>
    /// True when this entry is settled. A locked entry is refused by the save
    /// path rather than being quietly overwritten.
    /// </summary>
    [JsonPropertyName("locked")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Locked { get; set; }


    /// <summary>How this entry's name is recognised in prose.</summary>
    [JsonPropertyName("match")]
    public EntityMatchSettings Match { get; set; } = new();

    /// <summary>Whether this entry may be sent to an AI model, and when.</summary>
    [JsonPropertyName("ai")]
    public AiInclusion Ai { get; set; } = AiInclusion.WhenMentioned;

    /// <summary>
    /// Keep this entry out of anything a reader sees. Separate from the AI
    /// setting: a writer may be happy for a model to know the twist and never
    /// for a reader to find it.
    /// </summary>
    [JsonPropertyName("readerHidden")]
    public bool ReaderHidden { get; set; }

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

    /// <summary>
    /// True when this place is a world rather than somewhere inside one.
    ///
    /// The hierarchy was a flat parent string, so a project with two worlds
    /// had two unrelated piles of places and nothing saying which was which. A
    /// world is drawn at the top of the tree and never has a parent of its own:
    /// there is nothing above a world, which is what makes it one.
    /// </summary>
    [JsonPropertyName("isWorld")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsWorld { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("images")]
    public List<EntityImage> Images { get; set; } = [];

    /// <summary>
    /// Files kept with this entry: a recording, a scan, a PDF, a link.
    ///
    /// Entries held images and nothing else, so an interview recording or a
    /// deed had to live as a Research item linked back to here - stored and
    /// surfaced somewhere other than the entry it belongs to.
    /// </summary>
    [JsonPropertyName("attachments")]
    public List<EntityAttachment> Attachments { get; set; } = [];

    [JsonPropertyName("customProperties")]
    public Dictionary<string, string> CustomProperties { get; set; } = [];

    /// <summary>Named links to other entities (owner, faction, origin, ...). The
    /// same shape characters use, so the Wiki and peek resolve them identically.</summary>
    [JsonPropertyName("relationships")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
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


    [JsonPropertyName("sections")]
    public List<EntitySection> Sections { get; set; } = [];

    [JsonPropertyName("templateId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TemplateId { get; set; }
}
