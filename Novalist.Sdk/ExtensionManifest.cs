using System.Text.Json.Serialization;

namespace Novalist.Sdk;

/// <summary>
/// Describes an extension package. Deserialized from <c>extension.json</c> in the extension folder.
/// </summary>
public sealed class ExtensionManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("entryAssembly")]
    public string EntryAssembly { get; set; } = string.Empty;

    [JsonPropertyName("minHostVersion")]
    public string MinHostVersion { get; set; } = string.Empty;

    [JsonPropertyName("maxHostVersion")]
    public string MaxHostVersion { get; set; } = string.Empty;

    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = [];

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];
}
