#if NOVALIST_COLLABORATION
using System.Text.Json.Nodes;
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    private async Task ValidateSharedMetadataWriteAsync(SharedFieldBinding binding, JsonNode? value)
    {
        var root = sharedRoot ?? throw new InvalidOperationException("The shared project is closed.");
        var path = SharedProjectPath.Resolve(root, binding.Path);
        var entity = JsonNode.Parse(await FileService.ReadTextAsync(path)) as JsonObject ?? throw new InvalidDataException("Invalid shared metadata.");
        ValidateSharedMetadataProjection(entity, binding, value);
    }

    private static void ValidateSharedMetadataProjection(JsonObject entity, SharedFieldBinding binding, JsonNode? value)
    {
        SharedMetadataFields.Set(entity, binding, value);
        SharedMetadataFields.ValidateStorage(entity, binding);
    }
}
#endif
