using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Saved scene queries (smart lists): CRUD and evaluation.</summary>
public sealed class SmartListsRpc
{
    private readonly SmartListService _smartLists;

    public SmartListsRpc(Workspace workspace)
    {
        _smartLists = new SmartListService(workspace.Projects, new EntityService(workspace.Projects));
    }

    [JsonRpcMethod("smartLists/list")]
    public SmartListDto[] List() =>
        _smartLists.GetAll()
            .Select(l => new SmartListDto(l.Id, l.Name, l.ChapterStatus, l.PovContains, l.Tag))
            .ToArray();

    [JsonRpcMethod("smartLists/save")]
    public async Task<SmartListDto[]> SaveAsync(
        string? id, string name, string? chapterStatus, string? povContains, string? tag)
    {
        var existing = id == null ? null : _smartLists.GetAll().FirstOrDefault(l => l.Id == id);
        var list = existing ?? new SmartList();
        list.Name = name;
        list.ChapterStatus = Normalize(chapterStatus);
        list.PovContains = Normalize(povContains);
        list.Tag = Normalize(tag);
        await _smartLists.SaveAsync(list);
        return List();
    }

    [JsonRpcMethod("smartLists/delete")]
    public async Task<SmartListDto[]> DeleteAsync(string id)
    {
        await _smartLists.DeleteAsync(id);
        return List();
    }

    [JsonRpcMethod("smartLists/evaluate")]
    public async Task<SmartListMatchDto[]> EvaluateAsync(string id)
    {
        var list = _smartLists.GetAll().FirstOrDefault(l => l.Id == id)
            ?? throw new InvalidOperationException("Unknown smart list.");
        var matches = await _smartLists.EvaluateAsync(list);
        return matches
            .Select(m => new SmartListMatchDto(m.Chapter.Guid, m.Chapter.Title, m.Scene.Id, m.Scene.Title))
            .ToArray();
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

public sealed record SmartListDto(
    string Id, string Name, string? ChapterStatus, string? PovContains, string? Tag);

public sealed record SmartListMatchDto(
    string ChapterGuid, string ChapterTitle, string SceneId, string SceneTitle);
