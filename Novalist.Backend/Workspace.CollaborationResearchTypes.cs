#if NOVALIST_COLLABORATION
using System.Text;
using System.Text.Json.Nodes;
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    private readonly HashSet<Guid> retiredSharedBindings = [];
    private IEnumerable<SharedFieldBinding> ActiveFieldBindings => fieldBindings.Values.Where(binding => !retiredSharedBindings.Contains(binding.ItemId));
    private IEnumerable<SharedDocumentState> ActiveSharedProse => sharedProse.Values.Where(binding => !retiredSharedBindings.Contains(binding.ItemId));

    private async Task PublishResearchTypeAsync(SharedFieldBinding binding, JsonNode? value, CancellationToken ct)
    {
        var files = sharedProjectFiles ?? throw new InvalidOperationException("The shared project is closed.");
        var before = (await files.SnapshotAsync(ct))[binding.Path];
        var project = JsonNode.Parse(before) as JsonObject ?? throw new InvalidDataException("Invalid shared project.");
        SharedMetadataFields.Set(project, binding, value);
        var changes = new List<SharedFileChange> { new(binding.Path, before, Encoding.UTF8.GetBytes(project.ToJsonString())) };
        var added = await CaptureAddedBindingsAsync(changes, ct);
        await files.CommitAsync(changes, added, ct);
    }

    private async Task RetireMissingMetadataAsync(string root)
    {
        foreach (var group in ActiveFieldBindings.Where(binding => binding.Metadata is not null).GroupBy(binding => binding.Path).ToArray())
        {
            var path = SharedProjectPath.Resolve(root, group.Key);
            var entity = await FileService.ExistsAsync(path) ? JsonNode.Parse(await FileService.ReadTextAsync(path)) as JsonObject : null;
            foreach (var binding in group)
                if (entity is null || SharedMetadataFields.Target(entity, binding) is null) retiredSharedBindings.Add(binding.ItemId);
        }
    }
}
#endif
