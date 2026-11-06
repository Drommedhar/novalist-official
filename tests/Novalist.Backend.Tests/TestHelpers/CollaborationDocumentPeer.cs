using System.Text;
using System.Text.Json;
using Novalist.Sync.Client;
using StreamJsonRpc;

namespace Novalist.Backend.Tests.TestHelpers;

internal sealed class CollaborationDocumentPeer
{
    private readonly Dictionary<Guid, byte[]> states = [];
    private readonly Dictionary<Guid, byte[]> removed = [];
    internal Func<CancellationToken, Task>? BeforeSeed { get; set; }
    internal bool RejectNextLoad { get; set; }
    internal Guid? RejectLoadItem { get; set; }
    internal bool RemoveSeededScenes { get; set; }
    internal Guid? RejectRenameItem { get; set; }
    internal Func<Guid, Task>? AfterRename { get; set; }

    [JsonRpcMethod("collaboration/documents")]
    public async Task<object?> Documents(string action, JsonElement command = default, Guid? id = null,
        string? entityId = null, string? oldName = null, string? newName = null, CancellationToken cancellationToken = default)
    {
        if (action == "rename")
        {
            if (id is null || string.IsNullOrEmpty(entityId) || string.IsNullOrEmpty(oldName) || newName is null)
                throw new ArgumentException("Missing rename parameters.");
            if (RejectRenameItem == id) { RejectRenameItem = null; throw new IOException("Rename interrupted."); }
            var previous = Encoding.UTF8.GetString(states[id.Value]);
            var next = previous.Replace(oldName, newName, StringComparison.Ordinal);
            if (next == previous) return null;
            if (AfterRename is { } after) await after(id.Value);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(next));
        }
        if (action == "execute")
        {
            var item = command.GetProperty("id").GetGuid();
            var kind = command.GetProperty("kind").GetString();
            if (kind == "seed" && BeforeSeed is { } beforeSeed) await beforeSeed(cancellationToken);
            if (kind == "load" && (RejectNextLoad || RejectLoadItem == item))
            {
                RejectNextLoad = false;
                RejectLoadItem = null;
                throw new InvalidOperationException("Document host unavailable.");
            }
            if (kind == "seed") states[item] = Encoding.UTF8.GetBytes("html:" + command.GetProperty("html").GetString());
            if (kind == "seed" && RemoveSeededScenes) removed[item] = states[item];
            if (kind == "seedText") states[item] = Encoding.UTF8.GetBytes("text:" + command.GetProperty("text").GetString());
            if (kind == "load") { states[item] = command.GetProperty("state").GetBytesFromBase64(); removed.Remove(item); }
            if (kind == "remove") removed[item] = command.GetProperty("state").GetBytesFromBase64();
            if (kind == "receive")
            {
                states[item] = command.GetProperty("state").GetBytesFromBase64();
                return new SceneApplyResult(true, []);
            }
        }
        if (action != "capture" || id is null) return null;
        var state = states[id.Value];
        var value = Encoding.UTF8.GetString(state);
        if (value.StartsWith("html:", StringComparison.Ordinal)) value = value[5..];
        if (removed.TryGetValue(id.Value, out var deletedAt))
        {
            var changed = !state.AsSpan().SequenceEqual(deletedAt);
            return value.StartsWith("text:", StringComparison.Ordinal)
                ? new SharedSceneSnapshot(state, [deletedAt], null, null) { RecoveredText = changed ? value[5..] : null }
                : new SharedSceneSnapshot(state, [deletedAt], null, changed ? value : null);
        }
        return value.StartsWith("text:", StringComparison.Ordinal)
            ? new SharedSceneSnapshot(state, [], null, null) { Text = value[5..] }
            : new SharedSceneSnapshot(state, [], value, null);
    }
}
