using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;
using Novalist.Desktop.ViewModels;

namespace Novalist.Desktop.Editor;

public sealed class FocusPeekExtension : IEditorExtension
{
    private const double CardWidth = 540;
    private const double CardHeight = 420;
    private const double CardMargin = 12;
    private const string UsersIconPath = "M8 12A3 3 0 1 0 8 6A3 3 0 0 0 8 12ZM15.5 10A2.5 2.5 0 1 0 15.5 5A2.5 2.5 0 0 0 15.5 10ZM3.5 19C3.5 16.5147 5.51472 14.5 8 14.5C10.4853 14.5 12.5 16.5147 12.5 19V19.5H3.5V19ZM12.5 19.5V19C12.5 18.0739 12.2503 17.2061 11.8145 16.4601C12.4966 15.8667 13.3879 15.5 14.3654 15.5H14.6346C16.7743 15.5 18.5 17.2257 18.5 19.3654V19.5H12.5Z";

    private readonly FocusPeekViewModel _viewModel;
    private readonly IProjectService _projectService;
    private readonly IEntityService _entityService;
    private readonly IMapService _mapService;
    private readonly Action<EntityType, object> _openEntity;
    private readonly Func<string, string, Task> _navigateToPin;

    private EditorDocumentContext? _context;
    private CancellationTokenSource? _peekCts;
    private Dictionary<string, FocusPeekEntityReference> _entityLookup = new(StringComparer.OrdinalIgnoreCase);
    private List<LocationData> _locations = [];
    private string? _lastAlias;
    private FocusPeekEntityReference? _currentReference;
    /// <summary>Maps entityId → all map pins referencing that entity. Rebuilt
    /// alongside the entity-alias index whenever the project changes.</summary>
    private Dictionary<string, List<MapPinIndexEntry>> _pinIndex = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Fired after <see cref="RefreshEntityIndexAsync"/> rebuilds the
    /// entity-alias index so the host can re-push to the editor WebView.</summary>
    public event Action? EntityIndexChanged;

    /// <summary>Bounds of the editor control, set by EditorView when size changes.</summary>
    public Size EditorBounds { get; set; }

    public FocusPeekExtension(
        FocusPeekViewModel viewModel,
        IProjectService projectService,
        IEntityService entityService,
        IMapService mapService,
        Action<EntityType, object> openEntity,
        Func<string, string, Task> navigateToPin)
    {
        _viewModel = viewModel;
        _projectService = projectService;
        _entityService = entityService;
        _mapService = mapService;
        _openEntity = openEntity;
        _navigateToPin = navigateToPin;

        _viewModel.CloseRequested = HandleCloseRequested;
        _viewModel.TogglePinRequested = HandleTogglePinRequested;
        _viewModel.OpenRequested = HandleOpenRequested;
        _viewModel.PointerExitedRequested = HandleCardPointerExited;
    }

    private sealed record MapPinIndexEntry(string MapId, string MapName, string PinId, string PinLabel);

    public string Name => "Focus Peek";
    public int Priority => 50;

    public void OnDocumentOpened(EditorDocumentContext context)
    {
        _context = context;
        _lastAlias = null;
        _currentReference = null;
        _ = RefreshEntityIndexAsync();
    }

    public void OnDocumentClosing(EditorDocumentContext context)
    {
        _context = null;
        HideCard(force: true);
    }

