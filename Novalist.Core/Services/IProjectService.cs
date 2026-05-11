using Novalist.Core.Models;

namespace Novalist.Core.Services;

public interface IProjectService
{
    ProjectMetadata? CurrentProject { get; }
    ProjectSettings ProjectSettings { get; }
    BookData? ActiveBook { get; }
    ScenesManifest? ScenesManifest { get; }
    string? ProjectRoot { get; }
    string? ActiveBookRoot { get; }
    string? WorldBibleRoot { get; }
    bool IsProjectLoaded { get; }

    Task<ProjectMetadata> CreateProjectAsync(string parentDirectory, string projectName, string firstBookName);
    Task<ProjectMetadata> LoadProjectAsync(string projectDirectory);
    Task SaveProjectAsync();
    Task SaveProjectSettingsAsync();
    Task SaveScenesAsync();

    // Project management
    Task RenameProjectAsync(string newName);

    // Book management
    Task<BookData> CreateBookAsync(string bookName);
    Task SwitchBookAsync(string bookId);
    Task RenameBookAsync(string bookId, string newName);
    Task DeleteBookAsync(string bookId);

    // World Bible
    Task InitializeWorldBibleAsync();

    Task<ChapterData> CreateChapterAsync(string title, string date = "");
    Task<SceneData> CreateSceneAsync(string chapterGuid, string sceneTitle, string date = "");
    Task DeleteChapterAsync(string chapterGuid);
    Task DeleteSceneAsync(string chapterGuid, string sceneId);
    Task SetChapterDateAsync(string chapterGuid, string date);
    Task SetSceneDateAsync(string chapterGuid, string sceneId, string date);
    Task SetChapterFavoriteAsync(string chapterGuid, bool favorite);
    Task SetSceneFavoriteAsync(string chapterGuid, string sceneId, bool favorite);
    Task SetSceneAnalysisOverridesAsync(string chapterGuid, string sceneId, SceneAnalysisOverrides? overrides);
    Task ReorderChapterAsync(string chapterGuid, int newOrder);
    Task ReorderSceneAsync(string chapterGuid, string sceneId, int newOrder);
    Task MoveChaptersAsync(IReadOnlyList<string> chapterGuids, int targetIndex);
    Task MoveScenesAsync(IReadOnlyList<string> sceneIds, string targetChapterGuid, int targetIndex);
    Task RenameChapterAsync(string chapterGuid, string newTitle);
    Task RenameSceneAsync(string chapterGuid, string sceneId, string newTitle);

    string GetChapterFolderPath(ChapterData chapter);
    string GetSceneFilePath(ChapterData chapter, SceneData scene);
    Task<string> ReadSceneContentAsync(ChapterData chapter, SceneData scene);
    Task WriteSceneContentAsync(ChapterData chapter, SceneData scene, string content);

    List<ChapterData> GetChaptersOrdered();
    List<SceneData> GetScenesForChapter(string chapterGuid);
}
