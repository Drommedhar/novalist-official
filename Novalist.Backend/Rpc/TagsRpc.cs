using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>The project's tag vocabulary, across scenes, Codex and research.</summary>
public sealed class TagsRpc
{
    private readonly Workspace _workspace;

    public TagsRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private TagService Service
        => new(_workspace.Projects, new EntityService(_workspace.Projects));

    /// <summary>Every tag with its colour and what carries it.</summary>
    [JsonRpcMethod("tags/list")]
    public async Task<TagUsageDto[]> ListAsync()
        => [.. (await Service.ListAsync()).Select(t => new TagUsageDto(
            t.Name, t.Color, t.Scenes, t.Entities, t.Research, t.Total))];

    [JsonRpcMethod("tags/setColor")]
    public async Task<TagUsageDto[]> SetColorAsync(string name, string color)
    {
        await Service.SetColorAsync(name, color);
        return await ListAsync();
    }

    /// <summary>
    /// Renames a tag everywhere. Renaming onto one that already exists merges
    /// them, which is the only safe way to fix a vocabulary that drifted.
    /// </summary>
    [JsonRpcMethod("tags/rename")]
    public async Task<TagUsageDto[]> RenameAsync(string from, string to)
    {
        await Service.RenameAsync(from, to);
        return await ListAsync();
    }

    [JsonRpcMethod("tags/delete")]
    public async Task<TagUsageDto[]> DeleteAsync(string name)
    {
        await Service.DeleteAsync(name);
        return await ListAsync();
    }
}

/// <summary>One tag: its colour, and how many of each kind of thing carries it.</summary>
public sealed record TagUsageDto(
    string Name, string Color, int Scenes, int Entities, int Research, int Total);
