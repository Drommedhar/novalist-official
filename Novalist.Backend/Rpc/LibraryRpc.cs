using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Project image gallery and research library.</summary>
public sealed class LibraryRpc
{
    private readonly EntityService _entities;
    private readonly ResearchService _research;

    public LibraryRpc(Workspace workspace)
    {
        _entities = new EntityService(workspace.Projects);
        _research = new ResearchService(workspace.Projects, workspace.FileService);
    }

    [JsonRpcMethod("gallery/list")]
    public GalleryImageDto[] ListImages() =>
        _entities.GetProjectImages()
            .Select(path => new GalleryImageDto(path, _entities.ResolveProjectRelativeImage(path)))
            .ToArray();

    [JsonRpcMethod("research/list")]
    public ResearchItemDto[] ListResearch() =>
        _research.GetAll()
            .OrderBy(r => r.Order)
            .Select(r => new ResearchItemDto(
                r.Id, r.Title, r.Type.ToString(), r.Content, r.Tags.ToArray()))
            .ToArray();

    [JsonRpcMethod("research/save")]
    public async Task<ResearchItemDto[]> SaveResearchAsync(
        string? id, string title, string type, string content, string[] tags)
    {
        var existing = id == null ? null : _research.GetAll().FirstOrDefault(r => r.Id == id);
        var item = existing ?? new ResearchItem { Order = _research.GetAll().Count };
        item.Title = title;
        item.Type = Enum.Parse<ResearchItemType>(type);
        item.Content = content;
        item.Tags = tags.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        item.UpdatedAt = DateTime.UtcNow;
        await _research.SaveAsync(item);
        return ListResearch();
    }

    [JsonRpcMethod("research/delete")]
    public async Task<ResearchItemDto[]> DeleteResearchAsync(string id)
    {
        await _research.DeleteAsync(id);
        return ListResearch();
    }
}

public sealed record ResearchItemDto(
    string Id, string Title, string Type, string Content, IReadOnlyList<string> Tags);

public sealed record GalleryImageDto(string Path, string Url);
