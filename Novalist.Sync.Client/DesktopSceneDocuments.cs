using System.Text.Json;

namespace Novalist.Sync.Client;

public sealed record SharedSceneSnapshot(byte[] State, IReadOnlyList<byte[]> Deletions, string? Html, string? Recovered)
{
    public string? Text { get; init; }
    public string? RecoveredText { get; init; }
}
public sealed record SceneApplyResult(bool Accepted, IReadOnlyList<string> Rejected);

public sealed class DesktopSceneDocuments(Guid sessionId, Func<object, CancellationToken, Task<JsonElement>> request) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task OpenAsync(CancellationToken ct)
    {
        if (sessionId == Guid.Empty) throw new InvalidOperationException("A collaboration session identity is required.");
        await request(new { sessionId, action = "open" }, ct);
    }

    public Task SeedAsync(Guid item, string html, CancellationToken ct) =>
        ExecuteAsync(new { kind = "seed", id = item, html }, ct);

    public Task SeedTextAsync(Guid item, string text, CancellationToken ct) =>
        ExecuteAsync(new { kind = "seedText", id = item, text }, ct);

    public Task LoadAsync(Guid item, byte[] state, IReadOnlyList<byte[]> deletions, CancellationToken ct, bool plainText = false) =>
        ExecuteAsync(new { kind = "load", id = item, state, deletions, plainText }, ct);

    public async Task<SceneApplyResult> ReceiveAsync(Guid item, byte[] state, CancellationToken ct)
    {
        var response = await request(new { sessionId, action = "execute", command = new { kind = "receive", id = item, state } }, ct);
        return response.Deserialize<SceneApplyResult>(Json) is { Rejected: not null } result
            ? result : throw new InvalidDataException("The desktop did not acknowledge the shared edit.");
    }

    public Task ValidateAsync(Guid item, byte[] state, CancellationToken ct) =>
        ExecuteAsync(new { kind = "validate", id = item, state }, ct);

    public Task RemoveAsync(Guid item, byte[] state, CancellationToken ct) =>
        ExecuteAsync(new { kind = "remove", id = item, state }, ct);

    public async Task<byte[]?> RenameAsync(Guid item, string entityId, string oldName, string newName, CancellationToken ct)
    {
        var response = await request(new { sessionId, action = "rename", id = item, entityId, oldName, newName }, ct);
        if (response.ValueKind == JsonValueKind.Null) return null;
        return response.Deserialize<byte[]>(Json) is { Length: > 0 } update
            ? update : throw new InvalidDataException("The desktop returned an incomplete rename update.");
    }

    public async Task<SharedSceneSnapshot> CaptureAsync(Guid item, CancellationToken ct)
    {
        var response = await request(new { sessionId, action = "capture", id = item }, ct);
        var snapshot = response.Deserialize<SharedSceneSnapshot>(Json);
        return snapshot is { State.Length: > 0, Deletions: not null } && snapshot.Deletions.All(state => state is { Length: > 0 })
            ? snapshot : throw new InvalidDataException("The desktop returned an incomplete shared scene.");
    }

    private async Task ExecuteAsync(object command, CancellationToken ct) =>
        await request(new { sessionId, action = "execute", command }, ct);

    public async ValueTask DisposeAsync() =>
        await request(new { sessionId, action = "close" }, CancellationToken.None);
}
