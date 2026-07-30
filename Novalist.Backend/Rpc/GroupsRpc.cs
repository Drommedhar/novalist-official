using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Factions, houses, crews and families: named sets that span entity types.
///
/// The group was a bare string on each entry. It could say a house and a ship
/// both belong to the Ravens and nothing else - no colour, no description, no
/// count, and no rename, so correcting "the Ravens" to "House Raven" meant
/// opening every entry that said the first thing.
///
/// Renaming and deleting reach into the entries, because a registry that only
/// changes itself leaves the Codex saying the old thing.
/// </summary>
public sealed class GroupsRpc
{
    private readonly Workspace _workspace;
    private readonly IEntityService _entities;

    public GroupsRpc(Workspace workspace)
    {
        _workspace = workspace;
        _entities = new EntityService(workspace.Projects);
    }

    [JsonRpcMethod("groups/list")]
    public async Task<EntityGroupDto[]> ListAsync()
    {
        var book = _workspace.Projects.ActiveBook;
        if (book == null) return [];

        var counts = await CountsAsync();
        return [.. book.Groups
            .OrderBy(g => g.Order)
            .Select(g => new EntityGroupDto(
                g.Id, g.Name, g.Color, g.Description,
                counts.TryGetValue(g.Name, out var n) ? n : 0))];
    }

