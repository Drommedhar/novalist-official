using System.Text.Json;

namespace Novalist.Mobile.Services;

public sealed class DocumentTreeTransactions(string journalPath)
{
    public sealed record PendingWrite(string ParentId, string Name, string TemporaryName, string BackupName);

    public PendingWrite Begin(string parentId, string name, IReadOnlyCollection<string> existingNames)
    {
        if (File.Exists(journalPath)) throw new IOException("An earlier save still needs recovery.");
        PendingWrite pending;
        do
        {
            var id = Guid.NewGuid().ToString("N");
            pending = new PendingWrite(parentId, name, ".novalist-write-" + id, ".novalist-backup-" + id);
        } while (existingNames.Contains(pending.TemporaryName) || existingNames.Contains(pending.BackupName));
        Directory.CreateDirectory(Path.GetDirectoryName(journalPath) ?? throw new IOException("The recovery journal has no parent directory."));
        var temporary = journalPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(pending));
        File.Move(temporary, journalPath, true);
        return pending;
    }

    public void Recover(IDocumentTree tree)
    {
        if (!File.Exists(journalPath)) return;
        if (tree is CachedDocumentTree cached) cached.Invalidate();
        var pending = JsonSerializer.Deserialize<PendingWrite>(File.ReadAllText(journalPath))
            ?? throw new IOException("The recovery journal is invalid.");
        var entries = DocumentTreeOperations.Children(tree, pending.ParentId);
        var backup = entries.SingleOrDefault(entry => entry.Name == pending.BackupName);
        var temporary = entries.SingleOrDefault(entry => entry.Name == pending.TemporaryName);
        if (backup != null)
        {
            if (entries.Any(entry => entry.Name == pending.Name)) tree.Delete(backup.Id);
            else tree.Rename(backup.Id, pending.Name).RequireName(pending.Name);
        }
        if (temporary != null) tree.Delete(temporary.Id);
        File.Delete(journalPath);
    }
}
