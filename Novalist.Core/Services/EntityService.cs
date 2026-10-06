using System.Security.Cryptography;
using System.Text.Json;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public partial class EntityService : IEntityService
{
    private readonly IProjectService _projectService;
    private readonly IFileService _files;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public EntityService(IProjectService projectService)
    {
        _projectService = projectService;
        _files = ProjectFiles.For(projectService);
    }

    private string BookRoot => _projectService.ActiveBookRoot
        ?? throw new InvalidOperationException("No book active.");

    private string? WorldBibleRoot => _projectService.WorldBibleRoot;

    private BookData Book => _projectService.ActiveBook
        ?? throw new InvalidOperationException("No book active.");

    private ProjectMetadata Project => _projectService.CurrentProject
        ?? throw new InvalidOperationException("No project loaded.");

    // ── Characters ──────────────────────────────────────────────────

    public async Task<List<CharacterData>> LoadCharactersAsync()
        => await LoadEntitiesMergedAsync<CharacterData>(Book.CharacterFolder, Project.CharacterFolder);

    public Task SaveCharacterAsync(CharacterData character)
        => SaveEntityAsync(Book.CharacterFolder, Project.CharacterFolder, character.Id, character, character.IsWorldBible);

    public Task DeleteCharacterAsync(string id, bool isWorldBible = false)
        => DeleteEntityAsync(isWorldBible ? Project.CharacterFolder : Book.CharacterFolder, id, isWorldBible);

    // ── Locations ───────────────────────────────────────────────────

    public async Task<List<LocationData>> LoadLocationsAsync()
        => await LoadEntitiesMergedAsync<LocationData>(Book.LocationFolder, Project.LocationFolder);

    public Task SaveLocationAsync(LocationData location)
        => SaveEntityAsync(Book.LocationFolder, Project.LocationFolder, location.Id, location, location.IsWorldBible);

    public Task DeleteLocationAsync(string id, bool isWorldBible = false)
        => DeleteEntityAsync(isWorldBible ? Project.LocationFolder : Book.LocationFolder, id, isWorldBible);

    // ── Items ───────────────────────────────────────────────────────

    public async Task<List<ItemData>> LoadItemsAsync()
        => await LoadEntitiesMergedAsync<ItemData>(Book.ItemFolder, Project.ItemFolder);

    public Task SaveItemAsync(ItemData item)
        => SaveEntityAsync(Book.ItemFolder, Project.ItemFolder, item.Id, item, item.IsWorldBible);

    public Task DeleteItemAsync(string id, bool isWorldBible = false)
        => DeleteEntityAsync(isWorldBible ? Project.ItemFolder : Book.ItemFolder, id, isWorldBible);

    // ── Lore ────────────────────────────────────────────────────────

    public async Task<List<LoreData>> LoadLoreAsync()
        => await LoadEntitiesMergedAsync<LoreData>(Book.LoreFolder, Project.LoreFolder);

    public Task SaveLoreAsync(LoreData lore)
        => SaveEntityAsync(Book.LoreFolder, Project.LoreFolder, lore.Id, lore, lore.IsWorldBible);

    public Task DeleteLoreAsync(string id, bool isWorldBible = false)
        => DeleteEntityAsync(isWorldBible ? Project.LoreFolder : Book.LoreFolder, id, isWorldBible);

    // ── World Bible move operations ─────────────────────────────────

    public async Task MoveEntityToWorldBibleAsync(EntityType type, string id)
    {
        if (WorldBibleRoot == null) return;

        var (bookFolder, wbFolder) = GetEntityFolders(type);
        var sourceDir = Path.Combine(BookRoot, bookFolder);
        var destDir = Path.Combine(WorldBibleRoot, wbFolder);
        await _files.CreateDirectoryAsync(destDir);

        var sourceFile = Path.Combine(sourceDir, $"{id}.json");
        var destFile = Path.Combine(destDir, $"{id}.json");

        if (await _files.ExistsAsync(sourceFile))
            await _files.MoveFileAsync(sourceFile, destFile);
    }

    public async Task MoveEntityToBookAsync(EntityType type, string id)
    {
        if (WorldBibleRoot == null) return;

        var (bookFolder, wbFolder) = GetEntityFolders(type);
        var sourceDir = Path.Combine(WorldBibleRoot, wbFolder);
        var destDir = Path.Combine(BookRoot, bookFolder);
        await _files.CreateDirectoryAsync(destDir);

        var sourceFile = Path.Combine(sourceDir, $"{id}.json");
        var destFile = Path.Combine(destDir, $"{id}.json");

        if (await _files.ExistsAsync(sourceFile))
            await _files.MoveFileAsync(sourceFile, destFile);

        await Task.CompletedTask;
    }

    // ── Custom entities ─────────────────────────────────────────────

    public async Task<List<CustomEntityData>> LoadCustomEntitiesAsync(string entityTypeKey)
    {
        var typeDef = GetCustomEntityTypeOrThrow(entityTypeKey);
        var wbFolder = typeDef.FolderName;
        return await LoadEntitiesMergedAsync<CustomEntityData>(typeDef.FolderName, wbFolder);
    }

    public Task SaveCustomEntityAsync(CustomEntityData entity)
    {
        var typeDef = GetCustomEntityTypeOrThrow(entity.EntityTypeKey);
        return SaveEntityAsync(typeDef.FolderName, typeDef.FolderName, entity.Id, entity, entity.IsWorldBible);
    }

    public Task DeleteCustomEntityAsync(string entityTypeKey, string id, bool isWorldBible = false)
    {
        var typeDef = GetCustomEntityTypeOrThrow(entityTypeKey);
        return DeleteEntityAsync(isWorldBible ? typeDef.FolderName : typeDef.FolderName, id, isWorldBible);
    }

    public async Task MoveCustomEntityToWorldBibleAsync(string entityTypeKey, string id)
    {
        if (WorldBibleRoot == null) return;

        var typeDef = GetCustomEntityTypeOrThrow(entityTypeKey);
        var sourceDir = Path.Combine(BookRoot, typeDef.FolderName);
        var destDir = Path.Combine(WorldBibleRoot, typeDef.FolderName);
        await _files.CreateDirectoryAsync(destDir);

        var sourceFile = Path.Combine(sourceDir, $"{id}.json");
        var destFile = Path.Combine(destDir, $"{id}.json");

        if (await _files.ExistsAsync(sourceFile))
            await _files.MoveFileAsync(sourceFile, destFile);

        await Task.CompletedTask;
    }

    public async Task MoveCustomEntityToBookAsync(string entityTypeKey, string id)
    {
        if (WorldBibleRoot == null) return;

        var typeDef = GetCustomEntityTypeOrThrow(entityTypeKey);
        var sourceDir = Path.Combine(WorldBibleRoot, typeDef.FolderName);
        var destDir = Path.Combine(BookRoot, typeDef.FolderName);
        await _files.CreateDirectoryAsync(destDir);

        var sourceFile = Path.Combine(sourceDir, $"{id}.json");
        var destFile = Path.Combine(destDir, $"{id}.json");

        if (await _files.ExistsAsync(sourceFile))
            await _files.MoveFileAsync(sourceFile, destFile);

        await Task.CompletedTask;
    }

    public List<CustomEntityTypeDefinition> GetCustomEntityTypes()
        => Project.CustomEntityTypes;

    public async Task SaveCustomEntityTypeAsync(CustomEntityTypeDefinition definition)
    {
        var existing = Project.CustomEntityTypes.FindIndex(t =>
            string.Equals(t.TypeKey, definition.TypeKey, StringComparison.Ordinal));
        if (existing >= 0)
            Project.CustomEntityTypes[existing] = definition;
        else
            Project.CustomEntityTypes.Add(definition);

        await _projectService.SaveProjectAsync();
    }

    public async Task DeleteCustomEntityTypeAsync(string typeKey)
    {
        Project.CustomEntityTypes.RemoveAll(t =>
            string.Equals(t.TypeKey, typeKey, StringComparison.Ordinal));
        await _projectService.SaveProjectAsync();
    }

    private CustomEntityTypeDefinition GetCustomEntityTypeOrThrow(string typeKey)
        => Project.CustomEntityTypes.FirstOrDefault(t =>
               string.Equals(t.TypeKey, typeKey, StringComparison.Ordinal))
           ?? throw new InvalidOperationException($"Unknown custom entity type: {typeKey}");
}
