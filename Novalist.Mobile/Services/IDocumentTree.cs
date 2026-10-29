namespace Novalist.Mobile.Services;

public sealed record DocumentTreeEntry(string Id, string Name, bool IsDirectory, long Size, DateTime LastWriteTimeUtc)
{
    public DocumentTreeEntry RequireName(string requestedName)
        => Name == requestedName ? this : throw new IOException("The provider changed the requested document name.");
}

public interface IDocumentTree
{
    string RootId { get; }
    void EnsureAccess();
    IReadOnlyList<DocumentTreeEntry> GetChildren(string parentId);
    DocumentTreeEntry Create(string parentId, string name, bool directory);
    byte[] Read(string id);
    void Write(string id, byte[] bytes);
    DocumentTreeEntry Rename(string id, string name);
    void Delete(string id);
}
