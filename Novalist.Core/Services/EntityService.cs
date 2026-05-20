using System.Security.Cryptography;
using System.Text.Json;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public class EntityService : IEntityService
{
    private readonly IProjectService _projectService;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".svg", ".webp"];

    public EntityService(IProjectService projectService)
    {
        _projectService = projectService;
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

    public async Task<int> MigrateRelationshipDuplicatesAsync()
    {
        var characters = await LoadCharactersAsync();
        var changed = 0;
        foreach (var character in characters)
        {
            if (DeduplicateCharacterRelationships(character))
            {
                await SaveCharacterAsync(character);
                changed++;
            }
        }
        return changed;
    }

    /// <summary>
    /// Collapses a character's relationships so each role appears exactly once, with its
    /// targets merged and de-duplicated (case-insensitive, first-seen order, multi-target
    /// "A, B" lists split). Mutates <paramref name="character"/> in place; returns whether
    /// anything changed. Pure (no IO) so it is unit-testable on its own.
    /// </summary>
    public static bool DeduplicateCharacterRelationships(CharacterData character)
    {
        var roleOrder = new List<string>();
        var targetsByRole = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var seenByRole = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var relationship in character.Relationships)
        {
            var role = (relationship.Role ?? string.Empty).Trim();
            if (!targetsByRole.TryGetValue(role, out var targets))
            {
                targets = [];
                targetsByRole[role] = targets;
                seenByRole[role] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                roleOrder.Add(role);
            }

            var seen = seenByRole[role];
            foreach (var target in (relationship.Target ?? string.Empty)
                         .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (seen.Add(target))
                    targets.Add(target);
            }
        }

        var rebuilt = roleOrder
            .Select(role => new EntityRelationship { Role = role, Target = string.Join(", ", targetsByRole[role]) })
            .ToList();

        if (RelationshipsEqual(character.Relationships, rebuilt))
            return false;

        character.Relationships = rebuilt;
        return true;
    }

    private static bool RelationshipsEqual(List<EntityRelationship> a, List<EntityRelationship> b)
    {
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i].Role ?? string.Empty, b[i].Role ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals(a[i].Target ?? string.Empty, b[i].Target ?? string.Empty, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

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
        Directory.CreateDirectory(destDir);

        var sourceFile = Path.Combine(sourceDir, $"{id}.json");
        var destFile = Path.Combine(destDir, $"{id}.json");

        if (File.Exists(sourceFile))
            File.Move(sourceFile, destFile, overwrite: false);
    }

    public async Task MoveEntityToBookAsync(EntityType type, string id)
    {
        if (WorldBibleRoot == null) return;

        var (bookFolder, wbFolder) = GetEntityFolders(type);
        var sourceDir = Path.Combine(WorldBibleRoot, wbFolder);
        var destDir = Path.Combine(BookRoot, bookFolder);
        Directory.CreateDirectory(destDir);

        var sourceFile = Path.Combine(sourceDir, $"{id}.json");
        var destFile = Path.Combine(destDir, $"{id}.json");

        if (File.Exists(sourceFile))
            File.Move(sourceFile, destFile, overwrite: false);

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
        Directory.CreateDirectory(destDir);

        var sourceFile = Path.Combine(sourceDir, $"{id}.json");
        var destFile = Path.Combine(destDir, $"{id}.json");

        if (File.Exists(sourceFile))
            File.Move(sourceFile, destFile, overwrite: false);

        await Task.CompletedTask;
    }

    public async Task MoveCustomEntityToBookAsync(string entityTypeKey, string id)
    {
        if (WorldBibleRoot == null) return;

        var typeDef = GetCustomEntityTypeOrThrow(entityTypeKey);
        var sourceDir = Path.Combine(WorldBibleRoot, typeDef.FolderName);
        var destDir = Path.Combine(BookRoot, typeDef.FolderName);
        Directory.CreateDirectory(destDir);

        var sourceFile = Path.Combine(sourceDir, $"{id}.json");
        var destFile = Path.Combine(destDir, $"{id}.json");

        if (File.Exists(sourceFile))
            File.Move(sourceFile, destFile, overwrite: false);

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


    // ── Images ──────────────────────────────────────────────────────

    public async Task<string> ImportImageAsync(string sourcePath)
    {
        var imageDir = Path.Combine(BookRoot, Book.ImageFolder);
        Directory.CreateDirectory(imageDir);

        var sourceHash = await ComputeFileHashAsync(sourcePath);
        var sourceFullPath = Path.GetFullPath(sourcePath);

        foreach (var existingPath in Directory.GetFiles(imageDir)
                     .Where(file => ImageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())))
        {
            if (string.Equals(Path.GetFullPath(existingPath), sourceFullPath, StringComparison.OrdinalIgnoreCase))
                return Path.Combine(Book.ImageFolder, Path.GetFileName(existingPath)).Replace('\\', '/');

            var existingHash = await ComputeFileHashAsync(existingPath);
            if (string.Equals(existingHash, sourceHash, StringComparison.OrdinalIgnoreCase))
                return Path.Combine(Book.ImageFolder, Path.GetFileName(existingPath)).Replace('\\', '/');
        }

        var fileName = Path.GetFileName(sourcePath);
        var destName = GetUniqueImageFileName(imageDir, fileName);
        var destPath = Path.Combine(imageDir, destName);

        if (!string.Equals(Path.GetFullPath(destPath), sourceFullPath, StringComparison.OrdinalIgnoreCase))
            File.Copy(sourcePath, destPath);

        return Path.Combine(Book.ImageFolder, destName).Replace('\\', '/');
    }

    public List<string> GetProjectImages()
    {
        var results = new List<string>();

        // Book images (recursive to support subdirectories)
        var bookImageDir = Path.Combine(BookRoot, Book.ImageFolder);
        if (Directory.Exists(bookImageDir))
        {
            results.AddRange(Directory.GetFiles(bookImageDir, "*", SearchOption.AllDirectories)
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Select(f => Path.GetRelativePath(BookRoot, f).Replace('\\', '/')));
        }

        // World Bible images
        if (WorldBibleRoot != null)
        {
            var wbImageDir = Path.Combine(WorldBibleRoot, Project.ImageFolder);
            if (Directory.Exists(wbImageDir))
            {
                results.AddRange(Directory.GetFiles(wbImageDir, "*", SearchOption.AllDirectories)
                    .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .Select(f => Path.Combine(Project.WorldBibleFolder,
                        Path.GetRelativePath(WorldBibleRoot, f).Replace('\\', '/'))));
            }
        }

        return results.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public string GetImageFullPath(string relativePath)
    {
        if (_projectService.ProjectRoot == null)
            throw new InvalidOperationException("No project loaded.");

        // WB images use the project root, book images use the book root
        if (relativePath.StartsWith(Project.WorldBibleFolder, StringComparison.OrdinalIgnoreCase))
            return Path.Combine(_projectService.ProjectRoot, relativePath);

        return Path.Combine(BookRoot, relativePath);
    }

    // ── Generic helpers ─────────────────────────────────────────────

    private async Task<List<T>> LoadEntitiesMergedAsync<T>(string bookFolder, string wbFolder) where T : IEntityData
    {
        var result = new List<T>();

        // Load from book
        var bookDir = Path.Combine(BookRoot, bookFolder);
        if (Directory.Exists(bookDir))
        {
            foreach (var file in Directory.GetFiles(bookDir, "*.json"))
            {
                T? entity;
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    entity = JsonSerializer.Deserialize<T>(json, JsonOptions);
                }
                catch
                {
                    // Skip a corrupt/unreadable entity file rather than failing the whole load.
                    continue;
                }
                if (entity != null)
                {
                    entity.IsWorldBible = false;
                    result.Add(entity);
                }
            }
        }

        // Load from world bible
        if (WorldBibleRoot != null)
        {
            var wbDir = Path.Combine(WorldBibleRoot, wbFolder);
            if (Directory.Exists(wbDir))
            {
                var bookIds = new HashSet<string>(result.Select(e => e.Id), StringComparer.Ordinal);
                foreach (var file in Directory.GetFiles(wbDir, "*.json"))
                {
                    T? entity;
                    try
                    {
                        var json = await File.ReadAllTextAsync(file);
                        entity = JsonSerializer.Deserialize<T>(json, JsonOptions);
                    }
                    catch
                    {
                        // Skip a corrupt/unreadable entity file.
                        continue;
                    }
                    if (entity != null && !bookIds.Contains(entity.Id))
                    {
                        entity.IsWorldBible = true;
                        result.Add(entity);
                    }
                }
            }
        }

        return result;
    }

    private async Task SaveEntityAsync<T>(string bookFolder, string wbFolder, string id, T entity, bool isWorldBible)
    {
        string dir;
        if (isWorldBible && WorldBibleRoot != null)
        {
            dir = Path.Combine(WorldBibleRoot, wbFolder);
        }
        else
        {
            dir = Path.Combine(BookRoot, bookFolder);
        }

        Directory.CreateDirectory(dir);

        var filePath = Path.Combine(dir, $"{id}.json");
        var json = JsonSerializer.Serialize(entity, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    private Task DeleteEntityAsync(string folder, string id, bool isWorldBible)
    {
        string root = isWorldBible && WorldBibleRoot != null ? WorldBibleRoot : BookRoot;
        var filePath = Path.Combine(root, folder, $"{id}.json");
        if (File.Exists(filePath))
            File.Delete(filePath);
        return Task.CompletedTask;
    }

    private (string bookFolder, string wbFolder) GetEntityFolders(EntityType type) => type switch
    {
        EntityType.Character => (Book.CharacterFolder, Project.CharacterFolder),
        EntityType.Location => (Book.LocationFolder, Project.LocationFolder),
        EntityType.Item => (Book.ItemFolder, Project.ItemFolder),
        EntityType.Lore => (Book.LoreFolder, Project.LoreFolder),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static async Task<string> ComputeFileHashAsync(string filePath)
    {
        var bytes = await File.ReadAllBytesAsync(filePath);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GetUniqueImageFileName(string imageDir, string originalFileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(originalFileName);
        var extension = Path.GetExtension(originalFileName);
        var candidate = originalFileName;
        var suffix = 2;

        while (File.Exists(Path.Combine(imageDir, candidate)))
        {
            candidate = $"{baseName} ({suffix}){extension}";
            suffix++;
        }

        return candidate;
    }
}
