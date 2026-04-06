using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;

namespace Novalist.Desktop.ViewModels;

public partial class EntityPanelViewModel : ObservableObject
{
    private readonly IEntityService _entityService;
    private readonly IProjectService _projectService;
    private readonly HashSet<string> _selectedCharacterIds = [];
    private string? _lastSelectedCharacterId;

    [ObservableProperty]
    private EntityType _activeEntityType = EntityType.Character;

    [ObservableProperty]
    private ObservableCollection<CharacterData> _characters = [];

    [ObservableProperty]
    private ObservableCollection<CharacterGroupSectionViewModel> _characterGroups = [];

    [ObservableProperty]
    private string _characterGroupingMode = "Role";

    [ObservableProperty]
    private ObservableCollection<LocationData> _locations = [];

    [ObservableProperty]
    private ObservableCollection<LocationTreeItemViewModel> _locationTree = [];

    [ObservableProperty]
    private ObservableCollection<ItemData> _items = [];

    [ObservableProperty]
    private ObservableCollection<LoreData> _loreEntries = [];

    [ObservableProperty]
    private object? _selectedEntity;

    public event Action<EntityType, object>? EntityOpenRequested;
    public event Action? EntityDeleted;
    public event Action<LocationData>? LocationParentChanged;
    public Func<string, string, string, Task<string?>>? ShowInputDialog { get; set; }
    public Func<string, string, IReadOnlyList<EntityCreationTemplateOption>, Task<EntityCreationResult?>>? ShowEntityCreationDialog { get; set; }
    public Func<string, string, Task<bool>>? ShowConfirmDialog { get; set; }

    public EntityPanelViewModel(IEntityService entityService, IProjectService projectService)
    {
        _entityService = entityService;
        _projectService = projectService;
    }

    public async Task LoadAllAsync()
    {
        var chars = await _entityService.LoadCharactersAsync();
        Characters = new ObservableCollection<CharacterData>(chars.OrderBy(c => c.DisplayName));
        RefreshCharacterGroups();

        var locs = await _entityService.LoadLocationsAsync();
        Locations = new ObservableCollection<LocationData>(locs.OrderBy(l => l.Name));
        BuildLocationTree();

        var items = await _entityService.LoadItemsAsync();
        Items = new ObservableCollection<ItemData>(items.OrderBy(i => i.Name));

        var lore = await _entityService.LoadLoreAsync();
        LoreEntries = new ObservableCollection<LoreData>(lore.OrderBy(l => l.Name));
    }

    [RelayCommand]
    private void SetEntityType(string type)
    {
        if (Enum.TryParse<EntityType>(type, out var et))
            ActiveEntityType = et;
    }

    partial void OnCharacterGroupingModeChanged(string value)
    {
        RefreshCharacterGroups();
    }

    [RelayCommand]
    private void SetCharacterGroupingMode(string mode)
    {
        if (mode == "Role" || mode == "Group")
            CharacterGroupingMode = mode;
    }

    public void HandleCharacterSelection(CharacterListItemViewModel item, bool ctrl, bool shift, bool openEntity)
    {
        var visualOrder = CharacterGroups.SelectMany(group => group.Items).ToList();

        if (shift && _lastSelectedCharacterId != null)
        {
            var start = visualOrder.FindIndex(entry => entry.Character.Id == _lastSelectedCharacterId);
            var end = visualOrder.FindIndex(entry => entry.Character.Id == item.Character.Id);
            if (start == -1 || end == -1)
            {
                SelectSingleCharacter(item, openEntity);
                return;
            }

            ClearCharacterSelection();
            for (var index = Math.Min(start, end); index <= Math.Max(start, end); index++)
                SelectCharacterInternal(visualOrder[index]);

            return;
        }

        if (ctrl)
        {
            if (_selectedCharacterIds.Contains(item.Character.Id))
            {
                DeselectCharacterInternal(item);
            }
            else
            {
                SelectCharacterInternal(item);
                _lastSelectedCharacterId = item.Character.Id;
            }
            return;
        }

        SelectSingleCharacter(item, openEntity);
    }

    public IReadOnlyList<string> PrepareCharacterDrag(CharacterListItemViewModel item)
    {
        if (!_selectedCharacterIds.Contains(item.Character.Id))
            SelectSingleCharacter(item, openEntity: false);

        return _selectedCharacterIds.ToList();
    }

