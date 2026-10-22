#if NOVALIST_COLLABORATION
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    internal async Task<string> RecoverSharedFieldAsync(Guid sessionId, Guid item, Guid focus, string value, CancellationToken ct)
    {
        RequireSharedField(sessionId, item, focus);
        if (focus == Guid.Empty) throw new ArgumentException("A focused edit is required.", nameof(focus));
        ArgumentNullException.ThrowIfNull(value);
        ct.ThrowIfCancellationRequested();
        var binding = fieldBindings[item];
        var contents = JsonSerializer.Serialize(new { version = 1, kind = "codex-field", sessionId, itemId = item, focusId = focus,
            source = binding.Path, field = binding.Key, custom = binding.Custom, value }, new JsonSerializerOptions { WriteIndented = true });
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(contents)));
        var path = SharedProjectPath.Resolve(sharedRoot ?? throw new InvalidOperationException("The shared project is no longer open."),
            $"Recovered edits/{sessionId:N}/{focus:N}-{hash}.json");
        if (await FileService.ExistsAsync(path))
        {
            if (await FileService.ReadTextAsync(path) != contents) throw new IOException("This recovery file was changed outside Novalist. Its contents have been kept.");
        }
        else await FileService.WriteTextAsync(path, contents);
        return path;
    }
}
#endif
