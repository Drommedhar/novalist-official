using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

public class EntityImage : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _path = string.Empty;

    [JsonPropertyName("name")]
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    [JsonPropertyName("path")]
    public string Path
    {
        get => _path;
        set => SetField(ref _path, value);
    }

    private string _alt = string.Empty;

    /// <summary>
    /// What the picture shows, for a reader who cannot see it. Distinct from
    /// the display name on purpose: "Mira Vance" names the image, "a woman in
    /// a soaked coat on a harbour wall" describes it, and only the second is
    /// any use read aloud. Empty means undescribed, which is a thing the
    /// export preflight reports rather than papers over.
    /// </summary>
    [JsonPropertyName("alt")]
    public string Alt
    {
        get => _alt;
        set => SetField(ref _alt, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class EntitySection
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Withhold this section from anything sent to an AI model, while the rest
    /// of the entry still goes. This is how a writer keeps one twist out of the
    /// model's context without hiding the character it belongs to.
    /// </summary>
    [JsonPropertyName("aiHidden")]
    public bool AiHidden { get; set; }
}

public class EntityRelationship
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// What kind of tie this is - family, ally, rival, member, owner, place -
    /// so a graph can colour it and a reader can tell a marriage from a feud
    /// at a glance. Free text with a suggested set rather than an enum: the
    /// ties a book needs are the book's, and family was previously guessed
    /// from keywords in the role, which only worked in English.
    /// </summary>
    [JsonPropertyName("category")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string Category { get; set; } = string.Empty;
}

public enum EntityType
{
    Character,
    Location,
    Item,
    Lore,
    Custom
}

/// <summary>
/// Implemented by all entity data types to support World Bible tracking.
/// </summary>
public interface IEntityData
{
    string Id { get; }
    bool IsWorldBible { get; set; }

    /// <summary>How this entry's name is matched in prose. Never null.</summary>
    EntityMatchSettings Match { get; set; }

    /// <summary>Whether this entry may be sent to an AI model, and when.</summary>
    AiInclusion Ai { get; set; }

    /// <summary>What this entry is like at particular points in the story.
    /// Never null; empty means it is the same throughout.</summary>
    List<EntityStateOverride> StateOverrides { get; set; }

    /// <summary>Project-wide tags on this entry. Never null.</summary>
    List<string> Tags { get; set; }

    /// <summary>
    /// Rows saying what this entry is to something else. Never null.
    ///
    /// On the interface because the reciprocal write-back used to be
    /// character-only: an item's owner link was stored verbatim and never
    /// authored on the owner's record, so the relationship existed from one
    /// side and not the other.
    /// </summary>
    List<EntityRelationship> Relationships { get; set; }

    /// <summary>
    /// The group this entry belongs to, or empty for none.
    ///
    /// Groups used to be a plain string on characters alone, which cannot say
    /// that a house, a ship and a family crest all belong to the same faction -
    /// and a faction is exactly the thing that spans types.
    /// </summary>
    string Group { get; set; }

    /// <summary>What this entry is called. Read-only: each type composes it
    /// from its own fields, which for a character means name and surname.</summary>
    string DisplayName { get; }
}

/// <summary>
/// When a Codex entry is allowed into what an extension sends to an AI model.
///
/// The writer owns this, not the extension: a spoiler they have not written yet
/// should not reach a model because some extension decided the entry was
/// relevant. <see cref="WhenMentioned"/> is the default and reproduces what
/// Novalist always did.
/// </summary>
public enum AiInclusion
{
    /// <summary>Sent when the scene actually mentions the entry. The default.</summary>
    WhenMentioned = 0,

    /// <summary>Always sent, mentioned or not. For the things a model needs to
    /// know about the world in every scene.</summary>
    Always = 1,

    /// <summary>Never sent, however relevant it looks. For unrevealed twists,
    /// and for anything the writer simply does not want a model to see.</summary>
    Never = 2
}

/// <summary>
/// Controls how an entry's name is recognised in prose.
///
/// The defaults reproduce the old behaviour exactly - case-insensitive, no
/// exclusions, no plural matching - so an existing project reads the same until
/// the writer changes something.
/// </summary>
public class EntityMatchSettings
{
    /// <summary>
    /// When true, only an exact-case occurrence counts. Useful for a short name
    /// that is also an ordinary word: "Will" the character versus "will" the verb.
    /// </summary>
    [JsonPropertyName("caseSensitive")]
    public bool CaseSensitive { get; set; }

    /// <summary>
    /// When true, an English plural of the name also matches - "Ravens" for a
    /// faction called "Raven". Off by default because it is wrong for most
    /// personal names.
    /// </summary>
    [JsonPropertyName("matchPlurals")]
    public bool MatchPlurals { get; set; }

    /// <summary>
    /// Phrases that must never be treated as a reference to this entry, even
    /// when they contain its name. "Rose" the character, excluded from "rose
    /// garden" and "she rose".
    /// </summary>
    [JsonPropertyName("exclusions")]
    public List<string> Exclusions { get; set; } = [];

    /// <summary>
    /// Scene ids where this entry should never be detected. For the case where
    /// one scene keeps producing a false positive that is correct everywhere
    /// else.
    /// </summary>
    [JsonPropertyName("ignoredSceneIds")]
    public List<string> IgnoredSceneIds { get; set; } = [];

    /// <summary>
    /// Whether a candidate occurrence should count, given the surrounding text
    /// and the scene it is in. <paramref name="context"/> is the sentence or
    /// line the match sits in; pass the whole paragraph when unsure.
    /// </summary>
    public bool Allows(string name, string matchedText, string? context, string? sceneId)
    {
        if (sceneId != null && IgnoredSceneIds.Contains(sceneId, StringComparer.Ordinal))
            return false;

        if (CaseSensitive && !string.Equals(name, matchedText, StringComparison.Ordinal))
            return false;

        if (context != null)
        {
            foreach (var exclusion in Exclusions)
            {
                if (!string.IsNullOrWhiteSpace(exclusion)
                    && context.Contains(exclusion, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Plural forms of a name worth matching, when plural matching is on.
    /// Deliberately the two regular English rules only - an irregular plural is
    /// a different word and should be added as an alias.
    /// </summary>
    public IEnumerable<string> PluralFormsOf(string name)
    {
        if (!MatchPlurals || string.IsNullOrWhiteSpace(name))
            yield break;

        yield return name + "s";
        if (name.EndsWith('s') || name.EndsWith("sh", StringComparison.Ordinal)
            || name.EndsWith("ch", StringComparison.Ordinal) || name.EndsWith('x')
            || name.EndsWith('z'))
            yield return name + "es";
    }
}
