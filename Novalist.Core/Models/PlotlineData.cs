using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// A named plot thread that scenes can be assigned to. Drives the Plot Grid
/// view (rows = plotlines, cols = chapters/scenes).
/// </summary>
public sealed class PlotlineData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = System.Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#3498db";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; set; }

    /// <summary>Values for the writer's own fields, keyed by property key.</summary>
    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Properties { get; set; }

    /// <summary>
    /// How much of the book this thread is: main, subplot or minor.
    ///
    /// A grid of equal rows says a romance running through every chapter and a
    /// running joke are the same kind of thing. They are not, and the difference
    /// is what tells you whether a thread is under-served or simply small.
    /// </summary>
    [JsonPropertyName("importance")]
    public PlotlineImportance Importance { get; set; } = PlotlineImportance.Subplot;

    /// <summary>
    /// Ids of the Codex entries this thread belongs to. A plot thread is
    /// somebody's; a membership grid could say which scenes it touches and never
    /// whose story it is.
    /// </summary>
    [JsonPropertyName("castIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string> CastIds { get; set; } = [];

    /// <summary>
    /// What has to happen for this thread to be finished, in order.
    ///
    /// Membership answers "is this thread in this scene". It cannot answer the
    /// question a revision asks, which is whether the thread ever resolves - the
    /// commonest developmental note there is.
    /// </summary>
    [JsonPropertyName("steps")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<PlotlineStep> Steps { get; set; } = [];

    /// <summary>Steps still unresolved. Zero with no steps means nothing was
    /// planned, not that everything is done - the UI says which.</summary>
    [JsonIgnore]
    public int UnresolvedSteps => Steps.Count(s => !s.Resolved);
}

/// <summary>How much of the book a thread is.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlotlineImportance
{
    /// <summary>The spine. If this does not resolve, the book does not end.</summary>
    Main,

    /// <summary>A real thread with its own shape, running under the main one.</summary>
    Subplot,

    /// <summary>A thread of a few scenes: a running joke, a small debt.</summary>
    Minor
}

/// <summary>One thing that has to happen for a thread to be finished.</summary>
public sealed class PlotlineStep
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>The scene where it happens, once there is one.</summary>
    [JsonPropertyName("sceneId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string SceneId { get; set; } = string.Empty;

    [JsonPropertyName("resolved")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Resolved { get; set; }

    [JsonPropertyName("order")]
    public int Order { get; set; }
}
