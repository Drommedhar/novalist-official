using System.Text.Json;
using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class EntitiesRpc
{
    [JsonRpcMethod("entities/updateLists")]
    public async Task<JsonElement> UpdateListsAsync(
        string type,
        string id,
        string[]? aliases,
        EntitySectionDto[]? sections,
        RelationshipRowDto[]? relationships)
    {
        var entity = await FindEntityAsync(type, id) ?? throw Unknown(id);
        RefuseIfLocked(entity);

        if (aliases != null)
        {
            var target = entity.Aliases;
            target.Clear();
            target.AddRange(aliases.Where(a => !string.IsNullOrWhiteSpace(a)));
        }
        if (sections != null)
        {
            var target = entity.Sections;
            target.Clear();
            target.AddRange(sections.Select(s => new EntitySection { Title = s.Title, Content = s.Content }));
        }
        if (relationships != null)
        {
            // Every built-in type carries relationships, not just characters.
            var rows = relationships
                .Where(r => !string.IsNullOrWhiteSpace(r.Role) || !string.IsNullOrWhiteSpace(r.Target))
                .Select(r => new EntityRelationship { Role = r.Role, Target = r.Target })
                .ToList();
            entity.Relationships = rows;
        }

        await SaveEntityAsync(entity);
        return WithResolvedImages(entity);
    }

    /// <summary>
    /// Appends a block of text to one of an entity's free-form sections, creating
    /// that section when it does not exist yet. Used by the editor's "send this
    /// passage to the Codex" capture flow: an atomic append avoids the read/modify/
    /// write race a client-side rewrite of the whole section list would have.
    /// Works for every entity type, including custom ones.
    /// </summary>
    [JsonRpcMethod("entities/appendToSection")]
    public async Task<JsonElement> AppendToSectionAsync(
        string type, string id, string sectionTitle, string text)
    {
        var title = (sectionTitle ?? string.Empty).Trim();
        if (title.Length == 0)
            throw new InvalidOperationException("A section title is required.");
        var addition = (text ?? string.Empty).Trim();
        // Appending prose to a settled entry is a write like any other.
        RefuseIfLocked(await FindAnyAsync(type, id));

        if (IsCustomType(type))
        {
            var custom = (await _entities.LoadCustomEntitiesAsync(type)).FirstOrDefault(c => c.Id == id)
                ?? throw Unknown(id);
            AppendSection(custom.Sections, title, addition);
            await _entities.SaveCustomEntityAsync(custom);
            return WithResolvedImages(custom);
        }

        IEntityData entity = type switch
        {
            "character" => (await _entities.LoadCharactersAsync()).FirstOrDefault(c => c.Id == id) as IEntityData,
            "location" => (await _entities.LoadLocationsAsync()).FirstOrDefault(l => l.Id == id),
            "item" => (await _entities.LoadItemsAsync()).FirstOrDefault(i => i.Id == id),
            "lore" => (await _entities.LoadLoreAsync()).FirstOrDefault(l => l.Id == id),
            _ => throw new InvalidOperationException($"Unknown entity type '{type}'.")
        } ?? throw Unknown(id);

        AppendSection(entity.Sections, title, addition);

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

    /// <summary>Appends to the named section (case-insensitive), separating the new
    /// text from existing content by a blank line. Creates the section if missing.</summary>
    private static void AppendSection(List<EntitySection> sections, string title, string addition)
    {
        var section = sections.FirstOrDefault(s =>
            string.Equals(s.Title, title, StringComparison.OrdinalIgnoreCase));
        if (section == null)
        {
            sections.Add(new EntitySection { Title = title, Content = addition });
            return;
        }
        section.Content = section.Content.Length == 0
            ? addition
            : $"{section.Content.TrimEnd()}\n\n{addition}";
    }

    [JsonRpcMethod("entities/inverseRole")]
    public string InverseRole(string role) =>
        _workspace.Settings.Settings.GetKnownInverseRoles(role).FirstOrDefault() ?? string.Empty;

    /// <summary>
    /// Writes an entry's relationships and, for each row naming an entry that
    /// exists and carrying an inverse role, authors the reciprocal on that entry
    /// and learns the role pair.
    ///
    /// <paramref name="type"/> defaults to "character" so a caller written when
    /// this was character-only keeps working. It is not character-only any more:
    /// an item's owner link used to be stored verbatim and never authored on the
    /// owner's record, so the relationship existed from one side and not the
    /// other, and the graph could not see it.
    /// </summary>
    [JsonRpcMethod("entities/setRelationships")]
    public async Task<JsonElement> SetRelationshipsAsync(
        string id, RelationshipEditRowDto[] rows, string type = "character")
    {
        var subject = await FindEntityAsync(type, id) ?? throw Unknown(id);

        // The rule itself lives in core, so an extension writing a relationship
        // gets the same write-back the Codex does rather than a copy of it.
        var result = Core.Services.RelationshipWriter.Apply(
            subject,
            subject.DisplayName,
            [.. rows.Select(r => new Core.Services.RelationshipRow(
                r.Role, r.Target, r.Category, r.InverseRole))],
            await AllEntitiesAsync());

        await SaveEntityAsync(subject);
        foreach (var target in result.Changed)
            await SaveEntityAsync(target);

        var settingsChanged = false;
        foreach (var (role, inverse) in result.Pairs)
            settingsChanged |= _workspace.Settings.Settings.LearnRelationshipPair(role, inverse);
        if (settingsChanged) await _workspace.Settings.SaveAsync();

        return WithResolvedImages(subject);
    }

    /// <summary>Every Codex entry of every type, custom types included.</summary>
    private async Task<List<Core.Models.IEntityData>> AllEntitiesAsync()
    {
        var all = new List<Core.Models.IEntityData>();
        all.AddRange(await _entities.LoadCharactersAsync());
        all.AddRange(await _entities.LoadLocationsAsync());
        all.AddRange(await _entities.LoadItemsAsync());
        all.AddRange(await _entities.LoadLoreAsync());
        foreach (var typeDef in _entities.GetCustomEntityTypes())
            all.AddRange(await _entities.LoadCustomEntitiesAsync(typeDef.TypeKey));
        return all;
    }

    /// <summary>
    /// Every group name any entry uses, so the picker offers what this project
    /// actually has rather than asking for it to be spelled the same way twice.
    /// </summary>
    [JsonRpcMethod("entities/groups")]
    public async Task<string[]> GroupsAsync()
        => [.. (await AllEntitiesAsync())
            .Select(e => e.Group)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Puts an entry in a group, or takes it out with an empty name. Any type:
    /// a faction spans them, which is the whole reason a group is worth having.
    /// </summary>
    [JsonRpcMethod("entities/setGroup")]
    public async Task<string[]> SetGroupAsync(string type, string id, string? group)
    {
        var entity = await FindEntityAsync(type, id) ?? throw Unknown(id);
        entity.Group = (group ?? string.Empty).Trim();
        await SaveEntityAsync(entity);
        return await GroupsAsync();
    }

    /// <summary>
    /// Moves a place in the tree. An empty parent lifts it to the top.
    ///
    /// Reparenting used to mean typing into an autocomplete field, so nothing
    /// ever checked the answer: a place could be made its own ancestor and the
    /// whole branch would silently vanish, because a cycle has no root and the
    /// renderer refuses to recurse forever. Returns false when the move was
    /// refused, so a drag can snap back rather than appear to have worked.
    /// </summary>
    [JsonRpcMethod("entities/setParent")]
    public async Task<bool> SetParentAsync(string id, string? parentName)
    {
        var places = await _entities.LoadLocationsAsync();
        var child = places.FirstOrDefault(l => l.Id == id) ?? throw Unknown(id);

        if (!Core.Services.PlaceHierarchy.CanReparent(places, child, parentName)) return false;

        child.Parent = (parentName ?? string.Empty).Trim();
        await _entities.SaveLocationAsync(child);
        return true;
    }

    /// <summary>
    /// Marks a place as a world, or stops it being one. A world sits at the top
    /// of the tree, so becoming one drops whatever parent it had - there is
    /// nothing above a world, which is what makes it one.
    /// </summary>
    [JsonRpcMethod("entities/setIsWorld")]
    public async Task<bool> SetIsWorldAsync(string id, bool isWorld)
    {
        var place = (await _entities.LoadLocationsAsync()).FirstOrDefault(l => l.Id == id)
            ?? throw Unknown(id);

        place.IsWorld = isWorld;
        if (isWorld) place.Parent = string.Empty;
        await _entities.SaveLocationAsync(place);
        return true;
    }

    [JsonRpcMethod("scenes/setSynopsis")]
    public async Task SetSynopsisAsync(string chapterGuid, string sceneId, string synopsis)
    {
        var (_, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        scene.Synopsis = synopsis.Length == 0 ? null : synopsis;
        await _workspace.Projects.SaveScenesAsync();
    }

    [JsonRpcMethod("scenes/setNotes")]
    public async Task SetNotesAsync(string chapterGuid, string sceneId, string notes)
    {
        var (_, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        scene.Notes = notes.Length == 0 ? null : notes;
        await _workspace.Projects.SaveScenesAsync();
    }
}
