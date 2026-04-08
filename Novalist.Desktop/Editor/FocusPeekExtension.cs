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
    private readonly Action<EntityType, object> _openEntity;

    private EditorDocumentContext? _context;
    private CancellationTokenSource? _peekCts;
    private Dictionary<string, FocusPeekEntityReference> _entityLookup = new(StringComparer.OrdinalIgnoreCase);
    private List<LocationData> _locations = [];
    private string? _lastAlias;
    private FocusPeekEntityReference? _currentReference;

    /// <summary>Bounds of the editor control, set by EditorView when size changes.</summary>
    public Size EditorBounds { get; set; }

    public FocusPeekExtension(
        FocusPeekViewModel viewModel,
        IProjectService projectService,
        IEntityService entityService,
        Action<EntityType, object> openEntity)
    {
        _viewModel = viewModel;
        _projectService = projectService;
        _entityService = entityService;
        _openEntity = openEntity;

        _viewModel.CloseRequested = HandleCloseRequested;
        _viewModel.TogglePinRequested = HandleTogglePinRequested;
        _viewModel.OpenRequested = HandleOpenRequested;
        _viewModel.PointerExitedRequested = HandleCardPointerExited;
    }

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
    /// Returns a JSON array of entity names for the JS editor to detect on hover.
    /// </summary>
    public string GetEntityNamesJson()
    {
        if (_entityLookup.Count == 0) return "[]";
        var names = _entityLookup.Keys.OrderByDescending(k => k.Length);
        return "[" + string.Join(",", names.Select(n => "\"" + JsonEscape(n) + "\"")) + "]";
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
            AddAlias(aliasCandidates, character.DisplayName, new FocusPeekEntityReference(EntityType.Character, character));
            if (!string.Equals(character.Name, character.DisplayName, StringComparison.OrdinalIgnoreCase))
                AddAlias(aliasCandidates, character.Name, new FocusPeekEntityReference(EntityType.Character, character));
        }

        foreach (var location in _locations)
            AddAlias(aliasCandidates, location.Name, new FocusPeekEntityReference(EntityType.Location, location));

        foreach (var item in itemsTask.Result)
            AddAlias(aliasCandidates, item.Name, new FocusPeekEntityReference(EntityType.Item, item));

        foreach (var lore in loreTask.Result)
            AddAlias(aliasCandidates, lore.Name, new FocusPeekEntityReference(EntityType.Lore, lore));

        // Custom entity types
        var customTypes = _projectService.CurrentProject?.CustomEntityTypes;
        if (customTypes != null)
        {
            foreach (var typeDef in customTypes)
            {
                var entities = await _entityService.LoadCustomEntitiesAsync(typeDef.TypeKey);
                foreach (var entity in entities)
                    AddAlias(aliasCandidates, entity.Name, new FocusPeekEntityReference(EntityType.Custom, entity));
            }
        }

        _entityLookup = aliasCandidates
            .Where(pair => pair.Value.Count == 1 && !string.IsNullOrWhiteSpace(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value[0], StringComparer.OrdinalIgnoreCase);
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
        public FocusPeekEntityReference(EntityType type, object entity)
        {
            Type = type;
            Entity = entity;
        }

        public EntityType Type { get; }
        public object Entity { get; }
    }
}