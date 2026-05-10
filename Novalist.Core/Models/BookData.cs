using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// Represents a single book within a Novalist project.
/// Each book has its own chapters, scenes, entities, and templates.
/// </summary>
public class BookData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("folderName")]
    public string FolderName { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("chapters")]
    public List<ChapterData> Chapters { get; set; } = new();

    [JsonPropertyName("chapterFolder")]
    public string ChapterFolder { get; set; } = "Chapters";

    [JsonPropertyName("characterFolder")]
    public string CharacterFolder { get; set; } = "Characters";

    [JsonPropertyName("locationFolder")]
    public string LocationFolder { get; set; } = "Locations";

    [JsonPropertyName("itemFolder")]
    public string ItemFolder { get; set; } = "Items";

    [JsonPropertyName("loreFolder")]
    public string LoreFolder { get; set; } = "Lore";

    [JsonPropertyName("imageFolder")]
    public string ImageFolder { get; set; } = "Images";

    [JsonPropertyName("snapshotFolder")]
    public string SnapshotFolder { get; set; } = "Snapshots";

    [JsonPropertyName("coverImage")]
    public string CoverImage { get; set; } = string.Empty;

    [JsonPropertyName("characterTemplates")]
    public List<CharacterTemplate> CharacterTemplates { get; set; } = [];

    [JsonPropertyName("locationTemplates")]
    public List<LocationTemplate> LocationTemplates { get; set; } = [];

    [JsonPropertyName("itemTemplates")]
    public List<ItemTemplate> ItemTemplates { get; set; } = [];

    [JsonPropertyName("loreTemplates")]
    public List<LoreTemplate> LoreTemplates { get; set; } = [];

    [JsonPropertyName("activeCharacterTemplateId")]
    public string ActiveCharacterTemplateId { get; set; } = string.Empty;

    [JsonPropertyName("activeLocationTemplateId")]
    public string ActiveLocationTemplateId { get; set; } = string.Empty;

    [JsonPropertyName("activeItemTemplateId")]
    public string ActiveItemTemplateId { get; set; } = string.Empty;

    [JsonPropertyName("activeLoreTemplateId")]
    public string ActiveLoreTemplateId { get; set; } = string.Empty;

    /// <summary>
    /// Templates for custom entity types, shared across all custom types in this book.
    /// </summary>
    [JsonPropertyName("customEntityTemplates")]
    public List<CustomEntityTemplate> CustomEntityTemplates { get; set; } = [];

    /// <summary>
    /// Active template ID per custom entity type key.
    /// </summary>
    [JsonPropertyName("activeCustomEntityTemplateIds")]
    public Dictionary<string, string> ActiveCustomEntityTemplateIds { get; set; } = [];

    /// <summary>Plot threads defined for this book. Drives the Plot Grid view.</summary>
    [JsonPropertyName("plotlines")]
    public List<PlotlineData> Plotlines { get; set; } = [];
}
