using System.Security.Cryptography;
using System.Text.Json;

namespace Novalist.Sync.Client;

public sealed record SharedFieldBinding(Guid ItemId, string Path, string Key, bool Custom, string SourceSha256);
public sealed record SharedProjectState(IReadOnlyList<SharedDocumentState> Documents, IReadOnlyList<SharedFieldBinding> Fields);

public static class SharedCodexFields
{
    private static readonly IReadOnlyDictionary<string, string[]> BuiltIn = new Dictionary<string, string[]>
    {
        ["character"] = ["name", "surname", "gender", "age", "birthDate", "role", "group", "eyeColor", "hairColor", "hairLength", "height", "build", "skinTone"],
        ["location"] = ["name", "type", "parent", "group"],
        ["item"] = ["name", "type", "origin", "group"],
        ["lore"] = ["name", "category", "group"]
    };
    private static readonly string[] DefaultFolders = ["Characters", "Locations", "Items", "Lore"];
    private sealed record FolderSchema(string[] Keys, bool Custom);
    private sealed record Value(SharedFieldBinding Binding, string Text);

    public static async Task<IReadOnlyList<SharedFieldBinding>> CaptureAsync(string root, IReadOnlyList<string> paths, CancellationToken ct) =>
        (await EnumerateAsync(root, paths, ct)).Select(value => value.Binding with { ItemId = Guid.NewGuid() }).ToArray();

    public static async Task<IReadOnlyDictionary<Guid, string>> ReadAsync(string root, IReadOnlyList<SharedFieldBinding> bindings, CancellationToken ct)
    {
        ValidateBindings(bindings);
        if (bindings.Count == 0) return new Dictionary<Guid, string>();
        var values = (await EnumerateAsync(root, bindings.Select(binding => binding.Path).Distinct(StringComparer.Ordinal).ToArray(), ct))
            .ToDictionary(value => (value.Binding.Path, value.Binding.Key, value.Binding.Custom));
        var result = new Dictionary<Guid, string>();
        foreach (var binding in bindings)
        {
            if (!values.TryGetValue((binding.Path, binding.Key, binding.Custom), out var value))
                throw new InvalidDataException("A shared field is not an editable Codex field.");
            if (!string.Equals(value.Binding.SourceSha256, binding.SourceSha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("A Codex entry changed while sharing was starting. Its local changes have been kept.");
            result.Add(binding.ItemId, value.Text);
        }
        return result;
    }

    internal static void ValidateBindings(IReadOnlyList<SharedFieldBinding> bindings)
    {
        var identities = new HashSet<Guid>();
        var fields = new HashSet<(string, string, bool)>();
        foreach (var binding in bindings)
        {
            if (binding is null || binding.ItemId == Guid.Empty || !identities.Add(binding.ItemId) ||
                string.IsNullOrEmpty(binding.Key) || !fields.Add((binding.Path, binding.Key, binding.Custom)) ||
                binding.SourceSha256 is null || binding.SourceSha256.Length != 64 || !binding.SourceSha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid shared Codex field binding.");
            SharedProjectPath.Validate(binding.Path);
        }
    }

    private static async Task<IReadOnlyList<Value>> EnumerateAsync(string root, IReadOnlyList<string> paths, CancellationToken ct)
    {
        using var project = JsonDocument.Parse(await ReadBytesAsync(root, ".novalist/project.json", ct));
        var folders = Folders(project.RootElement);
        var result = new List<Value>();
        foreach (var path in paths)
        {
            var slash = path.LastIndexOf('/');
            if (slash < 0 || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || !folders.TryGetValue(path[..slash], out var schema)) continue;
            var bytes = await ReadBytesAsync(root, path, ct);
            using var entity = JsonDocument.Parse(bytes);
            var id = Text(entity.RootElement, "id");
            if (string.IsNullOrEmpty(id) || !string.Equals(Path.GetFileNameWithoutExtension(path), id, StringComparison.Ordinal))
                throw new InvalidDataException("A shared Codex entry must have a stable file identity.");
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var custom = Property(entity.RootElement, "fields");
            if (custom is { ValueKind: not JsonValueKind.Object }) throw new InvalidDataException("Invalid custom Codex fields.");
            var keys = schema.Custom ? schema.Keys.Concat(custom?.EnumerateObject().Select(field => field.Name) ?? []).Distinct(StringComparer.Ordinal) : schema.Keys;
            result.Add(new(new(Guid.Empty, path, "name", false, hash), Text(entity.RootElement, "name")));
            foreach (var key in keys.Where(key => key != "name"))
                result.Add(new(new(Guid.Empty, path, key, schema.Custom, hash),
                    schema.Custom ? custom is { } fields ? Text(fields, key, comparison: StringComparison.Ordinal) : "" : Text(entity.RootElement, key)));
        }
        return result;
    }

    private static Dictionary<string, FolderSchema> Folders(JsonElement project)
    {
        var result = new Dictionary<string, FolderSchema>(StringComparer.OrdinalIgnoreCase);
        var scopes = Elements(project, "books").Select(book => (Root: Text(book, "folderName"), Data: book))
            .Prepend((Text(project, "worldBibleFolder", "World Bible"), project));
        foreach (var (root, scope) in scopes)
        {
            SharedProjectPath.Validate(root);
            foreach (var ((type, keys), fallback) in BuiltIn.Zip(DefaultFolders))
                Add(root + '/' + Text(scope, type + "Folder", fallback), new(keys, false));
            foreach (var definition in Elements(project, "customEntityTypes"))
                Add(root + '/' + Text(definition, "folderName"), new(Elements(definition, "defaultFields").Select(field => Text(field, "key")).ToArray(), true));
        }
        return result;

        void Add(string path, FolderSchema schema)
        {
            SharedProjectPath.Validate(path);
            if (!result.TryAdd(path, schema)) throw new InvalidDataException("Codex types must have distinct storage folders to share them.");
        }
    }

    private static JsonElement? Property(JsonElement value, string key, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid Codex metadata.");
        var properties = value.EnumerateObject().Where(property => property.Name.Equals(key, comparison)).ToArray();
        if (properties.Length > 1) throw new InvalidDataException("Ambiguous Codex metadata.");
        return properties.Length == 0 ? null : properties[0].Value;
    }

    private static string Text(JsonElement value, string key, string fallback = "", StringComparison comparison = StringComparison.OrdinalIgnoreCase) =>
        Property(value, key, comparison) is not { } found || found.ValueKind == JsonValueKind.Null
        ? fallback : found.ValueKind == JsonValueKind.String ? found.GetString() ?? fallback : throw new InvalidDataException("A shared Codex field must contain text.");

    private static IEnumerable<JsonElement> Elements(JsonElement value, string key) => Property(value, key) is { } found
        ? found.ValueKind == JsonValueKind.Array ? found.EnumerateArray() : throw new InvalidDataException("Invalid Codex metadata collection.") : [];

    private static async Task<byte[]> ReadBytesAsync(string root, string relative, CancellationToken ct)
    {
        await using var stream = File.OpenRead(SharedProjectPath.Resolve(root, relative));
        if (stream.Length > 64 * 1024 * 1024) throw new InvalidDataException("Codex metadata is too large to share.");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, ct);
        if (await stream.ReadAsync(new byte[1], ct) != 0) throw new IOException("Codex metadata changed while it was being read.");
        return bytes;
    }
}
