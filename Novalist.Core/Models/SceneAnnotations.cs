using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// Something a scene sets up, and the scene that answers it.
///
/// Held on the setup rather than the payoff so that a promise nothing answers
/// still exists to be reported - which is the whole point of tracking them.
/// </summary>
public class ScenePromise
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = System.Guid.NewGuid().ToString();

    /// <summary>What was promised, in the writer's words: "the gun on the mantel".</summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>The scene that pays it off, or null while nothing does.</summary>
    [JsonPropertyName("payoffSceneId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PayoffSceneId { get; set; }
}

public class SceneFootnote
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = System.Guid.NewGuid().ToString();

    /// <summary>1-based ordinal number rendered in the superscript anchor.</summary>
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
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

    /// <summary>
    /// Who left it. Stamped from the project's author when a comment arrives
    /// without one, so a file that came back from an editor keeps saying whose
    /// note is whose.
    /// </summary>
    [JsonPropertyName("author")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Author { get; set; }

    /// <summary>
    /// Whether this is a job rather than a remark - "check the timetable",
    /// "this paragraph needs cutting". Both live in the same inbox; only one
    /// of them is a task.
    /// </summary>
    [JsonPropertyName("isTodo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsTodo { get; set; }

    /// <summary>
    /// What was decided about it: empty (undecided), "accepted", "considering"
    /// or "declined".
    ///
    /// Resolving said only that the note was finished with. A remark you acted
    /// on and a remark you disagreed with came out looking identical, which is
    /// the wrong shape for feedback: most of a beta reader's notes are opinions
    /// to weigh, and the ones turned down are the ones most worth still being
    /// able to see six weeks later when a second reader says the same thing.
    /// </summary>
    [JsonPropertyName("verdict")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Verdict { get; set; }

    /// <summary>Answers to the comment, oldest first. Null when nobody replied.</summary>
    [JsonPropertyName("replies")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CommentReply>? Replies { get; set; }
}

/// <summary>One answer in a comment thread.</summary>
public class CommentReply
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = System.Guid.NewGuid().ToString();

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public System.DateTime CreatedAt { get; set; } = System.DateTime.UtcNow;
}
