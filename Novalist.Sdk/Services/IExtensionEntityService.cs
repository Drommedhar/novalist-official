using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;

namespace Novalist.Sdk.Services;

/// <summary>
/// Read-only entity access exposed to extensions.
/// </summary>
public interface IExtensionEntityService
{
    Task<IReadOnlyList<CharacterInfo>> LoadCharactersAsync();
    Task<IReadOnlyList<LocationInfo>> LoadLocationsAsync();
    Task<IReadOnlyList<ItemInfo>> LoadItemsAsync();
    Task<IReadOnlyList<LoreInfo>> LoadLoreAsync();
    Task<IReadOnlyList<CustomEntityInfo>> LoadCustomEntitiesAsync(string typeKey);

    /// <summary>Reads authored content for a built-in or registered custom entry.
    /// Image paths use the same stored, project-aware references as GetProjectImages.
    /// Publishing must respect ReaderHidden on the entry and its sections.
    /// Use GetAiContextAsync instead when assembling context for an AI provider.</summary>
    Task<EntityContentInfo?> GetEntityContentAsync(string typeKey, string entityId);

    /// <summary>Returns all registered custom entity type keys and display names.</summary>
    IReadOnlyList<CustomEntityTypeInfo> GetCustomEntityTypes();

    /// <summary>Saves a custom entity to the active book. The entity type must be registered.</summary>
    Task SaveCustomEntityAsync(CustomEntityInfo entity);

    /// <summary>
    /// Creates a Codex entry and returns its new id, or null when the type is
    /// unknown or no book is open.
    ///
    /// <paramref name="typeKey"/> is one of the built-in kinds — "character",
    /// "location", "item", "lore" — or a registered custom entity type key.
    /// This is what lets an extension act on something it found in the prose
    /// (a name the Codex does not have yet) instead of only reporting it.
    /// </summary>
    Task<string?> CreateEntityAsync(string typeKey, string name, string description = "");

    /// <summary>
    /// The Codex entries this scene is allowed to put in front of an AI model,
    /// with withheld sections already removed.
    ///
    /// Use this rather than the Load* methods when assembling model context.
    /// Those return everything, because the Codex has to show everything; this
    /// applies the writer's per-entry inclusion setting and their per-section
    /// withholding, which the extension has no way to reconstruct on its own.
    /// A writer who marks an entry "never" means it, and an extension that
    /// assembles context from the raw lists breaks that promise.
    /// </summary>
    /// <param name="chapterGuid">Chapter of the scene the context is for.</param>
    /// <param name="sceneId">Scene the context is for. Entries the scene does
    /// not mention are included only when set to always.</param>
    Task<IReadOnlyList<AiContextEntryInfo>> GetAiContextAsync(string chapterGuid, string sceneId);

    /// <summary>
    /// Writes name, description and sections onto an existing Codex entry of
    /// any kind, built-in or custom. False when the id is unknown.
    ///
    /// CreateEntityAsync could make an entry and nothing could fill one in, so
    /// a questionnaire extension could ask a writer twenty questions about a
    /// character and then had nowhere to put the answers.
    /// </summary>
    /// <param name="sections">
    /// Sections to write. A section whose title already exists is replaced;
    /// anything else is appended. Sections the caller does not mention are left
    /// alone, so filling in one part of an entry does not wipe the rest.
    /// </param>
    Task<bool> SaveEntityAsync(
        string typeKey,
        string entityId,
        string? name = null,
        string? description = null,
        IReadOnlyList<CustomEntitySectionInfo>? sections = null);

    /// <summary>
    /// Writes an entry's own fields - a character's age and eye colour, a
    /// location's region, whatever that kind of entry has.
    ///
    /// SaveEntityAsync covers the name, the description and the free-text
    /// sections, which left every typed field on an entry unreachable: an
    /// importer could bring a character across with their whole biography and
    /// not their hair colour, and a questionnaire could ask about a location
    /// and have nowhere but a section to put the answer.
    ///
    /// Field names are the ones the Codex shows, matched without regard to
    /// case. A name the entry does not have is <em>returned</em> rather than
    /// quietly dropped, because a typo that silently loses a value is the
    /// worst way to find out about it.
    /// </summary>
    /// <returns>
    /// The field names that could not be written, empty when all of them were.
    /// A missing entry returns every name given.
    /// </returns>
    Task<IReadOnlyList<string>> SetEntityFieldsAsync(
        string typeKey,
        string entityId,
        IReadOnlyDictionary<string, string> fields);

    /// <summary>
    /// Sets one of the writer's own properties on an entry, or removes it with
    /// a null value.
    ///
    /// These are the fields the writer added themselves, so an extension that
    /// fills in an entry has to be able to reach them: a project whose
    /// characters all carry a "House" property gets nothing from an importer
    /// that can only write the built-in ones.
    /// </summary>
    Task<bool> SetEntityCustomPropertyAsync(
        string typeKey, string entityId, string key, string? value);

