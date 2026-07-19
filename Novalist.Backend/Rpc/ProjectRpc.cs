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

    private static readonly Novalist.Core.Services.ProjectTemplateService ProjectTemplates = new();

    [JsonRpcMethod("project/templates")]
    public ProjectTemplateDto[] GetProjectTemplates() =>
        ProjectTemplates.GetTemplates()
            .Select(t => new ProjectTemplateDto(t.Id, t.DisplayName, t.Description))
            .ToArray();

    [JsonRpcMethod("project/create")]
    public async Task<ProjectStateDto> CreateAsync(
        string parentDirectory, string projectName, string firstBookName, string? templateId = null)
    {
        await _workspace.Projects.CreateProjectAsync(parentDirectory, projectName, firstBookName);
        if (!string.IsNullOrWhiteSpace(templateId)
            && ProjectTemplates.GetById(templateId) is { } template)
        {
            await ProjectTemplates.ApplyAsync(_workspace.Projects, template);
        }
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

    [JsonRpcMethod("project/rename")]
    public async Task<ProjectStateDto> RenameAsync(string newName)
    {
        await _workspace.Projects.RenameProjectAsync(newName);
        return _workspace.BuildState();
    }

    [JsonRpcMethod("project/reorderChapter")]
    public async Task<ProjectStateDto> ReorderChapterAsync(string chapterGuid, int newOrder)
    {
        await _workspace.Projects.ReorderChapterAsync(chapterGuid, newOrder);
        return _workspace.BuildState();
    }

    [JsonRpcMethod("project/reorderScene")]
    public async Task<ProjectStateDto> ReorderSceneAsync(string chapterGuid, string sceneId, int newOrder)
    {
        await _workspace.Projects.ReorderSceneAsync(chapterGuid, sceneId, newOrder);
        return _workspace.BuildState();
    }

    [JsonRpcMethod("project/moveScenes")]
    public async Task<ProjectStateDto> MoveScenesAsync(
        string[] sceneIds, string targetChapterGuid, int targetIndex)
    {
        await _workspace.Projects.MoveScenesAsync(sceneIds, targetChapterGuid, targetIndex);
        return _workspace.BuildState();
    }

    [JsonRpcMethod("project/switchBook")]
    public async Task<ProjectStateDto> SwitchBookAsync(string bookId)
    {
        await _workspace.Projects.SwitchBookAsync(bookId);
        return _workspace.BuildState();
    }

    [JsonRpcMethod("project/createBook")]
    public async Task<ProjectStateDto> CreateBookAsync(string name)
    {
        await _workspace.Projects.CreateBookAsync(name);
        return _workspace.BuildState();
    }

    [JsonRpcMethod("project/drafts")]
    public DraftDto[] GetDrafts()
    {
        var book = _workspace.Projects.ActiveBook
            ?? throw new InvalidOperationException("No project open.");
        return book.Drafts
            .Select(d => new DraftDto(d.Id, d.Name, d.Id == book.ActiveDraftId))
            .ToArray();
    }

    [JsonRpcMethod("project/createDraft")]
    public async Task<DraftDto[]> CreateDraftAsync(string name, string? cloneFromDraftId)
    {
        await _workspace.Projects.CreateDraftAsync(name, cloneFromDraftId);
        return GetDrafts();
    }

    [JsonRpcMethod("project/switchDraft")]
    public async Task<ProjectStateDto> SwitchDraftAsync(string draftId)
    {
        await _workspace.Projects.SwitchDraftAsync(draftId);
        return _workspace.BuildState();
    }

    [JsonRpcMethod("project/setChapterAct")]
    public async Task<ProjectStateDto> SetChapterActAsync(string chapterGuid, string act)
    {
        var chapter = _workspace.ResolveChapter(chapterGuid);
        chapter.Act = act;
        await _workspace.Projects.SaveScenesAsync();
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

public sealed record DraftDto(string Id, string Name, bool IsActive);

public sealed record ProjectTemplateDto(string Id, string Name, string Description);
