using Novalist.Sdk.Models;

namespace Novalist.Sdk.Hooks;

/// <summary>
/// Hooks into AI prompt building and response processing.
/// </summary>
public interface IAiHook
{
    /// <summary>
    /// Called before the system prompt is sent to the AI.
    /// Return a string to append to the prompt, or null to skip.
    /// </summary>
    string? OnBuildSystemPrompt(AiPromptContext context);

    /// <summary>
    /// Called when an AI response chunk is received.
    /// Return the chunk to pass through, or modified text.
    /// Default implementation passes through unchanged.
    /// </summary>
    string OnResponseChunk(string chunk) => chunk;
}
