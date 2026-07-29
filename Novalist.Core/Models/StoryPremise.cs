using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// The book in one line, then one paragraph, then one summary per act.
///
/// Novalist has always had a Snowflake-shaped setup wizard in the codebase and
/// no way to reach it, and nowhere for its answers to live. A premise that only
/// exists in the writer's head cannot be checked against the book, and a
/// paragraph pasted into a scene is prose the export would print.
/// </summary>
public sealed class StoryPremise
{
    /// <summary>One sentence: a character wants something but something stops them.</summary>
    [JsonPropertyName("logline")]
    public string Logline { get; set; } = string.Empty;

    /// <summary>The premise opened out: world, stakes, inciting incident, rough climax.</summary>
    [JsonPropertyName("paragraph")]
    public string Paragraph { get; set; } = string.Empty;

    /// <summary>
    /// A summary per act, keyed by the act name the chapters use, so the ladder
    /// stays attached to the structure rather than assuming three acts.
    /// </summary>
    [JsonPropertyName("acts")]
    public Dictionary<string, string> Acts { get; set; } = [];
}
