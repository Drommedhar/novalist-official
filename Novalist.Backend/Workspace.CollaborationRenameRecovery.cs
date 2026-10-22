#if NOVALIST_COLLABORATION
using System.Text.Json;
using System.Text.Json.Nodes;
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    private async Task ResumeSharedRenamesAsync(CancellationToken ct)
    {
        if (fieldSession is not { } session) return;
        foreach (var edit in session.PendingCompoundEdits())
        {
            var rename = JsonSerializer.Deserialize<SharedRename>(edit.Payload) ?? throw new InvalidDataException("Invalid pending rename.");
            var binding = PendingRenameBinding(edit, rename);
            if (binding is null) { session.CompleteCompoundEdit(edit.ItemId); continue; }
            await using var graph = await AcquireSharedGraphAsync(sharedSession, binding.ItemId, Guid.NewGuid(), ct);
            await CompletePendingSharedRenamesAsync(graph?.Condition, ct);
            break;
        }
    }

    private async Task CompletePendingSharedRenamesAsync(FieldLeaseCondition? required, CancellationToken ct)
    {
        var session = fieldSession ?? throw new InvalidOperationException("The shared project is closed.");
        foreach (var edit in session.PendingCompoundEdits())
        {
            var fields = sharedFields ?? throw new InvalidOperationException("The shared fields are closed.");
            var intent = JsonSerializer.Deserialize<SharedRename>(edit.Payload) ?? throw new InvalidDataException("Invalid pending rename.");
            if (PendingRenameBinding(edit, intent) is { } binding)
            {
                var path = SharedProjectPath.Resolve(sharedRoot ?? throw new InvalidOperationException("The shared project is closed."), binding.Path);
                var entity = JsonNode.Parse(await FileService.ReadTextAsync(path)) as JsonObject ?? throw new InvalidDataException("Invalid shared Codex entry.");
                var name = SharedEntityName(entity);
                if (!edit.Confirmed && name != intent.Next) { session.CompleteCompoundEdit(edit.ItemId); continue; }
                var rename = intent with { Next = name };
                var references = new List<SharedRenameReference>();
                try
                {
                    await AcquireRenameReferencesAsync(rename, references, ct);
                    foreach (var reference in references)
                        await fields.WriteAsync(reference.Lease with { RequiredLease = required }, reference.Value, ct);
                    await RenameSharedProseAsync(rename, ct);
                }
                finally { await Task.WhenAll(references.Select(reference => session.ReleaseFieldAsync(reference.Lease, CancellationToken.None))); }
            }
            session.CompleteCompoundEdit(edit.ItemId);
        }
    }

    private SharedFieldBinding? PendingRenameBinding(PendingCompoundEdit edit, SharedRename rename) =>
        ActiveFieldBindings.SingleOrDefault(binding => binding.ItemId == edit.ItemId ||
            !binding.Custom && binding.Metadata is null && binding.Path == rename.Path && binding.Key == rename.Key);
}
#endif
