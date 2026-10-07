using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Novalist.Core;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;
using Novalist.Sdk.Services;

namespace Novalist.Backend.Extensions;

public sealed partial class HostServices
{
    async Task<EntityContentInfo?> IExtensionEntityService.GetEntityContentAsync(string typeKey, string entityId)
    {
        var entity = await LoadAnyAsync(typeKey, entityId);
        if (entity == null) return null;
        return new EntityContentInfo
        {
            Id = entity.Id,
            Name = entity.DisplayName,
            Description = entity switch
            {
                LocationData location => location.Description,
                ItemData item => item.Description,
                LoreData lore => lore.Description,
                _ => string.Empty
            },
            Sections = entity.Sections.Select(section => new CustomEntitySectionInfo
            {
                Title = section.Title,
                Content = section.Content,
                ReaderHidden = section.ReaderHidden
            }).ToArray(),
            ImagePaths = entity.Images.Select(image => image.Path).ToArray(),
            Aliases = entity.Aliases.ToArray(),
            ReaderHidden = entity.ReaderHidden
        };
    }

    // ── IExtensionEntityService ────────────────────────────────────

    async Task<IReadOnlyList<Sdk.Services.CharacterInfo>> IExtensionEntityService.LoadCharactersAsync()
    {
        var characters = await _entityService.LoadCharactersAsync();
        return characters.Select(c => new Sdk.Services.CharacterInfo
        {
            Id = c.Id,
            DisplayName = c.DisplayName,
            Role = c.Role
        }).ToList();
    }

    async Task<IReadOnlyList<Sdk.Services.LocationInfo>> IExtensionEntityService.LoadLocationsAsync()
    {
        var locations = await _entityService.LoadLocationsAsync();
        return locations.Select(l => new Sdk.Services.LocationInfo
        {
            Id = l.Id,
            Name = l.Name,
            Type = l.Type
        }).ToList();
    }

    async Task<IReadOnlyList<Sdk.Services.ItemInfo>> IExtensionEntityService.LoadItemsAsync()
    {
        var items = await _entityService.LoadItemsAsync();
        return items.Select(i => new Sdk.Services.ItemInfo
        {
            Id = i.Id,
            Name = i.Name,
            Type = i.Type
        }).ToList();
    }

    async Task<IReadOnlyList<Sdk.Services.LoreInfo>> IExtensionEntityService.LoadLoreAsync()
    {
        var lore = await _entityService.LoadLoreAsync();
        return lore.Select(l => new Sdk.Services.LoreInfo
        {
            Id = l.Id,
            Name = l.Name,
            Category = l.Category
        }).ToList();
    }

    async Task<IReadOnlyList<Sdk.Services.CustomEntityInfo>> IExtensionEntityService.LoadCustomEntitiesAsync(string typeKey)
    {
        var entities = await _entityService.LoadCustomEntitiesAsync(typeKey);
        return entities.Select(e => new Sdk.Services.CustomEntityInfo
        {
            Id = e.Id,
            Name = e.Name,
            EntityTypeKey = e.EntityTypeKey,
            Fields = e.Fields
        }).ToList();
    }

    IReadOnlyList<Sdk.Services.CustomEntityTypeInfo> IExtensionEntityService.GetCustomEntityTypes()
    {
        return _entityService.GetCustomEntityTypes().Select(t => new Sdk.Services.CustomEntityTypeInfo
        {
            TypeKey = t.TypeKey,
            DisplayName = t.DisplayName,
            DisplayNamePlural = t.DisplayNamePlural,
            Icon = t.Icon
        }).ToList();
    }

    async Task IExtensionEntityService.SaveCustomEntityAsync(Sdk.Services.CustomEntityInfo entity)
    {
        var data = new CustomEntityData
        {
            Id = entity.Id,
            Name = entity.Name,
            EntityTypeKey = entity.EntityTypeKey,
            Fields = new Dictionary<string, string>(entity.Fields),
        };
        if (entity.Sections is { } sections)
        {
            data.Sections = sections.Select(s => new EntitySection
            {
                Title = s.Title,
                Content = s.Content,
            }).ToList();
        }
        await _entityService.SaveCustomEntityAsync(data);
    }

