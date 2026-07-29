using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>
/// Operations that act on a set of scenes at once, for the multi-select bulk bar
/// in the binder, corkboard, outliner and calendar.
/// </summary>
public interface ISceneBulkService
{
    /// <summary>Resolves scene ids to their scenes and owning chapters, in book
    /// order. Ids that name nothing are dropped rather than throwing, because a
    /// selection can outlive the scene it points at.</summary>
    IReadOnlyList<ResolvedScene> Resolve(IReadOnlyList<string> sceneIds);

    /// <summary>Deletes every named scene. Returns how many were deleted.</summary>
    Task<int> DeleteAsync(IReadOnlyList<string> sceneIds);

    /// <summary>Archives every named scene. Returns how many were archived.</summary>
    Task<int> ArchiveAsync(IReadOnlyList<string> sceneIds);

    /// <summary>Adds tags to every named scene, or replaces their tags outright.
    /// Returns how many scenes changed.</summary>
    Task<int> SetTagsAsync(IReadOnlyList<string> sceneIds, IReadOnlyList<string> tags, bool replace);

    /// <summary>What a date shift would do, without doing it. Every named scene
    /// appears, including the ones that carry no date and would not move.</summary>
    IReadOnlyList<SceneDateShift> PreviewDateShift(IReadOnlyList<string> sceneIds, long days);

    /// <summary>Shifts every named scene's dates by <paramref name="days"/>.
    /// Returns how many actually moved.</summary>
    Task<int> ShiftDatesAsync(IReadOnlyList<string> sceneIds, long days);
}

/// <summary>A scene together with the chapter that owns it.</summary>
public sealed record ResolvedScene(string ChapterGuid, SceneData Scene);

/// <summary>One row of a date-shift preview: what the scene reads now, and what
/// it would read after. Equal values mean the scene does not move.</summary>
public sealed record SceneDateShift(
    string SceneId,
    string Title,
    string Before,
    string After);
