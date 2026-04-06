using Novalist.Core.Models;

namespace Novalist.Core.Services;

public interface IAiService
{
    /// <summary>Configure the service from settings.</summary>
    void Configure(AiSettings settings);

    /// <summary>Check if the LM Studio server is reachable.</summary>
    Task<bool> IsServerRunningAsync();

    /// <summary>List available LLM models on the LM Studio server.</summary>
    Task<List<AiModelInfo>> ListModelsAsync();

    /// <summary>Ensure the configured model is loaded with the desired context length.</summary>
    Task EnsureModelLoadedAsync();

    /// <summary>Streaming chat generation. Returns full response + thinking text.</summary>
    Task<AiChatResult> GenerateChatAsync(
        List<AiChatMessage> messages,
        Action<string>? onChunk = null,
        double? temperature = null,
        Action<string>? onThinkingChunk = null,
        CancellationToken cancellationToken = default);

    /// <summary>Analyse a scene/chapter in a single LLM call with streaming.</summary>
    Task<AiAnalysisResult> AnalyseChapterWholeAsync(
        string chapterText,
        List<EntitySummary> entities,
        List<string>? alreadyFound = null,
        ChapterContext? context = null,
        EnabledChecks? checks = null,
        Action<string>? onResponseChunk = null,
        Action<string>? onThinkingChunk = null,
        bool findAllReferences = false,
        CancellationToken cancellationToken = default);

    /// <summary>Cross-chapter whole-story analysis.</summary>
    Task<AiAnalysisResult> AnalyseWholeStoryAsync(
        List<ChapterTextEntry> chapters,
        List<EntitySummary> entities,
        List<ChapterFindingsEntry> cachedFindings,
        Action<string>? onResponseChunk = null,
        Action<string>? onThinkingChunk = null,
        CancellationToken cancellationToken = default);

    /// <summary>Cancel any in-flight request.</summary>
    void Cancel();

    /// <summary>Reset the chat session (clears server-side conversation history for Copilot). No-op for LM Studio.</summary>
    Task ResetChatSessionAsync();
}

public class AiModelInfo
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

public class AiChatMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
}

public class AiChatResult
{
    public string Response { get; set; } = string.Empty;
    public string Thinking { get; set; } = string.Empty;
    public bool WasTruncated { get; set; }
}

public class AiAnalysisResult
{
    public List<AiFinding> Findings { get; set; } = [];
    public string RawResponse { get; set; } = string.Empty;
    public string Thinking { get; set; } = string.Empty;
}

public class ChapterTextEntry
{
    public string Name { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

public class ChapterFindingsEntry
{
    public string ChapterName { get; set; } = string.Empty;
    public List<CachedAiFinding> Findings { get; set; } = [];
}
