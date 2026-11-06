namespace Novalist.Mobile.Services;

internal static class DocumentTreeOperations
{
    public static IReadOnlyList<DocumentTreeEntry> Children(IDocumentTree tree, string parent)
    {
        var entries = tree.GetChildren(parent);
        foreach (var entry in entries) ValidatePathComponent(entry.Name);
        if (entries.Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count() != entries.Count)
            throw new IOException("The provider returned multiple documents with the same name.");
        return entries;
    }

    public static DocumentTreeEntry? Find(DocumentTreePath path, bool createDirectories = false)
    {
        var current = new DocumentTreeEntry(path.Tree.RootId, "", true, 0, DateTime.UnixEpoch);
        foreach (var name in path.Segments)
        {
            ValidatePathComponent(name);
            if (!current.IsDirectory) throw new DirectoryNotFoundException("A parent document is not a folder.");
            var child = Children(path.Tree, current.Id).SingleOrDefault(entry => entry.Name == name);
            if (child == null && createDirectories) child = path.Tree.Create(current.Id, name, true);
            if (child == null) return null;
            current = child;
        }
        return current;
    }

    public static void Write(DocumentTreePath path, byte[] bytes)
    {
        if (path.Segments.Length == 0) throw new IOException("A selected folder cannot be replaced by a file.");
        var name = path.Segments[^1];
        ValidatePathComponent(name);
        var parent = Find(path with { Segments = path.Segments[..^1] }, true)
            ?? throw new DirectoryNotFoundException("The provider did not create the parent folder.");
        var children = Children(path.Tree, parent.Id);
        var existing = children.SingleOrDefault(entry => entry.Name == name);
        if (existing?.IsDirectory == true) throw new IOException("A folder already uses this file name.");
        var transaction = path.Transactions.Begin(parent.Id, name, children.Select(entry => entry.Name).ToArray());
        try
        {
            var temporary = path.Tree.Create(parent.Id, transaction.TemporaryName, false);
            path.Tree.Write(temporary.Id, bytes);
            if (existing != null) path.Tree.Rename(existing.Id, transaction.BackupName).RequireName(transaction.BackupName);
            path.Tree.Rename(temporary.Id, name).RequireName(name);
        }
        catch (Exception failure)
        {
            try { path.Transactions.Recover(path.Tree); }
            catch (Exception recovery) { throw new AggregateException("Saving failed; a recovery copy remains in the selected folder.", failure, recovery); }
            throw;
        }
        path.Transactions.Recover(path.Tree);
    }

    private static void ValidatePathComponent(string name)
    {
        if (string.IsNullOrEmpty(name) || name is "." or ".." || name.IndexOfAny(['/', '\\', '\0']) >= 0 || Path.IsPathRooted(name))
            throw new IOException("The provider returned an invalid document name.");
    }

    public static void ListFiles(IDocumentTree tree, string parent, string path, string pattern, bool recursive, List<string> files)
    {
        foreach (var entry in Children(tree, parent))
        {
            ValidatePathComponent(entry.Name);
            var child = Path.Combine(path, entry.Name);
            if (entry.IsDirectory)
            {
                if (recursive) ListFiles(tree, entry.Id, child, pattern, true, files);
            }
            else if (System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, entry.Name)) files.Add(child);
        }
    }
}
