using System.Text.Json.Serialization;

namespace Novalist.Sdk.Models;

public class AiFinding
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("excerpt")]
    public string Excerpt { get; set; } = string.Empty;

    [JsonPropertyName("entityName")]
    public string EntityName { get; set; } = string.Empty;

    [JsonPropertyName("entityType")]
    public string EntityType { get; set; } = string.Empty;

    // Scene stats fields (only for type "scene_stats")
    [JsonPropertyName("scenePov")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ScenePov { get; set; }

    [JsonPropertyName("sceneEmotion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SceneEmotion { get; set; }

    [JsonPropertyName("sceneIntensity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SceneIntensity { get; set; }

    [JsonPropertyName("sceneConflict")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SceneConflict { get; set; }
}

public class CachedAiFinding
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("excerpt")]
    public string Excerpt { get; set; } = string.Empty;

    [JsonPropertyName("entityName")]
    public string EntityName { get; set; } = string.Empty;

    [JsonPropertyName("entityType")]
    public string EntityType { get; set; } = string.Empty;

    [JsonPropertyName("scenePov")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ScenePov { get; set; }

    [JsonPropertyName("sceneEmotion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SceneEmotion { get; set; }

    [JsonPropertyName("sceneIntensity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SceneIntensity { get; set; }

    [JsonPropertyName("sceneConflict")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SceneConflict { get; set; }
}

public class WholeStoryAnalysisResult
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("findings")]
    public List<CachedAiFinding> Findings { get; set; } = [];

    [JsonPropertyName("thinking")]
    public string Thinking { get; set; } = string.Empty;

    [JsonPropertyName("rawResponse")]
    public string RawResponse { get; set; } = string.Empty;
}

public class ChapterAnalysisResult
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("scenes")]
    public Dictionary<string, SceneAnalysisResult> Scenes { get; set; } = new();
}

public class SceneAnalysisResult
{
    [JsonPropertyName("findings")]
    public List<CachedAiFinding> Findings { get; set; } = [];
}

public class EntitySummary
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

public class ChapterContext
{
    public string ChapterName { get; set; } = string.Empty;
    public string? ActName { get; set; }
    public string? SceneName { get; set; }
    public string? Date { get; set; }
}

public class EnabledChecks
{
    public bool References { get; set; } = true;
    public bool Inconsistencies { get; set; } = true;
    public bool Suggestions { get; set; } = true;
    public bool SceneStats { get; set; }
}
