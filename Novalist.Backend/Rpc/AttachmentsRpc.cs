using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Files kept with a Codex entry.
///
/// Entries could hold images and nothing else, so a recorded interview with the
/// person a character is based on, the deed that settles who owns the house, or
/// a clip of how a name is pronounced had to be filed as a Research item and
/// linked back - stored and surfaced somewhere other than the entry it is about.
/// </summary>
public sealed class AttachmentsRpc
{
    private readonly Workspace _workspace;
    private readonly IEntityService _entities;

    public AttachmentsRpc(Workspace workspace)
    {
        _workspace = workspace;
        _entities = new EntityService(workspace.Projects);
    }

    [JsonRpcMethod("attachments/list")]
    public async Task<AttachmentDto[]> ListAsync(string type, string id)
    {
        var entity = await FindAsync(type, id);
        return entity == null ? [] : [.. AttachmentsOf(entity).Select(ToDto)];
    }

    /// <summary>
    /// Copies a file into the project and attaches it. The kind is read from
    /// the extension so the writer sees a recording as a recording without
    /// having to say so.
    /// </summary>
    [JsonRpcMethod("attachments/add")]
    public async Task<AttachmentDto[]> AddAsync(string type, string id, string sourcePath, string? name = null)
    {
        var entity = await FindAsync(type, id) ?? throw Unknown(id);
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new InvalidOperationException("No such file.");

        var stored = await _entities.ImportAttachmentAsync(sourcePath);
        AttachmentsOf(entity).Add(new EntityAttachment
        {
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(sourcePath) : name.Trim(),
            Path = stored,
            Kind = AttachmentKinds.Of(sourcePath)
        });
        await SaveAsync(entity);
        return [.. AttachmentsOf(entity).Select(ToDto)];
    }

    /// <summary>
    /// Attaches a web address. Nothing is copied - a link is a link, and
    /// pretending to have saved the page would be a promise this cannot keep.
    /// </summary>
    [JsonRpcMethod("attachments/addLink")]
    public async Task<AttachmentDto[]> AddLinkAsync(string type, string id, string url, string? name = null)
    {
        var entity = await FindAsync(type, id) ?? throw Unknown(id);
        var trimmed = (url ?? string.Empty).Trim();
        if (trimmed.Length == 0) throw new InvalidOperationException("No address given.");

        AttachmentsOf(entity).Add(new EntityAttachment
        {
            Name = string.IsNullOrWhiteSpace(name) ? trimmed : name.Trim(),
            Url = trimmed,
            Kind = AttachmentKind.Link
        });
        await SaveAsync(entity);
        return [.. AttachmentsOf(entity).Select(ToDto)];
    }

    /// <summary>Renames an attachment, or changes the note on it.</summary>
    [JsonRpcMethod("attachments/update")]
    public async Task<AttachmentDto[]> UpdateAsync(
        string type, string id, string attachmentId, string? name = null, string? note = null)
    {
        var entity = await FindAsync(type, id) ?? throw Unknown(id);
        var attachment = AttachmentsOf(entity).FirstOrDefault(a => a.Id == attachmentId);
        if (attachment != null)
        {
            // A blank name would leave a row nobody can tell from the next one,
            // so an empty rename is no rename.
            if (!string.IsNullOrWhiteSpace(name)) attachment.Name = name.Trim();
            if (note != null) attachment.Note = note.Trim();
            await SaveAsync(entity);
        }
        return [.. AttachmentsOf(entity).Select(ToDto)];
    }

    /// <summary>
    /// Takes an attachment off the entry. The copied file stays in the project:
    /// another entry may point at the same one, and deleting somebody's only
    /// copy of a recording because they tidied a Codex entry is not a trade
    /// anybody would accept.
    /// </summary>
    [JsonRpcMethod("attachments/remove")]
    public async Task<AttachmentDto[]> RemoveAsync(string type, string id, string attachmentId)
    {
        var entity = await FindAsync(type, id) ?? throw Unknown(id);
        if (AttachmentsOf(entity).RemoveAll(a => a.Id == attachmentId) > 0) await SaveAsync(entity);
        return [.. AttachmentsOf(entity).Select(ToDto)];
    }

    private AttachmentDto ToDto(EntityAttachment a)
        => new(
            a.Id,
            a.Name,
            a.Kind.ToString(),
            a.Url,
            a.Note,
            // The absolute path, so the renderer can open it in whatever the
            // machine uses for that kind of file. Empty for a link.
            a.IsLink || string.IsNullOrWhiteSpace(a.Path)
                ? string.Empty
                : _entities.GetAttachmentFullPath(a.Path));

    private static List<EntityAttachment> AttachmentsOf(IEntityData entity) => entity switch
    {
        CharacterData c => c.Attachments,
        LocationData l => l.Attachments,
        ItemData i => i.Attachments,
        LoreData lo => lo.Attachments,
        _ => ((CustomEntityData)entity).Attachments
    };

    private async Task<IEntityData?> FindAsync(string type, string id) => type switch
    {
        "character" => (await _entities.LoadCharactersAsync()).FirstOrDefault(c => c.Id == id),
        "location" => (await _entities.LoadLocationsAsync()).FirstOrDefault(l => l.Id == id),
        "item" => (await _entities.LoadItemsAsync()).FirstOrDefault(i => i.Id == id),
        "lore" => (await _entities.LoadLoreAsync()).FirstOrDefault(l => l.Id == id),
        _ => (await _entities.LoadCustomEntitiesAsync(type)).FirstOrDefault(e => e.Id == id)
    };

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

    private static InvalidOperationException Unknown(string id)
        => new($"Unknown entry '{id}'.");
}

/// <summary>One file or link kept with an entry.</summary>
public sealed record AttachmentDto(
    string Id, string Name, string Kind, string Url, string Note, string FullPath);
