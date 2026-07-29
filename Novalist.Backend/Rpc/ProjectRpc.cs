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

    /// <param name="templateId">
    /// A scene template to start from, or null for a blank scene - which is
    /// what a new scene has always been and stays by default.
    /// </param>
    [JsonRpcMethod("project/createScene")]
    public async Task<ProjectStateDto> CreateSceneAsync(
        string chapterGuid, string title, string? templateId = null)
    {
        var template = string.IsNullOrWhiteSpace(templateId)
            ? null
            : _workspace.Projects.ActiveBook?.SceneTemplates.FirstOrDefault(t => t.Id == templateId);
        await _workspace.Projects.CreateSceneAsync(chapterGuid, title, template: template);
        return _workspace.BuildState();
    }

    [JsonRpcMethod("project/renameChapter")]
    public async Task<ProjectStateDto> RenameChapterAsync(string chapterGuid, string newTitle)
    {
        await _workspace.Projects.RenameChapterAsync(chapterGuid, newTitle);
        return _workspace.BuildState();
    }

    /// <summary>
    /// The chapter's opener: a subtitle under the title, and whether the
    /// heading is printed at all. Both are export typography rather than
    /// binder data, which is why they sit beside the rename rather than in it.
    /// </summary>
    [JsonRpcMethod("project/setChapterOpener")]
    public async Task<ProjectStateDto> SetChapterOpenerAsync(
        string chapterGuid, string? subtitle, bool hideHeading)
    {
        var chapter = _workspace.Projects.GetChaptersOrdered()
            .FirstOrDefault(c => c.Guid == chapterGuid)
            ?? throw new InvalidOperationException("Unknown chapter.");

        chapter.Subtitle = string.IsNullOrWhiteSpace(subtitle) ? null : subtitle.Trim();
        chapter.HideHeading = hideHeading;
        await _workspace.Projects.SaveProjectAsync();
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

    /// <summary>Chapters in the trash, most recently deleted first.</summary>
    [JsonRpcMethod("project/trashedChapters")]
    public TrashedChapterDto[] TrashedChapters() =>
        _workspace.Projects.GetTrashedChapters()
            .Select(c => new TrashedChapterDto(
                c.Guid, c.Title, c.DeletedAt?.ToString("o") ?? string.Empty,
                _workspace.Projects.GetArchivedScenes().Count(s => s.OriginChapterGuid == c.Guid)))
            .ToArray();

    /// <summary>Brings a chapter back from the trash with its scenes.</summary>
    [JsonRpcMethod("project/restoreChapter")]
    public async Task<ProjectStateDto> RestoreChapterAsync(string chapterGuid)
    {
        await _workspace.Projects.RestoreChapterAsync(chapterGuid);
        return _workspace.BuildState();
    }

    /// <summary>Erases a trashed chapter and its scenes. The only path that destroys anything.</summary>
    [JsonRpcMethod("project/purgeChapter")]
    public Task<bool> PurgeChapterAsync(string chapterGuid) =>
        _workspace.Projects.PurgeChapterAsync(chapterGuid);

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
        _workspace.RaiseBookChanged();
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

    [JsonRpcMethod("project/deleteDraft")]
    public async Task<DraftDto[]> DeleteDraftAsync(string draftId)
    {
        await _workspace.Projects.DeleteDraftAsync(draftId);
        return GetDrafts();
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

    [JsonRpcMethod("project/getSceneEdit")]
    public SceneEditDto GetSceneEdit(string chapterGuid, string sceneId)
    {
        var (_, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        var range = scene.DateRange;
        return new SceneEditDto(
            scene.AnalysisOverrides?.Pov ?? string.Empty,
            range?.Start ?? string.Empty,
            range?.End ?? string.Empty,
            range?.Note ?? string.Empty);
    }

    [JsonRpcMethod("project/setSceneDateRange")]
    public async Task<SceneEditDto> SetSceneDateRangeAsync(
        string chapterGuid, string sceneId, string start, string end, string note)
    {
        var range = new Novalist.Core.Models.StoryDateRange
        {
            Start = start.Trim(),
            End = end.Trim(),
            Note = note.Trim(),
        };
        await _workspace.Projects.SetSceneDateRangeAsync(
            chapterGuid, sceneId, range.HasValue ? range : null);
        return GetSceneEdit(chapterGuid, sceneId);
    }
}

public sealed record DraftDto(string Id, string Name, bool IsActive);

public sealed record ProjectTemplateDto(string Id, string Name, string Description);

public sealed record SceneEditDto(string Pov, string DateStart, string DateEnd, string DateNote);

/// <summary>One chapter in the trash, with how many scenes came with it.</summary>
public sealed record TrashedChapterDto(
    string Guid, string Title, string DeletedAt, int SceneCount);