    public async Task MoveCharactersToGroupAsync(IReadOnlyList<string> characterIds, string targetGroupValue)
    {
        if (characterIds.Count == 0) return;

        var characters = Characters.Where(character => characterIds.Contains(character.Id)).ToList();
        foreach (var character in characters)
        {
            if (CharacterGroupingMode == "Group")
                character.Group = targetGroupValue;
            else
                character.Role = targetGroupValue;

            await _entityService.SaveCharacterAsync(character);
        }

        RefreshCharacterGroups();
    }

    [RelayCommand]
    private async Task CreateCharacterAsync()
    {
        var book = _projectService.ActiveBook;
        var templates = book?.CharacterTemplates
            .Select(t => new EntityCreationTemplateOption(t.Id, t.Name))
            .ToList() ?? [];

        var result = await (ShowEntityCreationDialog?.Invoke(
            Loc.T("entityPanel.newCharacter"), Loc.T("entityPanel.characterNamePrompt"), templates)
            ?? Task.FromResult<EntityCreationResult?>(null));
        if (result == null) return;

        var character = new CharacterData { Name = result.Name };
        if (result.TemplateId != null)
            ApplyCharacterTemplate(character, result.TemplateId);
        await _entityService.SaveCharacterAsync(character);
        Characters.Add(character);
        Characters = new ObservableCollection<CharacterData>(Characters.OrderBy(c => c.DisplayName));
        RefreshCharacterGroups();
        EntityOpenRequested?.Invoke(EntityType.Character, character);
    }

    [RelayCommand]
    private async Task CreateLocationAsync()
    {
        var book = _projectService.ActiveBook;
        var templates = book?.LocationTemplates
            .Select(t => new EntityCreationTemplateOption(t.Id, t.Name))
            .ToList() ?? [];

        var result = await (ShowEntityCreationDialog?.Invoke(
            Loc.T("entityPanel.newLocation"), Loc.T("entityPanel.locationNamePrompt"), templates)
            ?? Task.FromResult<EntityCreationResult?>(null));
        if (result == null) return;

        var location = new LocationData { Name = result.Name };
        if (result.TemplateId != null)
            ApplyLocationTemplate(location, result.TemplateId);
        await _entityService.SaveLocationAsync(location);
        Locations.Add(location);
        BuildLocationTree();
        EntityOpenRequested?.Invoke(EntityType.Location, location);
    }

    [RelayCommand]
    private async Task CreateItemAsync()
    {
        var book = _projectService.ActiveBook;
        var templates = book?.ItemTemplates
            .Select(t => new EntityCreationTemplateOption(t.Id, t.Name))
            .ToList() ?? [];

        var result = await (ShowEntityCreationDialog?.Invoke(
            Loc.T("entityPanel.newItem"), Loc.T("entityPanel.itemNamePrompt"), templates)
            ?? Task.FromResult<EntityCreationResult?>(null));
        if (result == null) return;

        var item = new ItemData { Name = result.Name };
        if (result.TemplateId != null)
            ApplyItemTemplate(item, result.TemplateId);
        await _entityService.SaveItemAsync(item);
        Items.Add(item);
        EntityOpenRequested?.Invoke(EntityType.Item, item);
    }

    [RelayCommand]
    private async Task CreateLoreAsync()
    {
        var book = _projectService.ActiveBook;
        var templates = book?.LoreTemplates
            .Select(t => new EntityCreationTemplateOption(t.Id, t.Name))
            .ToList() ?? [];

        var result = await (ShowEntityCreationDialog?.Invoke(
            Loc.T("entityPanel.newLore"), Loc.T("entityPanel.loreNamePrompt"), templates)
            ?? Task.FromResult<EntityCreationResult?>(null));
        if (result == null) return;

        var lore = new LoreData { Name = result.Name };
        if (result.TemplateId != null)
            ApplyLoreTemplate(lore, result.TemplateId);
        await _entityService.SaveLoreAsync(lore);
        LoreEntries.Add(lore);
        EntityOpenRequested?.Invoke(EntityType.Lore, lore);
    }

    [RelayCommand]
    private void OpenEntity(object? entity)
    {
        if (entity == null) return;
        var type = entity switch
        {
            CharacterData => EntityType.Character,
            LocationData => EntityType.Location,
            ItemData => EntityType.Item,
            LoreData => EntityType.Lore,
            _ => EntityType.Character
        };
        EntityOpenRequested?.Invoke(type, entity);
    }

