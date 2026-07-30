using Novalist.Core.Models;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Hand-curated scene sets.
///
/// Saved lists are queries and recompute on open; the nearest thing to curation
/// was a favourite flag no RPC exposed. So the eight scenes to fix before
/// Tuesday, or the run being read to a writing group, had nowhere to live.
///
/// Membership is stored on the collection and never on the scene, so a scene can
/// belong to five of them without anything about the manuscript changing.
/// </summary>
public sealed class CollectionsRpc
{
    private readonly Workspace _workspace;

    public CollectionsRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    [JsonRpcMethod("collections/list")]
    public CollectionDto[] List()
    {
        var book = _workspace.Projects.ActiveBook;
        if (book == null) return [];

        var titles = SceneTitles();
        return [.. book.Collections
            .OrderBy(c => c.Order)
            .Select(c => new CollectionDto(
                c.Id,
                c.Name,
                // Only scenes that still exist. A collection outlives the
                // scenes in it, and a row that opens nothing is worse than a
                // shorter list.
                [.. c.SceneIds
                    .Where(titles.ContainsKey)
                    .Select(id => new CollectionSceneDto(id, titles[id].Chapter, titles[id].Title))]))];
    }

    [JsonRpcMethod("collections/create")]
    public async Task<CollectionDto[]> CreateAsync(string name, string[]? sceneIds = null)
    {
        var book = _workspace.Projects.ActiveBook
            ?? throw new InvalidOperationException("No active book.");

        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0) return List();

        book.Collections.Add(new SceneCollection
        {
            Name = trimmed,
            Order = book.Collections.Count == 0 ? 1 : book.Collections.Max(c => c.Order) + 1,
            SceneIds = [.. (sceneIds ?? []).Distinct(StringComparer.Ordinal)]
        });
        await _workspace.Projects.SaveProjectAsync();
        return List();
    }

    [JsonRpcMethod("collections/rename")]
    public async Task<CollectionDto[]> RenameAsync(string collectionId, string name)
    {
        var collection = Find(collectionId);
        var trimmed = (name ?? string.Empty).Trim();
        // A collection with no name is a row nobody can tell from the next one.
        if (collection != null && trimmed.Length > 0)
        {
            collection.Name = trimmed;
            await _workspace.Projects.SaveProjectAsync();
        }
        return List();
    }

    [JsonRpcMethod("collections/delete")]
    public async Task<CollectionDto[]> DeleteAsync(string collectionId)
    {
        var book = _workspace.Projects.ActiveBook;
        var collection = Find(collectionId);
        if (book != null && collection != null)
        {
            // Only the set goes. Deleting a collection has never been a way to
            // delete scenes and must never become one.
            book.Collections.Remove(collection);
            await _workspace.Projects.SaveProjectAsync();
        }
        return List();
    }

    /// <summary>Adds scenes, keeping the order they arrive in and skipping duplicates.</summary>
    [JsonRpcMethod("collections/add")]
    public async Task<CollectionDto[]> AddAsync(string collectionId, string[] sceneIds)
    {
        var collection = Find(collectionId);
        if (collection == null) return List();

        var added = false;
        foreach (var id in sceneIds ?? [])
        {
            if (string.IsNullOrWhiteSpace(id) || collection.SceneIds.Contains(id, StringComparer.Ordinal))
                continue;
            collection.SceneIds.Add(id);
            added = true;
        }
        if (added) await _workspace.Projects.SaveProjectAsync();
        return List();
    }

    [JsonRpcMethod("collections/remove")]
    public async Task<CollectionDto[]> RemoveAsync(string collectionId, string sceneId)
    {
        var collection = Find(collectionId);
        if (collection?.SceneIds.Remove(sceneId) == true)
            await _workspace.Projects.SaveProjectAsync();
        return List();
    }

    /// <summary>
    /// Moves one scene within a collection. The order is the writer's statement
    /// about the set - a revision run is often deliberately out of reading
    /// order - so it is stored rather than recomputed.
    /// </summary>
    [JsonRpcMethod("collections/move")]
    public async Task<CollectionDto[]> MoveAsync(string collectionId, string sceneId, int toIndex)
    {
        var collection = Find(collectionId);
        var from = collection?.SceneIds.IndexOf(sceneId) ?? -1;
        if (collection == null || from < 0) return List();

        collection.SceneIds.RemoveAt(from);
        collection.SceneIds.Insert(Math.Clamp(toIndex, 0, collection.SceneIds.Count), sceneId);
        await _workspace.Projects.SaveProjectAsync();
        return List();
    }

    private SceneCollection? Find(string collectionId)
        => _workspace.Projects.ActiveBook?.Collections
            .FirstOrDefault(c => c.Id == collectionId);

    /// <summary>Every live scene by id, with the chapter it is in.</summary>
    private Dictionary<string, (string Chapter, string Title)> SceneTitles()
    {
        var map = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (var chapter in _workspace.Projects.GetChaptersOrdered())
            foreach (var scene in _workspace.Projects.GetScenesForChapter(chapter.Guid))
                map[scene.Id] = (chapter.Guid, scene.Title);
        return map;
    }
}

/// <summary>One collection, with the scenes still in it.</summary>
public sealed record CollectionDto(
    string Id, string Name, IReadOnlyList<CollectionSceneDto> Scenes);

/// <summary>A scene inside a collection: enough to draw the row and open it.</summary>
public sealed record CollectionSceneDto(string SceneId, string ChapterGuid, string Title);
