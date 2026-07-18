using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Project lifecycle: open, state, structure edits, recents.</summary>
public sealed class ProjectRpc
{
    private readonly Workspace _workspace;

    public ProjectRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    [JsonRpcMethod("project/open")]
    public Task<ProjectStateDto> OpenAsync(string path) => _workspace.OpenProjectAsync(path);

    [JsonRpcMethod("project/getState")]
    public ProjectStateDto GetState() => _workspace.BuildState();

    [JsonRpcMethod("project/recent")]
    public Task<RecentProjectDto[]> GetRecentAsync() => _workspace.GetRecentProjectsAsync();

    [JsonRpcMethod("project/create")]
    public async Task<ProjectStateDto> CreateAsync(string parentDirectory, string projectName, string firstBookName)
    {
        await _workspace.Projects.CreateProjectAsync(parentDirectory, projectName, firstBookName);
        var root = _workspace.Projects.ProjectRoot!;
        return await _workspace.OpenProjectAsync(root);
    }

    [JsonRpcMethod("project/createChapter")]
    public async Task<ProjectStateDto> CreateChapterAsync(string title)
    {
        await _workspace.Projects.CreateChapterAsync(title);
        return _workspace.BuildState();
    }

    [JsonRpcMethod("project/createScene")]
    public async Task<ProjectStateDto> CreateSceneAsync(string chapterGuid, string title)
    {
        await _workspace.Projects.CreateSceneAsync(chapterGuid, title);
        return _workspace.BuildState();
    }

    [JsonRpcMethod("project/renameChapter")]
    public async Task<ProjectStateDto> RenameChapterAsync(string chapterGuid, string newTitle)
    {
        await _workspace.Projects.RenameChapterAsync(chapterGuid, newTitle);
        return _workspace.BuildState();
    }

    [JsonRpcMethod("project/renameScene")]
    public async Task<ProjectStateDto> RenameSceneAsync(string chapterGuid, string sceneId, string newTitle)
    {
        await _workspace.Projects.RenameSceneAsync(chapterGuid, sceneId, newTitle);
        return _workspace.BuildState();
    }

    [JsonRpcMethod("project/deleteChapter")]
    public async Task<ProjectStateDto> DeleteChapterAsync(string chapterGuid)
    {
        await _workspace.Projects.DeleteChapterAsync(chapterGuid);
        return _workspace.BuildState();
    }

    [JsonRpcMethod("project/deleteScene")]
    public async Task<ProjectStateDto> DeleteSceneAsync(string chapterGuid, string sceneId)
    {
        await _workspace.Projects.DeleteSceneAsync(chapterGuid, sceneId);
        return _workspace.BuildState();
    }

    [JsonRpcMethod("project/setChapterStatus")]
    public async Task<ProjectStateDto> SetChapterStatusAsync(string chapterGuid, string status)
    {
        var chapter = _workspace.ResolveChapter(chapterGuid);
        chapter.Status = Enum.Parse<Novalist.Core.Models.ChapterStatus>(status);
        await _workspace.Projects.SaveScenesAsync();
        return _workspace.BuildState();
    }
}
