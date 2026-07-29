using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>How a saved list combines its rules.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SmartListMatch
{
    /// <summary>Every rule must hold.</summary>
    All,

    /// <summary>Any one rule is enough.</summary>
    Any
}

/// <summary>What a rule tests.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SmartListOperator
{
    Is,
    Contains,
    GreaterThan,
    LessThan,
    IsSet,
    IsNotSet
}

/// <summary>
/// One condition in a saved list.
///
/// <see cref="Field"/> names a scene or chapter attribute: <c>chapterStatus</c>,
/// <c>pov</c>, <c>tag</c>, <c>plotline</c>, <c>stage</c>, <c>title</c>,
/// <c>synopsis</c>, <c>notes</c>, <c>words</c>, <c>target</c>, <c>beat</c>,
/// <c>act</c>, <c>cast</c> or <c>focus</c> for who is in the scene, or
/// <c>prop:&lt;key&gt;</c> for one of the writer's own fields.
/// </summary>
public sealed class SmartListRule
{
    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    [JsonPropertyName("op")]
    public SmartListOperator Op { get; set; } = SmartListOperator.Contains;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// A saved scene query, akin to Scrivener "Collections". Persisted in
/// <see cref="ProjectMetadata"/>. UI for managing these is wired up at the
/// Explorer/Manuscript layer.
/// </summary>
public sealed class SmartList
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = System.Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether every rule must hold or any one of them is enough. Four ANDed
    /// filters could not express "scenes from either POV", which is the
    /// question a writer comparing two threads actually asks.
    /// </summary>
    [JsonPropertyName("match")]
    public SmartListMatch Match { get; set; } = SmartListMatch.All;

    [JsonPropertyName("rules")]
    public List<SmartListRule> Rules { get; set; } = [];

    /// <summary>One of <see cref="ChapterStatus"/> names; null = any status.</summary>
    /// <remarks>Superseded by <see cref="Rules"/>; read from projects saved
    /// before saved lists became rule-based, and converted on first use.</remarks>
    [JsonPropertyName("chapterStatus")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChapterStatus { get; set; }

    /// <summary>Substring match on resolved POV (override or auto-detected); null = any.</summary>
    /// <remarks>Superseded by <see cref="Rules"/>.</remarks>
    [JsonPropertyName("povContains")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PovContains { get; set; }

    /// <summary>Tag that must appear in <see cref="SceneAnalysisOverrides.Tags"/>.</summary>
    /// <remarks>Superseded by <see cref="Rules"/>.</remarks>
    [JsonPropertyName("tag")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tag { get; set; }

    /// <summary>Plotline id the scene must belong to (Plot Grid membership); null = any.</summary>
    /// <remarks>Superseded by <see cref="Rules"/>.</remarks>
    [JsonPropertyName("plotlineId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlotlineId { get; set; }

    /// <summary>
    /// The rules to evaluate: the saved ones, or the pre-rules fields turned
    /// into rules so a list saved by an older build keeps working untouched.
    /// </summary>
    public List<SmartListRule> EffectiveRules()
    {
        if (Rules.Count > 0) return Rules;

        var converted = new List<SmartListRule>();
        void Add(string field, string? value, SmartListOperator op)
        {
            if (!string.IsNullOrWhiteSpace(value))
                converted.Add(new SmartListRule { Field = field, Op = op, Value = value! });
        }
        Add("chapterStatus", ChapterStatus, SmartListOperator.Is);
        Add("pov", PovContains, SmartListOperator.Contains);
        Add("tag", Tag, SmartListOperator.Is);
        Add("plotline", PlotlineId, SmartListOperator.Is);
        return converted;
    }
}
