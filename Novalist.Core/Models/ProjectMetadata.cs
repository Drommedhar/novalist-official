using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// Top-level project metadata stored in .novalist/project.json.
/// A project is a container that holds multiple books and a shared World Bible.
/// </summary>
public class ProjectMetadata
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 2;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("activeBookId")]
    public string ActiveBookId { get; set; } = string.Empty;

    [JsonPropertyName("books")]
    public List<BookData> Books { get; set; } = new();

    [JsonPropertyName("worldBibleFolder")]
    public string WorldBibleFolder { get; set; } = "World Bible";

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

    [JsonPropertyName("coverImage")]
    public string CoverImage { get; set; } = string.Empty;

    public BookData? GetActiveBook()
        => Books.FirstOrDefault(b => b.Id == ActiveBookId) ?? Books.FirstOrDefault();
}