    /// <summary>
    /// Replaces an entry's relationships, and writes the other half of each
    /// one onto the entry it names.
    ///
    /// A relationship that exists from one side only is worse than none: the
    /// graph draws an edge that vanishes when looked at from the other end.
    /// Give <see cref="EntityRelationshipInfo.InverseRole"/> and the far side
    /// is authored too; leave it empty and the other entry is left alone
    /// rather than guessed at.
    /// </summary>
    /// <returns>False when there is no such entry.</returns>
    Task<bool> SetEntityRelationshipsAsync(
        string typeKey, string entityId, IReadOnlyList<EntityRelationshipInfo> relationships);

    /// <summary>Notifies the host that entities have changed and the UI should refresh.</summary>
    void RequestEntityRefresh();

    List<string> GetProjectImages();
    string GetImageFullPath(string relativePath);

    /// <summary>
    /// Returns the absolute filesystem path of the character's image as it
    /// applies in the given chapter/scene context, walking per-chapter /
    /// per-scene overrides before falling back to the default. Returns null
    /// when the character has no image. The host owns the override
    /// resolution so extensions stay opaque to chapter/scene/act fall-back
    /// rules.
    /// </summary>
    Task<string?> GetCharacterImagePathAsync(string characterId, string? chapterGuid, string? sceneId);

    /// <summary>
    /// Returns the character's full profile resolved for the given chapter/scene
    /// context. Per-scene overrides take precedence over per-chapter, which take
    /// precedence over per-act, which fall back to the base profile. Returns
    /// null when the character does not exist.
    /// </summary>
    Task<CharacterDetailedInfo?> GetCharacterDetailedAsync(string characterId, string? chapterGuid, string? sceneId);
}

/// <summary>Lightweight character info for read-only access.</summary>
public sealed class CharacterInfo
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public List<string> Aliases { get; init; } = [];
}

/// <summary>
/// Rich character info with all profile data, resolved for a specific
/// chapter/scene context (per-scene → per-chapter → per-act override fallback
/// applied by the host before return). Extensions never see raw override
/// lists; they get the effective view for the requested context.
/// </summary>
public sealed class CharacterDetailedInfo
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Surname { get; init; } = string.Empty;
    public List<string> Aliases { get; init; } = [];
    public string Age { get; init; } = string.Empty;
    public string Gender { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
    public string EyeColor { get; init; } = string.Empty;
    public string HairColor { get; init; } = string.Empty;
    public string HairLength { get; init; } = string.Empty;
    public string Height { get; init; } = string.Empty;
    public string Build { get; init; } = string.Empty;
    public string SkinTone { get; init; } = string.Empty;
    public string DistinguishingFeatures { get; init; } = string.Empty;
    public Dictionary<string, string> CustomProperties { get; init; } = new();
    public List<CharacterRelationshipInfo> Relationships { get; init; } = [];
    public List<CharacterSectionInfo> Sections { get; init; } = [];

    /// <summary>Scope label of the override that produced the resolved view
    /// (e.g. "Scene: 04 - Bridge"). Empty when the base profile was used.</summary>
    public string ResolvedFromScope { get; init; } = string.Empty;
}

public sealed class CharacterRelationshipInfo
{
    public string Role { get; init; } = string.Empty;
    public string TargetName { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
}

/// <summary>
/// A relationship as an extension writes it.
///
/// Separate from <see cref="CharacterRelationshipInfo"/>, which is the
/// read-side shape and carries a note rather than the far half of the link.
/// </summary>
public sealed class EntityRelationshipInfo
{
    /// <summary>What this entry is to the target: "mother", "owner", "capital of".</summary>
    public string Role { get; init; } = string.Empty;

    /// <summary>The entry it names, by the name the Codex shows.</summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>The writer's own grouping for the row. Optional.</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// What the target is back - "daughter" to a "mother". Leave it empty and
    /// the target's own record is not touched.
    /// </summary>
    public string InverseRole { get; init; } = string.Empty;
}

public sealed class CharacterSectionInfo
{
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
}

/// <summary>Lightweight location info for read-only access.</summary>
public sealed class LocationInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
}

/// <summary>Lightweight item info for read-only access.</summary>
public sealed class ItemInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
}

/// <summary>Lightweight lore info for read-only access.</summary>
public sealed class LoreInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
}

/// <summary>Custom entity info for reading and writing.</summary>
public sealed class CustomEntityInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string EntityTypeKey { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<CustomEntitySectionInfo>? Sections { get; init; }
}

/// <summary>A section of rich-text content within a custom entity.</summary>
public sealed class CustomEntitySectionInfo
{
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    /// <summary>Withheld from reader-facing output; still visible to the writer.</summary>
    public bool ReaderHidden { get; init; }
}

/// <summary>Authored Codex content for local reports and publishing.</summary>
public sealed class EntityContentInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<CustomEntitySectionInfo> Sections { get; init; } = [];
    public IReadOnlyList<string> ImagePaths { get; init; } = [];
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public bool ReaderHidden { get; init; }
}

/// <summary>Describes a registered custom entity type.</summary>
public sealed class CustomEntityTypeInfo
{
    public string TypeKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string DisplayNamePlural { get; init; } = string.Empty;
    /// <summary>
    /// Optional icon name. Empty by default: the icon system is SVG
    /// paths and lucide names, and a pictograph here was the only
    /// emoji in the entity model.
    /// </summary>
    public string Icon { get; init; } = string.Empty;
}
