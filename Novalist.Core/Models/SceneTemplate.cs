using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// A scene worth starting from: everything a new scene can be born with rather
/// than filled in afterwards.
///
/// A new scene has always been blank - no point of view, no stage, no
/// plotlines, no shape to the prose - so a writer who works in a repeatable
/// scene form retyped it every time. A template is made from a scene that
/// already reads the way they want, because describing one in the abstract is
/// harder than pointing at one.
/// </summary>
public sealed class SceneTemplate
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The synopsis the new scene starts with. Often a beat skeleton.</summary>
    [JsonPropertyName("synopsis")]
    public string Synopsis { get; set; } = string.Empty;

    /// <summary>The prose the new scene starts with - headings, prompts, a shape.</summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("pov")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Pov { get; set; }

    [JsonPropertyName("stage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Stage { get; set; }

    [JsonPropertyName("labelKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LabelKey { get; set; }

    [JsonPropertyName("tags")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("plotlineIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string> PlotlineIds { get; set; } = [];
}