    [RelayCommand]
    private async Task DeleteCharacterAsync(CharacterData? character)
    {
        if (character == null) return;
        var confirmed = await (ShowConfirmDialog?.Invoke(
            Loc.T("entityEditor.deleteConfirmTitle"),
            Loc.T("entityEditor.deleteConfirmMessage", character.DisplayName)) ?? Task.FromResult(false));
        if (!confirmed) return;
        await _entityService.DeleteCharacterAsync(character.Id, character.IsWorldBible);
        Characters.Remove(character);
        RefreshCharacterGroups();
        EntityDeleted?.Invoke();
    }

    [RelayCommand]
    private async Task DeleteLocationAsync(LocationData? location)
    {
        if (location == null) return;
        var confirmed = await (ShowConfirmDialog?.Invoke(
            Loc.T("entityEditor.deleteConfirmTitle"),
            Loc.T("entityEditor.deleteConfirmMessage", location.Name)) ?? Task.FromResult(false));
        if (!confirmed) return;
        await _entityService.DeleteLocationAsync(location.Id, location.IsWorldBible);
        Locations.Remove(location);
        BuildLocationTree();
        EntityDeleted?.Invoke();
    }

    [RelayCommand]
    private async Task DeleteItemAsync(ItemData? item)
    {
        if (item == null) return;
        var confirmed = await (ShowConfirmDialog?.Invoke(
            Loc.T("entityEditor.deleteConfirmTitle"),
            Loc.T("entityEditor.deleteConfirmMessage", item.Name)) ?? Task.FromResult(false));
        if (!confirmed) return;
        await _entityService.DeleteItemAsync(item.Id, item.IsWorldBible);
        Items.Remove(item);
        EntityDeleted?.Invoke();
    }

    [RelayCommand]
    private async Task DeleteLoreAsync(LoreData? lore)
    {
        if (lore == null) return;
        var confirmed = await (ShowConfirmDialog?.Invoke(
            Loc.T("entityEditor.deleteConfirmTitle"),
            Loc.T("entityEditor.deleteConfirmMessage", lore.Name)) ?? Task.FromResult(false));
        if (!confirmed) return;
        await _entityService.DeleteLoreAsync(lore.Id, lore.IsWorldBible);
        LoreEntries.Remove(lore);
        EntityDeleted?.Invoke();
    }

    [RelayCommand]
    private async Task ToggleWorldBibleCharacterAsync(CharacterData? character)
    {
        if (character == null) return;
        if (character.IsWorldBible)
            await _entityService.MoveEntityToBookAsync(EntityType.Character, character.Id);
        else
            await _entityService.MoveEntityToWorldBibleAsync(EntityType.Character, character.Id);
        character.IsWorldBible = !character.IsWorldBible;
        RefreshCharacterGroups();
    }

    [RelayCommand]
    private async Task ToggleWorldBibleLocationAsync(LocationData? location)
    {
        if (location == null) return;
        if (location.IsWorldBible)
            await _entityService.MoveEntityToBookAsync(EntityType.Location, location.Id);
        else
            await _entityService.MoveEntityToWorldBibleAsync(EntityType.Location, location.Id);
        location.IsWorldBible = !location.IsWorldBible;
        BuildLocationTree();
    }

    [RelayCommand]
    private async Task ToggleWorldBibleItemAsync(ItemData? item)
    {
        if (item == null) return;
        if (item.IsWorldBible)
            await _entityService.MoveEntityToBookAsync(EntityType.Item, item.Id);
        else
            await _entityService.MoveEntityToWorldBibleAsync(EntityType.Item, item.Id);
        item.IsWorldBible = !item.IsWorldBible;
    }

    [RelayCommand]
    private async Task ToggleWorldBibleLoreAsync(LoreData? lore)
    {
        if (lore == null) return;
        if (lore.IsWorldBible)
            await _entityService.MoveEntityToBookAsync(EntityType.Lore, lore.Id);
        else
            await _entityService.MoveEntityToWorldBibleAsync(EntityType.Lore, lore.Id);
        lore.IsWorldBible = !lore.IsWorldBible;
    }

