using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Novalist.Sdk.Models;

/// <summary>A scene and its current text, for asking the host which scenes are
/// stale in one call rather than one round-trip each.</summary>
public sealed class SceneTextPair
{
    [JsonPropertyName("sceneId")]
    public string SceneId { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

/// <summary>How an entity relates to a scene. The distinction matters: a
/// character who is merely discussed did not witness anything, so knowledge must
/// not be attributed to them.</summary>
public static class ScenePresence
{
    /// <summary>Physically in the scene and taking part.</summary>
    public const string Present = "present";

    /// <summary>Referred to or talked about, but not there.</summary>
    public const string Mentioned = "mentioned";

    /// <summary>Considered and ruled out. Stored so the scene is not re-examined.</summary>
    public const string Absent = "absent";
}

/// <summary>One entity the analysis found in a scene.</summary>
public sealed class SceneEntityRef
{
    /// <summary>The name as it appears in the prose.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The Codex entity this resolved to, or null when the name matches
    /// nothing (or is ambiguous). An unresolved name is a candidate for a new
    /// Codex entry rather than an error.</summary>
    [JsonPropertyName("entityId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EntityId { get; set; }

    /// <summary>"character", "location", "item", "lore", or a custom type key.</summary>
    [JsonPropertyName("entityType")]
    public string EntityType { get; set; } = string.Empty;

    /// <summary>One of the <see cref="ScenePresence"/> values.</summary>
    [JsonPropertyName("presence")]
    public string Presence { get; set; } = ScenePresence.Mentioned;

    /// <summary>One short line on what the scene says about it.</summary>
    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;
}

/// <summary>
/// What one character did, perceived and learned in a single scene. Same field
/// set as the per-character knowledge files this replaces, so nothing is lost —
/// only now it is stored with the scene rather than with the character, and
/// carries the three-way <see cref="Presence"/> instead of a bool.
/// </summary>
public sealed class SceneCharacterKnowledge
{
    [JsonPropertyName("characterId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CharacterId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>One of the <see cref="ScenePresence"/> values. Knowledge is only
    /// meaningful for a character who was actually present.</summary>
    [JsonPropertyName("presence")]
    public string Presence { get; set; } = ScenePresence.Absent;

    /// <summary>Things the character directly perceived (saw, heard, did).</summary>
    [JsonPropertyName("observed")]
    public List<string> Observed { get; set; } = [];

    /// <summary>Things the character was told or learned indirectly.</summary>
    [JsonPropertyName("learned")]
    public List<string> Learned { get; set; } = [];

    /// <summary>Things said by the character (intent, claims, lies).</summary>
    [JsonPropertyName("said")]
    public List<string> Said { get; set; } = [];

    /// <summary>Open questions / things the character is uncertain about.</summary>
    [JsonPropertyName("uncertain")]
    public List<string> Uncertain { get; set; } = [];

    /// <summary>Emotional state at the end of the scene.</summary>
    [JsonPropertyName("emotion")]
    public string Emotion { get; set; } = string.Empty;

    /// <summary>Where the character physically is during the scene.</summary>
    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;

    /// <summary>Other characters present alongside this one.</summary>
    [JsonPropertyName("companions")]
    public List<string> Companions { get; set; } = [];

    /// <summary>Physical condition at the end of the scene.</summary>
    [JsonPropertyName("physicalState")]
    public string PhysicalState { get; set; } = string.Empty;

    /// <summary>Short-term goals or intentions when the scene ends.</summary>
    [JsonPropertyName("goals")]
    public List<string> Goals { get; set; } = [];

    /// <summary>Relationship updates with other characters.</summary>
    [JsonPropertyName("relationshipChanges")]
    public List<string> RelationshipChanges { get; set; } = [];

    /// <summary>Secrets protected, revealed, or newly created by this scene.</summary>
    [JsonPropertyName("secrets")]
    public List<string> Secrets { get; set; } = [];

    /// <summary>How the character speaks/moves in this scene.</summary>
    [JsonPropertyName("voiceNotes")]
    public string VoiceNotes { get; set; } = string.Empty;

    /// <summary>Items gained, lost, used, or now carried.</summary>
    [JsonPropertyName("inventoryChanges")]
    public List<string> InventoryChanges { get; set; } = [];
}

/// <summary>
/// Everything one pass over a single scene produced: which entities it involves
/// and how, what each present character came away with, and any findings worth
/// showing the writer.
///
/// One record per scene is the unit of both generation and invalidation — a
/// scene whose text still hashes to <see cref="SceneContentHash"/> never needs
/// analysing again. Every cumulative view ("what does she know by chapter nine")
/// is a deterministic roll-up over these records, never a separate model call.
/// </summary>
public sealed class SceneAnalysisRecord
{
    /// <summary>Bumped when the schema grows fields worth re-analysing for.
    /// Records written under an older version count as stale.</summary>
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyName("sceneId")]
    public string SceneId { get; set; } = string.Empty;

    [JsonPropertyName("chapterGuid")]
    public string ChapterGuid { get; set; } = string.Empty;

    [JsonPropertyName("chapterTitle")]
    public string ChapterTitle { get; set; } = string.Empty;

    [JsonPropertyName("sceneTitle")]
    public string SceneTitle { get; set; } = string.Empty;

    /// <summary>Hash of the scene text this was produced from.</summary>
    [JsonPropertyName("sceneContentHash")]
    public string SceneContentHash { get; set; } = string.Empty;

    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = string.Empty;

    /// <summary>Which model produced it, so a model change can be spotted.</summary>
    [JsonPropertyName("modelId")]
    public string ModelId { get; set; } = string.Empty;

    [JsonPropertyName("entities")]
    public List<SceneEntityRef> Entities { get; set; } = [];

    [JsonPropertyName("characters")]
    public List<SceneCharacterKnowledge> Characters { get; set; } = [];

    [JsonPropertyName("findings")]
    public List<CachedAiFinding> Findings { get; set; } = [];
}
