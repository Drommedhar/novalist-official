using Novalist.Core.Models;

namespace Novalist.Core.Services;

public interface ISnapshotService
{
    Task<SceneSnapshot> TakeAsync(ChapterData chapter, SceneData scene, string label);
    Task<IReadOnlyList<SceneSnapshot>> ListAsync(SceneData scene);
    Task<SceneSnapshot?> LoadAsync(SceneData scene, string snapshotId);
    Task<bool> RestoreAsync(ChapterData chapter, SceneData scene, string snapshotId);
    Task DeleteAsync(SceneData scene, string snapshotId);
}