    private void RefreshCharacterGroups()
    {
        var unassignedLabel = GetUnassignedCharacterGroupLabel();

        var groups = Characters
            .GroupBy(character => new
            {
                Label = GetCharacterGroupLabel(character, unassignedLabel),
                Value = GetCharacterGroupValue(character)
            })
            .OrderBy(group => group.Key.Label == unassignedLabel ? "~~~" : group.Key.Label, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CharacterGroupSectionViewModel(
                group.Key.Label,
                group.Key.Value,
                group.OrderBy(character => character.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(character => new CharacterListItemViewModel(character)
                    {
                        IsSelected = _selectedCharacterIds.Contains(character.Id)
                    })))
            .ToList();

        CharacterGroups = new ObservableCollection<CharacterGroupSectionViewModel>(groups);
    }

    public string GetUnassignedCharacterGroupLabel()
        => CharacterGroupingMode == "Group" ? Loc.T("entityPanel.noGroup") : Loc.T("entityPanel.noRole");

    private string GetCharacterGroupLabel(CharacterData character, string unassignedLabel)
    {
        var value = GetCharacterGroupValue(character);
        return string.IsNullOrWhiteSpace(value) ? unassignedLabel : value.Trim();
    }

    private string GetCharacterGroupValue(CharacterData character)
        => CharacterGroupingMode == "Group" ? character.Group : character.Role;

    private void SelectSingleCharacter(CharacterListItemViewModel item, bool openEntity)
    {
        ClearCharacterSelection();
        SelectCharacterInternal(item);
        _lastSelectedCharacterId = item.Character.Id;

        if (openEntity)
            EntityOpenRequested?.Invoke(EntityType.Character, item.Character);
    }

    private void SelectCharacterInternal(CharacterListItemViewModel item)
    {
        _selectedCharacterIds.Add(item.Character.Id);
        item.IsSelected = true;
    }

    private void DeselectCharacterInternal(CharacterListItemViewModel item)
    {
        _selectedCharacterIds.Remove(item.Character.Id);
        item.IsSelected = false;
    }

    private void ClearCharacterSelection()
    {
        foreach (var item in CharacterGroups.SelectMany(group => group.Items))
            item.IsSelected = false;
        _selectedCharacterIds.Clear();
    }

    private void ApplyCharacterTemplate(CharacterData character, string templateId)
    {
        var book = _projectService.ActiveBook;
        if (book == null) return;
        var template = book.CharacterTemplates.FirstOrDefault(t =>
            string.Equals(t.Id, templateId, StringComparison.Ordinal));
        if (template == null) return;

        character.TemplateId = template.Id;
        if (template.AgeMode == "date")
        {
            character.AgeMode = "date";
            character.AgeIntervalUnit = template.AgeIntervalUnit ?? IntervalUnit.Years;
        }

        foreach (var field in template.Fields)
        {
            ApplyCharacterField(character, field.Key, field.DefaultValue);
        }

        foreach (var def in template.CustomPropertyDefs)
        {
            if (!character.CustomProperties.ContainsKey(def.Key))
                character.CustomProperties[def.Key] = def.DefaultValue;
        }

        foreach (var section in template.Sections)
        {
            if (!character.Sections.Any(s => string.Equals(s.Title, section.Title, StringComparison.OrdinalIgnoreCase)))
                character.Sections.Add(new EntitySection { Title = section.Title, Content = section.DefaultContent });
        }
    }

    private static void ApplyCharacterField(CharacterData character, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        switch (key)
        {
            case "Gender": character.Gender = value; break;
            case "Age": character.Age = value; break;
            case "Role": character.Role = value; break;
            case "EyeColor": character.EyeColor = value; break;
            case "HairColor": character.HairColor = value; break;
            case "HairLength": character.HairLength = value; break;
            case "Height": character.Height = value; break;
            case "Build": character.Build = value; break;
            case "SkinTone": character.SkinTone = value; break;
            case "DistinguishingFeatures": character.DistinguishingFeatures = value; break;
        }
    }

    private void ApplyLocationTemplate(LocationData location, string templateId)
    {
        var book = _projectService.ActiveBook;
        if (book == null) return;
        var template = book.LocationTemplates.FirstOrDefault(t =>
            string.Equals(t.Id, templateId, StringComparison.Ordinal));
        if (template == null) return;

        location.TemplateId = template.Id;
        foreach (var field in template.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.DefaultValue)) continue;
            switch (field.Key)
            {
                case "Type": location.Type = field.DefaultValue; break;
                case "Description": location.Description = field.DefaultValue; break;
            }
        }

