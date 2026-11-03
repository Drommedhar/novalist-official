using System.Text.Json;

namespace Novalist.Sync.Client;

public sealed class DesktopCredentials(Func<object, CancellationToken, Task<JsonElement>> request) : ICollaborationCredentials
{
    public async Task<string?> ReadAsync(string key, CancellationToken ct)
    {
        var result = await request(new { action = "read", key }, ct);
        return result.ValueKind == JsonValueKind.Null ? null : result.GetString();
    }

    public async Task WriteAsync(string key, string value, CancellationToken ct) =>
        await request(new { action = "write", key, value }, ct);

    public async Task RemoveAsync(string key, CancellationToken ct) =>
        await request(new { action = "remove", key }, ct);
}
