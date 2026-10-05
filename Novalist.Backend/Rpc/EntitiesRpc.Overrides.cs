using System.Text.Json;
using Novalist.Core.Models;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class EntitiesRpc
{
    [JsonRpcMethod("entities/setOverride")]
    public async Task<JsonElement> SetOverrideAsync(
        string characterId,
        string chapterGuid,
        string? sceneTitle,
        Dictionary<string, string> fields,
        Dictionary<string, string>? customProperties = null)
    {
        var character = (await _entities.LoadCharactersAsync()).FirstOrDefault(c => c.Id == characterId)
            ?? throw Unknown(characterId);
        var existing = FindOrCreateOverride(character, chapterGuid, sceneTitle);
        foreach (var (key, value) in fields)
        {
            var property = typeof(CharacterOverride).GetProperty(
                char.ToUpperInvariant(key[0]) + key[1..]);
            if (property?.CanWrite == true && property.PropertyType == typeof(string))
            {
                // Empty means inherit the base value (stored as null, the diff model).
                property.SetValue(existing, string.IsNullOrEmpty(value) ? null : value);
            }
        }

        // Per-scope custom-property overrides layer over the base set in the peek
        // card; an empty map clears them (inherit the base entirely). Blank values
        // are dropped so a cleared field inherits rather than blanks the base.
        if (customProperties != null)
        {
            var kept = customProperties
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            existing.CustomProperties = kept.Count == 0 ? null : kept;
        }

        await _entities.SaveCharacterAsync(character);
        return WithResolvedImages(character);
    }

    [JsonRpcMethod("entities/removeOverride")]
    public async Task<JsonElement> RemoveOverrideAsync(
        string characterId, string chapterGuid, string? sceneTitle)
    {
        var character = (await _entities.LoadCharactersAsync()).FirstOrDefault(c => c.Id == characterId)
            ?? throw Unknown(characterId);
        character.ChapterOverrides.RemoveAll(o =>
            o.Chapter == chapterGuid && (o.Scene ?? string.Empty) == (sceneTitle ?? string.Empty));
        await _entities.SaveCharacterAsync(character);
        return WithResolvedImages(character);
    }

    /// <summary>
    /// Replaces the per-scope override image list. <paramref name="images"/> null
    /// resets the scope to inherit the base entity's images; a list (possibly
    /// empty) replaces them wholesale — the desktop
    /// <c>EntityEditorViewModel</c> override-image semantics (null = inherit,
    /// otherwise the override owns the list). Used by the inline overrides editor
    /// for gallery-add, remove, rename, and reset-to-inherit.
    /// </summary>
    [JsonRpcMethod("entities/setOverrideImages")]
    public async Task<JsonElement> SetOverrideImagesAsync(
        string characterId, string chapterGuid, string? sceneTitle, EntityImageDto[]? images)
    {
        var character = await LoadCharacterAsync(characterId);
        var ovr = FindOrCreateOverride(character, chapterGuid, sceneTitle);
        ovr.Images = images?
            .Select(i => new EntityImage { Name = i.Name, Path = i.Path })
            .ToList();
        await _entities.SaveCharacterAsync(character);
        return WithResolvedImages(character);
    }

    /// <summary>Imports a file into the entity image folder and appends it to the
    /// scope's override image list (seeding the list from the base images the
    /// first time it diverges). Serves both file-picker and clipboard paste.</summary>
    [JsonRpcMethod("entities/addOverrideImage")]
    public async Task<JsonElement> AddOverrideImageAsync(
        string characterId, string chapterGuid, string? sceneTitle, string path)
    {
        var relative = await _entities.ImportImageAsync(path);
        var name = Path.GetFileNameWithoutExtension(relative);
        return await MutateOverrideImagesAsync(characterId, chapterGuid, sceneTitle,
            images => images.Add(new EntityImage { Name = name, Path = relative }));
    }

    /// <summary>Downloads a remote image and appends it to the scope's override
    /// image list (seeding from the base images on first divergence).</summary>
    [JsonRpcMethod("entities/addOverrideImageFromUrl")]
    public async Task<JsonElement> AddOverrideImageFromUrlAsync(
        string characterId, string chapterGuid, string? sceneTitle, string url)
    {
        var (relative, fileName) = await _imageImporter.ImportAsync(url);
        return await MutateOverrideImagesAsync(characterId, chapterGuid, sceneTitle,
            images => images.Add(new EntityImage
            {
                Name = Path.GetFileNameWithoutExtension(fileName),
                Path = relative
            }));
    }

    /// <summary>Replaces the per-scope override relationship list. Null resets the
    /// scope to inherit the base relationships; a list (possibly empty, blank rows
    /// dropped) replaces them. Mirrors desktop override-relationship semantics.</summary>
    [JsonRpcMethod("entities/setOverrideRelationships")]
    public async Task<JsonElement> SetOverrideRelationshipsAsync(
        string characterId, string chapterGuid, string? sceneTitle, RelationshipRowDto[]? relationships)
    {
        var character = await LoadCharacterAsync(characterId);
        var ovr = FindOrCreateOverride(character, chapterGuid, sceneTitle);
        ovr.Relationships = relationships?
            .Where(r => !string.IsNullOrWhiteSpace(r.Role) || !string.IsNullOrWhiteSpace(r.Target))
            .Select(r => new EntityRelationship { Role = r.Role.Trim(), Target = r.Target.Trim() })
            .ToList();
        await _entities.SaveCharacterAsync(character);
        return WithResolvedImages(character);
    }

    /// <summary>Replaces the per-scope override section list. Null resets the scope
    /// to inherit the base sections; a list (possibly empty) replaces them. Mirrors
    /// desktop override-section semantics.</summary>
    [JsonRpcMethod("entities/setOverrideSections")]
    public async Task<JsonElement> SetOverrideSectionsAsync(
        string characterId, string chapterGuid, string? sceneTitle, EntitySectionDto[]? sections)
    {
        var character = await LoadCharacterAsync(characterId);
        var ovr = FindOrCreateOverride(character, chapterGuid, sceneTitle);
        ovr.Sections = sections?
            .Select(s => new EntitySection { Title = s.Title, Content = s.Content })
            .ToList();
        await _entities.SaveCharacterAsync(character);
        return WithResolvedImages(character);
    }

    private async Task<CharacterData> LoadCharacterAsync(string characterId) =>
        (await _entities.LoadCharactersAsync()).FirstOrDefault(c => c.Id == characterId)
            ?? throw Unknown(characterId);

    /// <summary>Finds the chapter/scene override matching the scope, creating and
    /// appending an empty one when absent. Scene is matched exactly (null == "").</summary>
    private static CharacterOverride FindOrCreateOverride(
        CharacterData character, string chapterGuid, string? sceneTitle)
    {
        var existing = character.ChapterOverrides.FirstOrDefault(o =>
            o.Chapter == chapterGuid && (o.Scene ?? string.Empty) == (sceneTitle ?? string.Empty));
        if (existing == null)
        {
            existing = new CharacterOverride { Chapter = chapterGuid, Scene = sceneTitle };
            character.ChapterOverrides.Add(existing);
        }
        return existing;
    }

    /// <summary>Mutates the scope's override image list in place, seeding it from a
    /// deep copy of the base images the first time the scope diverges (so an add
    /// starts from what the peek currently shows), then persists.</summary>
    private async Task<JsonElement> MutateOverrideImagesAsync(
        string characterId, string chapterGuid, string? sceneTitle, Action<List<EntityImage>> mutate)
    {
        var character = await LoadCharacterAsync(characterId);
        var ovr = FindOrCreateOverride(character, chapterGuid, sceneTitle);
        var images = ovr.Images
            ?? character.Images.Select(i => new EntityImage { Name = i.Name, Path = i.Path }).ToList();
        mutate(images);
        ovr.Images = images;
        await _entities.SaveCharacterAsync(character);
        return WithResolvedImages(character);
    }
}
