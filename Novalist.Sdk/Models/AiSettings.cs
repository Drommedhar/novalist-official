using System.Text.Json.Serialization;

namespace Novalist.Sdk.Models;

public class AiSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "lmstudio";

    [JsonPropertyName("analysisMode")]
    public string AnalysisMode { get; set; } = "chapter";

    [JsonPropertyName("lmStudioBaseUrl")]
    public string LmStudioBaseUrl { get; set; } = "http://localhost:1234";

    [JsonPropertyName("lmStudioModel")]
    public string LmStudioModel { get; set; } = string.Empty;

    [JsonPropertyName("lmStudioApiToken")]
    public string LmStudioApiToken { get; set; } = string.Empty;

    [JsonPropertyName("copilotPath")]
    public string CopilotPath { get; set; } = "copilot";

    [JsonPropertyName("copilotModel")]
    public string CopilotModel { get; set; } = string.Empty;

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.7;

    [JsonPropertyName("contextLength")]
    public int ContextLength { get; set; }

    [JsonPropertyName("topP")]
    public double TopP { get; set; } = 0.9;

    [JsonPropertyName("minP")]
    public double MinP { get; set; } = 0.05;

    [JsonPropertyName("frequencyPenalty")]
    public double FrequencyPenalty { get; set; } = 1.1;

    [JsonPropertyName("repeatLastN")]
    public int RepeatLastN { get; set; } = 64;

    [JsonPropertyName("checkReferences")]
    public bool CheckReferences { get; set; } = true;

    [JsonPropertyName("checkInconsistencies")]
    public bool CheckInconsistencies { get; set; } = true;

    [JsonPropertyName("checkSuggestions")]
    public bool CheckSuggestions { get; set; } = true;

    [JsonPropertyName("checkSceneStats")]
    public bool CheckSceneStats { get; set; } = true;

    [JsonPropertyName("disableRegexReferences")]
    public bool DisableRegexReferences { get; set; }

    [JsonPropertyName("grammarCheckEnabled")]
    public bool GrammarCheckEnabled { get; set; } = true;

    /// <summary>
    /// Master toggle for the per-character / per-scene knowledge tracking
    /// feature. When off, "Talk as character" sends the codex persona only.
    /// </summary>
    [JsonPropertyName("enableCharacterKnowledge")]
    public bool EnableCharacterKnowledge { get; set; }

    /// <summary>
    /// Set to true once the initial knowledge scan has finished for the
    /// current project. Used to skip re-prompting for scan on every open.
    /// </summary>
    [JsonPropertyName("knowledgeScanCompleted")]
    public bool KnowledgeScanCompleted { get; set; }

    /// <summary>
    /// Maximum number of LLM prompts dispatched in parallel during the
    /// knowledge scan. Should be ≤ the LM Studio model's "Max parallel
    /// requests" setting; otherwise the server queues overflow on the
    /// single loaded instance. Copilot CLI is always serial.
    /// </summary>
    [JsonPropertyName("maxParallelPrompts")]
    public int MaxParallelPrompts { get; set; } = 4;

    /// <summary>
    /// Override the language used for AI analysis output (titles, descriptions).
    /// When empty, defaults to the application UI language.
    /// </summary>
    [JsonPropertyName("responseLanguage")]
    public string ResponseLanguage { get; set; } = string.Empty;

    [JsonPropertyName("systemPrompt")]
    public string SystemPrompt { get; set; } = string.Empty;

    public const string DefaultSystemPrompt =
        """
        You are a creative writing assistant for a novel-writing project. The user is working in a writing environment called Novalist. Below you will find all known project entities (characters, locations, items, lore) and the content of the chapter the user is currently editing (if any).
        Answer questions, offer plot advice, suggest improvements, and help with writing tasks. Be concise but thorough. Respect the established world and characters.

        IMPORTANT: Always respond in {{LANGUAGE}}. The user's UI is set to this language and they expect answers in it.

        IMPORTANT: The entity data below has already been adjusted for the current chapter and scene. Character ages, roles, appearances, and other properties reflect their state at this point in the story. You MUST treat these values as authoritative — do NOT invent different values.
        """;
}