    /// <summary>
    /// Returns a JSON array of entity-detection records for the editor.
    /// Each item is {name, entityId, entityType, isAlias}. Longer names first so
    /// the regex matches them preferentially.
    /// </summary>
    public string GetEntityNamesJson()
    {
        if (_entityLookup.Count == 0) return "[]";
        var ordered = _entityLookup
            .OrderByDescending(pair => pair.Key.Length)
            .ToList();
        var sb = new System.Text.StringBuilder();
        sb.Append('[');
        for (int i = 0; i < ordered.Count; i++)
        {
            var name = ordered[i].Key;
            var entRef = ordered[i].Value;
            var entityId = GetEntityId(entRef.Entity);
            var typeKey = entRef.Type == EntityType.Custom
                ? (entRef.Entity is CustomEntityData cd ? cd.EntityTypeKey : "custom")
                : entRef.Type.ToString().ToLowerInvariant();
            if (i > 0) sb.Append(',');
            sb.Append("{\"name\":\"").Append(JsonEscape(name)).Append('\"');
            sb.Append(",\"entityId\":\"").Append(JsonEscape(entityId)).Append('\"');
            sb.Append(",\"entityType\":\"").Append(JsonEscape(typeKey)).Append('\"');
            sb.Append(",\"isAlias\":").Append(entRef.IsAlias ? "true" : "false");
            sb.Append('}');
        }
        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>
    /// Returns a JSON array of mention candidates for the @-picker — every
    /// entity + alias resolves to its primary record.
    /// </summary>
    public string GetMentionCandidatesJson()
    {
        if (_entityLookup.Count == 0) return "[]";
        var sb = new System.Text.StringBuilder();
        sb.Append('[');
        // Build one row per (display name OR alias) so the picker can fuzzy-match.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool first = true;
        foreach (var pair in _entityLookup)
        {
            var key = pair.Value.IsAlias ? $"{GetEntityId(pair.Value.Entity)}|alias|{pair.Key}" : $"{GetEntityId(pair.Value.Entity)}|primary";
            if (!seen.Add(key)) continue;

            var entityId = GetEntityId(pair.Value.Entity);
            var typeKey = pair.Value.Type == EntityType.Custom
                ? (pair.Value.Entity is CustomEntityData cd ? cd.EntityTypeKey : "custom")
                : pair.Value.Type.ToString().ToLowerInvariant();
            var primaryName = GetPrimaryName(pair.Value.Entity);
            var matchedText = pair.Key;
            var subtitle = GetSubtitle(pair.Value);

            if (!first) sb.Append(',');
            first = false;
            sb.Append('{');
            sb.Append("\"entityId\":\"").Append(JsonEscape(entityId)).Append('\"');
            sb.Append(",\"entityType\":\"").Append(JsonEscape(typeKey)).Append('\"');
            sb.Append(",\"primaryName\":\"").Append(JsonEscape(primaryName)).Append('\"');
            sb.Append(",\"matchedText\":\"").Append(JsonEscape(matchedText)).Append('\"');
            sb.Append(",\"isAlias\":").Append(pair.Value.IsAlias ? "true" : "false");
            sb.Append(",\"subtitle\":\"").Append(JsonEscape(subtitle)).Append('\"');
            sb.Append('}');
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static string GetEntityId(object entity) => entity switch
    {
        CharacterData c => c.Id,
        LocationData l => l.Id,
        ItemData i => i.Id,
        LoreData lo => lo.Id,
        CustomEntityData ce => ce.Id,
        _ => string.Empty
    };

    private static string GetPrimaryName(object entity) => entity switch
    {
        CharacterData c => c.DisplayName,
        LocationData l => l.Name,
        ItemData i => i.Name,
        LoreData lo => lo.Name,
        CustomEntityData ce => ce.Name,
        _ => string.Empty
    };

    private string GetSubtitle(FocusPeekEntityReference entRef)
    {
        return entRef.Entity switch
        {
            CharacterData c => string.IsNullOrWhiteSpace(c.Role) ? Loc.T("focusPeek.typeCharacter") : c.Role,
            LocationData l => string.IsNullOrWhiteSpace(l.Type) ? Loc.T("focusPeek.typeLocation") : l.Type,
            ItemData i => string.IsNullOrWhiteSpace(i.Type) ? Loc.T("focusPeek.typeItem") : i.Type,
            LoreData lo => string.IsNullOrWhiteSpace(lo.Category) ? Loc.T("focusPeek.typeLore") : lo.Category,
            CustomEntityData ce => _projectService.CurrentProject?.CustomEntityTypes
                .FirstOrDefault(t => string.Equals(t.TypeKey, ce.EntityTypeKey, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? ce.EntityTypeKey,
            _ => string.Empty
        };
    }

    /// <summary>
    /// Resolves an entity by ID for direct mention-span hover handling (preferred
    /// over the alias-text path because it survives renames).
    /// </summary>
    public Task<FocusPeekDisplayData?> BuildDisplayDataByIdAsync(string entityId)
    {
        var match = _entityLookup.Values.FirstOrDefault(r => GetEntityId(r.Entity) == entityId);
        if (match == null) return Task.FromResult<FocusPeekDisplayData?>(null);
        return BuildDisplayDataNullableAsync(match);
    }

    private async Task<FocusPeekDisplayData?> BuildDisplayDataNullableAsync(FocusPeekEntityReference reference)
    {
        try { return await BuildDisplayDataAsync(reference); }
        catch { return null; }
    }

    /// <summary>
    /// Called by EditorView when JS reports a hover on an `nv-entity-mention` span
    /// carrying a stable entity id.
    /// </summary>
    public async Task OnEntityHoverByIdAsync(string entityId, double x, double y)
    {
        if (_viewModel.IsPinned) return;

        CancelPendingPeek();
        _peekCts = new CancellationTokenSource();
        var token = _peekCts.Token;

        try
        {
            await Task.Delay(250, token);
            if (token.IsCancellationRequested) return;

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var reference = _entityLookup.Values.FirstOrDefault(r => GetEntityId(r.Entity) == entityId);
                if (reference == null) return;
                _currentReference = reference;
                _lastAlias = GetPrimaryName(reference.Entity);
                var displayData = await BuildDisplayDataAsync(reference);
                var position = CalculatePosition(x, y);
                _viewModel.Show(displayData, position.X, position.Y);
            });
        }
        catch (OperationCanceledException) { }
    }

    private void CancelPendingPeek()
    {
        _peekCts?.Cancel();
        _peekCts?.Dispose();
        _peekCts = null;
    }

    /// <summary>
    /// Called by EditorView when JS reports an entity hover.
    /// </summary>
    public async Task OnEntityHoverAsync(string alias, double x, double y)
    {
        if (_viewModel.IsPinned) return;

        CancelPendingPeek();
        _peekCts = new CancellationTokenSource();
        var token = _peekCts.Token;

        try
        {
            await Task.Delay(250, token);
            if (token.IsCancellationRequested) return;

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (!_entityLookup.TryGetValue(alias, out var entityRef)) return;

                if (_viewModel.IsOpen && string.Equals(_lastAlias, alias, StringComparison.OrdinalIgnoreCase))
                    return;

                _lastAlias = alias;
                _currentReference = entityRef;
                var displayData = await BuildDisplayDataAsync(entityRef);
                var position = CalculatePosition(x, y);
                _viewModel.Show(displayData, position.X, position.Y);
            });
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Called when the pointer exits the editor or moves off an entity.
    /// </summary>
    public void OnEntityExit()
    {
        Dispatcher.UIThread.Post(HideIfNotPinned, DispatcherPriority.Background);
    }

    /// <summary>
    /// Called when a pointer press occurs in the editor.
    /// </summary>
    public void OnPointerPressed()
    {
        HideIfNotPinned();
    }

    /// <summary>
    /// Called when the editor size changes.
    /// </summary>
    public void OnEditorSizeChanged(Size newSize)
    {
        EditorBounds = newSize;
        if (_viewModel.IsPinned)
            PositionPinnedCard();
    }

    public async Task RefreshEntityIndexAsync()
    {
        if (!_projectService.IsProjectLoaded)
            return;

        var charactersTask = _entityService.LoadCharactersAsync();
        var locationsTask = _entityService.LoadLocationsAsync();
        var itemsTask = _entityService.LoadItemsAsync();
        var loreTask = _entityService.LoadLoreAsync();

        await Task.WhenAll(charactersTask, locationsTask, itemsTask, loreTask);

        _locations = locationsTask.Result;

        var aliasCandidates = new Dictionary<string, List<FocusPeekEntityReference>>(StringComparer.OrdinalIgnoreCase);

        foreach (var character in charactersTask.Result)
        {
            var refPrimary = new FocusPeekEntityReference(EntityType.Character, character, isAlias: false);
            AddAlias(aliasCandidates, character.DisplayName, refPrimary);
            if (!string.Equals(character.Name, character.DisplayName, StringComparison.OrdinalIgnoreCase))
                AddAlias(aliasCandidates, character.Name, refPrimary);
            foreach (var alias in character.Aliases)
                AddAlias(aliasCandidates, alias, new FocusPeekEntityReference(EntityType.Character, character, isAlias: true));
        }

        foreach (var location in _locations)
        {
            var refPrimary = new FocusPeekEntityReference(EntityType.Location, location, isAlias: false);
            AddAlias(aliasCandidates, location.Name, refPrimary);
            foreach (var alias in location.Aliases)
                AddAlias(aliasCandidates, alias, new FocusPeekEntityReference(EntityType.Location, location, isAlias: true));
        }

        foreach (var item in itemsTask.Result)
        {
            var refPrimary = new FocusPeekEntityReference(EntityType.Item, item, isAlias: false);
            AddAlias(aliasCandidates, item.Name, refPrimary);
            foreach (var alias in item.Aliases)
                AddAlias(aliasCandidates, alias, new FocusPeekEntityReference(EntityType.Item, item, isAlias: true));
        }

        foreach (var lore in loreTask.Result)
        {
            var refPrimary = new FocusPeekEntityReference(EntityType.Lore, lore, isAlias: false);
            AddAlias(aliasCandidates, lore.Name, refPrimary);
            foreach (var alias in lore.Aliases)
                AddAlias(aliasCandidates, alias, new FocusPeekEntityReference(EntityType.Lore, lore, isAlias: true));
        }

        // Custom entity types
        var customTypes = _projectService.CurrentProject?.CustomEntityTypes;
        if (customTypes != null)
        {
            foreach (var typeDef in customTypes)
            {
                var entities = await _entityService.LoadCustomEntitiesAsync(typeDef.TypeKey);
                foreach (var entity in entities)
                {
                    var refPrimary = new FocusPeekEntityReference(EntityType.Custom, entity, isAlias: false);
                    AddAlias(aliasCandidates, entity.Name, refPrimary);
                    foreach (var alias in entity.Aliases)
                        AddAlias(aliasCandidates, alias, new FocusPeekEntityReference(EntityType.Custom, entity, isAlias: true));
                }
            }
        }

        _entityLookup = aliasCandidates
            .Where(pair => pair.Value.Count == 1 && !string.IsNullOrWhiteSpace(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value[0], StringComparer.OrdinalIgnoreCase);

        await RefreshMapPinIndexAsync();

        EntityIndexChanged?.Invoke();
    }

    /// <summary>Scans every map in the active book and indexes pins by EntityId
    /// so a peek card can show "linked on N maps" and jump to the pin.</summary>
    private async Task RefreshMapPinIndexAsync()
    {
        var fresh = new Dictionary<string, List<MapPinIndexEntry>>(StringComparer.OrdinalIgnoreCase);
        var book = _projectService.ActiveBook;
        if (book == null) { _pinIndex = fresh; return; }
        foreach (var mapRef in book.Maps)
        {
            MapData? map;
            try { map = await _mapService.LoadMapAsync(mapRef.Id); }
            catch { continue; }
            if (map == null) continue;
            foreach (var pin in map.Pins)
            {
                if (string.IsNullOrEmpty(pin.EntityId)) continue;
                if (!fresh.TryGetValue(pin.EntityId, out var list))
                {
                    list = new List<MapPinIndexEntry>();
                    fresh[pin.EntityId] = list;
                }
                list.Add(new MapPinIndexEntry(mapRef.Id, mapRef.Name ?? map.Name, pin.Id, pin.Label ?? string.Empty));
            }
        }
        _pinIndex = fresh;
    }

    /// <summary>Builds the per-entity map-pin list for the peek card.</summary>
    private IReadOnlyList<FocusPeekMapPinItem> GetMapPinsForEntity(string entityId)
    {
        if (string.IsNullOrEmpty(entityId) || !_pinIndex.TryGetValue(entityId, out var entries))
            return [];
        return entries
            .Select(e => new FocusPeekMapPinItem(e.MapId, e.MapName, e.PinId, e.PinLabel,
                (mapId, pinId) => _navigateToPin(mapId, pinId)))
            .ToList();
    }

    private Point CalculatePosition(double x, double y)
    {
        var width = EditorBounds.Width;
        var height = EditorBounds.Height;

        var left = Math.Clamp(x + CardMargin, CardMargin, Math.Max(CardMargin, width - CardWidth - CardMargin));
        var below = y + CardMargin;
        var top = below + CardHeight > height
            ? Math.Max(CardMargin, y - CardHeight - CardMargin)
            : below;

        return new Point(left, top);
    }

    private void PositionPinnedCard()
    {
        var top = Math.Max(CardMargin, EditorBounds.Height - CardHeight - CardMargin);
        _viewModel.UpdatePosition(CardMargin, top);
    }

    private async Task<FocusPeekDisplayData> BuildDisplayDataAsync(FocusPeekEntityReference entityReference)
    {
        var displayData = entityReference.Type switch
        {
            EntityType.Character => await BuildCharacterDisplayDataAsync((CharacterData)entityReference.Entity),
            EntityType.Location => BuildLocationDisplayData((LocationData)entityReference.Entity),
            EntityType.Item => BuildItemDisplayData((ItemData)entityReference.Entity),
            EntityType.Lore => BuildLoreDisplayData((LoreData)entityReference.Entity),
            EntityType.Custom => BuildCustomEntityDisplayData((CustomEntityData)entityReference.Entity),
            _ => throw new InvalidOperationException("Unsupported entity type.")
        };

        var entityName = displayData.Title;
        var aiFindings = GetCachedAiFindings(entityName);
        if (aiFindings.Count > 0)
            displayData.AiFindings = aiFindings;

        var entityId = GetEntityId(entityReference.Entity);
        var pins = GetMapPinsForEntity(entityId);
        if (pins.Count > 0)
            displayData.MapPins = pins;

        return displayData;
    }

    private List<FocusPeekAiFindingItem> GetCachedAiFindings(string entityName)
    {
        var results = new List<FocusPeekAiFindingItem>();
        if (_context == null) return results;

        var chapterAnalysis = _projectService.ProjectSettings.ChapterAnalysis;
        if (chapterAnalysis == null) return results;

        var chapterGuid = _context.ChapterGuid;
        if (!chapterAnalysis.TryGetValue(chapterGuid, out var chapterResult)) return results;

        foreach (var scene in chapterResult.Scenes.Values)
        {
            foreach (var f in scene.Findings)
            {
                if (f.Type == "scene_stats") continue;
                if (!string.Equals(f.EntityName, entityName, StringComparison.OrdinalIgnoreCase)) continue;
                results.Add(new FocusPeekAiFindingItem
                {
                    Type = f.Type,
                    Title = f.Title,
                    Description = f.Description,
                    Excerpt = f.Excerpt,
                });
            }
        }

        return results;
    }

    private Task<FocusPeekDisplayData> BuildCharacterDisplayDataAsync(CharacterData character)
    {
        var overrideMatch = ResolveCharacterOverride(character);
        var displayName = string.IsNullOrWhiteSpace(overrideMatch?.Name) ? character.Name : overrideMatch.Name;
        var displaySurname = string.IsNullOrWhiteSpace(overrideMatch?.Surname) ? character.Surname : overrideMatch.Surname;
        var displayRole = string.IsNullOrWhiteSpace(overrideMatch?.Role) ? character.Role : overrideMatch.Role;
        var displayGender = string.IsNullOrWhiteSpace(overrideMatch?.Gender) ? character.Gender : overrideMatch.Gender;
        var displayAge = ResolveCharacterAge(character, overrideMatch);
        var displayEyeColor = string.IsNullOrWhiteSpace(overrideMatch?.EyeColor) ? character.EyeColor : overrideMatch.EyeColor;
        var displayHairColor = string.IsNullOrWhiteSpace(overrideMatch?.HairColor) ? character.HairColor : overrideMatch.HairColor;
        var displayHairLength = string.IsNullOrWhiteSpace(overrideMatch?.HairLength) ? character.HairLength : overrideMatch.HairLength;
        var displayHeight = string.IsNullOrWhiteSpace(overrideMatch?.Height) ? character.Height : overrideMatch.Height;
        var displayBuild = string.IsNullOrWhiteSpace(overrideMatch?.Build) ? character.Build : overrideMatch.Build;
        var displaySkinTone = string.IsNullOrWhiteSpace(overrideMatch?.SkinTone) ? character.SkinTone : overrideMatch.SkinTone;
        var displayDistinguishingFeatures = string.IsNullOrWhiteSpace(overrideMatch?.DistinguishingFeatures) ? character.DistinguishingFeatures : overrideMatch.DistinguishingFeatures;
        var title = string.IsNullOrWhiteSpace(displaySurname) ? displayName : $"{displayName} {displaySurname}";

        var relationships = overrideMatch?.Relationships ?? character.Relationships;
        var customProperties = new Dictionary<string, string>(character.CustomProperties, StringComparer.OrdinalIgnoreCase);
        if (overrideMatch?.CustomProperties != null)
        {
            foreach (var property in overrideMatch.CustomProperties)
                customProperties[property.Key] = property.Value;
        }

        var images = overrideMatch?.Images is { Count: > 0 } ? overrideMatch.Images : character.Images;
        var pills = new List<FocusPeekPillItem>();
        AddPill(pills, displayRole, "#3B4466");
        AddPill(pills, displayGender, "#314355");
        AddPill(pills, string.IsNullOrWhiteSpace(displayAge) ? string.Empty : Loc.T("focusPeek.agePill", displayAge), "#2E344D", true);
        AddPill(pills, string.IsNullOrWhiteSpace(character.Group) ? string.Empty : character.Group, "#2A3C38", true);
        AddPill(pills, relationships.Count > 0 ? relationships.Count.ToString() : string.Empty, "#2E344D", true, UsersIconPath);

        var appearanceProperties = new List<FocusPeekPropertyItem>();
        AddProperty(appearanceProperties, Loc.T("focusPeek.eyes"), displayEyeColor);
        AddProperty(appearanceProperties, Loc.T("focusPeek.hair"), displayHairColor);
        AddProperty(appearanceProperties, Loc.T("focusPeek.hairLength"), displayHairLength);
        AddProperty(appearanceProperties, Loc.T("focusPeek.height"), displayHeight);
        AddProperty(appearanceProperties, Loc.T("focusPeek.build"), displayBuild);
        AddProperty(appearanceProperties, Loc.T("focusPeek.skin"), displaySkinTone);
        AddProperty(appearanceProperties, Loc.T("focusPeek.distinguishing"), displayDistinguishingFeatures);

        return Task.FromResult(new FocusPeekDisplayData
        {
            EntityType = EntityType.Character,
            Entity = character,
            Title = title,
            TypeLabel = Loc.T("focusPeek.typeCharacter"),
            TypeBadgeBackground = "#5B3F7A",
            ChapterInfo = overrideMatch?.ScopeLabel ?? string.Empty,
            Pills = pills,
            Images = images.Select(image => new FocusPeekImageItem { Name = image.Name, Path = image.Path }).ToList(),
            Relationships = relationships.Select(rel => BuildRelationshipItem(rel.Role, rel.Target)).ToList(),
            AppearanceProperties = appearanceProperties,
            CustomProperties = customProperties.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)).Select(pair => new FocusPeekPropertyItem { Key = pair.Key, Value = pair.Value }).ToList(),
            Sections = character.Sections.Select(section => new FocusPeekSectionItem { Title = section.Title, Content = section.Content }).ToList()
        });
    }

    private FocusPeekDisplayData BuildLocationDisplayData(LocationData location)
    {
        var childCount = _locations.Count(other => string.Equals(NormalizeEntityReference(other.Parent), location.Name, StringComparison.OrdinalIgnoreCase));
        var pills = new List<FocusPeekPillItem>();
        AddPill(pills, location.Type, "#314355");
        AddPill(pills, string.IsNullOrWhiteSpace(location.Parent) ? string.Empty : Loc.T("focusPeek.inPill", NormalizeEntityReference(location.Parent)), "#2E344D", true);
        AddPill(pills, childCount > 0 ? Loc.T("focusPeek.sublocationsPill", childCount) : string.Empty, "#2E344D", true);

        return new FocusPeekDisplayData
        {
            EntityType = EntityType.Location,
            Entity = location,
            Title = location.Name,
            TypeLabel = Loc.T("focusPeek.typeLocation"),
            TypeBadgeBackground = "#355C7D",
            Description = location.Description,
            Pills = pills,
            Images = location.Images.Select(image => new FocusPeekImageItem { Name = image.Name, Path = image.Path }).ToList(),
            CustomProperties = location.CustomProperties.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)).Select(pair => new FocusPeekPropertyItem { Key = pair.Key, Value = pair.Value }).ToList(),
            Sections = location.Sections.Select(section => new FocusPeekSectionItem { Title = section.Title, Content = section.Content }).ToList()
        };
    }