    async Task<string?> IExtensionEntityService.CreateEntityAsync(
        string typeKey, string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var trimmed = name.Trim();

        switch ((typeKey ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "character":
                {
                    // A character has no description field, so the text becomes a
                    // section — the same place the Codex editor would put it.
                    var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    var data = new CharacterData
                    {
                        Name = parts.Length > 0 ? parts[0] : trimmed,
                        Surname = parts.Length > 1 ? parts[1] : string.Empty,
                    };
                    if (!string.IsNullOrWhiteSpace(description))
                        data.Sections.Add(new EntitySection { Title = "Notes", Content = description });
                    await _entityService.SaveCharacterAsync(data);
                    return data.Id;
                }
            case "location":
                {
                    var data = new LocationData { Name = trimmed, Description = description ?? string.Empty };
                    await _entityService.SaveLocationAsync(data);
                    return data.Id;
                }
            case "item":
                {
                    var data = new ItemData { Name = trimmed, Description = description ?? string.Empty };
                    await _entityService.SaveItemAsync(data);
                    return data.Id;
                }
            case "lore":
                {
                    var data = new LoreData { Name = trimmed, Description = description ?? string.Empty };
                    await _entityService.SaveLoreAsync(data);
                    return data.Id;
                }
            default:
                {
                    // Fall through to a registered custom type; unknown keys return
                    // null rather than silently creating the wrong kind of entry.
                    var types = _entityService.GetCustomEntityTypes();
                    var match = types.FirstOrDefault(t =>
                        string.Equals(t.TypeKey, typeKey, StringComparison.OrdinalIgnoreCase));
                    if (match == null) return null;

                    var data = new CustomEntityData { Name = trimmed, EntityTypeKey = match.TypeKey };
                    if (!string.IsNullOrWhiteSpace(description))
                        data.Sections = [new EntitySection { Title = "Notes", Content = description }];
                    await _entityService.SaveCustomEntityAsync(data);
                    return data.Id;
                }
        }
    }

    /// <summary>
    /// The entries this scene may put in front of a model. The host resolves
    /// which entries the scene mentions and applies the writer's inclusion
    /// setting, so an extension cannot leak an entry marked "never" by
    /// assembling context from the raw lists instead.
    /// </summary>
    async Task<IReadOnlyList<Sdk.Services.AiContextEntryInfo>>
        IExtensionEntityService.GetAiContextAsync(string chapterGuid, string sceneId)
    {
        var chapter = _projectService.GetChaptersOrdered()
            .FirstOrDefault(c => c.Guid == chapterGuid);
        var scene = chapter == null
            ? null
            : _projectService.GetScenesForChapter(chapterGuid).FirstOrDefault(s => s.Id == sceneId);
        if (chapter == null || scene == null) return [];

        var html = await _projectService.ReadSceneContentAsync(chapter, scene);
        var prose = Core.Utilities.TextDiff.StripHtml(html);

        var characters = await _entityService.LoadCharactersAsync();
        var locations = await _entityService.LoadLocationsAsync();
        var items = await _entityService.LoadItemsAsync();
        var lore = await _entityService.LoadLoreAsync();

        var all = new List<Core.Services.AiContextEntry>();
        all.AddRange(characters.Select(c => new Core.Services.AiContextEntry(
            c.Id, "character", EntityResolveIndex.Compose(c.Name, c.Surname), c.Ai, c.Sections)));
        all.AddRange(locations.Select(l => new Core.Services.AiContextEntry(
            l.Id, "location", l.Name, l.Ai, l.Sections)));
        all.AddRange(items.Select(i => new Core.Services.AiContextEntry(
            i.Id, "item", i.Name, i.Ai, i.Sections)));
        all.AddRange(lore.Select(lo => new Core.Services.AiContextEntry(
            lo.Id, "lore", lo.Name, lo.Ai, lo.Sections)));
        foreach (var type in _entityService.GetCustomEntityTypes())
        {
            var entities = await _entityService.LoadCustomEntitiesAsync(type.TypeKey);
            all.AddRange(entities.Select(ce => new Core.Services.AiContextEntry(
                ce.Id, type.TypeKey, ce.Name, ce.Ai, ce.Sections)));
        }

        // "Mentioned" is a plain name match against the scene's prose. It is
        // deliberately generous: a near-miss that includes one extra entry is a
        // better failure than one that withholds context the model needed.
        var mentioned = all
            .Where(e => e.Name.Length > 0
                        && prose.Contains(e.Name, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Id)
            .ToHashSet(StringComparer.Ordinal);

        return [.. Core.Services.AiContextPolicy.Allowed(all, mentioned)
            .Select(e => new Sdk.Services.AiContextEntryInfo
            {
                Id = e.Id,
                TypeKey = e.TypeKey,
                Name = e.Name,
                Inclusion = e.Inclusion.ToString(),
                Sections = [.. e.Sections.Select(sec => new Sdk.Services.AiContextSectionInfo
                {
                    Title = sec.Title,
                    Content = sec.Content
                })]
            })];
    }

    void IExtensionEntityService.RequestEntityRefresh()
    {
        EntityRefreshRequested?.Invoke();
    }

    List<string> IExtensionEntityService.GetProjectImages() => _entityService.GetProjectImages();
    string IExtensionEntityService.GetImageFullPath(string relativePath) => _entityService.GetImageFullPath(relativePath);

    async Task<string?> IExtensionEntityService.GetCharacterImagePathAsync(string characterId, string? chapterGuid, string? sceneId)
    {
        var characters = await _entityService.LoadCharactersAsync();
        var character = characters.FirstOrDefault(c => string.Equals(c.Id, characterId, StringComparison.OrdinalIgnoreCase));
        if (character == null) return null;

        // Resolve chapter title + scene title for override matching (Scene
        // field on overrides is stored as title, not id).
        Novalist.Core.Models.ChapterData? chapter = null;
        Novalist.Core.Models.SceneData? scene = null;
        if (!string.IsNullOrEmpty(chapterGuid))
        {
            chapter = _projectService.GetChaptersOrdered()
                .FirstOrDefault(c => string.Equals(c.Guid, chapterGuid, StringComparison.OrdinalIgnoreCase));
            if (chapter != null && !string.IsNullOrEmpty(sceneId))
            {
                scene = _projectService.GetScenesForChapter(chapter.Guid)
                    .FirstOrDefault(s => string.Equals(s.Id, sceneId, StringComparison.OrdinalIgnoreCase));
            }
        }

        bool ChapterMatches(Novalist.Core.Models.CharacterOverride o)
            => chapter != null
               && (string.Equals(o.Chapter, chapter.Guid, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(o.Chapter, chapter.Title, StringComparison.OrdinalIgnoreCase));

        // Prefer override that matches both chapter AND scene; then chapter-only.
        Novalist.Core.Models.CharacterOverride? match = null;
        if (chapter != null && scene != null)
        {
            match = character.ChapterOverrides.FirstOrDefault(o =>
                ChapterMatches(o)
                && !string.IsNullOrWhiteSpace(o.Scene)
                && string.Equals(o.Scene, scene.Title, StringComparison.OrdinalIgnoreCase));
        }
        match ??= chapter == null
            ? null
            : character.ChapterOverrides.FirstOrDefault(o => ChapterMatches(o) && string.IsNullOrWhiteSpace(o.Scene));

        var images = match?.Images ?? character.Images;
        if (images == null || images.Count == 0)
            images = character.Images;

        var first = images.FirstOrDefault(img => !string.IsNullOrWhiteSpace(img.Path));
        if (first == null) return null;

        var abs = _entityService.GetImageFullPath(first.Path);
        return string.IsNullOrEmpty(abs) ? null : abs;
    }

    async Task<Sdk.Services.CharacterDetailedInfo?> IExtensionEntityService.GetCharacterDetailedAsync(string characterId, string? chapterGuid, string? sceneId)
    {
        var characters = await _entityService.LoadCharactersAsync();
        var character = characters.FirstOrDefault(c => string.Equals(c.Id, characterId, StringComparison.OrdinalIgnoreCase));
        if (character == null) return null;

        Novalist.Core.Models.ChapterData? chapter = null;
        Novalist.Core.Models.SceneData? scene = null;
        if (!string.IsNullOrEmpty(chapterGuid))
        {
            chapter = _projectService.GetChaptersOrdered()
                .FirstOrDefault(c => string.Equals(c.Guid, chapterGuid, StringComparison.OrdinalIgnoreCase));
            if (chapter != null && !string.IsNullOrEmpty(sceneId))
            {
                scene = _projectService.GetScenesForChapter(chapter.Guid)
                    .FirstOrDefault(s => string.Equals(s.Id, sceneId, StringComparison.OrdinalIgnoreCase));
            }
        }

        var (sceneOverride, chapterOverride, actOverride) = FindCharacterOverrides(character, chapter, scene);
        return CharacterDetails(character, sceneOverride, chapterOverride, actOverride);
    }

    private static (CharacterOverride? Scene, CharacterOverride? Chapter, CharacterOverride? Act)
        FindCharacterOverrides(CharacterData character, ChapterData? chapter, SceneData? scene)
    {
        bool ChapterMatches(Novalist.Core.Models.CharacterOverride o)
            => chapter != null
               && (string.Equals(o.Chapter, chapter.Guid, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(o.Chapter, chapter.Title, StringComparison.OrdinalIgnoreCase));

        bool ActMatches(Novalist.Core.Models.CharacterOverride o)
            => chapter != null
               && !string.IsNullOrWhiteSpace(o.Act)
               && !string.IsNullOrWhiteSpace(chapter.Act)
               && string.Equals(o.Act, chapter.Act, StringComparison.OrdinalIgnoreCase);

        // Resolution order: scene-scoped override → chapter-scoped override → act-scoped override.
        Novalist.Core.Models.CharacterOverride? sceneOverride = null;
        Novalist.Core.Models.CharacterOverride? chapterOverride = null;
        Novalist.Core.Models.CharacterOverride? actOverride = null;

        if (scene != null)
        {
            sceneOverride = character.ChapterOverrides.FirstOrDefault(o =>
                ChapterMatches(o)
                && !string.IsNullOrWhiteSpace(o.Scene)
                && string.Equals(o.Scene, scene.Title, StringComparison.OrdinalIgnoreCase));
        }
        chapterOverride = character.ChapterOverrides.FirstOrDefault(o =>
            ChapterMatches(o) && string.IsNullOrWhiteSpace(o.Scene));
        actOverride = character.ChapterOverrides.FirstOrDefault(o =>
            ActMatches(o) && string.IsNullOrWhiteSpace(o.Scene) && string.IsNullOrWhiteSpace(o.Chapter));

        return (sceneOverride, chapterOverride, actOverride);
    }

    private static Sdk.Services.CharacterDetailedInfo CharacterDetails(
        CharacterData character, CharacterOverride? sceneOverride,
        CharacterOverride? chapterOverride, CharacterOverride? actOverride)
    {
        // Per-field resolution: scene > chapter > act > base.
        string Pick(Func<Novalist.Core.Models.CharacterOverride?, string?> selector, string baseValue)
        {
            var v = selector(sceneOverride);
            if (!string.IsNullOrWhiteSpace(v)) return v;
            v = selector(chapterOverride);
            if (!string.IsNullOrWhiteSpace(v)) return v;
            v = selector(actOverride);
            if (!string.IsNullOrWhiteSpace(v)) return v;
            return baseValue;
        }

        var relationships = sceneOverride?.Relationships
                            ?? chapterOverride?.Relationships
                            ?? actOverride?.Relationships
                            ?? character.Relationships;
        var customProps = sceneOverride?.CustomProperties
                          ?? chapterOverride?.CustomProperties
                          ?? actOverride?.CustomProperties
                          ?? character.CustomProperties;
        var sections = sceneOverride?.Sections
                       ?? chapterOverride?.Sections
                       ?? actOverride?.Sections
                       ?? character.Sections;

        var resolvedFrom = sceneOverride != null ? sceneOverride.ScopeLabel
            : chapterOverride != null ? chapterOverride.ScopeLabel
            : actOverride != null ? actOverride.ScopeLabel
            : string.Empty;

        return new Sdk.Services.CharacterDetailedInfo
        {
            Id = character.Id,
            DisplayName = character.DisplayName,
            Name = Pick(o => o?.Name, character.Name),
            Surname = Pick(o => o?.Surname, character.Surname),
            Aliases = [],
            Age = Pick(o => o?.Age, character.Age),
            Gender = Pick(o => o?.Gender, character.Gender),
            Role = Pick(o => o?.Role, character.Role),
            Group = character.Group,
            EyeColor = Pick(o => o?.EyeColor, character.EyeColor),
            HairColor = Pick(o => o?.HairColor, character.HairColor),
            HairLength = Pick(o => o?.HairLength, character.HairLength),
            Height = Pick(o => o?.Height, character.Height),
            Build = Pick(o => o?.Build, character.Build),
            SkinTone = Pick(o => o?.SkinTone, character.SkinTone),
            DistinguishingFeatures = Pick(o => o?.DistinguishingFeatures, character.DistinguishingFeatures),
            CustomProperties = customProps?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? new Dictionary<string, string>(),
            Relationships = (relationships ?? []).Select(r => new Sdk.Services.CharacterRelationshipInfo
            {
                Role = r.Role ?? string.Empty,
                TargetName = r.Target ?? string.Empty,
                Note = string.Empty,
            }).ToList(),
            Sections = (sections ?? []).Select(s => new Sdk.Services.CharacterSectionInfo
            {
                Title = s.Title ?? string.Empty,
                Content = s.Content ?? string.Empty,
            }).ToList(),
            ResolvedFromScope = resolvedFrom ?? string.Empty,
        };
    }
}
