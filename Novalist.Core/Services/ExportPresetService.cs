using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>
/// The export layouts on offer: the four Novalist ships, plus whatever the
/// writer has authored.
///
/// Built-ins are read-only and can only be copied. That is deliberate - a
/// writer who edits "Shunn manuscript" until it no longer matches Shunn's
/// standard has quietly lost the thing they were relying on, and no submission
/// guideline will tell them.
/// </summary>
public sealed class ExportPresetService
{
    private readonly IProjectService _projectService;

    public ExportPresetService(IProjectService projectService)
    {
        _projectService = projectService;
    }

    /// <summary>Built-ins first, then the writer's own.</summary>
    public IReadOnlyList<ExportPreset> All()
        => [.. ExportPresets.All, .. _projectService.ActiveBook?.ExportPresets ?? []];

    /// <summary>One preset by id, falling back to the default so an export
    /// against a deleted preset still produces a readable file.</summary>
    public ExportPreset ById(string? id)
    {
        var key = (id ?? string.Empty).Trim();
        return All().FirstOrDefault(p => p.Id == key)
               ?? ExportPresets.GetById(ExportPresets.DefaultId);
    }

    /// <summary>
    /// A copy of an existing preset under a new name, which is how a writer
    /// starts one. Returns the new preset, or null with no book open.
    /// </summary>
    public async Task<ExportPreset?> DuplicateAsync(string sourceId, string displayName)
    {
        var book = _projectService.ActiveBook;
        if (book == null) return null;

        var source = ById(sourceId);
        var name = string.IsNullOrWhiteSpace(displayName)
            ? $"{source.DisplayName} (copy)"
            : displayName.Trim();

        var copy = source with
        {
            // A generated id, because a copy sharing the source's id would be
            // shadowed by the built-in it came from.
            Id = $"custom-{Guid.NewGuid():N}",
            DisplayName = name,
            Description = string.Empty,
            IsCustom = true
        };

        book.ExportPresets.Add(copy);
        await _projectService.SaveProjectAsync();
        return copy;
    }

    /// <summary>
    /// Replaces a user preset. A built-in is left alone: editing one in place
    /// would silently change what a named standard means.
    /// </summary>
    public async Task<bool> SaveAsync(ExportPreset preset)
    {
        var book = _projectService.ActiveBook;
        if (book == null) return false;

        var index = book.ExportPresets.FindIndex(p => p.Id == preset.Id);
        if (index < 0) return false;

        book.ExportPresets[index] = preset with { IsCustom = true };
        await _projectService.SaveProjectAsync();
        return true;
    }

    /// <summary>Deletes a user preset. Built-ins cannot be deleted.</summary>
    public async Task<bool> DeleteAsync(string id)
    {
        var book = _projectService.ActiveBook;
        if (book == null) return false;

        if (book.ExportPresets.RemoveAll(p => p.Id == id) == 0) return false;
        await _projectService.SaveProjectAsync();
        return true;
    }
}
