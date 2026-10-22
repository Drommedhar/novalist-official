using System.Text;
using System.Text.Json;
using Novalist.Sync.Client;
using StreamJsonRpc;

namespace Novalist.Backend.Tests.TestHelpers;

internal sealed class CollaborationDocumentPeer
{
    private readonly Dictionary<Guid, byte[]> states = [];
    internal bool RejectNextLoad { get; set; }

    [JsonRpcMethod("collaboration/documents")]
    public object? Documents(string action, JsonElement command = default, Guid? id = null)
    {
        if (action == "execute")
        {
            var item = command.GetProperty("id").GetGuid();
            var kind = command.GetProperty("kind").GetString();
            if (kind == "load" && RejectNextLoad) { RejectNextLoad = false; throw new InvalidOperationException("Document host unavailable."); }
            if (kind == "seed") states[item] = Encoding.UTF8.GetBytes(command.GetProperty("html").GetString() ?? "");
            if (kind == "seedText") states[item] = Encoding.UTF8.GetBytes("text:" + command.GetProperty("text").GetString());
            if (kind == "load") states[item] = command.GetProperty("state").GetBytesFromBase64();
            if (kind == "receive")
            {
                states[item] = command.GetProperty("state").GetBytesFromBase64();
                return new SceneApplyResult(true, []);
            }
        }
        if (action != "capture" || id is null) return null;
        var state = states[id.Value];
        var value = Encoding.UTF8.GetString(state);
        return value.StartsWith("text:", StringComparison.Ordinal)
            ? new SharedSceneSnapshot(state, [], null, null) { Text = value[5..] }
            : new SharedSceneSnapshot(state, [], value, null);
    }
}
