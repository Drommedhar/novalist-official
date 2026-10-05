
namespace Novalist.Backend.Rpc;

public sealed record EntityTemplateDto(string Id, string Name);

public sealed record CustomTypeSpecDto(
    string? TypeKey,
    string DisplayName,
    string? DisplayNamePlural,
    CustomFieldSpecDto[]? Fields,
    bool IncludeImages,
    bool IncludeRelationships,
    bool IncludeSections);

public sealed record CustomFieldSpecDto(
    string? Key,
    string DisplayName,
    string Type,
    string? DefaultValue,
    string[]? EnumOptions,
    bool Required,
    /// <summary>A question saying what belongs in this field, shown under it on
    /// the entry. Optional so a caller written before it existed still works.</summary>
    string? Prompt = null);

public sealed record CustomPropDto(string Key, string Value, string PropType, IReadOnlyList<string> EnumOptions);

public sealed record EntitySectionDto(string Title, string Content);

/// <summary>A stored entity image (display name + project-relative path) as sent
/// by the inline overrides editor when replacing a scope's image list.</summary>
public sealed record EntityImageDto(string Name, string Path);

public sealed record RelationshipRowDto(string Role, string Target);

public sealed record RelationshipEditRowDto(
    string Role, string Target, string? InverseRole,
    /// <summary>What kind of tie it is, for the graph's colour. May be empty.</summary>
    string? Category = null);

public sealed record RelationshipSuggestionsDto(
    IReadOnlyList<string> CharacterNames,
    IReadOnlyList<string> Roles);

public sealed record EntitySummaryDto(
    string Id,
    string Name,
    string Detail,
    bool IsWorldBible,
    string? ImagePath,
    IReadOnlyList<string> Aliases,
    string? Group = null,
    string? Gender = null,
    string? Parent = null,
    string? FirstName = null,
    /// <summary>The bare surname, as a second hover / mention target ("Calder"
    /// for "Liam Calder"). Null when the entry has none, or when it is already
    /// the whole display name.</summary>
    string? Surname = null,
    EntityMatchDto? Match = null,
    /// <summary>True for a place that is a world: drawn at the top of the tree,
    /// and never given a parent of its own.</summary>
    bool IsWorld = false,
    /// <summary>True when this entry is settled and the save path refuses it.</summary>
    bool Locked = false);

/// <summary>How this entry's name is recognised in prose. Rides along with the
/// summary so the editor can apply the rules without a second round trip per
/// entity. Null when the entry uses the defaults, which is the common case.</summary>
/// <summary>One time-scoped restatement of an entry.</summary>
public sealed record StateOverrideDto(
    string? Act,
    string? Chapter,
    string? Scene,
    string? Name,
    string? Description,
    Dictionary<string, string>? Fields,
    string? Note,
    /// <summary>From here the entry is out of the story: dead, departed,
    /// destroyed. Read by the continuity gates.</summary>
    bool Gone = false);

/// <summary>What an entry is like in one context. <c>IsOverridden</c> false
/// means the entry reads as itself and nothing else here is meaningful.</summary>
public sealed record ResolvedStateDto(
    string? Name,
    string? Description,
    Dictionary<string, string> Fields,
    string? Note,
    string ScopeLabel,
    bool IsOverridden);

/// <summary>An entry's AI-inclusion setting plus which of its sections are
/// withheld from a model.</summary>
public sealed record AiPolicyDto(string Inclusion, AiSectionDto[] Sections);

/// <summary>One section of an entry, and whether it is withheld. The index is
/// its position in the entry's section list, which is what the setter takes.</summary>
public sealed record AiSectionDto(int Index, string Title, bool Hidden);

public sealed record EntityMatchDto(
    bool CaseSensitive,
    bool MatchPlurals,
    IReadOnlyList<string> Exclusions,
    IReadOnlyList<string> IgnoredSceneIds,
    IReadOnlyList<string> Plurals);

/// <summary>Rich focus-peek card payload. <c>TypeKey</c> is the built-in key
/// (character/location/item/lore) or a custom type key; <c>CustomTypeLabel</c>
/// carries the display name for custom types (null for built-ins, which the
/// client localizes). Colors are hex strings straight from the desktop card.</summary>
public sealed record EntityPeekDto(
    string Id,
    string TypeKey,
    string Title,
    string? CustomTypeLabel,
    string BadgeColor,
    string Description,
    PeekImageDto[] Images,
    PeekPillDto[] Pills,
    PeekPropDto[] AppearanceProps,
    PeekPropDto[] CustomProps,
    PeekRelationshipDto[] Relationships,
    EntitySectionDto[] Sections,
    PeekMapPinDto[] MapPins,
    string? ScopeLabel = null,
    PeekFindingDto[]? AiFindings = null);

/// <summary>One cached AI analysis finding about this entity in the open chapter.
/// Read-only: the host never generates these, it only surfaces what an extension's
/// chapter analysis previously stored in the project. <see cref="Type"/> is the
/// finding kind ("reference", "inconsistency", "suggestion"), which the renderer
/// turns into a marker.</summary>
public sealed record PeekFindingDto(string Type, string Title, string Description, string Excerpt);

/// <summary>A single framed image; <c>Url</c> is project-root-relative for the
/// novalist-project:// protocol.</summary>
public sealed record PeekImageDto(string Name, string Url);

/// <summary>An attribute pill. Exactly one of <c>Text</c> (literal) or
/// <c>LabelKey</c>+<c>Arg</c> (client-localized template, e.g. "Age {0}") is set.
/// <c>Icon</c> is an SVG path geometry (the users glyph) or null.</summary>
public sealed record PeekPillDto(
    string? Text, string? LabelKey, string? Arg, bool Dim, string Color, string? Icon);

/// <summary>A key/value property. For appearance props the <c>Key</c> is a
/// localization key; for custom props it is a literal field label.</summary>
public sealed record PeekPropDto(string Key, string Value);

public sealed record PeekRelationshipDto(string Role, PeekRelationshipTargetDto[] Targets);

/// <summary><c>EntityId</c>/<c>TypeKey</c> are null when the name resolves to no
/// (or an ambiguous) entity — the client renders those as plain disabled text.</summary>
public sealed record PeekRelationshipTargetDto(string Name, string? EntityId, string? TypeKey);

public sealed record PeekMapPinDto(string MapId, string MapName, string PinId, string PinLabel);

/// <summary>A proposed Codex entry from an extension's entity extractor. Nothing
/// is written until the writer accepts it.</summary>
public sealed record EntityProposalDto(string TypeKey, string Name, string Detail);

/// <summary>The result of a scene scan: the proposals that survived filtering, or
/// a short error when the extractor failed.</summary>
public sealed record EntityProposalsDto(EntityProposalDto[] Proposals, string? Error);

public sealed record MatchSettingsDto(
    bool CaseSensitive, bool MatchPlurals, string[] Exclusions, string[] IgnoredSceneIds);

/// <summary>One earlier state of a Codex entry, for the history list.</summary>
public sealed record EntityRevisionDto(string Id, string SavedAt, long SizeBytes);

/// <summary>One section, and whether readers are kept from it.</summary>
public sealed record ReaderSectionDto(int Index, string Title, bool Hidden);

/// <summary>What a reader may see of one entry.</summary>
public sealed record ReaderPolicyDto(bool Hidden, IReadOnlyList<ReaderSectionDto> Sections);
