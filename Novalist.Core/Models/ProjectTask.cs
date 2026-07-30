using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// One thing to do before the book is finished.
///
/// Novalist had todo comments, which are anchored to a passage and belong to
/// the scene they sit in. "Check the dates in act two", "read the whole thing
/// aloud", "decide whether Tomas survives" belong to no passage and to no
/// scene, so they were kept on paper or in a scene called Notes.
/// </summary>
public sealed class ProjectTask
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>What to do, in the writer's words.</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// The list it belongs to, or empty for the loose pile.
    ///
    /// A name rather than an id: a revision checklist is copied between books
    /// by typing the same name, and an id would make that impossible.
    /// </summary>
    [JsonPropertyName("list")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string List { get; set; } = string.Empty;

    [JsonPropertyName("done")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Done { get; set; }

    /// <summary>
    /// When it was ticked. Kept so a finished list can be read as a record of
    /// a revision pass rather than only as a row of ticks.
    /// </summary>
    [JsonPropertyName("doneAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? DoneAt { get; set; }

    /// <summary>The scene it is about, when it is about one.</summary>
    [JsonPropertyName("chapterGuid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string ChapterGuid { get; set; } = string.Empty;

    [JsonPropertyName("sceneId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string SceneId { get; set; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; set; }
}
