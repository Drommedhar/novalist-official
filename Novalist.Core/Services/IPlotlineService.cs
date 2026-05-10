using Novalist.Core.Models;

namespace Novalist.Core.Services;

public interface IPlotlineService
{
    IReadOnlyList<PlotlineData> GetPlotlines();
    Task<PlotlineData> CreateAsync(string name, string color = "#3498db");
    Task UpdateAsync(PlotlineData plotline);
    Task DeleteAsync(string plotlineId);
    Task ReorderAsync(IReadOnlyList<string> orderedIds);

    /// <summary>Toggles the scene's membership in the given plotline.</summary>
    Task ToggleSceneAsync(string chapterGuid, string sceneId, string plotlineId);

    /// <summary>True when the scene currently belongs to the plotline.</summary>
    bool IsSceneInPlotline(SceneData scene, string plotlineId);
}
