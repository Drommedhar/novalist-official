#if NOVALIST_COLLABORATION
using System.Text.Json;
using System.Text.Json.Nodes;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed record SharedEntityFieldsDto(Guid SessionId, IReadOnlyDictionary<string, Guid> Fields);

public sealed partial class Workspace
{
    private SharedFieldWorkspace? sharedFields;
    private CollaborationSession? fieldSession;
    private readonly Dictionary<Guid, SharedFieldBinding> fieldBindings = [];

    internal async Task AttachSharedProjectAsync(Guid sessionId, CollaborationSession session, SharedProjectState state, CancellationToken ct)
    {
        try
        {
            await AttachSharedFieldsAsync(sessionId, session, state.Fields, ct);
            await AttachSharedScenesAsync(sessionId, session, state.Documents, ct);
        }
        catch
        {
            if (sharedSession == sessionId) await DetachSharedScenesAsync();
            throw;
        }
    }

    internal async Task AttachSharedFieldsAsync(Guid sessionId, CollaborationSession session, IReadOnlyList<SharedFieldBinding> bindings, CancellationToken ct)
    {
        if (sharedFields is not null) throw new InvalidOperationException("Close the current collaboration session first.");
        if (sessionId == Guid.Empty) throw new ArgumentException("A session identity is required.", nameof(sessionId));
        var root = Projects.ProjectRoot ?? throw new InvalidOperationException("Open a project first.");
        var values = await SharedCodexFields.ReadAsync(root, bindings, ct);
        var known = bindings.ToDictionary(binding => binding.ItemId);
        var fields = new SharedFieldWorkspace(session, async (item, value, _) =>
        {
            if (Projects.ProjectRoot != root) throw new InvalidOperationException("The shared project is no longer open.");
            await ProjectFieldAsync(root, known[item], value);
            if (Coordinator?.Notify is { } notify) await notify("collaboration/fieldUpdated", new { sessionId, itemId = item, value });
        });
        if (DispatchSharedUpdate is { } dispatch) fields.DispatchRemote = dispatch;
        try { await fields.StartAsync(values, ct); }
        catch { await fields.DisposeAsync(); throw; }
        sharedFields = fields;
        fieldSession = session;
        sharedRoot = root;
        sharedSession = sessionId;
        foreach (var (item, binding) in known) fieldBindings.Add(item, binding);
    }

    internal SharedEntityFieldsDto? SharedEntityFields(IEntityData entity)
    {
        if (sharedFields is null || Projects.ProjectRoot != sharedRoot || Projects.ActiveBook is not { } book || Projects.CurrentProject is not { } project) return null;
        var folders = entity switch
        {
            CharacterData => (book.CharacterFolder, project.CharacterFolder),
            LocationData => (book.LocationFolder, project.LocationFolder),
            ItemData => (book.ItemFolder, project.ItemFolder),
            LoreData => (book.LoreFolder, project.LoreFolder),
            CustomEntityData custom => CustomFolders(custom.EntityTypeKey),
            _ => throw new InvalidOperationException("Unknown Codex entry type.")
        };
        var path = (entity.IsWorldBible ? project.WorldBibleFolder + '/' + folders.Item2 : book.FolderName + '/' + folders.Item1) + '/' + entity.Id + ".json";
        var fields = fieldBindings.Values.Where(binding => binding.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(binding => binding.Key, binding => binding.ItemId, StringComparer.Ordinal);
        return fields.Count == 0 ? null : new(sharedSession, fields);

        (string, string) CustomFolders(string type)
        {
            var folder = project.CustomEntityTypes.Single(definition => definition.TypeKey == type).FolderName;
            return (folder, folder);
        }
    }

    internal (SharedFieldWorkspace Fields, CollaborationSession Session, FieldLeaseRequest Lease) RequireSharedField(Guid sessionId, Guid item, Guid focus)
    {
        if (sharedFields is null || fieldSession is null || sessionId != sharedSession || Projects.ProjectRoot != sharedRoot || !fieldBindings.ContainsKey(item))
            throw new InvalidOperationException("This shared Codex field is no longer active.");
        var context = fieldSession.Context;
        return (sharedFields, fieldSession, new(context.ProjectId, item, context.Epoch, focus));
    }

    internal async Task<(SharedFieldWorkspace Fields, CollaborationSession Session, FieldLeaseRequest Lease)> RequireEditableSharedFieldAsync(Guid sessionId, Guid item, Guid focus)
    {
        var field = RequireSharedField(sessionId, item, focus);
        var binding = fieldBindings[item];
        var path = SharedProjectPath.Resolve(sharedRoot ?? throw new InvalidOperationException("The shared project is no longer open."), binding.Path);
        var entity = JsonNode.Parse(await FileService.ReadTextAsync(path)) as JsonObject
            ?? throw new InvalidDataException("The shared Codex entry is invalid.");
        if (entity[PropertyName(entity, "locked")]?.GetValue<bool>() == true)
            throw new InvalidOperationException(Rpc.EntitiesRpc.LockedMessage);
        return field;
    }

    private async Task ProjectFieldAsync(string root, SharedFieldBinding binding, string value)
    {
        var path = SharedProjectPath.Resolve(root, binding.Path);
        var previous = await FileService.ReadTextAsync(path);
        var entity = JsonNode.Parse(previous) as JsonObject ?? throw new InvalidDataException("The shared Codex entry is invalid.");
        var id = entity[PropertyName(entity, "id")]?.GetValue<string>();
        if (id != Path.GetFileNameWithoutExtension(binding.Path)) throw new InvalidDataException("The shared Codex entry's identity changed.");
        var target = entity;
        if (binding.Custom)
        {
            var key = PropertyName(entity, "fields");
            if (entity[key] is null) entity[key] = new JsonObject();
            target = entity[key] as JsonObject ?? throw new InvalidDataException("Invalid custom Codex fields.");
        }
        target[binding.Custom ? binding.Key : PropertyName(target, binding.Key)] = value;
        var next = entity.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await FieldHistory(root, binding).RecordAsync(id, previous, next);
        await FileService.WriteTextAsync(path, next);
    }

    private EntityHistory FieldHistory(string root, SharedFieldBinding binding)
    {
        var project = Projects.CurrentProject ?? throw new InvalidOperationException("The shared project is no longer open.");
        var book = project.Books.SingleOrDefault(book => binding.Path.StartsWith(book.FolderName + '/', StringComparison.OrdinalIgnoreCase));
        var bookRoot = book?.FolderName + (book?.ActiveDraft is { } draft ? "/Drafts/" + draft.FolderName : "");
        var snapshots = book is null ? project.WorldBibleFolder + "/Snapshots" : bookRoot + '/' + book.SnapshotFolder;
        return new EntityHistory(Projects, snapshotRoot: SharedProjectPath.Resolve(root, snapshots));
    }

    private static string PropertyName(JsonObject value, string key) => value.Select(property => property.Key)
        .SingleOrDefault(name => name.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? key;

    private async Task DetachSharedFieldsAsync()
    {
        if (sharedFields is not null) await sharedFields.CloseAsync(CancellationToken.None);
        sharedFields = null;
        fieldSession = null;
        fieldBindings.Clear();
    }
}
#endif