        foreach (var def in template.CustomPropertyDefs)
        {
            if (!location.CustomProperties.ContainsKey(def.Key))
                location.CustomProperties[def.Key] = def.DefaultValue;
        }

        foreach (var section in template.Sections)
        {
            if (!location.Sections.Any(s => string.Equals(s.Title, section.Title, StringComparison.OrdinalIgnoreCase)))
                location.Sections.Add(new EntitySection { Title = section.Title, Content = section.DefaultContent });
        }
    }

    private void ApplyItemTemplate(ItemData item, string templateId)
    {
        var book = _projectService.ActiveBook;
        if (book == null) return;
        var template = book.ItemTemplates.FirstOrDefault(t =>
            string.Equals(t.Id, templateId, StringComparison.Ordinal));
        if (template == null) return;

        item.TemplateId = template.Id;
        foreach (var field in template.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.DefaultValue)) continue;
            switch (field.Key)
            {
                case "Type": item.Type = field.DefaultValue; break;
                case "Description": item.Description = field.DefaultValue; break;
                case "Origin": item.Origin = field.DefaultValue; break;
            }
        }

        foreach (var def in template.CustomPropertyDefs)
        {
            if (!item.CustomProperties.ContainsKey(def.Key))
                item.CustomProperties[def.Key] = def.DefaultValue;
        }

        foreach (var section in template.Sections)
        {
            if (!item.Sections.Any(s => string.Equals(s.Title, section.Title, StringComparison.OrdinalIgnoreCase)))
                item.Sections.Add(new EntitySection { Title = section.Title, Content = section.DefaultContent });
        }
    }

    private void ApplyLoreTemplate(LoreData lore, string templateId)
    {
        var book = _projectService.ActiveBook;
        if (book == null) return;
        var template = book.LoreTemplates.FirstOrDefault(t =>
            string.Equals(t.Id, templateId, StringComparison.Ordinal));
        if (template == null) return;

        lore.TemplateId = template.Id;
        foreach (var field in template.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.DefaultValue)) continue;
            switch (field.Key)
            {
                case "Category": lore.Category = field.DefaultValue; break;
                case "Description": lore.Description = field.DefaultValue; break;
            }
        }

        foreach (var def in template.CustomPropertyDefs)
        {
            if (!lore.CustomProperties.ContainsKey(def.Key))
                lore.CustomProperties[def.Key] = def.DefaultValue;
        }

        foreach (var section in template.Sections)
        {
            if (!lore.Sections.Any(s => string.Equals(s.Title, section.Title, StringComparison.OrdinalIgnoreCase)))
                lore.Sections.Add(new EntitySection { Title = section.Title, Content = section.DefaultContent });
        }
    }

    // ── Location tree building ──────────────────────────────────────

    public void BuildLocationTree()
    {
        // Capture current expand state before rebuild
        var expandState = new Dictionary<string, bool>(StringComparer.Ordinal);
        CollectExpandState(LocationTree, expandState);

        var all = Locations.ToList();
        var nodeMap = new Dictionary<string, LocationTreeItemViewModel>();

        foreach (var loc in all)
            nodeMap[loc.Id] = new LocationTreeItemViewModel(loc);

        // Track visited nodes to break circular parent chains
        var roots = new List<LocationTreeItemViewModel>();
        var placed = new HashSet<string>();

        foreach (var loc in all)
        {
            var node = nodeMap[loc.Id];
            if (string.IsNullOrWhiteSpace(loc.Parent))
            {
                roots.Add(node);
                placed.Add(loc.Id);
                continue;
            }

            var parent = all.FirstOrDefault(l =>
                string.Equals(l.Name, loc.Parent, StringComparison.OrdinalIgnoreCase) && l.Id != loc.Id);
            if (parent != null && nodeMap.TryGetValue(parent.Id, out var parentNode) && !CreatesCircle(loc, parent, all))
            {
                parentNode.Children.Add(node);
                placed.Add(loc.Id);
            }
            else
            {
                roots.Add(node);
                placed.Add(loc.Id);
            }
        }

        // Sort children recursively
        SortLocationTree(roots);

        // Restore expand state from before rebuild
        RestoreExpandState(roots, expandState);

        LocationTree = new ObservableCollection<LocationTreeItemViewModel>(
            roots.OrderBy(n => n.Location.Name, StringComparer.OrdinalIgnoreCase));
    }

    private static void CollectExpandState(IEnumerable<LocationTreeItemViewModel> nodes, Dictionary<string, bool> state)
    {
        foreach (var node in nodes)
        {
            state[node.Location.Id] = node.IsExpanded;
            if (node.Children.Count > 0)
                CollectExpandState(node.Children, state);
        }
    }

    private static void RestoreExpandState(IEnumerable<LocationTreeItemViewModel> nodes, Dictionary<string, bool> state)
    {
        foreach (var node in nodes)
        {
            if (state.TryGetValue(node.Location.Id, out var expanded))
                node.IsExpanded = expanded;
            if (node.Children.Count > 0)
                RestoreExpandState(node.Children, state);
        }
    }

    public async Task SetLocationParentAsync(LocationData location, string parentName)
    {
        location.Parent = parentName;
        await _entityService.SaveLocationAsync(location);
        BuildLocationTree();
        LocationParentChanged?.Invoke(location);
    }

    private static bool CreatesCircle(LocationData child, LocationData proposedParent, List<LocationData> all)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { child.Id };
        var current = proposedParent;
        while (current != null && !string.IsNullOrWhiteSpace(current.Parent))
        {
            if (!visited.Add(current.Id))
                return true;
            current = all.FirstOrDefault(l =>
                string.Equals(l.Name, current.Parent, StringComparison.OrdinalIgnoreCase) && l.Id != current.Id);
        }
        return current != null && !visited.Add(current.Id);
    }

    private static void SortLocationTree(IEnumerable<LocationTreeItemViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Children.Count <= 0) continue;
            var sorted = node.Children.OrderBy(c => c.Location.Name, StringComparer.OrdinalIgnoreCase).ToList();
            node.Children.Clear();
            foreach (var child in sorted)
                node.Children.Add(child);
            SortLocationTree(node.Children);
        }
    }
}

