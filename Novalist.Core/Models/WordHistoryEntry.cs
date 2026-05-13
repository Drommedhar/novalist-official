using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

public sealed class WordHistoryEntry
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty; // ISO yyyy-MM-dd local

    [JsonPropertyName("sceneId")]
    public string SceneId { get; set; } = string.Empty;

    [JsonPropertyName("bookId")]
    public string BookId { get; set; } = string.Empty;

    [JsonPropertyName("words")]
    public int Words { get; set; }

    [JsonPropertyName("delta")]
    public int Delta { get; set; }

    public DateOnly DateOnly()
        => global::System.DateOnly.TryParse(Date, System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d
            : global::System.DateOnly.MinValue;
}
