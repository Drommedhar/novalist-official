using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// Represents a single Novalist writing project.
/// </summary>
public class NovalistProject
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public static NovalistProject CreateNew(string name, string path)
    {
        return new NovalistProject
        {
            Id = $"project-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            Name = name,
            Path = path,
            CreatedAt = DateTime.UtcNow
        };
    }
}