public sealed class EntityCreationTemplateOption(string id, string name)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
}

public sealed class EntityCreationResult(string name, string? templateId)
{
    public string Name { get; } = name;
    public string? TemplateId { get; } = templateId;
}

public class CharacterGroupSectionViewModel
{
    public string Title { get; }
    public string GroupValue { get; }
    public ObservableCollection<CharacterListItemViewModel> Items { get; }

    public CharacterGroupSectionViewModel(string title, string groupValue, IEnumerable<CharacterListItemViewModel> items)
    {
        Title = title;
        GroupValue = groupValue;
        Items = new ObservableCollection<CharacterListItemViewModel>(items);
    }
}

public partial class CharacterListItemViewModel : ObservableObject
{
    public CharacterData Character { get; }

    [ObservableProperty]
    private bool _isSelected;

    public string DisplayName => Character.DisplayName;

    public string Gender => Character.Gender?.Trim() ?? string.Empty;

    public bool HasGender => !string.IsNullOrWhiteSpace(Gender);

    public bool IsWorldBible => Character.IsWorldBible;

    public IBrush GenderBadgeBackground => CreateGenderBadgeBackground(Gender);

    public CharacterListItemViewModel(CharacterData character)
    {
        Character = character;
    }

    private static IBrush CreateGenderBadgeBackground(string gender)
    {
        var normalized = gender.Trim().ToLowerInvariant();
        return normalized switch
        {
            "female" or "weiblich" => new SolidColorBrush(Color.Parse("#4C6A92")),
            "male" or "männlich" or "mannlich" => new SolidColorBrush(Color.Parse("#585B70")),
            _ => new SolidColorBrush(Color.Parse("#45475A"))
        };
    }
}

public partial class LocationTreeItemViewModel : ObservableObject
{
    public LocationData Location { get; }

    [ObservableProperty]
    private bool _isExpanded = true;

    public ObservableCollection<LocationTreeItemViewModel> Children { get; } = [];

    public bool HasChildren => Children.Count > 0;

    public bool HasParent => !string.IsNullOrWhiteSpace(Location.Parent);

    public bool IsWorldBible => Location.IsWorldBible;

    public LocationTreeItemViewModel(LocationData location)
    {
        Location = location;
    }
}
