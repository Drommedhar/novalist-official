using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// Per-project settings stored inside the project folder at .novalist/settings.json.
/// Syncs across devices along with the project.
/// </summary>
public class ProjectSettings
{
    [JsonPropertyName("viewState")]
    public ProjectViewState ViewState { get; set; } = new();

    [JsonPropertyName("wordCountGoals")]
    public ProjectWordCountGoals WordCountGoals { get; set; } = new();

    [JsonPropertyName("timeline")]
    public TimelineData Timeline { get; set; } = new();

    [JsonPropertyName("wholeStoryAnalysis")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WholeStoryAnalysisResult? WholeStoryAnalysis { get; set; }

    [JsonPropertyName("chapterAnalysis")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, ChapterAnalysisResult>? ChapterAnalysis { get; set; }
}

public class ProjectViewState
{
    [JsonPropertyName("isExplorerVisible")]
    public bool IsExplorerVisible { get; set; } = true;

    [JsonPropertyName("isContextSidebarVisible")]
    public bool IsContextSidebarVisible { get; set; } = true;

    [JsonPropertyName("isSceneNotesVisible")]
    public bool IsSceneNotesVisible { get; set; }
}

public class ProjectWordCountGoals
{
    [JsonPropertyName("dailyGoal")]
    public int DailyGoal { get; set; } = 1000;

    [JsonPropertyName("projectGoal")]
    public int ProjectGoal { get; set; } = 50000;

    [JsonPropertyName("deadline")]
    public string? Deadline { get; set; }

    [JsonPropertyName("dailyBaselineWords")]
    public int? DailyBaselineWords { get; set; }

    [JsonPropertyName("dailyBaselineDate")]
    public string? DailyBaselineDate { get; set; }
}
