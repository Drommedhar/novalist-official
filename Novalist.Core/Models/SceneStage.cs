using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// One stage a scene can be in, as the writer defines it.
///
/// The chapter statuses are a fixed five-value enum compiled into the app.
/// Stages are data instead, because revision is scene-granular and no two
/// writers agree on what the stages are — "needs a beta read" and "cut but
/// keeping" are real stages for the people who use them and meaningless to
/// everyone else.
/// </summary>
public sealed class SceneStage
{
    /// <summary>Stable identifier stored on the scene. Never shown.</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>What the writer calls it.</summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>Hex colour for the binder dot and the Dashboard breakdown.</summary>
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#8b8b8b";

    /// <summary>
    /// Whether words in a scene at this stage count as written.
    ///
    /// An outline placeholder full of notes-to-self is not progress, and
    /// counting it inflates every total the writer uses to decide whether they
    /// are on track.
    /// </summary>
    [JsonPropertyName("countsAsWritten")]
    public bool CountsAsWritten { get; set; } = true;
}

/// <summary>The stages a project starts with.</summary>
public static class SceneStageDefaults
{
    /// <summary>
    /// Mirrors the five chapter statuses, so a project that never touches this
    /// reads the way it always did. Outline does not count as written: at that
    /// stage the words are usually notes rather than prose.
    /// </summary>
    public static List<SceneStage> Build() =>
    [
        new() { Key = "outline", Label = "Outline", Color = "#8b8b8b", CountsAsWritten = false },
        new() { Key = "firstDraft", Label = "First draft", Color = "#c98b3a" },
        new() { Key = "revised", Label = "Revised", Color = "#5b8dbe" },
        new() { Key = "edited", Label = "Edited", Color = "#7a9d54" },
        new() { Key = "final", Label = "Final", Color = "#4f8f4f" }
    ];
}
