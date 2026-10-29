using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Novalist.Mobile.Services;

public sealed class DocumentTreeMounts
{
    private readonly string _root;
    private readonly Func<string, IDocumentTree> _open;
    private readonly Dictionary<string, string> _uris;
    private readonly Dictionary<string, CachedDocumentTree> _trees = new();
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    public DocumentTreeMounts(string root, Func<string, IDocumentTree> open, TimeProvider? clock = null)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        _open = open;
        _clock = clock ?? TimeProvider.System;
        _uris = File.Exists(RegistryPath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(RegistryPath))
                ?? throw new IOException("The saved folder permissions are invalid.")
            : new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private string RegistryPath => _root + ".json";

    public string Register(string uri)
    {
        _open(uri).EnsureAccess();
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(uri)));
        lock (_gate)
        {
            if (!_uris.ContainsKey(key))
            {
                var updated = new Dictionary<string, string>(_uris) { [key] = uri };
                Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath)
                    ?? throw new IOException("The folder registry has no parent directory."));
                var temporary = RegistryPath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(updated));
                File.Move(temporary, RegistryPath, true);
                _uris.Add(key, uri);
            }
        }
        return Path.Combine(_root, key);
    }

    public bool IsExternalPath(string path)
        => Path.IsPathFullyQualified(path) && IsWithinRoot(Path.GetFullPath(path));

    public DocumentTreePath? Resolve(string path)
    {
        if (!IsExternalPath(path)) return null;
        var relative = Path.GetRelativePath(_root, Path.GetFullPath(path));
        var parts = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        string? uri;
        lock (_gate) _uris.TryGetValue(parts[0], out uri);
        if (uri == null) throw new UnauthorizedAccessException("Choose this folder again to grant access.");
        CachedDocumentTree tree;
        lock (_gate)
        {
            if (!_trees.TryGetValue(uri, out var existing))
                _trees.Add(uri, existing = new CachedDocumentTree(_open(uri), _clock));
            tree = existing;
        }
        tree.EnsureAccess();
        var transactions = new DocumentTreeTransactions(Path.Combine(_root + ".transactions", parts[0] + ".json"));
        return new DocumentTreePath(tree, parts.Skip(1).ToArray(), transactions);
    }

    public void Invalidate(string path)
    {
        if (Resolve(path)?.Tree is CachedDocumentTree tree) tree.Invalidate();
    }

    public void InvalidateAll()
    {
        lock (_gate)
            foreach (var tree in _trees.Values) tree.Invalidate();
    }

    private bool IsWithinRoot(string path)
        => path.Equals(_root, StringComparison.Ordinal)
            || path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
}

public sealed record DocumentTreePath(IDocumentTree Tree, string[] Segments, DocumentTreeTransactions Transactions);
