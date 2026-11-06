#if NOVALIST_COLLABORATION
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    private async Task<(SharedProjectState State, IReadOnlyDictionary<string, byte[]> Files)> ReconcileSharedSourceFormattingAsync(
        SharedProjectState state, IReadOnlyDictionary<string, byte[]> baseline, CancellationToken ct)
    {
        var root = Projects.ProjectRoot ?? throw new InvalidOperationException("Open a project first.");
        var sources = state.Fields.Select(field => (field.Path, Hash: (string?)field.SourceSha256))
            .Concat(state.Documents.Select(document => (document.Path, Hash: document.SourceSha256)));
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var files = new Dictionary<string, byte[]>(baseline, StringComparer.Ordinal);
        var canonical = state.FileBaselines is { } existing ? new Dictionary<string, byte[]>(existing, StringComparer.Ordinal) : [];
        foreach (var source in sources.Where(source => source.Hash is not null).GroupBy(source => source.Path, StringComparer.Ordinal))
        {
            if (!source.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || !files.TryGetValue(source.Key, out var before)) continue;
            var originalHash = Convert.ToHexStringLower(SHA256.HashData(before));
            if (source.Any(binding => !string.Equals(binding.Hash, originalHash, StringComparison.OrdinalIgnoreCase))) continue;
            var current = await File.ReadAllBytesAsync(SharedProjectPath.Resolve(root, source.Key), ct);
            if (before.AsSpan().SequenceEqual(current) || !JsonNode.DeepEquals(JsonNode.Parse(before), JsonNode.Parse(current))) continue;
            hashes[source.Key] = Convert.ToHexStringLower(SHA256.HashData(current));
            files[source.Key] = current;
            canonical.TryAdd(source.Key, before);
        }
        return (state with
        {
            Documents = state.Documents.Select(document => hashes.TryGetValue(document.Path, out var hash) ? document with { SourceSha256 = hash } : document).ToArray(),
            Fields = state.Fields.Select(field => hashes.TryGetValue(field.Path, out var hash) ? field with { SourceSha256 = hash } : field).ToArray(),
            FileBaselines = canonical
        }, files);
    }
}
#endif
