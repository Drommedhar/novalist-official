using System.Text.Json;
using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class EntitiesRpc
{
    [JsonRpcMethod("entities/addImage")]
    public async Task<JsonElement> AddImageAsync(
        string type, string id, string path, bool import)
    {
        var relative = import ? await _entities.ImportImageAsync(path) : path;
        var name = Path.GetFileNameWithoutExtension(relative);
        return await MutateImagesAsync(type, id, images =>
            images.Add(new EntityImage { Name = name, Path = relative }));
    }

    [JsonRpcMethod("entities/removeImage")]
    public Task<JsonElement> RemoveImageAsync(string type, string id, string path) =>
        MutateImagesAsync(type, id, images =>
            images.RemoveAll(i => i.Path == path));

    /// <summary>Renames the stored image whose <c>Path</c> matches, leaving its
    /// path (and on-disk file) untouched so add/remove still match on it.</summary>
    /// <summary>
    /// Sets what the picture shows, for a reader who cannot see it. Separate
    /// from the display name: one names the image, the other describes it, and
    /// only the second is any use read aloud.
    /// </summary>
    [JsonRpcMethod("entities/setImageAlt")]
    public Task<JsonElement> SetImageAltAsync(string type, string id, string path, string alt) =>
        MutateImagesAsync(type, id, images =>
        {
            var image = images.FirstOrDefault(i => i.Path == path);
            if (image != null) image.Alt = alt ?? string.Empty;
        });

    [JsonRpcMethod("entities/renameImage")]
    public Task<JsonElement> RenameImageAsync(string type, string id, string path, string newName) =>
        MutateImagesAsync(type, id, images =>
        {
            var image = images.FirstOrDefault(i => i.Path == path);
            if (image != null) image.Name = newName;
        });

    /// <summary>Swaps the stored path of the image matching <paramref name="oldPath"/>
    /// for <paramref name="newPath"/> (a project-relative path already on disk),
    /// keeping the display name unless it was empty.</summary>
    [JsonRpcMethod("entities/replaceImage")]
    public Task<JsonElement> ReplaceImageAsync(string type, string id, string oldPath, string newPath) =>
        MutateImagesAsync(type, id, images =>
        {
            var image = images.FirstOrDefault(i => i.Path == oldPath);
            if (image != null)
            {
                image.Path = newPath;
                if (string.IsNullOrWhiteSpace(image.Name))
                    image.Name = Path.GetFileNameWithoutExtension(newPath);
            }
        });

    /// <summary>Downloads an image from a remote URL into the entity's image
    /// folder (via <see cref="EntityService.ImportImageAsync"/>) and attaches it.
    /// Download uses the injected <see cref="HttpClient"/> so tests stub the
    /// transport; a failed request surfaces as a clean error.</summary>
    [JsonRpcMethod("entities/addImageFromUrl")]
    public async Task<JsonElement> AddImageFromUrlAsync(string type, string id, string url)
    {
        var (relative, fileName) = await _imageImporter.ImportAsync(url);
        return await MutateImagesAsync(type, id, images =>
            images.Add(new EntityImage
            {
                Name = Path.GetFileNameWithoutExtension(fileName),
                Path = relative
            }));
    }

    private async Task<JsonElement> MutateImagesAsync(
        string type, string id, Action<List<EntityImage>> mutate)
    {
        object entity = type switch
        {
            "character" => (await _entities.LoadCharactersAsync()).FirstOrDefault(c => c.Id == id) as object,
            "location" => (await _entities.LoadLocationsAsync()).FirstOrDefault(l => l.Id == id),
            "item" => (await _entities.LoadItemsAsync()).FirstOrDefault(i => i.Id == id),
            "lore" => (await _entities.LoadLoreAsync()).FirstOrDefault(l => l.Id == id),
            _ => throw new InvalidOperationException($"Unknown entity type '{type}'.")
        } ?? throw Unknown(id);

        var images = (List<EntityImage>)entity.GetType().GetProperty("Images")!.GetValue(entity)!;
        mutate(images);

        switch (entity)
        {
            case CharacterData c:
                await _entities.SaveCharacterAsync(c);
                break;
            case LocationData l:
                await _entities.SaveLocationAsync(l);
                break;
            case ItemData i:
                await _entities.SaveItemAsync(i);
                break;
            default:
                await _entities.SaveLoreAsync((LoreData)entity);
                break;
        }
        return WithResolvedImages(entity);
    }

    /// <summary>
    /// Serializes an entity and annotates each image with a <c>url</c> field
    /// (project-root-relative) for display, leaving <c>path</c> (the stored
    /// value) intact so add/remove still match on it.
    /// </summary>
    private JsonElement WithResolvedImages(object entity)
    {
        var node = JsonSerializer.SerializeToNode(entity, JsonOptions);
        if (node is System.Text.Json.Nodes.JsonObject obj)
        {
            ResolveImageUrls(obj["images"] as System.Text.Json.Nodes.JsonArray);
            // Per-scope character overrides carry their own image lists; annotate
            // each so the inline overrides editor can render them by resolved url.
            if (obj["chapterOverrides"] is System.Text.Json.Nodes.JsonArray overrides)
            {
                foreach (var ovr in overrides)
                {
                    if (ovr is System.Text.Json.Nodes.JsonObject ovrObj)
                        ResolveImageUrls(ovrObj["images"] as System.Text.Json.Nodes.JsonArray);
                }
            }
        }
        return JsonSerializer.SerializeToElement(node, JsonOptions);
    }

    /// <summary>Annotates each image object in the array with a project-root-relative
    /// <c>url</c> for the novalist-project:// protocol, leaving <c>path</c> intact.</summary>
    private void ResolveImageUrls(System.Text.Json.Nodes.JsonArray? images)
    {
        if (images == null) return;
        foreach (var image in images)
        {
            if (image is System.Text.Json.Nodes.JsonObject imageObj
                && imageObj["path"]?.GetValue<string>() is { } path)
            {
                imageObj["url"] = _entities.ResolveProjectRelativeImage(path);
            }
        }
    }
}
