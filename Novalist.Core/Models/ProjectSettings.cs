using System.Text.Json.Serialization;
using Novalist.Sdk.Models;

namespace Novalist.Core.Models;

/// <summary>
/// Per-project settings stored inside the project folder at .novalist/settings.json.
/// Syncs across devices along with the project.
/// </summary>
public class ProjectSettings
{
    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

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

    /// <summary>Anchor date for the calendar view (ISO yyyy-MM-dd).</summary>
    [JsonPropertyName("calendarAnchor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CalendarAnchor { get; set; }
}

public class ProjectViewState
{
    [JsonPropertyName("isExplorerVisible")]
    public bool IsExplorerVisible { get; set; } = true;

    [JsonPropertyName("isContextSidebarVisible")]
    public bool IsContextSidebarVisible { get; set; } = true;

    [JsonPropertyName("isSceneNotesVisible")]
    public bool IsSceneNotesVisible { get; set; }

    [JsonPropertyName("contextCharactersExpanded")]
    public bool ContextCharactersExpanded { get; set; } = true;

    [JsonPropertyName("contextMentionsExpanded")]
    public bool ContextMentionsExpanded { get; set; } = true;

    [JsonPropertyName("contextLocationsExpanded")]
    public bool ContextLocationsExpanded { get; set; } = true;

    [JsonPropertyName("contextItemsExpanded")]
    public bool ContextItemsExpanded { get; set; } = true;

    [JsonPropertyName("contextLoreExpanded")]
    public bool ContextLoreExpanded { get; set; } = true;

    [JsonPropertyName("contextSceneAnalysisExpanded")]
    public bool ContextSceneAnalysisExpanded { get; set; } = true;
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
