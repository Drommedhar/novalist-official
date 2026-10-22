#if NOVALIST_COLLABORATION
using Novalist.Core.Models;
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    private async Task AddSharedProjectBindingsAsync(SharedProjectState added, CancellationToken ct)
    {
        var root = sharedRoot ?? throw new InvalidOperationException("The shared project is closed.");
        foreach (var document in added.Documents)
        {
            if (document.TextField is not null) { sharedProse[document.ItemId] = document; continue; }
            var content = await FileService.ReadTextAsync(SharedProjectPath.Resolve(root, document.Path));
            if (!FileFrontMatter.TryParse(content, out var marker)) throw new InvalidDataException("A shared scene needs an identity.");
            if (sharedSceneFiles.TryGetValue(document.ItemId, out var previous)) sharedItems.Remove(previous.Relative);
            else
            {
                sharedSceneProjected.Add(document.ItemId, FileFrontMatter.Strip(content));
                sharedSceneBaseline.Add(document.ItemId, document.State);
            }
            sharedSceneFiles[document.ItemId] = (document.Path, marker.Id);
            sharedItems[document.Path] = document.ItemId;
        }
        if (sharedScenes is { } scenes)
        {
            await scenes.AddAsync(added.Documents, ct);
            foreach (var (item, file) in sharedSceneFiles)
                if (!await FileService.ExistsAsync(SharedProjectPath.Resolve(root, file.Relative)))
                    await scenes.RemoveAsync(item, sharedSceneBaseline[item], ct);
            foreach (var (item, binding) in sharedProse)
            {
                var path = SharedProjectPath.Resolve(root, binding.Path);
                var entity = await FileService.ExistsAsync(path) ? System.Text.Json.Nodes.JsonNode.Parse(await FileService.ReadTextAsync(path)) as System.Text.Json.Nodes.JsonObject : null;
                if (entity is null || SharedProseFields.Target(entity, RequireTextField(binding)) is null)
                    await scenes.RemoveAsync(item, binding.State, ct);
            }
        }
        var values = await SharedCodexFields.ReadAsync(root, added.Fields, ct);
        foreach (var binding in added.Fields) fieldBindings[binding.ItemId] = binding;
        if (sharedFields is { } fields) await fields.AddAsync(values, ct);
    }
}
#endif
