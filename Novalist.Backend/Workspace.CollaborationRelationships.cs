#if NOVALIST_COLLABORATION
using System.Text.Json;
using System.Text.Json.Nodes;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    private async Task<string> AcquireRelationshipWritesAsync(SharedFieldBinding binding, JsonNode? value, List<MetadataWrite> acquired, CancellationToken ct)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var rows = value?.Deserialize<RelationshipRow[]>(options) ?? [];
        var entities = await SharedRelationshipEntitiesAsync();
        var subject = entities.Single(entity => SharedEntityPath(entity) == binding.Path);
        var result = RelationshipWriter.Apply(subject, subject.DisplayName, rows, entities);
        foreach (var target in result.Changed)
        {
            var path = SharedEntityPath(target);
            var counterpart = fieldBindings.Values.Single(other => other.Metadata?.Kind == "codex" && other.Path == path && other.Key == "relationships");
            await AcquireMetadataWriteAsync(counterpart, acquired, current =>
            {
                target.Relationships = current?.Deserialize<List<EntityRelationship>>(options) ?? [];
                RelationshipWriter.Apply(subject, subject.DisplayName, rows, [target]);
                return JsonSerializer.SerializeToNode(target.Relationships);
            }, ct);
        }
        return JsonSerializer.Serialize(subject.Relationships);
    }

    private async Task<List<IEntityData>> SharedRelationshipEntitiesAsync()
    {
        var service = new EntityService(Projects);
        var result = new List<IEntityData>();
        result.AddRange(await service.LoadCharactersAsync());
        result.AddRange(await service.LoadLocationsAsync());
        result.AddRange(await service.LoadItemsAsync());
        result.AddRange(await service.LoadLoreAsync());
        foreach (var type in service.GetCustomEntityTypes()) result.AddRange(await service.LoadCustomEntitiesAsync(type.TypeKey));
        return result;
    }
}
#endif
