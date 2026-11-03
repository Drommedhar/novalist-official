namespace Novalist.Sync.Client;

public sealed partial class CollaborationSession
{
    public IReadOnlyList<PendingCompoundEdit> PendingCompoundEdits() => journal.PendingCompoundEdits();

    public void SaveCompoundEdit(PendingCompoundEdit edit)
    {
        EnsureOpen();
        if (edit.ItemId == Guid.Empty || edit.Payload.Length == 0 || edit.AfterSequence < 0)
            throw new ArgumentException("A compound edit requires an item, intent and observed sequence.", nameof(edit));
        journal.SaveCompoundEdit(edit);
    }

    public void CompleteCompoundEdit(Guid item)
    {
        EnsureOpen();
        journal.CompleteCompoundEdit(item);
    }
}