    private FocusPeekDisplayData BuildItemDisplayData(ItemData item)
    {
        var pills = new List<FocusPeekPillItem>();
        AddPill(pills, item.Type, "#5C4C2F");
        AddPill(pills, item.Origin, "#2E344D", true);

        return new FocusPeekDisplayData
        {
            EntityType = EntityType.Item,
            Entity = item,
            Title = item.Name,
            TypeLabel = Loc.T("focusPeek.typeItem"),
            TypeBadgeBackground = "#6A4D2F",
            Description = item.Description,
            Pills = pills,
            Images = item.Images.Select(image => new FocusPeekImageItem { Name = image.Name, Path = image.Path }).ToList(),
            CustomProperties = item.CustomProperties.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)).Select(pair => new FocusPeekPropertyItem { Key = pair.Key, Value = pair.Value }).ToList(),
            Sections = item.Sections.Select(section => new FocusPeekSectionItem { Title = section.Title, Content = section.Content }).ToList()
        };
    }

    private FocusPeekDisplayData BuildLoreDisplayData(LoreData lore)
    {
        var pills = new List<FocusPeekPillItem>();
        AddPill(pills, lore.Category, "#47506D");

        return new FocusPeekDisplayData
        {
            EntityType = EntityType.Lore,
            Entity = lore,
            Title = lore.Name,
            TypeLabel = Loc.T("focusPeek.typeLore"),
            TypeBadgeBackground = "#4B5A73",
            Description = lore.Description,
            Pills = pills,
            Images = lore.Images.Select(image => new FocusPeekImageItem { Name = image.Name, Path = image.Path }).ToList(),
            CustomProperties = lore.CustomProperties.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)).Select(pair => new FocusPeekPropertyItem { Key = pair.Key, Value = pair.Value }).ToList(),
            Sections = lore.Sections.Select(section => new FocusPeekSectionItem { Title = section.Title, Content = section.Content }).ToList()
        };
    }

    private FocusPeekDisplayData BuildCustomEntityDisplayData(CustomEntityData entity)
    {
        var typeDef = _projectService.CurrentProject?.CustomEntityTypes
            .FirstOrDefault(t => string.Equals(t.TypeKey, entity.EntityTypeKey, StringComparison.OrdinalIgnoreCase));
        var typeLabel = typeDef?.DisplayName ?? entity.EntityTypeKey;
        var fieldDefs = typeDef?.DefaultFields ?? [];

        var pills = new List<FocusPeekPillItem>();
        AddPill(pills, entity.Relationships.Count > 0 ? entity.Relationships.Count.ToString() : string.Empty, "#2E344D", true, UsersIconPath);

        var fieldProperties = new List<FocusPeekPropertyItem>();
        var entityRefRelationships = new List<FocusPeekRelationshipItem>();

        foreach (var pair in entity.Fields)
        {
            if (string.IsNullOrWhiteSpace(pair.Value)) continue;
            var def = fieldDefs.FirstOrDefault(f => string.Equals(f.Key, pair.Key, StringComparison.OrdinalIgnoreCase));
            var label = def?.DisplayName ?? pair.Key;

            if (def?.Type == CustomPropertyType.EntityRef)
                entityRefRelationships.Add(BuildRelationshipItem(label, pair.Value));
            else
                fieldProperties.Add(new FocusPeekPropertyItem { Key = label, Value = pair.Value });
        }

        foreach (var pair in entity.CustomProperties)
        {
            if (string.IsNullOrWhiteSpace(pair.Value)) continue;
            fieldProperties.Add(new FocusPeekPropertyItem { Key = pair.Key, Value = pair.Value });
        }

        var relationships = entity.Relationships
            .Select(rel => BuildRelationshipItem(rel.Role, rel.Target))
            .Concat(entityRefRelationships)
            .ToList();

        return new FocusPeekDisplayData
        {
            EntityType = EntityType.Custom,
            Entity = entity,
            Title = entity.Name,
            TypeLabel = typeLabel,
            TypeBadgeBackground = "#4A6A5A",
            Pills = pills,
            Images = entity.Images.Select(image => new FocusPeekImageItem { Name = image.Name, Path = image.Path }).ToList(),
            Relationships = relationships,
            CustomProperties = fieldProperties,
            Sections = entity.Sections.Select(section => new FocusPeekSectionItem { Title = section.Title, Content = section.Content }).ToList()
        };
    }

    private CharacterOverride? ResolveCharacterOverride(CharacterData character)
    {
        if (_context == null)
            return null;

        var sceneMatch = character.ChapterOverrides.FirstOrDefault(overrideItem =>
            ChapterMatches(overrideItem)
            && !string.IsNullOrWhiteSpace(overrideItem.Scene)
            && string.Equals(overrideItem.Scene, _context.SceneTitle, StringComparison.OrdinalIgnoreCase));
        if (sceneMatch != null)
            return sceneMatch;

        return character.ChapterOverrides.FirstOrDefault(overrideItem =>
            ChapterMatches(overrideItem)
            && string.IsNullOrWhiteSpace(overrideItem.Scene));
    }

    private bool ChapterMatches(CharacterOverride overrideItem)
        => _context != null
           && (string.Equals(overrideItem.Chapter, _context.ChapterGuid, StringComparison.OrdinalIgnoreCase)
               || string.Equals(overrideItem.Chapter, _context.ChapterTitle, StringComparison.OrdinalIgnoreCase));

    private string ResolveCharacterAge(CharacterData character, CharacterOverride? overrideMatch)
    {
        if (string.Equals(character.AgeMode, "date", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(character.BirthDate)
            && _context != null)
        {
            string? referenceDate = null;
            var chapter = _projectService.GetChaptersOrdered()
                .FirstOrDefault(c => string.Equals(c.Guid, _context.ChapterGuid, StringComparison.OrdinalIgnoreCase));
            if (chapter != null)
            {
                var scene = _projectService.GetScenesForChapter(chapter.Guid)
                    .FirstOrDefault(s => string.Equals(s.Id, _context.SceneId, StringComparison.OrdinalIgnoreCase));
                referenceDate = !string.IsNullOrWhiteSpace(scene?.Date) ? scene.Date
                    : !string.IsNullOrWhiteSpace(chapter.Date) ? chapter.Date
                    : null;
            }

            var computed = AgeComputation.ComputeAge(character.BirthDate, referenceDate,
                character.AgeIntervalUnit ?? IntervalUnit.Years);
            if (!string.IsNullOrWhiteSpace(computed))
                return computed;
        }

        return string.IsNullOrWhiteSpace(overrideMatch?.Age) ? character.Age : overrideMatch.Age!;
    }

    private FocusPeekRelationshipItem BuildRelationshipItem(string role, string target)
    {
        var names = target
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(n => NormalizeEntityReference(n))
            .Where(n => n.Length > 0)
            .ToList();

        var targets = names.Select((n, i) => new FocusPeekRelationshipTarget(n, NavigateToEntityAsync, CanNavigate(n), i > 0)).ToList();
        return new FocusPeekRelationshipItem(role, targets);
    }

    private bool CanNavigate(string target)
        => _entityLookup.ContainsKey(NormalizeEntityReference(target));

    private async Task NavigateToEntityAsync(string target)
    {
        var normalized = NormalizeEntityReference(target);
        if (!_entityLookup.TryGetValue(normalized, out var entityReference))
            return;

        _currentReference = entityReference;
        _lastAlias = normalized;
        var displayData = await BuildDisplayDataAsync(entityReference);
        _viewModel.Show(displayData, _viewModel.Left, _viewModel.Top);
    }

    private void HandleCloseRequested()
    {
        _lastAlias = null;
        HideCard(force: true);
    }

    private void HandleTogglePinRequested()
    {
        var isPinned = !_viewModel.IsPinned;
        _viewModel.SetPinned(isPinned);
        if (isPinned)
        {
            PositionPinnedCard();
        }
    }

    private void HandleOpenRequested()
    {
        if (_currentReference != null)
            _openEntity(_currentReference.Type, _currentReference.Entity);
    }

    private void HideCard(bool force = false)
    {
        CancelPendingPeek();
        if (force || !_viewModel.IsPinned)
        {
            _currentReference = null;
            _viewModel.SetPinned(false);
            _viewModel.Hide();
        }
    }

    private void HideIfNotPinned()
    {
        if (_viewModel.IsPinned)
            return;

        if (_viewModel.IsPointerOverCard)
            return;

        if (_viewModel.HasOpenPopup?.Invoke() == true)
            return;

        HideCard(force: true);
    }

    private void HandleCardPointerExited()
    {
        Dispatcher.UIThread.Post(HideIfNotPinned, DispatcherPriority.Background);
    }

    private static void AddAlias(IDictionary<string, List<FocusPeekEntityReference>> aliasMap, string alias, FocusPeekEntityReference entityReference)
    {
        var normalized = NormalizeEntityReference(alias);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        if (!aliasMap.TryGetValue(normalized, out var entries))
        {
            entries = [];
            aliasMap[normalized] = entries;
        }

        entries.Add(entityReference);
    }

    private static string NormalizeEntityReference(string value)
        => value.Replace("[[", string.Empty, StringComparison.Ordinal)
            .Replace("]]", string.Empty, StringComparison.Ordinal)
            .Trim();

    private static void AddPill(ICollection<FocusPeekPillItem> target, string text, string background, bool dim = false, string iconPath = "")
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        target.Add(new FocusPeekPillItem
        {
            Text = text,
            IconPath = iconPath,
            Background = background,
            Foreground = "#CDD6F4",
            Dim = dim
        });
    }

    private static void AddProperty(ICollection<FocusPeekPropertyItem> target, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        target.Add(new FocusPeekPropertyItem
        {
            Key = key,
            Value = value
        });
    }

    private static string JsonEscape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private sealed class FocusPeekEntityReference
    {
        public FocusPeekEntityReference(EntityType type, object entity, bool isAlias = false)
        {
            Type = type;
            Entity = entity;
            IsAlias = isAlias;
        }

        public EntityType Type { get; }
        public object Entity { get; }
        public bool IsAlias { get; }
    }
}