    /// <summary>
    /// Replaces the registry. Unnamed entries are dropped and duplicates fold
    /// away: two groups spelt the same are the problem a registry prevents.
    /// </summary>
    [JsonRpcMethod("groups/save")]
    public async Task<EntityGroupDto[]> SaveAsync(EntityGroupDto[] groups)
    {
        var book = _workspace.Projects.ActiveBook
            ?? throw new InvalidOperationException("No active book.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = 0;
        book.Groups = [.. (groups ?? [])
            .Where(g => !string.IsNullOrWhiteSpace(g.Name))
            .Where(g => seen.Add(g.Name!.Trim()))
            .Select(g => new EntityGroup
            {
                Id = string.IsNullOrWhiteSpace(g.Id) ? Guid.NewGuid().ToString() : g.Id!,
                Name = g.Name!.Trim(),
                Color = string.IsNullOrWhiteSpace(g.Color) ? "#8b8b8b" : g.Color!.Trim(),
                Description = (g.Description ?? string.Empty).Trim(),
                Order = order++
            })];

        await _workspace.Projects.SaveProjectAsync();
        return await ListAsync();
    }

    /// <summary>
    /// Renames a group and every entry in it at once. This is the whole reason
    /// the registry exists.
    /// </summary>
    [JsonRpcMethod("groups/rename")]
    public async Task<EntityGroupDto[]> RenameAsync(string groupId, string name)
    {
        var book = _workspace.Projects.ActiveBook;
        var group = book?.Groups.FirstOrDefault(g => g.Id == groupId);
        var trimmed = (name ?? string.Empty).Trim();
        if (book == null || group == null || trimmed.Length == 0) return await ListAsync();

        // Renaming onto a name already in use would merge two groups by
        // accident, which is a thing the writer has to ask for.
        if (book.Groups.Any(g => g.Id != groupId
                                 && string.Equals(g.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            return await ListAsync();

        var old = group.Name;
        group.Name = trimmed;
        await _workspace.Projects.SaveProjectAsync();
        await RewriteAsync(old, trimmed);
        return await ListAsync();
    }

    /// <summary>
    /// Removes a group from the registry, and from every entry unless asked
    /// otherwise. Leaving forty entries claiming a group nobody lists is how
    /// this drifted in the first place.
    /// </summary>
    [JsonRpcMethod("groups/delete")]
    public async Task<EntityGroupDto[]> DeleteAsync(string groupId, bool clearFromEntities = true)
    {
        var book = _workspace.Projects.ActiveBook;
        var group = book?.Groups.FirstOrDefault(g => g.Id == groupId);
        if (book == null || group == null) return await ListAsync();

        book.Groups.Remove(group);
        await _workspace.Projects.SaveProjectAsync();
        if (clearFromEntities) await RewriteAsync(group.Name, string.Empty);
        return await ListAsync();
    }

    /// <summary>
    /// Adds every group name the Codex already uses to the registry.
    ///
    /// Without this an existing project starts on an empty list, which makes
    /// the colours and the rename useless to whoever has the most groups.
    /// </summary>
    [JsonRpcMethod("groups/harvest")]
    public async Task<EntityGroupDto[]> HarvestAsync()
    {
        var book = _workspace.Projects.ActiveBook
            ?? throw new InvalidOperationException("No active book.");

        var known = book.Groups.Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var order = book.Groups.Count == 0 ? 0 : book.Groups.Max(g => g.Order) + 1;
        var added = false;

        foreach (var name in (await CountsAsync()).Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            if (!known.Add(name)) continue;
            book.Groups.Add(new EntityGroup { Name = name, Order = order++ });
            added = true;
        }

        if (added) await _workspace.Projects.SaveProjectAsync();
        return await ListAsync();
    }

    /// <summary>The entries in a group, across every type.</summary>
    [JsonRpcMethod("groups/members")]
    public async Task<GroupMemberDto[]> MembersAsync(string groupId)
    {
        var group = _workspace.Projects.ActiveBook?.Groups.FirstOrDefault(g => g.Id == groupId);
        if (group == null) return [];

        return [.. (await AllAsync())
            .Where(e => string.Equals(e.Group, group.Name, StringComparison.OrdinalIgnoreCase))
            .Select(e => new GroupMemberDto(e.Id, TypeKeyOf(e), e.DisplayName))];
    }

    /// <summary>How many entries carry each group name.</summary>
    private async Task<Dictionary<string, int>> CountsAsync()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in await AllAsync())
        {
            if (string.IsNullOrWhiteSpace(entity.Group)) continue;
            var name = entity.Group.Trim();
            counts[name] = counts.TryGetValue(name, out var n) ? n + 1 : 1;
        }
        return counts;
    }

    /// <summary>Rewrites one group name across every entry of every type.</summary>
    private async Task RewriteAsync(string from, string to)
    {
        foreach (var entity in await AllAsync())
        {
            if (!string.Equals(entity.Group, from, StringComparison.OrdinalIgnoreCase)) continue;
            entity.Group = to;
            await SaveAsync(entity);
        }
    }

    private async Task<List<IEntityData>> AllAsync()
    {
        var all = new List<IEntityData>();
        all.AddRange(await _entities.LoadCharactersAsync());
        all.AddRange(await _entities.LoadLocationsAsync());
        all.AddRange(await _entities.LoadItemsAsync());
        all.AddRange(await _entities.LoadLoreAsync());
        foreach (var typeDef in _entities.GetCustomEntityTypes())
            all.AddRange(await _entities.LoadCustomEntitiesAsync(typeDef.TypeKey));
        return all;
    }

    private async Task SaveAsync(IEntityData entity)
    {
        switch (entity)
        {
            case CharacterData c: await _entities.SaveCharacterAsync(c); break;
            case LocationData l: await _entities.SaveLocationAsync(l); break;
            case ItemData i: await _entities.SaveItemAsync(i); break;
            case LoreData lo: await _entities.SaveLoreAsync(lo); break;
            default: await _entities.SaveCustomEntityAsync((CustomEntityData)entity); break;
        }
    }

    /// <summary>Which kind of entry this is, in the key the Codex navigates by.</summary>
    private static string TypeKeyOf(IEntityData entity) => entity switch
    {
        CharacterData => "character",
        LocationData => "location",
        ItemData => "item",
        LoreData => "lore",
        _ => ((CustomEntityData)entity).EntityTypeKey
    };
}

/// <summary>One group, with how many entries belong to it.</summary>
public sealed record EntityGroupDto(
    string? Id, string? Name, string? Color, string? Description, int MemberCount);

/// <summary>One entry inside a group: enough to draw the row and open it.</summary>
public sealed record GroupMemberDto(string Id, string TypeKey, string DisplayName);
