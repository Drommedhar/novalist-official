using Novalist.Core.Models;

namespace Novalist.Core.Services;

public interface ISmartListService
{
    IReadOnlyList<SmartList> GetAll();
    Task SaveAsync(SmartList list);
    Task DeleteAsync(string listId);

    /// <summary>
    /// Returns all (chapter, scene) pairs in the active book matching the
    /// list's filter. Reads scene plain text on demand for POV/tag matching.
    /// </summary>
    Task<IReadOnlyList<(ChapterData Chapter, SceneData Scene)>> EvaluateAsync(SmartList list);
}
