#if NOVALIST_COLLABORATION
using System.Text.Json.Nodes;
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    private async Task AcquireSceneDateWritesAsync(SharedFieldBinding binding, JsonNode? value,
        List<MetadataWrite> acquired, CancellationToken ct)
    {
        var start = value?["start"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(start)) return;
        var counterpart = ActiveFieldBindings.Single(other => other.Path == binding.Path &&
            other.Metadata == binding.Metadata && other.Key == "date");
        await AcquireMetadataWriteAsync(counterpart, acquired, _ => JsonValue.Create(start), ct);
    }
}
#endif
