using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>Whether a manuscript property is asked of scenes or of chapters.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ManuscriptPropertyScope
{
    Scene,
    Chapter
}

/// <summary>
/// One field the writer added to every scene or every chapter.
///
/// Codex entries have had typed custom properties for a long time; the
/// manuscript never did, so a writer who wanted to track tension, a draft note
/// or which pass a scene was on had to overload tags - and nothing downstream
/// could tell a tension of 7 from the word "7".
/// </summary>
public sealed class ManuscriptPropertyDefinition
{
    /// <summary>Stable identifier stored on the scene or chapter. Never shown.</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>What the writer calls it, shown as the field and column label.</summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Reuses the Codex property types, minus the two that only make sense
    /// there: a scene has no entity references and no ages to compute.
    /// </summary>
    [JsonPropertyName("type")]
    public CustomPropertyType Type { get; set; } = CustomPropertyType.String;

    /// <summary>Allowed values for an <see cref="CustomPropertyType.Enum"/> property.</summary>
    [JsonPropertyName("enumOptions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? EnumOptions { get; set; }

    [JsonPropertyName("scope")]
    public ManuscriptPropertyScope Scope { get; set; } = ManuscriptPropertyScope.Scene;

    /// <summary>
    /// Whether the Manuscript outliner shows a column for it. Off by default:
    /// a writer with a dozen properties does not want a dozen columns, and the
    /// ones worth seeing at a glance are usually two or three.
    /// </summary>
    [JsonPropertyName("showInOutliner")]
    public bool ShowInOutliner { get; set; }
}
