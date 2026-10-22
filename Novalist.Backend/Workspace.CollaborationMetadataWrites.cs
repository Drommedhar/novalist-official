#if NOVALIST_COLLABORATION
using System.Text.Json.Nodes;
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    private sealed record MetadataWrite(FieldLeaseRequest Lease, string Value);

    private async Task<bool> WriteSharedMetadataAsync(Guid sessionId, Guid item, Guid focus, string value, CancellationToken ct, FieldLeaseCondition? required = null)
    {
        var field = await RequireEditableSharedFieldAsync(sessionId, item, focus);
        var binding = fieldBindings[item];
        if (binding.Metadata is null) return false;
        var parsed = SharedMetadataFields.ParseValue(binding, value);
        var acquired = new List<MetadataWrite>();
        try
        {
            if (binding.Metadata.Kind == "research" && binding.Key == "relatedIds")
                await AcquireResearchLinkWritesAsync(binding, parsed, acquired, ct);
            if (binding.Metadata.Kind == "research" && binding.Key == "type" && parsed?.GetValue<string>() is "Note" or "Link")
            {
                var content = ActiveFieldBindings.SingleOrDefault(other => other.Metadata == binding.Metadata && other.Key == "content");
                if (content is not null) await AcquireMetadataWriteAsync(content, acquired, current => current, ct);
            }
            if (binding.Metadata.Kind == "codex" && binding.Key == "relationships")
                value = await AcquireRelationshipWritesAsync(binding, parsed, acquired, ct);
            if (binding.Metadata.Kind == "codex" && binding.Key == "isWorld" && parsed?.GetValue<bool>() == true)
            {
                var parent = ActiveFieldBindings.Single(other => other.Path == binding.Path && other.Key == "parent" && other.Metadata is null);
                await AcquireDependentFieldWriteAsync(parent, acquired, _ => "", ct);
            }
            if (binding.Metadata.Kind == "scene" && binding.Key == "cast")
                await AcquireClearedSceneFieldAsync(binding, "focusEntityId", parsed, acquired, ct);
            if (binding.Metadata.Kind == "scene" && binding.Key == "narrativeMode" && parsed?.GetValue<string>() != "parallel")
                await AcquireClearedSceneFieldAsync(binding, "strand", null, acquired, ct);
            var origin = new MetadataWrite(field.Lease, value);
            var writes = binding.Key == "isWorld" ? acquired.Append(origin) : acquired.Prepend(origin);
            foreach (var write in writes) await field.Fields.WriteAsync(write.Lease with { RequiredLease = required }, write.Value, ct);
            if (binding.Metadata.Kind == "research" && binding.Key == "type")
                await PublishResearchTypeAsync(binding, parsed, CancellationToken.None);
            return true;
        }
        finally
        {
            await Task.WhenAll(acquired.Select(write => field.Session.ReleaseFieldAsync(write.Lease, CancellationToken.None)));
        }
    }

    private async Task AcquireResearchLinkWritesAsync(SharedFieldBinding binding, JsonNode? value, List<MetadataWrite> acquired, CancellationToken ct)
    {
        var identity = binding.Metadata?.Identity ?? throw new InvalidDataException("Missing research identity.");
        var desired = Strings(value);
        var original = RequireSharedField(sharedSession, binding.ItemId, Guid.NewGuid());
        var previous = Strings(JsonNode.Parse(await original.Fields.ReadAsync(binding.ItemId, ct)));
        foreach (var id in previous.Union(desired, StringComparer.Ordinal).Where(id => id != identity))
        {
            var counterpart = ActiveFieldBindings.SingleOrDefault(other => other.Metadata?.Kind == "research" && other.Metadata.Identity == id && other.Key == "relatedIds");
            if (counterpart is null) throw new InvalidOperationException("The linked research item is no longer available.");
            await AcquireMetadataWriteAsync(counterpart, acquired, current =>
            {
                var links = Strings(current);
                if (desired.Contains(id, StringComparer.Ordinal)) links.Add(identity);
                else links.RemoveAll(link => link == identity);
                return new JsonArray(links.Distinct(StringComparer.Ordinal).Select(link => JsonValue.Create(link)).ToArray());
            }, ct);
        }
    }

    private async Task AcquireClearedSceneFieldAsync(SharedFieldBinding binding, string key, JsonNode? retained, List<MetadataWrite> acquired, CancellationToken ct)
    {
        var counterpart = ActiveFieldBindings.Single(other => other.Path == binding.Path && other.Metadata == binding.Metadata && other.Key == key);
        var field = RequireSharedField(sharedSession, counterpart.ItemId, Guid.NewGuid());
        var current = JsonNode.Parse(await field.Fields.ReadAsync(counterpart.ItemId, ct));
        if (current is null || Strings(retained).Contains(current.GetValue<string>(), StringComparer.Ordinal)) return;
        await AcquireMetadataWriteAsync(counterpart, acquired, latest => latest is null || Strings(retained).Contains(latest.GetValue<string>(), StringComparer.Ordinal) ? latest : null, ct);
    }

    private async Task AcquireMetadataWriteAsync(SharedFieldBinding binding, List<MetadataWrite> acquired, Func<JsonNode?, JsonNode?> update, CancellationToken ct)
        => await AcquireDependentFieldWriteAsync(binding, acquired, current => update(JsonNode.Parse(current))?.ToJsonString() ?? "null", ct);

    private async Task AcquireDependentFieldWriteAsync(SharedFieldBinding binding, List<MetadataWrite> acquired, Func<string, string> update, CancellationToken ct)
    {
        var field = await RequireEditableSharedFieldAsync(sharedSession, binding.ItemId, Guid.NewGuid());
        var grant = await field.Fields.AcquireAsync(field.Lease, ct);
        if (!grant.Lease.Acquired)
        {
            var participants = PeerProjects?.IsActive == true ? PeerProjects.Participants(sharedSession) : HostedProjects?.Participants(sharedSession);
            var writer = participants?.FirstOrDefault(participant => participant.DeviceId == grant.Lease.DeviceId)?.Name;
            throw new InvalidOperationException(writer is null ? "A related field is being edited. Retry after it is released." : $"{writer} is editing a related field. Retry after it is released.");
        }
        acquired.Add(new(field.Lease, grant.Value));
        acquired[^1] = new(field.Lease, update(grant.Value));
    }

    private static List<string> Strings(JsonNode? value) => (value as JsonArray)?.Select(item => item?.GetValue<string>() ?? throw new InvalidDataException("Invalid metadata list.")).ToList() ?? [];
}
#endif
