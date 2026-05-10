using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

public class SceneData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = System.Guid.NewGuid().ToString();

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("chapterGuid")]
    public string ChapterGuid { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("wordCount")]
    public int WordCount { get; set; }

    [JsonPropertyName("notes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Notes { get; set; }

    [JsonPropertyName("synopsis")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Synopsis { get; set; }

    [JsonPropertyName("labelColor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LabelColor { get; set; }

    /// <summary>Plotline ids this scene contributes to (Plot Grid).</summary>
    [JsonPropertyName("plotlineIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? PlotlineIds { get; set; }

    /// <summary>Inline comments anchored to text ranges in the scene HTML.</summary>
    [JsonPropertyName("comments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SceneComment>? Comments { get; set; }

    [JsonPropertyName("analysisOverrides")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SceneAnalysisOverrides? AnalysisOverrides { get; set; }
}

public class SceneComment
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = System.Guid.NewGuid().ToString();

    /// <summary>The text snippet the comment was originally anchored to —
    /// shown in the comment list.</summary>
    [JsonPropertyName("anchorText")]
    public string AnchorText { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public System.DateTime CreatedAt { get; set; } = System.DateTime.UtcNow;

    [JsonPropertyName("resolved")]
    public bool Resolved { get; set; }
}

public class SceneAnalysisOverrides
{
    [JsonPropertyName("pov")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Pov { get; set; }

    [JsonPropertyName("emotion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Emotion { get; set; }

    [JsonPropertyName("intensity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Intensity { get; set; }

    [JsonPropertyName("conflict")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Conflict { get; set; }

    [JsonPropertyName("tags")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Tags { get; set; }

    [JsonIgnore]
    public bool HasValues
        => Pov != null
           || Emotion != null
           || Intensity.HasValue
           || Conflict != null
           || Tags != null;

    public SceneAnalysisOverrides Clone()
        => new()
        {
            Pov = Pov,
            Emotion = Emotion,
            Intensity = Intensity,
            Conflict = Conflict,
            Tags = Tags != null ? [.. Tags] : null
        };
}

/// <summary>
/// Maps chapter GUIDs to their ordered scene lists.
/// Stored in .novalist/scenes.json.
/// </summary>
public class ScenesManifest
{
    [JsonPropertyName("chapters")]
    public Dictionary<string, List<SceneData>> Chapters { get; set; } = new();
}
