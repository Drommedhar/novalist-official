using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;
using Novalist.Sdk.Models;

namespace Novalist.Desktop.ViewModels;

public partial class CodexHubViewModel : ObservableObject
{
    private readonly IEntityService _entityService;
    private readonly IProjectService _projectService;

    private List<CharacterData> _allCharacters = [];
    private List<LocationData> _allLocations = [];
    private List<ItemData> _allItems = [];
    private List<LoreData> _allLore = [];
    private Dictionary<string, List<CustomEntityData>> _allCustom = [];

    [ObservableProperty]
    private string _activeTab = "All";

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private ObservableCollection<CodexEntityItem> _filteredEntities = [];

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _characterCount;

    [ObservableProperty]
    private int _locationCount;

    [ObservableProperty]
    private int _itemCount;

    [ObservableProperty]
    private int _loreCount;

    [ObservableProperty]
    private ObservableCollection<CodexCustomTab> _customTabs = [];

    [ObservableProperty]
    private CodexCustomTab? _selectedCustomTab;

    [ObservableProperty]
    private bool _isLoading;

    public IReadOnlyList<EntityTypeDescriptor> ExtensionEntityTypes { get; set; } = [];

    public event Action<EntityType, object>? EntityOpenRequested;

    public CodexHubViewModel(IEntityService entityService, IProjectService projectService)
    {
        _entityService = entityService;
        _projectService = projectService;
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
        await LoadInternalAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadInternalAsync()
    {
        _allCharacters = (await _entityService.LoadCharactersAsync()).OrderBy(c => c.DisplayName).ToList();
        _allLocations = (await _entityService.LoadLocationsAsync()).OrderBy(l => l.Name).ToList();
        _allItems = (await _entityService.LoadItemsAsync()).OrderBy(i => i.Name).ToList();
        _allLore = (await _entityService.LoadLoreAsync()).OrderBy(l => l.Name).ToList();

        CharacterCount = _allCharacters.Count;
        LocationCount = _allLocations.Count;
        ItemCount = _allItems.Count;
        LoreCount = _allLore.Count;

        _allCustom.Clear();
        var tabs = new List<CodexCustomTab>();
        var types = _entityService.GetCustomEntityTypes();
        foreach (var typeDef in types)
        {
            var entities = (await _entityService.LoadCustomEntitiesAsync(typeDef.TypeKey))
                .OrderBy(e => e.Name).ToList();
            _allCustom[typeDef.TypeKey] = entities;
            tabs.Add(new CodexCustomTab
            {
                TypeKey = typeDef.TypeKey,
                DisplayName = typeDef.DisplayNamePlural,
                Icon = typeDef.Icon,
                Count = entities.Count,
            });
        }
        CustomTabs = new ObservableCollection<CodexCustomTab>(tabs);

        var customTotal = _allCustom.Values.Sum(l => l.Count);
        TotalCount = CharacterCount + LocationCount + ItemCount + LoreCount + customTotal;

        ApplyFilter();
    }

    public void Refresh() => _ = LoadAsync();

    partial void OnActiveTabChanged(string value) => ApplyFilter();
    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    partial void OnSelectedCustomTabChanged(CodexCustomTab? value)
    {
        if (value is not null)
            ActiveTab = value.TypeKey;
    }

    [RelayCommand]
    private void SetTab(string tab)
    {
        SelectedCustomTab = null;
        ActiveTab = tab;
    }

    [RelayCommand]
    private void OpenEntity(CodexEntityItem? item)
    {
        if (item == null) return;
        EntityOpenRequested?.Invoke(item.EntityType, item.Entity);
    }

    private void ApplyFilter()
    {
        var query = SearchQuery?.Trim() ?? string.Empty;
        var items = new List<CodexEntityItem>();

        if (ActiveTab is "All" or "Character")
        {
            foreach (var c in _allCharacters)
            {
                if (MatchesSearch(c.DisplayName, query))
                    items.Add(new CodexEntityItem(EntityType.Character, c, c.DisplayName, c.Role, CodexEntityItem.UserIconData, c.IsWorldBible,
                        imagePath: c.Images.FirstOrDefault()?.Path));
            }
        }

        if (ActiveTab is "All" or "Location")
        {
            foreach (var l in _allLocations)
            {
                if (MatchesSearch(l.Name, query))
                    items.Add(new CodexEntityItem(EntityType.Location, l, l.Name, l.Type, CodexEntityItem.MapPinIconData, l.IsWorldBible,
                        imagePath: l.Images.FirstOrDefault()?.Path));
            }
        }

        if (ActiveTab is "All" or "Item")
        {
            foreach (var i in _allItems)
            {
                if (MatchesSearch(i.Name, query))
                    items.Add(new CodexEntityItem(EntityType.Item, i, i.Name, i.Type, CodexEntityItem.SwordIconData, i.IsWorldBible));
            }
        }

        if (ActiveTab is "All" or "Lore")
        {
            foreach (var l in _allLore)
            {
                if (MatchesSearch(l.Name, query))
                    items.Add(new CodexEntityItem(EntityType.Lore, l, l.Name, l.Category.ToString(), CodexEntityItem.ScrollIconData, l.IsWorldBible));
            }
        }

        // Custom entity types
        foreach (var (typeKey, entities) in _allCustom)
        {
            if (ActiveTab != "All" && ActiveTab != typeKey) continue;
            var typeDef = _entityService.GetCustomEntityTypes()
                .FirstOrDefault(t => string.Equals(t.TypeKey, typeKey, StringComparison.Ordinal));
            var icon = typeDef?.Icon ?? "📋";
            foreach (var ce in entities)
            {
                if (MatchesSearch(ce.Name, query))
                {
                    var subtitle = ce.Fields.Count > 0 ? ce.Fields.Values.FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "" : "";
                    items.Add(new CodexEntityItem(EntityType.Custom, ce, ce.Name, subtitle, icon, ce.IsWorldBible, isEmoji: true));
                }
            }
        }

        FilteredEntities = new ObservableCollection<CodexEntityItem>(items);
    }

    private static bool MatchesSearch(string name, string query)
    {
        if (string.IsNullOrEmpty(query)) return true;
        return name.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class CodexCustomTab
{
    public string TypeKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Icon { get; init; } = "📋";
    public int Count { get; init; }
}

public sealed class CodexEntityItem
{
    // Lucide-style 24x24 stroke path data for entity type icons
    internal const string UserIconData = "M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2M12 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8z";
    internal const string MapPinIconData = "M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0zM12 13a3 3 0 1 0 0-6 3 3 0 0 0 0 6z";
    internal const string SwordIconData = "M14.5 17.5L3 6V3h3l11.5 11.5M13 19l6-6M3 3l18 18";
    internal const string ScrollIconData = "M8 21h12a2 2 0 0 0 2-2v-2H10v2a2 2 0 1 1-4 0V5a2 2 0 1 0-4 0v3h4M14 3v2M14 7v2M14 11v2";

    public EntityType EntityType { get; }
    public object Entity { get; }
    public string Name { get; }
    public string Subtitle { get; }
    public string Icon { get; }
    public bool IsWorldBible { get; }
    public bool IsEmoji { get; }
    public string? ImagePath { get; }
    public bool HasImage => !string.IsNullOrEmpty(ImagePath);

    public CodexEntityItem(EntityType type, object entity, string name, string subtitle, string icon, bool isWorldBible, bool isEmoji = false, string? imagePath = null)
    {
        EntityType = type;
        Entity = entity;
        Name = name;
        Subtitle = subtitle ?? string.Empty;
        Icon = icon;
        IsWorldBible = isWorldBible;
        IsEmoji = isEmoji;
        ImagePath = imagePath;
    }
}
