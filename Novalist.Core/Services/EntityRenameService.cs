using System.Text.RegularExpressions;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>
/// Renaming an entity used to orphan everything that pointed at it, because most
/// references are stored as the entity's display name rather than its id:
/// <see cref="EntityRelationship.Target"/>, <see cref="LocationData.Parent"/>,
/// the per-scene POV override, and [[wiki-links]] inside entity sections.
///
/// Prose mentions are the exception - those carry <c>data-entity-id</c>, so they
/// are rewritten exactly rather than by name matching.
/// </summary>
public sealed class EntityRenameService : IEntityRenameService
{
    private readonly IProjectService _projectService;
    private readonly IEntityService _entityService;

    public EntityRenameService(IProjectService projectService, IEntityService entityService)
    {
        _projectService = projectService;
        _entityService = entityService;
    }

    public async Task<EntityRenameReport> CascadeAsync(string entityId, string oldName, string newName)
    {
        var report = new EntityRenameReport();

        if (string.IsNullOrWhiteSpace(oldName)
            || string.IsNullOrWhiteSpace(newName)
            || string.Equals(oldName, newName, StringComparison.Ordinal))
            return report;

        // Prose mention spans: id-keyed, so exact regardless of the name.
        if (!string.IsNullOrEmpty(entityId))
            report.ScenesUpdated = await _projectService.SyncMentionDisplayTextAsync(entityId, newName);

        await CascadeCharactersAsync(oldName, newName, report);
        await CascadeLocationsAsync(oldName, newName, report);
        await CascadeItemsAsync(oldName, newName, report);
        await CascadeLoreAsync(oldName, newName, report);
        await CascadeCustomAsync(oldName, newName, report);
        CascadePovOverrides(oldName, newName, report);

        if (report.PovOverridesUpdated > 0)
            await _projectService.SaveScenesAsync();

        return report;
    }

    private async Task CascadeCharactersAsync(string oldName, string newName, EntityRenameReport report)
    {
        foreach (var c in await _entityService.LoadCharactersAsync())
        {
            var touched = RetargetRelationships(c.Relationships, oldName, newName, report);
            touched |= RelinkSections(c.Sections, oldName, newName, report);
            if (touched)
                await _entityService.SaveCharacterAsync(c);
        }
    }

    private async Task CascadeLocationsAsync(string oldName, string newName, EntityRenameReport report)
    {
        foreach (var l in await _entityService.LoadLocationsAsync())
        {
            var touched = RelinkSections(l.Sections, oldName, newName, report);
            if (string.Equals(l.Parent, oldName, StringComparison.Ordinal))
            {
                l.Parent = newName;
                report.ParentsUpdated++;
                touched = true;
            }
            if (touched)
                await _entityService.SaveLocationAsync(l);
        }
    }

    private async Task CascadeItemsAsync(string oldName, string newName, EntityRenameReport report)
    {
        foreach (var i in await _entityService.LoadItemsAsync())
        {
            if (RelinkSections(i.Sections, oldName, newName, report))
                await _entityService.SaveItemAsync(i);
        }
    }

    private async Task CascadeLoreAsync(string oldName, string newName, EntityRenameReport report)
    {
        foreach (var l in await _entityService.LoadLoreAsync())
        {
            if (RelinkSections(l.Sections, oldName, newName, report))
                await _entityService.SaveLoreAsync(l);
        }
    }

    private async Task CascadeCustomAsync(string oldName, string newName, EntityRenameReport report)
    {
        foreach (var type in _entityService.GetCustomEntityTypes())
        {
            foreach (var e in await _entityService.LoadCustomEntitiesAsync(type.TypeKey))
            {
                var touched = RetargetRelationships(e.Relationships, oldName, newName, report);
                touched |= RelinkSections(e.Sections, oldName, newName, report);
                if (touched)
                    await _entityService.SaveCustomEntityAsync(e);
            }
        }
    }

    /// <summary>
    /// Per-scene POV overrides name a character. Held in the scenes manifest, so
    /// the caller saves once rather than per scene.
    /// </summary>
    private void CascadePovOverrides(string oldName, string newName, EntityRenameReport report)
    {
        foreach (var chapter in _projectService.GetChaptersOrdered())
        {
            foreach (var scene in _projectService.GetScenesForChapter(chapter.Guid))
            {
                var pov = scene.AnalysisOverrides?.Pov;
                if (pov != null && string.Equals(pov, oldName, StringComparison.Ordinal))
                {
                    scene.AnalysisOverrides!.Pov = newName;
                    report.PovOverridesUpdated++;
                }
            }
        }
    }

    private static bool RetargetRelationships(
        List<EntityRelationship>? relationships, string oldName, string newName, EntityRenameReport report)
    {
        if (relationships == null)
            return false;

        var touched = false;
        foreach (var r in relationships)
        {
            if (string.Equals(r.Target, oldName, StringComparison.Ordinal))
            {
                r.Target = newName;
                report.RelationshipsUpdated++;
                touched = true;
            }
        }
        return touched;
    }

    /// <summary>
    /// Rewrites explicit <c>[[Old Name]]</c> and <c>[[Old Name|shown text]]</c>
    /// links in section markdown. Only the link target is touched: the shown text
    /// is the author's wording and is left exactly as written. Bare prose
    /// occurrences of the name are deliberately not rewritten - they are not
    /// references, and editing them would be an unrequested edit to the writing.
    /// </summary>
    private static bool RelinkSections(
        List<EntitySection>? sections, string oldName, string newName, EntityRenameReport report)
    {
        if (sections == null)
            return false;

        var pattern = new Regex(
            @"\[\[\s*" + Regex.Escape(oldName) + @"\s*(\|[^\]]*)?\]\]",
            RegexOptions.IgnoreCase);

        var touched = false;
        foreach (var s in sections)
        {
            if (string.IsNullOrEmpty(s.Content) || !s.Content.Contains("[[", StringComparison.Ordinal))
                continue;

            var rewritten = pattern.Replace(s.Content, m => $"[[{newName}{m.Groups[1].Value}]]");
            if (!string.Equals(rewritten, s.Content, StringComparison.Ordinal))
            {
                s.Content = rewritten;
                report.SectionLinksUpdated++;
                touched = true;
            }
        }
        return touched;
    }
}
