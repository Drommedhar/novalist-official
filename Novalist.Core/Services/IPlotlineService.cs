using Novalist.Core.Models;

namespace Novalist.Core.Services;

public interface IPlotlineService
{
    IReadOnlyList<PlotlineData> GetPlotlines();
    Task<PlotlineData> CreateAsync(string name, string color = "#3498db");
    /// <param name="previousJson">
    /// What the thread said before the caller changed it. Callers edit the very
    /// object the book holds, so the old state has to be taken where the thread
    /// was still untouched; null records no version.
    /// </param>
    Task UpdateAsync(PlotlineData plotline, string? previousJson = null);
    Task DeleteAsync(string plotlineId);
    Task ReorderAsync(IReadOnlyList<string> orderedIds);

    /// <summary>Toggles the scene's membership in the given plotline.</summary>
    Task ToggleSceneAsync(string chapterGuid, string sceneId, string plotlineId);

    /// <summary>True when the scene currently belongs to the plotline.</summary>
    bool IsSceneInPlotline(SceneData scene, string plotlineId);
}
