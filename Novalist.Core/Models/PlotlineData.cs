using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// A named plot thread that scenes can be assigned to. Drives the Plot Grid
/// view (rows = plotlines, cols = chapters/scenes).
/// </summary>
public sealed class PlotlineData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = System.Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#3498db";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; set; }
}
