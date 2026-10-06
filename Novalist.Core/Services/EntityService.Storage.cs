using System.Security.Cryptography;
using System.Text.Json;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public partial class EntityService
{
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
        foreach (var (left, right) in a.Zip(b))
        {
            if (!string.Equals(left.Role ?? string.Empty, right.Role ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals(left.Target ?? string.Empty, right.Target ?? string.Empty, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    // ── Generic helpers ─────────────────────────────────────────────

    private async Task<List<T>> LoadEntitiesMergedAsync<T>(string bookFolder, string wbFolder) where T : IEntityData
    {
        var result = new List<T>();

        // Load from book
        var bookDir = Path.Combine(BookRoot, bookFolder);
        if (await _files.DirectoryExistsAsync(bookDir))
        {
            foreach (var file in await _files.GetFilesAsync(bookDir, "*.json"))
            {
                T? entity;
                try
                {
                    var json = await _files.ReadTextAsync(file);
                    entity = JsonSerializer.Deserialize<T>(json, JsonOptions);
                }
                catch (JsonException)
                {
                    // Skip malformed JSON, but surface access/download failures:
                    // unavailable cloud data must not look like a missing entry.
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
            if (await _files.DirectoryExistsAsync(wbDir))
            {
                var bookIds = new HashSet<string>(result.Select(e => e.Id), StringComparer.Ordinal);
                foreach (var file in await _files.GetFilesAsync(wbDir, "*.json"))
                {
                    T? entity;
                    try
                    {
                        var json = await _files.ReadTextAsync(file);
                        entity = JsonSerializer.Deserialize<T>(json, JsonOptions);
                    }
                    catch (JsonException)
                    {
                        // A malformed entry does not prevent loading its neighbours.
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

        await _files.CreateDirectoryAsync(dir);

        var filePath = Path.Combine(dir, $"{id}.json");
        var json = JsonSerializer.Serialize(entity, JsonOptions);

        // What the entry said before this write, kept so overwriting a
        // character sheet has an answer inside the app. Taken before the write
        // because the state worth keeping is the one being replaced.
        if (await _files.ExistsAsync(filePath))
        {
            var previous = await _files.ReadTextAsync(filePath);
            await new EntityHistory(_projectService).RecordAsync(id, previous, json);
        }

        await _files.WriteTextAsync(filePath, json);
    }

    private async Task DeleteEntityAsync(string folder, string id, bool isWorldBible)
    {
        string root = isWorldBible && WorldBibleRoot != null ? WorldBibleRoot : BookRoot;
        var filePath = Path.Combine(root, folder, $"{id}.json");
        if (await _files.ExistsAsync(filePath))
            await _files.DeleteFileAsync(filePath);
    }

    private (string bookFolder, string wbFolder) GetEntityFolders(EntityType type) => type switch
    {
        EntityType.Character => (Book.CharacterFolder, Project.CharacterFolder),
        EntityType.Location => (Book.LocationFolder, Project.LocationFolder),
        EntityType.Item => (Book.ItemFolder, Project.ItemFolder),
        EntityType.Lore => (Book.LoreFolder, Project.LoreFolder),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
