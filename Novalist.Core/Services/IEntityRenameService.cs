namespace Novalist.Core.Services;

/// <summary>What a cascade rename touched. Every count is of records changed,
/// not of individual textual replacements.</summary>
public sealed class EntityRenameReport
{
    /// <summary>Scene files whose mention spans were rewritten.</summary>
    public int ScenesUpdated { get; set; }

    /// <summary>Relationship entries on other entities that pointed at the old name.</summary>
    public int RelationshipsUpdated { get; set; }

    /// <summary>Locations whose parent pointed at the old name.</summary>
    public int ParentsUpdated { get; set; }

    /// <summary>Scenes whose POV override named the renamed character.</summary>
    public int PovOverridesUpdated { get; set; }

    /// <summary>Entity sections whose [[wiki-links]] named the old name.</summary>
    public int SectionLinksUpdated { get; set; }

    /// <summary>True when nothing anywhere referenced the old name.</summary>
    public bool IsEmpty =>
        ScenesUpdated == 0
        && RelationshipsUpdated == 0
        && ParentsUpdated == 0
        && PovOverridesUpdated == 0
        && SectionLinksUpdated == 0;
}

/// <summary>
/// Propagates an entity rename to everything that refers to the entity by name
/// rather than by id.
/// </summary>
public interface IEntityRenameService
{
    /// <summary>
    /// Rewrites every name-keyed reference to <paramref name="oldName"/> so it
    /// reads <paramref name="newName"/>. No-op when the names match or either is
    /// blank. <paramref name="entityId"/> is used for the prose mention spans,
    /// which are id-keyed and so are rewritten exactly.
    /// </summary>
    Task<EntityRenameReport> CascadeAsync(string entityId, string oldName, string newName);
}
