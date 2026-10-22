using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Novalist.Sync.Client;

internal static class SharedProjectFileBindings
{
    internal static SharedProjectState Copy(SharedProjectState source) => new(
        source.Documents.Select(document => document with { State = document.State.ToArray() }).ToArray(), source.Fields.ToArray());

    internal static void Validate(SharedProjectState state, IReadOnlyDictionary<string, byte[]> files)
    {
        if (state is null || state.Documents is null || state.Fields is null) throw new InvalidDataException("Missing project file bindings.");
        SharedCodexFields.ValidateBindings(state.Fields);
        var identities = state.Fields.Select(field => field.ItemId).ToHashSet();
        var paths = new HashSet<(string, SharedTextField?)>();
        foreach (var document in state.Documents)
        {
            if (document is null || document.ItemId == Guid.Empty || !identities.Add(document.ItemId) ||
                !paths.Add((document.Path.ToUpperInvariant(), document.TextField)) || document.State is not { Length: > 0 })
                throw new InvalidDataException("Invalid shared document binding.");
            CheckSource(document.Path, document.SourceSha256, files);
            SharedProseFields.Validate(document.TextField);
        }
        foreach (var field in state.Fields) CheckSource(field.Path, field.SourceSha256, files);
    }

    private static void CheckSource(string path, string? hash, IReadOnlyDictionary<string, byte[]> files)
    {
        SharedProjectPath.Validate(path);
        if (!files.TryGetValue(path, out var content) || (hash is not null &&
            !string.Equals(hash, Convert.ToHexStringLower(SHA256.HashData(content)), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("A shared binding does not match its project file.");
    }

    internal static SharedProjectState Accept(SharedProjectState added, IReadOnlyList<SharedFileChange> changes, IReadOnlyDictionary<string, byte[]> files)
    {
        var sources = changes.ToDictionary(change => change.Path, StringComparer.OrdinalIgnoreCase);
        bool Matches(string path) => files.TryGetValue(path, out var content) && sources.TryGetValue(path, out var change) && SharedFileMerge.Equal(change.After, content);
        return new(added.Documents.Where(document => Matches(document.Path)).ToArray(), added.Fields.Where(field => Matches(field.Path)).ToArray());
    }

    internal static SharedProjectState Update(SharedProjectState current, SharedProjectState added, IReadOnlyDictionary<string, byte[]> files)
    {
        var documents = current.Documents.ToDictionary(document => document.ItemId);
        var fields = current.Fields.ToDictionary(field => field.ItemId);
        foreach (var document in added.Documents) documents[document.ItemId] = document;
        foreach (var field in added.Fields) fields[field.ItemId] = field;
        var result = new SharedProjectState(documents.Values.Where(document => Exists(document, files)).ToArray(),
            fields.Values.Where(field => Exists(field, files)).ToArray());
        if (result.Documents.Select(document => document.ItemId).Intersect(result.Fields.Select(field => field.ItemId)).Any() ||
            result.Documents.Select(document => (document.Path.ToUpperInvariant(), document.TextField)).Distinct().Count() != result.Documents.Count ||
            result.Fields.Select(field => (field.Path.ToUpperInvariant(), field.Key, field.Custom, field.Metadata)).Distinct().Count() != result.Fields.Count)
            throw new InvalidDataException("Conflicting project file bindings.");
        return result;
    }

    private static bool Exists(SharedDocumentState document, IReadOnlyDictionary<string, byte[]> files)
    {
        if (!files.TryGetValue(document.Path, out var content)) return false;
        if (document.TextField is not { } field) return true;
        var entity = JsonNode.Parse(content) as JsonObject ?? throw new InvalidDataException("Invalid shared prose entry.");
        return SharedProseFields.Target(entity, field) is not null;
    }

    private static bool Exists(SharedFieldBinding binding, IReadOnlyDictionary<string, byte[]> files)
    {
        if (!files.TryGetValue(binding.Path, out var content)) return false;
        if (binding.Metadata is null) return true;
        var entity = JsonNode.Parse(content) as JsonObject ?? throw new InvalidDataException("Invalid shared metadata.");
        return SharedMetadataFields.Target(entity, binding) is not null;
    }
}
