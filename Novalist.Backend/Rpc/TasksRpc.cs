using Novalist.Core.Models;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>One thing to do before the book is finished.</summary>
public sealed record TaskDto(
    string Id, string Text, string List, bool Done, DateTime? DoneAt,
    string ChapterGuid, string SceneId, int Order);

/// <summary>
/// Things to do, in named lists.
///
/// Novalist had todo comments, which are anchored to a passage and belong to
/// the scene they sit in. "Check the dates in act two", "read the whole thing
/// aloud", "decide whether Tomas survives" belong to no passage and to no
/// scene, so they were kept on paper.
/// </summary>
public class TasksRpc(Workspace workspace)
{
    private readonly Workspace _workspace = workspace;

    private ProjectMetadata Project => _workspace.Projects.CurrentProject
        ?? throw new InvalidOperationException("No project loaded.");

    private static TaskDto ToDto(ProjectTask t)
        => new(t.Id, t.Text, t.List, t.Done, t.DoneAt, t.ChapterGuid, t.SceneId, t.Order);

    /// <summary>
    /// Everything to do, unfinished first inside each list.
    ///
    /// A finished item stays visible rather than disappearing: a checklist is
    /// a record of a revision pass, and one that empties as it is worked reads
    /// as though nothing was done.
    /// </summary>
    [JsonRpcMethod("tasks/list")]
    public TaskDto[] List()
        => [.. (_workspace.Projects.CurrentProject?.Tasks ?? [])
            .OrderBy(t => t.List, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.Done)
            .ThenBy(t => t.Order)
            .Select(ToDto)];

    /// <summary>Adds or rewrites a task.</summary>
    [JsonRpcMethod("tasks/save")]
    public async Task<TaskDto[]> SaveAsync(
        string? id, string text, string? list = null,
        string? chapterGuid = null, string? sceneId = null)
    {
        var what = (text ?? string.Empty).Trim();
        if (what.Length == 0)
            throw new InvalidOperationException("A task needs something to do.");

        var project = Project;
        var task = project.Tasks.FirstOrDefault(t => t.Id == id);
        if (task == null)
        {
            task = new ProjectTask
            {
                Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString() : id,
                Order = project.Tasks.Count
            };
            project.Tasks.Add(task);
        }

        task.Text = what;
        task.List = (list ?? task.List).Trim();
        task.ChapterGuid = (chapterGuid ?? task.ChapterGuid).Trim();
        task.SceneId = (sceneId ?? task.SceneId).Trim();

        await _workspace.Projects.SaveProjectAsync();
        return List();
    }

    /// <summary>Ticks a task, or unticks it.</summary>
    [JsonRpcMethod("tasks/setDone")]
    public async Task<TaskDto[]> SetDoneAsync(string id, bool done)
    {
        var task = Project.Tasks.FirstOrDefault(t => t.Id == id);
        if (task == null) return List();

        task.Done = done;
        // Cleared on unticking: a date saying it was finished, on a row that
        // is not, is worse than no date.
        task.DoneAt = done ? DateTime.UtcNow : null;
        await _workspace.Projects.SaveProjectAsync();
        return List();
    }

    /// <summary>Removes a task for good.</summary>
    [JsonRpcMethod("tasks/remove")]
    public async Task<TaskDto[]> RemoveAsync(string id)
    {
        if (Project.Tasks.RemoveAll(t => t.Id == id) > 0)
            await _workspace.Projects.SaveProjectAsync();
        return List();
    }

    /// <summary>
    /// Unticks every task in a list, so a revision checklist can be run again
    /// on the next pass rather than being retyped.
    /// </summary>
    [JsonRpcMethod("tasks/resetList")]
    public async Task<TaskDto[]> ResetListAsync(string list)
    {
        var name = (list ?? string.Empty).Trim();
        var touched = 0;
        foreach (var task in Project.Tasks.Where(
                     t => t.List.Trim().Equals(name, StringComparison.CurrentCultureIgnoreCase)))
        {
            if (!task.Done) continue;
            task.Done = false;
            task.DoneAt = null;
            touched++;
        }

        if (touched > 0) await _workspace.Projects.SaveProjectAsync();
        return List();
    }
}
