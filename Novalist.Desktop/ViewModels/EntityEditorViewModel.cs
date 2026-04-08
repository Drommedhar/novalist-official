using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Dialogs;
using Novalist.Desktop.Localization;

namespace Novalist.Desktop.ViewModels;

public partial class EntityEditorViewModel : ObservableObject
{
    private readonly IEntityService _entityService;
    private readonly ISettingsService _settingsService;
    private readonly IProjectService _projectService;
    private CancellationTokenSource? _autoSaveCts;
    private bool _isLoading;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private EntityType _entityType;
    [ObservableProperty] private string _title = string.Empty;

    // ── Override state ──────────────────────────────────────────────
    [ObservableProperty] private bool _isOverrideMode;
    [ObservableProperty] private string _overrideScopeDisplay = string.Empty;
    [ObservableProperty] private ObservableCollection<OverrideListItemViewModel> _overrideItems = [];
    [ObservableProperty] private ObservableCollection<ChapterScopeOption> _availableChapters = [];
    [ObservableProperty] private ObservableCollection<SceneScopeOption> _availableScenes = [];
    [ObservableProperty] private ChapterScopeOption? _selectedOverrideChapter;
    [ObservableProperty] private SceneScopeOption? _selectedOverrideScene;
    private CharacterOverride? _activeOverride;

    // ── Character fields ────────────────────────────────────────────
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _surname = string.Empty;
    [ObservableProperty] private string _gender = string.Empty;
    [ObservableProperty] private string _age = string.Empty;
    [ObservableProperty] private string _role = string.Empty;
    [ObservableProperty] private string _group = string.Empty;
    [ObservableProperty] private string _eyeColor = string.Empty;
    [ObservableProperty] private string _hairColor = string.Empty;
    [ObservableProperty] private string _hairLength = string.Empty;
    [ObservableProperty] private string _height = string.Empty;
    [ObservableProperty] private string _build = string.Empty;
    [ObservableProperty] private string _skinTone = string.Empty;
    [ObservableProperty] private string _distinguishingFeatures = string.Empty;

    // ── Date-based age fields ───────────────────────────────────────
    [ObservableProperty] private bool _isDateAge;
    [ObservableProperty] private DateTime? _birthDate;
    [ObservableProperty] private string _computedAge = string.Empty;
    [ObservableProperty] private string _ageIntervalUnitDisplay = string.Empty;

    // ── Location fields ─────────────────────────────────────────────
    [ObservableProperty] private string _locationType = string.Empty;
    [ObservableProperty] private string _parentLocation = string.Empty;

    // ── Item fields ─────────────────────────────────────────────────
    [ObservableProperty] private string _itemType = string.Empty;
    [ObservableProperty] private string _origin = string.Empty;

    // ── Lore fields ─────────────────────────────────────────────────
    [ObservableProperty] private string _category = "Other";

    // ── Shared fields ───────────────────────────────────────────────
    [ObservableProperty] private string _description = string.Empty;

    [ObservableProperty]
    private ObservableCollection<EntityImage> _images = [];

    [ObservableProperty]
    private ObservableCollection<ObservableRelationship> _relationships = [];

    [ObservableProperty]
    private ObservableCollection<ObservableKeyValue> _customProperties = [];

    [ObservableProperty]
    private ObservableCollection<ObservableSection> _sections = [];

    [ObservableProperty]
    private ObservableCollection<CharacterOverride> _chapterOverrides = [];

    [ObservableProperty]
    private ObservableCollection<string> _relationshipRoleSuggestions = [];

    [ObservableProperty]
    private ObservableCollection<string> _characterRelationshipSuggestions = [];

    [ObservableProperty]
    private ObservableCollection<string> _parentLocationSuggestions = [];

    [ObservableProperty]
    private bool _isParentLocationSuggestionOpen;

    public bool ParentLocationSuggestionsVisible => IsParentLocationSuggestionOpen && ParentLocationSuggestions.Count > 0;

    private List<string> _allLocationNames = [];

    // Backing entity references
    private CharacterData? _character;
    private LocationData? _location;
    private ItemData? _item;
    private LoreData? _lore;
    private CustomEntityData? _customEntity;

    // ── Custom entity fields ────────────────────────────────────────
    [ObservableProperty]
    private ObservableCollection<ObservableKeyValue> _customEntityFields = [];

    [ObservableProperty]
    private string _customEntityTypeKey = string.Empty;

    public Func<Task<string?>>? BrowseImageRequested { get; set; }
    public Func<Task<AddImageSourceChoice?>>? ChooseAddImageSourceRequested { get; set; }
    public Func<string?, Task<string?>>? PickProjectImageRequested { get; set; }
    public Func<string, string, string, IReadOnlyList<string>, Task<string?>>? ShowInverseRelationshipDialog { get; set; }
    public Func<string, string, Task<bool>>? ConfirmDeleteRequested { get; set; }
    public event Action? Saved;
    public event Action? Deleted;

    public string[] LoreCategories => LoreData.Categories;
    public bool ShowAgeDatePicker => IsDateAge && !IsOverrideMode;
    public bool ShowAgeTextField => !IsDateAge || IsOverrideMode;

    public EntityEditorViewModel(IEntityService entityService, ISettingsService settingsService, IProjectService projectService)
    {
        _entityService = entityService;
        _settingsService = settingsService;
        _projectService = projectService;
    }

    // ── Open methods ────────────────────────────────────────────────

    public void OpenCharacter(CharacterData c)
    {
        ResetCurrentEntityState();
        _isLoading = true;
        _character = c;
        EntityType = EntityType.Character;
        Title = c.DisplayName;

        Name = c.Name; Surname = c.Surname;
        Gender = c.Gender; Age = c.Age; Role = c.Role; Group = c.Group;
        IsDateAge = string.Equals(c.AgeMode, "date", StringComparison.OrdinalIgnoreCase);
        if (IsDateAge && DateTime.TryParse(c.BirthDate, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var bd))
            BirthDate = bd;
        else
            BirthDate = null;
        UpdateComputedAge();
        EyeColor = c.EyeColor; HairColor = c.HairColor; HairLength = c.HairLength;
        Height = c.Height; Build = c.Build; SkinTone = c.SkinTone;
        DistinguishingFeatures = c.DistinguishingFeatures;
        Images = new(c.Images);
        Relationships = new(c.Relationships.Select(CreateObservableRelationship));
        CustomProperties = BuildTypedCustomProperties(c.CustomProperties, c.TemplateId, EntityType.Character);
        Sections = new(c.Sections.Select(s => new ObservableSection(s.Title, s.Content)));
        ChapterOverrides = new(c.ChapterOverrides);

        IsOpen = true;
        _isLoading = false;
        LoadOverrideContext();
        _ = RefreshRelationshipSuggestionsAsync();
    }

    public void OpenLocation(LocationData l)
    {
        ResetCurrentEntityState();
        _isLoading = true;
        _location = l;
        EntityType = EntityType.Location;
        Title = l.Name;

        Name = l.Name; LocationType = l.Type; ParentLocation = l.Parent;
        Description = l.Description;
        Images = new(l.Images);
        CustomProperties = BuildTypedCustomProperties(l.CustomProperties, l.TemplateId, EntityType.Location);
        Sections = new(l.Sections.Select(s => new ObservableSection(s.Title, s.Content)));

        IsOpen = true;
        _isLoading = false;
        _ = LoadLocationNamesAsync();
    }

    public void OpenItem(ItemData i)
    {
        ResetCurrentEntityState();
        _isLoading = true;
        _item = i;
        EntityType = EntityType.Item;
        Title = i.Name;

        Name = i.Name; ItemType = i.Type; Origin = i.Origin;
        Description = i.Description;
        Images = new(i.Images);
        CustomProperties = BuildTypedCustomProperties(i.CustomProperties, i.TemplateId, EntityType.Item);
        Sections = new(i.Sections.Select(s => new ObservableSection(s.Title, s.Content)));

        IsOpen = true;
        _isLoading = false;
    }

    public void OpenLore(LoreData l)
    {
        ResetCurrentEntityState();
        _isLoading = true;
        _lore = l;
        EntityType = EntityType.Lore;
        Title = l.Name;

        Name = l.Name; Category = l.Category;
        Description = l.Description;
        Images = new(l.Images);
        CustomProperties = BuildTypedCustomProperties(l.CustomProperties, l.TemplateId, EntityType.Lore);
        Sections = new(l.Sections.Select(s => new ObservableSection(s.Title, s.Content)));

        IsOpen = true;
        _isLoading = false;
    }

    public void OpenCustomEntity(CustomEntityData e)
    {
        ResetCurrentEntityState();
        _isLoading = true;
        _customEntity = e;
        EntityType = EntityType.Custom;
        CustomEntityTypeKey = e.EntityTypeKey;
        Title = e.Name;

        Name = e.Name;
        Description = e.Fields.GetValueOrDefault("Description", string.Empty);

        // Build typed fields from the entity type definition
        var typeDef = _entityService.GetCustomEntityTypes()
            .FirstOrDefault(t => string.Equals(t.TypeKey, e.EntityTypeKey, StringComparison.Ordinal));
        var fieldDefs = typeDef?.DefaultFields ?? [];
        CustomEntityFields = new(fieldDefs
            .Where(fd => !string.Equals(fd.Key, "Description", StringComparison.OrdinalIgnoreCase))
            .Select(fd =>
            {
                var value = e.Fields.GetValueOrDefault(fd.Key, fd.DefaultValue);
                if (fd.Type == CustomPropertyType.EntityRef)
                {
                    var refTarget = fd.EnumOptions is { Count: > 0 } ? fd.EnumOptions[0] : "Character";
                    var kv = new ObservableKeyValue(fd.DisplayName, value, CustomPropertyType.EntityRef, null);
                    kv.EntityRefTargetType = refTarget;
                    return kv;
                }
                return fd.EnumOptions is { Count: > 0 }
                    ? new ObservableKeyValue(fd.DisplayName, value, CustomPropertyType.Enum, fd.EnumOptions)
                    : new ObservableKeyValue(fd.DisplayName, value, fd.Type, null);
            }));

        // Features
        var features = typeDef?.Features ?? new CustomEntityFeatures();
        if (features.IncludeImages)
            Images = new(e.Images);
        if (features.IncludeRelationships)
            Relationships = new(e.Relationships.Select(CreateObservableRelationship));
        if (features.IncludeSections)
            Sections = new(e.Sections.Select(s => new ObservableSection(s.Title, s.Content)));

        CustomProperties = BuildTypedCustomProperties(e.CustomProperties, e.TemplateId, EntityType.Custom);

        // Populate entity reference names for EntityRef fields
        _ = PopulateEntityRefNamesAsync();

        IsOpen = true;
        _isLoading = false;
    }

    // ── Save ────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SaveAsync()
    {
        var didSave = false;

        switch (EntityType)
        {
            case EntityType.Character when _character != null:
                if (IsOverrideMode && _activeOverride != null)
                    WriteBackActiveOverride();
                else
                    WriteBackCharacter();
                _character.ChapterOverrides = [.. ChapterOverrides];
                await _entityService.SaveCharacterAsync(_character);
                if (!IsOverrideMode)
                {
                    await SyncInverseRelationshipsAsync(_character);
                    Title = _character.DisplayName;
                    await RefreshRelationshipSuggestionsAsync();
                }
                didSave = true;
                break;
            case EntityType.Location when _location != null:
                WriteBackLocation();
                await _entityService.SaveLocationAsync(_location);
                Title = _location.Name;
                didSave = true;
                break;
            case EntityType.Item when _item != null:
                WriteBackItem();
                await _entityService.SaveItemAsync(_item);
                Title = _item.Name;
                didSave = true;
                break;
            case EntityType.Lore when _lore != null:
                WriteBackLore();
                await _entityService.SaveLoreAsync(_lore);
                Title = _lore.Name;
                didSave = true;
                break;
            case EntityType.Custom when _customEntity != null:
                WriteBackCustomEntity();
                await _entityService.SaveCustomEntityAsync(_customEntity);
                Title = _customEntity.Name;
                didSave = true;
                break;
        }

        if (didSave)
            Saved?.Invoke();
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        if (IsOverrideMode)
            ExitOverrideMode();
        await SaveAsync();
        CloseCurrentEntity();
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var entityName = Title;
        var confirmTitle = Loc.T("entityEditor.deleteConfirmTitle");
        var confirmMessage = Loc.T("entityEditor.deleteConfirmMessage", entityName);

        var confirmed = await (ConfirmDeleteRequested?.Invoke(confirmTitle, confirmMessage) ?? Task.FromResult(false));
        if (!confirmed) return;

        switch (EntityType)
        {
            case EntityType.Character when _character != null:
                await _entityService.DeleteCharacterAsync(_character.Id);
                break;
            case EntityType.Location when _location != null:
                await _entityService.DeleteLocationAsync(_location.Id);
                break;
            case EntityType.Item when _item != null:
                await _entityService.DeleteItemAsync(_item.Id);
                break;
            case EntityType.Lore when _lore != null:
                await _entityService.DeleteLoreAsync(_lore.Id);
                break;
            case EntityType.Custom when _customEntity != null:
                await _entityService.DeleteCustomEntityAsync(_customEntity.EntityTypeKey, _customEntity.Id);
                break;
        }

        CloseCurrentEntity();
        Deleted?.Invoke();
    }

    // ── Collections management ──────────────────────────────────────

    [RelayCommand]
    private void AddRelationship()
    {
        Relationships.Add(CreateObservableRelationship(new EntityRelationship()));
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void RemoveRelationship(ObservableRelationship? r)
    {
        if (r != null)
        {
            r.PropertyChanged -= OnRelationshipPropertyChanged;
            Relationships.Remove(r);
        }
        ScheduleAutoSave();
    }

    public async Task AddRelationshipTargetAsync(ObservableRelationship? relationship)
    {
        if (relationship == null)
            return;

        var targetNames = ParseRelationshipTargets(relationship.PendingTarget);
        if (targetNames.Count == 0)
            return;

        var anyAdded = false;
        foreach (var targetName in targetNames)
        {
            if (!relationship.AddTarget(targetName))
                continue;

            anyAdded = true;
            await EnsureInverseRelationshipAsync(relationship, targetName);
        }

        relationship.PendingTarget = string.Empty;
        if (!anyAdded)
            return;

        ScheduleAutoSave();
        await RefreshRelationshipSuggestionsAsync();
    }

    [RelayCommand]
    private void RemoveRelationshipTarget(ObservableRelationshipTarget? target)
    {
        if (target == null)
            return;

        target.Owner.RemoveTarget(target);
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void AddCustomProperty()
    {
        CustomProperties.Add(new ObservableKeyValue("", ""));
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void RemoveCustomProperty(ObservableKeyValue? kv)
    {
        if (kv != null) CustomProperties.Remove(kv);
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void AddSection()
    {
        Sections.Add(new ObservableSection(Loc.T("section.newSection"), ""));
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void RemoveSection(ObservableSection? s)
    {
        if (s != null) Sections.Remove(s);
        ScheduleAutoSave();
    }

    [RelayCommand]
    private async Task AddImageAsync()
    {
        var sourceChoice = await (ChooseAddImageSourceRequested?.Invoke() ?? Task.FromResult<AddImageSourceChoice?>(null));
        if (sourceChoice == null)
            return;

        string? relativePath;
        string? imageName;

        switch (sourceChoice.Value)
        {
            case AddImageSourceChoice.Library:
                relativePath = await (PickProjectImageRequested?.Invoke(null) ?? Task.FromResult<string?>(null));
                if (string.IsNullOrWhiteSpace(relativePath))
                    return;

                imageName = Path.GetFileNameWithoutExtension(relativePath);
                break;
            case AddImageSourceChoice.Import:
                var path = await (BrowseImageRequested?.Invoke() ?? Task.FromResult<string?>(null));
                if (string.IsNullOrWhiteSpace(path))
                    return;

                relativePath = await _entityService.ImportImageAsync(path);
                imageName = Path.GetFileNameWithoutExtension(path);
                break;
            default:
                return;
        }

        Images.Add(new EntityImage { Name = imageName, Path = relativePath });
        ScheduleAutoSave();
    }

    public async Task SelectProjectImageAsync(EntityImage? image)
    {
        if (image == null)
            return;

        var selectedPath = await (PickProjectImageRequested?.Invoke(image.Path) ?? Task.FromResult<string?>(null));
        if (string.IsNullOrWhiteSpace(selectedPath)
            || string.Equals(selectedPath, image.Path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        image.Path = selectedPath;
        if (string.IsNullOrWhiteSpace(image.Name))
            image.Name = Path.GetFileNameWithoutExtension(selectedPath);

        ScheduleAutoSave();
    }

    [RelayCommand]
    private void RemoveImage(EntityImage? img)
    {
        if (img != null) Images.Remove(img);
        ScheduleAutoSave();
    }

    public string GetImageFullPath(string relativePath)
        => _entityService.GetImageFullPath(relativePath);

    // ── Auto-save ───────────────────────────────────────────────────

    private static readonly HashSet<string> AutoSaveProperties = new(StringComparer.Ordinal)
    {
        nameof(Name), nameof(Surname), nameof(Gender), nameof(Age), nameof(Role), nameof(Group),
        nameof(EyeColor), nameof(HairColor), nameof(HairLength), nameof(Height), nameof(Build),
        nameof(SkinTone), nameof(DistinguishingFeatures),
        nameof(LocationType), nameof(ParentLocation),
        nameof(ItemType), nameof(Origin),
        nameof(Category),
        nameof(Description),
    };

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_isLoading && IsOpen && e.PropertyName != null && AutoSaveProperties.Contains(e.PropertyName))
            ScheduleAutoSave();
    }

    public void ScheduleAutoSave()
    {
        _autoSaveCts?.Cancel();
        _autoSaveCts?.Dispose();
        _autoSaveCts = new CancellationTokenSource();
        var token = _autoSaveCts.Token;

        _ = DelayedAutoSaveAsync(token);
    }

    private async Task DelayedAutoSaveAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(1500, token);
            if (!token.IsCancellationRequested)
                await SaveAsync();
        }
        catch (OperationCanceledException) { }
    }

    public async Task RefreshRelationshipSuggestionsAsync()
    {
        var characters = await _entityService.LoadCharactersAsync();
        var roleSuggestions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var characterSuggestions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var role in _settingsService.Settings.RelationshipPairs.Keys)
            roleSuggestions.Add(role);

        foreach (var inverseRoles in _settingsService.Settings.RelationshipPairs.Values)
        {
            foreach (var inverseRole in inverseRoles)
                roleSuggestions.Add(inverseRole);
        }

        foreach (var character in characters)
        {
            if (_character != null && string.Equals(character.Id, _character.Id, StringComparison.OrdinalIgnoreCase))
                continue;

            characterSuggestions.Add(character.DisplayName);

            foreach (var relationship in character.Relationships)
            {
                if (!string.IsNullOrWhiteSpace(relationship.Role))
                    roleSuggestions.Add(relationship.Role.Trim());
            }
        }

        RelationshipRoleSuggestions = new ObservableCollection<string>(roleSuggestions.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        CharacterRelationshipSuggestions = new ObservableCollection<string>(characterSuggestions.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
    }

    // ── Write-back helpers ──────────────────────────────────────────

    private void WriteBackCharacter()
    {
        if (_character == null) return;
        _character.Name = Name; _character.Surname = Surname;
        _character.Gender = Gender; _character.Age = Age;
        _character.AgeMode = IsDateAge ? "date" : null;
        _character.BirthDate = IsDateAge && BirthDate.HasValue
            ? BirthDate.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            : null;
        _character.Role = Role; _character.Group = Group;
        _character.EyeColor = EyeColor; _character.HairColor = HairColor;
        _character.HairLength = HairLength; _character.Height = Height;
        _character.Build = Build; _character.SkinTone = SkinTone;
        _character.DistinguishingFeatures = DistinguishingFeatures;
        _character.Images = [.. Images];
        _character.Relationships = Relationships
            .Select(relationship => relationship.ToEntityRelationship())
            .Where(relationship => !string.IsNullOrWhiteSpace(relationship.Role)
                                   || !string.IsNullOrWhiteSpace(relationship.Target))
            .ToList();
        _character.CustomProperties = CustomProperties.ToDictionary(kv => kv.Key, kv => kv.Value);
        _character.Sections = Sections.Select(s => new EntitySection { Title = s.Title, Content = s.Content }).ToList();
        _character.ChapterOverrides = [.. ChapterOverrides];
    }

    private async Task SyncInverseRelationshipsAsync(CharacterData sourceCharacter)
    {
        var allCharacters = await _entityService.LoadCharactersAsync();
        var sourceName = sourceCharacter.DisplayName;
        var inverseRoleCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var changedTargets = new List<CharacterData>();

        foreach (var relationship in sourceCharacter.Relationships)
        {
            var relationshipRole = relationship.Role?.Trim();
            var relationshipTargets = ParseRelationshipTargets(relationship.Target);
            if (string.IsNullOrWhiteSpace(relationshipRole) || relationshipTargets.Count == 0)
                continue;

            foreach (var relationshipTarget in relationshipTargets)
            {
                var targetCharacter = FindCharacterByRelationshipTarget(allCharacters, relationshipTarget, sourceCharacter.Id);
                if (targetCharacter == null)
                    continue;

                if (!inverseRoleCache.TryGetValue(relationshipRole, out var inverseRole))
                {
                    inverseRole = await ResolveInverseRoleAsync(relationshipRole, sourceName, targetCharacter.DisplayName);
                    if (string.IsNullOrWhiteSpace(inverseRole))
                        continue;

                    inverseRoleCache[relationshipRole] = inverseRole;
                }

                if (targetCharacter.Relationships.Any(existing =>
                        string.Equals(existing.Role?.Trim(), inverseRole, StringComparison.OrdinalIgnoreCase)
                        && RelationshipTargetMatches(existing.Target, sourceCharacter)))
                {
                    continue;
                }

                targetCharacter.Relationships.Add(new EntityRelationship
                {
                    Role = inverseRole,
                    Target = sourceName
                });
                changedTargets.Add(targetCharacter);
            }
        }

        foreach (var changedTarget in changedTargets.DistinctBy(character => character.Id))
            await _entityService.SaveCharacterAsync(changedTarget);
    }

    private async Task<string> ResolveInverseRoleAsync(string relationshipRole, string sourceName, string targetName)
    {
        var knownInverse = _settingsService.Settings
            .GetKnownInverseRoles(relationshipRole)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(knownInverse))
            return knownInverse;

        var suggestionPool = _settingsService.Settings.GetKnownInverseRoles(relationshipRole)
            .Concat(RelationshipRoleSuggestions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var chosenInverse = await (ShowInverseRelationshipDialog?.Invoke(relationshipRole, sourceName, targetName, suggestionPool)
            ?? Task.FromResult<string?>(null));
        if (string.IsNullOrWhiteSpace(chosenInverse))
            return string.Empty;

        if (_settingsService.Settings.LearnRelationshipPair(relationshipRole, chosenInverse))
            await _settingsService.SaveAsync();

        return chosenInverse.Trim();
    }

    private async Task EnsureInverseRelationshipAsync(ObservableRelationship relationship, string targetName)
    {
        if (_character == null)
            return;

        var relationshipRole = relationship.Role?.Trim();
        if (string.IsNullOrWhiteSpace(relationshipRole))
            return;

        var allCharacters = await _entityService.LoadCharactersAsync();
        var targetCharacter = FindCharacterByRelationshipTarget(allCharacters, targetName, _character.Id);
        if (targetCharacter == null)
            return;

        var sourceName = GetCurrentCharacterDisplayName();
        var inverseRole = await ResolveInverseRoleAsync(relationshipRole, sourceName, targetCharacter.DisplayName);
        if (string.IsNullOrWhiteSpace(inverseRole))
            return;

        if (targetCharacter.Relationships.Any(existing =>
                string.Equals(existing.Role?.Trim(), inverseRole, StringComparison.OrdinalIgnoreCase)
                && RelationshipTargetMatches(existing.Target, sourceName, Name)))
        {
            return;
        }

        targetCharacter.Relationships.Add(new EntityRelationship
        {
            Role = inverseRole,
            Target = sourceName
        });
        await _entityService.SaveCharacterAsync(targetCharacter);
    }

    private static CharacterData? FindCharacterByRelationshipTarget(IEnumerable<CharacterData> characters, string relationshipTarget, string sourceId)
    {
        var normalizedTarget = NormalizeRelationshipTarget(relationshipTarget);
        return characters.FirstOrDefault(character =>
            !string.Equals(character.Id, sourceId, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(character.DisplayName, normalizedTarget, StringComparison.OrdinalIgnoreCase)
                || string.Equals(character.Name, normalizedTarget, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool RelationshipTargetMatches(string? target, CharacterData character)
    {
        var normalizedTarget = NormalizeRelationshipTarget(target);
        return string.Equals(normalizedTarget, character.DisplayName, StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalizedTarget, character.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool RelationshipTargetMatches(string? target, string displayName, string firstName)
    {
        var normalizedTarget = NormalizeRelationshipTarget(target);
        return string.Equals(normalizedTarget, displayName, StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalizedTarget, firstName, StringComparison.OrdinalIgnoreCase);
    }

    private ObservableRelationship CreateObservableRelationship(EntityRelationship relationship)
    {
        var observableRelationship = new ObservableRelationship(relationship.Role, relationship.Target);
        observableRelationship.PropertyChanged += OnRelationshipPropertyChanged;
        return observableRelationship;
    }

    private void OnRelationshipPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ObservableRelationship.Role))
            ScheduleAutoSave();
    }

    private string GetCurrentCharacterDisplayName()
        => string.IsNullOrWhiteSpace(Surname) ? Name.Trim() : $"{Name.Trim()} {Surname.Trim()}".Trim();

    private static List<string> ParseRelationshipTargets(string? rawValue)
        => (rawValue ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeRelationshipTarget)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string NormalizeRelationshipTarget(string? value)
        => (value ?? string.Empty)
            .Replace("[[", string.Empty, StringComparison.Ordinal)
            .Replace("]]", string.Empty, StringComparison.Ordinal)
            .Trim();

    private void WriteBackLocation()
    {
        if (_location == null) return;
        _location.Name = Name; _location.Type = LocationType;
        _location.Parent = ParentLocation; _location.Description = Description;
        _location.Images = [.. Images];
        _location.CustomProperties = CustomProperties.ToDictionary(kv => kv.Key, kv => kv.Value);
        _location.Sections = Sections.Select(s => new EntitySection { Title = s.Title, Content = s.Content }).ToList();
    }

    private void WriteBackItem()
    {
        if (_item == null) return;
        _item.Name = Name; _item.Type = ItemType; _item.Origin = Origin;
        _item.Description = Description;
        _item.Images = [.. Images];
        _item.CustomProperties = CustomProperties.ToDictionary(kv => kv.Key, kv => kv.Value);
        _item.Sections = Sections.Select(s => new EntitySection { Title = s.Title, Content = s.Content }).ToList();
    }

    private void WriteBackLore()
    {
        if (_lore == null) return;
        _lore.Name = Name; _lore.Category = Category;
        _lore.Description = Description;
        _lore.Images = [.. Images];
        _lore.CustomProperties = CustomProperties.ToDictionary(kv => kv.Key, kv => kv.Value);
        _lore.Sections = Sections.Select(s => new EntitySection { Title = s.Title, Content = s.Content }).ToList();
    }

    private void WriteBackCustomEntity()
    {
        if (_customEntity == null) return;
        _customEntity.Name = Name;

        // Write back typed fields
        var typeDef = _entityService.GetCustomEntityTypes()
            .FirstOrDefault(t => string.Equals(t.TypeKey, _customEntity.EntityTypeKey, StringComparison.Ordinal));
        var fieldDefs = typeDef?.DefaultFields ?? [];
        var nonDescFields = fieldDefs
            .Where(fd => !string.Equals(fd.Key, "Description", StringComparison.OrdinalIgnoreCase))
            .ToList();

        for (var i = 0; i < nonDescFields.Count && i < CustomEntityFields.Count; i++)
            _customEntity.Fields[nonDescFields[i].Key] = CustomEntityFields[i].Value;

        if (!string.IsNullOrEmpty(Description) || _customEntity.Fields.ContainsKey("Description"))
            _customEntity.Fields["Description"] = Description;

        var features = typeDef?.Features ?? new CustomEntityFeatures();
        if (features.IncludeImages)
            _customEntity.Images = [.. Images];
        if (features.IncludeRelationships)
            _customEntity.Relationships = Relationships
                .Select(r => r.ToEntityRelationship())
                .Where(r => !string.IsNullOrWhiteSpace(r.Role) || !string.IsNullOrWhiteSpace(r.Target))
                .ToList();
        if (features.IncludeSections)
            _customEntity.Sections = Sections.Select(s => new EntitySection { Title = s.Title, Content = s.Content }).ToList();

        _customEntity.CustomProperties = CustomProperties.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private void CloseCurrentEntity()
    {
        ResetCurrentEntityState();
        IsOpen = false;
    }

    partial void OnBirthDateChanged(DateTime? value)
    {
        UpdateComputedAge();
        ScheduleAutoSave();
    }

    partial void OnIsDateAgeChanged(bool value)
    {
        UpdateComputedAge();
        ScheduleAutoSave();
        OnPropertyChanged(nameof(ShowAgeDatePicker));
        OnPropertyChanged(nameof(ShowAgeTextField));
    }

    private void UpdateComputedAge()
    {
        if (!IsDateAge || !BirthDate.HasValue)
        {
            ComputedAge = string.Empty;
            return;
        }

        var unit = _character?.AgeIntervalUnit ?? IntervalUnit.Years;
        ComputedAge = AgeComputation.ComputeInterval(BirthDate.Value, DateTime.Today, unit);
    }

    private void ResetCurrentEntityState()
    {
        _autoSaveCts?.Cancel();
        _autoSaveCts?.Dispose();
        _autoSaveCts = null;

        IsOverrideMode = false;
        _activeOverride = null;
        OverrideScopeDisplay = string.Empty;
        SelectedOverrideChapter = null;
        SelectedOverrideScene = null;
        AvailableChapters.Clear();
        AvailableScenes.Clear();
        OverrideItems.Clear();

        _character = null; _location = null; _item = null; _lore = null; _customEntity = null;
        Name = Surname = Gender = Age = Role = Group = string.Empty;
        IsDateAge = false; BirthDate = null; ComputedAge = string.Empty;
        EyeColor = HairColor = HairLength = Height = Build = SkinTone = DistinguishingFeatures = string.Empty;
        LocationType = ParentLocation = ItemType = Origin = Description = string.Empty;
        Category = "Other";
        CustomEntityTypeKey = string.Empty;
        CustomEntityFields.Clear();
        Images.Clear(); Relationships.Clear(); CustomProperties.Clear();
        Sections.Clear(); ChapterOverrides.Clear();
        RelationshipRoleSuggestions.Clear();
        CharacterRelationshipSuggestions.Clear();
        _allLocationNames.Clear();
        ParentLocationSuggestions.Clear();
        IsParentLocationSuggestionOpen = false;
    }

    // ── Location suggestion helpers ─────────────────────────────────

    private ObservableCollection<ObservableKeyValue> BuildTypedCustomProperties(
        Dictionary<string, string> properties, string? templateId, EntityType entityType)
    {
        var defs = ResolveCustomPropertyDefs(templateId, entityType);
        return new(properties.Select(kv =>
        {
            var def = defs.FirstOrDefault(d => string.Equals(d.Key, kv.Key, StringComparison.OrdinalIgnoreCase));
            return def != null
                ? new ObservableKeyValue(kv.Key, kv.Value, def.Type, def.EnumOptions)
                : new ObservableKeyValue(kv.Key, kv.Value);
        }));
    }

    private List<CustomPropertyDefinition> ResolveCustomPropertyDefs(string? templateId, EntityType entityType)
    {
        if (string.IsNullOrEmpty(templateId))
            return [];

        var book = _projectService.ActiveBook;
        if (book == null)
            return [];

        return entityType switch
        {
            EntityType.Character => book.CharacterTemplates
                .FirstOrDefault(t => string.Equals(t.Id, templateId, StringComparison.Ordinal))
                ?.CustomPropertyDefs ?? [],
            EntityType.Location => book.LocationTemplates
                .FirstOrDefault(t => string.Equals(t.Id, templateId, StringComparison.Ordinal))
                ?.CustomPropertyDefs ?? [],
            EntityType.Item => book.ItemTemplates
                .FirstOrDefault(t => string.Equals(t.Id, templateId, StringComparison.Ordinal))
                ?.CustomPropertyDefs ?? [],
            EntityType.Lore => book.LoreTemplates
                .FirstOrDefault(t => string.Equals(t.Id, templateId, StringComparison.Ordinal))
                ?.CustomPropertyDefs ?? [],
            EntityType.Custom => book.CustomEntityTemplates
                .FirstOrDefault(t => string.Equals(t.Id, templateId, StringComparison.Ordinal))
                ?.CustomPropertyDefs ?? [],
            _ => []
        };
    }

    partial void OnIsParentLocationSuggestionOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(ParentLocationSuggestionsVisible));
    }

    public async Task LoadLocationNamesAsync()
    {
        var locations = await _entityService.LoadLocationsAsync();
        _allLocationNames = locations
            .Where(l => _location == null || l.Id != _location.Id)
            .Select(l => l.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> AllLocationNames => _allLocationNames;

    private async Task PopulateEntityRefNamesAsync()
    {
        foreach (var field in CustomEntityFields)
        {
            if (field.PropertyType != CustomPropertyType.EntityRef) continue;

            var target = field.EntityRefTargetType;
            List<string> names;
            switch (target)
            {
                case "Character":
                    names = (await _entityService.LoadCharactersAsync())
                        .Select(c => c.DisplayName).ToList();
                    break;
                case "Location":
                    names = (await _entityService.LoadLocationsAsync())
                        .Select(l => l.Name).ToList();
                    break;
                case "Item":
                    names = (await _entityService.LoadItemsAsync())
                        .Select(i => i.Name).ToList();
                    break;
                case "Lore":
                    names = (await _entityService.LoadLoreAsync())
                        .Select(l => l.Name).ToList();
                    break;
                default:
                    // Custom entity type — resolve display name to typeKey
                    var typeKey = _projectService.CurrentProject?.CustomEntityTypes
                        .FirstOrDefault(ct => string.Equals(ct.DisplayName, target, StringComparison.OrdinalIgnoreCase)
                                           || string.Equals(ct.TypeKey, target, StringComparison.OrdinalIgnoreCase))
                        ?.TypeKey ?? target;
                    names = (await _entityService.LoadCustomEntitiesAsync(typeKey))
                        .Select(e => e.Name).ToList();
                    break;
            }

            field.AllEntityRefNames = names
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public void SetParentLocationSuggestions(IEnumerable<string> suggestions)
    {
        ParentLocationSuggestions = new ObservableCollection<string>(suggestions);
        OnPropertyChanged(nameof(ParentLocationSuggestionsVisible));
        IsParentLocationSuggestionOpen = ParentLocationSuggestions.Count > 0;
    }

    public void HideParentLocationSuggestions()
    {
        IsParentLocationSuggestionOpen = false;
        OnPropertyChanged(nameof(ParentLocationSuggestionsVisible));
    }

    public void UpdateLocationParent(LocationData location)
    {
        if (_location != null && _location.Id == location.Id)
            ParentLocation = location.Parent;
    }

    // ── Override management ─────────────────────────────────────────

    partial void OnIsOverrideModeChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowAgeDatePicker));
        OnPropertyChanged(nameof(ShowAgeTextField));
    }

    partial void OnSelectedOverrideChapterChanged(ChapterScopeOption? value)
    {
        SelectedOverrideScene = null;
        if (value == null)
        {
            AvailableScenes.Clear();
            return;
        }
        var scenes = _projectService.GetScenesForChapter(value.Guid);
        AvailableScenes = new(scenes.Select(s => new SceneScopeOption(s.Title)));
    }

    private void LoadOverrideContext()
    {
        if (!_projectService.IsProjectLoaded) return;

        var chapters = _projectService.GetChaptersOrdered();
        AvailableChapters = new(chapters.Select(c =>
            new ChapterScopeOption(c.Guid, c.Title, string.IsNullOrWhiteSpace(c.Act) ? null : c.Act)));

        RefreshOverrideItems();
    }

    private void RefreshOverrideItems()
    {
        var chapters = _projectService.IsProjectLoaded
            ? _projectService.GetChaptersOrdered()
            : [];

        OverrideItems = new(ChapterOverrides.Select(o =>
            new OverrideListItemViewModel(o, BuildOverrideDisplayLabel(o, chapters))));
    }

    private static string BuildOverrideDisplayLabel(CharacterOverride ov, IReadOnlyList<ChapterData> chapters)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(ov.Act) && string.IsNullOrEmpty(ov.Chapter))
            parts.Add($"Act: {ov.Act}");
        if (!string.IsNullOrEmpty(ov.Chapter))
        {
            var ch = chapters.FirstOrDefault(c =>
                string.Equals(c.Guid, ov.Chapter, StringComparison.OrdinalIgnoreCase));
            parts.Add(ch != null ? ch.Title : ov.Chapter);
        }
        if (!string.IsNullOrEmpty(ov.Scene))
            parts.Add(ov.Scene);
        return parts.Count > 0 ? string.Join(" → ", parts) : Loc.T("entityEditor.unknownScope");
    }

    [RelayCommand]
    private void EditOrCreateOverride()
    {
        if (_character == null || SelectedOverrideChapter == null) return;

        if (IsOverrideMode)
            ExitOverrideMode();

        var chapterGuid = SelectedOverrideChapter.Guid;
        var sceneName = SelectedOverrideScene?.Title;

        var existing = ChapterOverrides.FirstOrDefault(o =>
            string.Equals(o.Chapter, chapterGuid, StringComparison.OrdinalIgnoreCase)
            && (sceneName == null
                ? string.IsNullOrWhiteSpace(o.Scene)
                : string.Equals(o.Scene, sceneName, StringComparison.OrdinalIgnoreCase)));

        if (existing != null)
        {
            EnterOverrideMode(existing);
        }
        else
        {
            var newOverride = new CharacterOverride
            {
                Chapter = chapterGuid,
                Scene = sceneName,
                Act = SelectedOverrideChapter.Act
            };
            ChapterOverrides.Add(newOverride);
            RefreshOverrideItems();
            EnterOverrideMode(newOverride);
        }
    }

    [RelayCommand]
    private void EditExistingOverride(OverrideListItemViewModel? item)
    {
        if (item == null || _character == null) return;
        if (IsOverrideMode)
            ExitOverrideMode();
        EnterOverrideMode(item.Override);
    }

    [RelayCommand]
    private void RemoveOverride(OverrideListItemViewModel? item)
    {
        if (item == null) return;
        if (_activeOverride == item.Override)
        {
            _activeOverride = null;
            IsOverrideMode = false;
            ReloadBaseToFields();
            OverrideScopeDisplay = string.Empty;
        }
        ChapterOverrides.Remove(item.Override);
        RefreshOverrideItems();
        ScheduleAutoSave();
    }

    [RelayCommand]
    private async Task StopOverrideModeAsync()
    {
        ExitOverrideMode();
        await SaveAsync();
    }

    private void EnterOverrideMode(CharacterOverride ov)
    {
        if (_character == null) return;

        // Save base data first
        if (!IsOverrideMode)
            WriteBackCharacter();

        _activeOverride = ov;
        IsOverrideMode = true;
        LoadOverrideToFields(ov);

        var chapters = _projectService.IsProjectLoaded
            ? _projectService.GetChaptersOrdered()
            : [];
        OverrideScopeDisplay = Loc.T("entityEditor.editingOverride", BuildOverrideDisplayLabel(ov, chapters));
    }

    private void ExitOverrideMode()
    {
        _autoSaveCts?.Cancel();
        _autoSaveCts?.Dispose();
        _autoSaveCts = null;

        if (_activeOverride != null && _character != null)
            WriteBackActiveOverride();

        _activeOverride = null;
        IsOverrideMode = false;
        OverrideScopeDisplay = string.Empty;
        ReloadBaseToFields();
        RefreshOverrideItems();
    }

    private void LoadOverrideToFields(CharacterOverride ov)
    {
        if (_character == null) return;

        Name = ov.Name ?? _character.Name;
        Surname = ov.Surname ?? _character.Surname;
        Gender = ov.Gender ?? _character.Gender;
        Age = ov.Age ?? _character.Age;
        Role = ov.Role ?? _character.Role;
        // Group not overridable — keep base
        Group = _character.Group;
        EyeColor = ov.EyeColor ?? _character.EyeColor;
        HairColor = ov.HairColor ?? _character.HairColor;
        HairLength = ov.HairLength ?? _character.HairLength;
        Height = ov.Height ?? _character.Height;
        Build = ov.Build ?? _character.Build;
        SkinTone = ov.SkinTone ?? _character.SkinTone;
        DistinguishingFeatures = ov.DistinguishingFeatures ?? _character.DistinguishingFeatures;

        // Custom properties: base merged with override
        var merged = new Dictionary<string, string>(_character.CustomProperties);
        if (ov.CustomProperties != null)
        {
            foreach (var kv in ov.CustomProperties)
                merged[kv.Key] = kv.Value;
        }
        CustomProperties = BuildTypedCustomProperties(merged, _character.TemplateId, EntityType.Character);

        // Images: override replaces base entirely if set (deep-copy to avoid mutating base)
        Images = ov.Images != null
            ? new(ov.Images.Select(img => new EntityImage { Name = img.Name, Path = img.Path }))
            : new(_character.Images.Select(img => new EntityImage { Name = img.Name, Path = img.Path }));

        // Relationships: override replaces base entirely if set
        Relationships = ov.Relationships != null
            ? new(ov.Relationships.Select(CreateObservableRelationship))
            : new(_character.Relationships.Select(CreateObservableRelationship));

        // Sections: override replaces base entirely if set
        Sections = ov.Sections != null
            ? new(ov.Sections.Select(s => new ObservableSection(s.Title, s.Content)))
            : new(_character.Sections.Select(s => new ObservableSection(s.Title, s.Content)));
    }

    private void ReloadBaseToFields()
    {
        if (_character == null) return;

        Name = _character.Name; Surname = _character.Surname;
        Gender = _character.Gender; Age = _character.Age;
        Role = _character.Role; Group = _character.Group;
        IsDateAge = string.Equals(_character.AgeMode, "date", StringComparison.OrdinalIgnoreCase);
        if (IsDateAge && DateTime.TryParse(_character.BirthDate, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var bd))
            BirthDate = bd;
        else
            BirthDate = null;
        UpdateComputedAge();
        EyeColor = _character.EyeColor; HairColor = _character.HairColor;
        HairLength = _character.HairLength; Height = _character.Height;
        Build = _character.Build; SkinTone = _character.SkinTone;
        DistinguishingFeatures = _character.DistinguishingFeatures;
        Images = new(_character.Images.Select(img => new EntityImage { Name = img.Name, Path = img.Path }));
        Relationships = new(_character.Relationships.Select(CreateObservableRelationship));
        CustomProperties = BuildTypedCustomProperties(_character.CustomProperties, _character.TemplateId, EntityType.Character);
        Sections = new(_character.Sections.Select(s => new ObservableSection(s.Title, s.Content)));
    }

    private void WriteBackActiveOverride()
    {
        if (_activeOverride == null || _character == null) return;

        _activeOverride.Name = !string.Equals(Name, _character.Name, StringComparison.Ordinal) ? Name : null;
        _activeOverride.Surname = !string.Equals(Surname, _character.Surname, StringComparison.Ordinal) ? Surname : null;
        _activeOverride.Gender = !string.Equals(Gender, _character.Gender, StringComparison.Ordinal) ? Gender : null;
        _activeOverride.Age = !string.Equals(Age, _character.Age, StringComparison.Ordinal) ? Age : null;
        _activeOverride.Role = !string.Equals(Role, _character.Role, StringComparison.Ordinal) ? Role : null;
        _activeOverride.EyeColor = !string.Equals(EyeColor, _character.EyeColor, StringComparison.Ordinal) ? EyeColor : null;
        _activeOverride.HairColor = !string.Equals(HairColor, _character.HairColor, StringComparison.Ordinal) ? HairColor : null;
        _activeOverride.HairLength = !string.Equals(HairLength, _character.HairLength, StringComparison.Ordinal) ? HairLength : null;
        _activeOverride.Height = !string.Equals(Height, _character.Height, StringComparison.Ordinal) ? Height : null;
        _activeOverride.Build = !string.Equals(Build, _character.Build, StringComparison.Ordinal) ? Build : null;
        _activeOverride.SkinTone = !string.Equals(SkinTone, _character.SkinTone, StringComparison.Ordinal) ? SkinTone : null;
        _activeOverride.DistinguishingFeatures = !string.Equals(DistinguishingFeatures, _character.DistinguishingFeatures, StringComparison.Ordinal)
            ? DistinguishingFeatures : null;

        var currentProps = CustomProperties.ToDictionary(kv => kv.Key, kv => kv.Value);
        var baseProps = _character.CustomProperties;
        var propsChanged = currentProps.Count != baseProps.Count
                           || currentProps.Any(kv => !baseProps.TryGetValue(kv.Key, out var bv) || bv != kv.Value);
        _activeOverride.CustomProperties = propsChanged ? currentProps : null;

        // Images: store if different from base
        var currentImages = Images.ToList();
        var imagesChanged = currentImages.Count != _character.Images.Count
                            || currentImages.Zip(_character.Images).Any(pair =>
                                pair.First.Path != pair.Second.Path || pair.First.Name != pair.Second.Name);
        _activeOverride.Images = imagesChanged ? currentImages : null;

        // Relationships: store if different from base
        var currentRels = Relationships
            .Select(r => r.ToEntityRelationship())
            .Where(r => !string.IsNullOrWhiteSpace(r.Role) || !string.IsNullOrWhiteSpace(r.Target))
            .ToList();
        var baseRels = _character.Relationships;
        var relsChanged = currentRels.Count != baseRels.Count
                          || currentRels.Zip(baseRels).Any(pair =>
                              pair.First.Role != pair.Second.Role || pair.First.Target != pair.Second.Target);
        _activeOverride.Relationships = relsChanged ? currentRels : null;

        // Sections: store if different from base
        var currentSections = Sections.Select(s => new EntitySection { Title = s.Title, Content = s.Content }).ToList();
        var baseSections = _character.Sections;
        var sectionsChanged = currentSections.Count != baseSections.Count
                              || currentSections.Zip(baseSections).Any(pair =>
                                  pair.First.Title != pair.Second.Title || pair.First.Content != pair.Second.Content);
        _activeOverride.Sections = sectionsChanged ? currentSections : null;
    }
}

// ── Override helper types ───────────────────────────────────────────

public record ChapterScopeOption(string Guid, string Title, string? Act)
{
    public string DisplayTitle => string.IsNullOrEmpty(Act)
        ? Title
        : $"{Title} ({Act})";
}

public record SceneScopeOption(string Title);

public partial class OverrideListItemViewModel : ObservableObject
{
    public CharacterOverride Override { get; }
    public string DisplayLabel { get; }

    public OverrideListItemViewModel(CharacterOverride ov, string displayLabel)
    {
        Override = ov;
        DisplayLabel = displayLabel;
    }
}

// ── Observable wrappers for collection items ────────────────────────

public partial class ObservableKeyValue : ObservableObject
{
    [ObservableProperty] private string _key;
    [ObservableProperty] private string _value;
    [ObservableProperty] private CustomPropertyType _propertyType;
    [ObservableProperty] private DateTime? _dateValue;
    [ObservableProperty] private bool _boolValue;
    [ObservableProperty] private List<string> _enumOptions = [];

    // EntityRef support
    [ObservableProperty] private ObservableCollection<string> _entityRefSuggestions = [];
    [ObservableProperty] private bool _isEntityRefSuggestionOpen;

    /// <summary>For EntityRef fields: which entity type to reference (e.g. "Character", "Location").</summary>
    public string EntityRefTargetType { get; set; } = string.Empty;

    public bool IsDateType => PropertyType == CustomPropertyType.Date;
    public bool IsBoolType => PropertyType == CustomPropertyType.Bool;
    public bool IsEnumType => PropertyType == CustomPropertyType.Enum;
    public bool IsEntityRefType => PropertyType == CustomPropertyType.EntityRef;
    public bool IsTextType => !IsDateType && !IsBoolType && !IsEnumType && !IsEntityRefType;
    public bool EntityRefSuggestionsVisible => IsEntityRefSuggestionOpen && EntityRefSuggestions.Count > 0;

    private bool _suppressSync;

    public ObservableKeyValue(string key, string value)
    {
        _key = key;
        _value = value;
    }

    public ObservableKeyValue(string key, string value, CustomPropertyType type, List<string>? enumOptions = null)
    {
        _key = key;
        _value = value;
        _propertyType = type;
        _enumOptions = enumOptions ?? [];
        _suppressSync = true;
        InitializeFromValue(value, type);
        _suppressSync = false;
    }

    private void InitializeFromValue(string value, CustomPropertyType type)
    {
        switch (type)
        {
            case CustomPropertyType.Date:
                if (DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var dto))
                    DateValue = dto;
                break;
            case CustomPropertyType.Bool:
                BoolValue = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                break;
        }
    }

    partial void OnDateValueChanged(DateTime? value)
    {
        if (_suppressSync) return;
        Value = value.HasValue
            ? value.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
    }

    partial void OnBoolValueChanged(bool value)
    {
        if (_suppressSync) return;
        Value = value ? "true" : "false";
    }

    partial void OnIsEntityRefSuggestionOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(EntityRefSuggestionsVisible));
    }

    /// <summary>All entity names for the referenced type, set by the editor.</summary>
    public List<string> AllEntityRefNames { get; set; } = [];

    public void SetEntityRefSuggestions(IEnumerable<string> suggestions)
    {
        EntityRefSuggestions = new ObservableCollection<string>(suggestions);
        OnPropertyChanged(nameof(EntityRefSuggestionsVisible));
        IsEntityRefSuggestionOpen = EntityRefSuggestions.Count > 0;
    }

    public void HideEntityRefSuggestions()
    {
        IsEntityRefSuggestionOpen = false;
    }
}

public partial class ObservableSection : ObservableObject
{
    [ObservableProperty] private string _title;
    [ObservableProperty] private string _content;

    public ObservableSection(string title, string content)
    {
        _title = title;
        _content = content;
    }
}

public partial class ObservableRelationship : ObservableObject
{
    [ObservableProperty] private string _role;
    [ObservableProperty] private string _pendingTarget = string.Empty;
    [ObservableProperty] private ObservableCollection<string> _roleSuggestions = [];
    [ObservableProperty] private ObservableCollection<string> _targetSuggestions = [];
    [ObservableProperty] private bool _isRoleSuggestionOpen;
    [ObservableProperty] private bool _isTargetSuggestionOpen;

    public ObservableCollection<ObservableRelationshipTarget> Targets { get; } = [];

    public bool HasTargets => Targets.Count > 0;
    public bool HasRoleSuggestions => RoleSuggestions.Count > 0;
    public bool HasTargetSuggestions => TargetSuggestions.Count > 0;
    public bool RoleSuggestionsVisible => IsRoleSuggestionOpen && HasRoleSuggestions;
    public bool TargetSuggestionsVisible => IsTargetSuggestionOpen && HasTargetSuggestions;

    public ObservableRelationship(string role, string target)
    {
        _role = role;

        foreach (var parsedTarget in (target ?? string.Empty)
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Select(value => value.Replace("[[", string.Empty, StringComparison.Ordinal)
                                           .Replace("]]", string.Empty, StringComparison.Ordinal)
                                           .Trim())
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Targets.Add(new ObservableRelationshipTarget(parsedTarget, this));
        }
    }

    public bool AddTarget(string target)
    {
        if (Targets.Any(existing => string.Equals(existing.Name, target, StringComparison.OrdinalIgnoreCase)))
            return false;

        Targets.Add(new ObservableRelationshipTarget(target, this));
        OnPropertyChanged(nameof(HasTargets));
        return true;
    }

    public void RemoveTarget(ObservableRelationshipTarget target)
    {
        Targets.Remove(target);
        OnPropertyChanged(nameof(HasTargets));
    }

    public void SetRoleSuggestions(IEnumerable<string> suggestions)
    {
        RoleSuggestions = new ObservableCollection<string>(suggestions);
        OnPropertyChanged(nameof(HasRoleSuggestions));
        OnPropertyChanged(nameof(RoleSuggestionsVisible));
        IsRoleSuggestionOpen = RoleSuggestions.Count > 0;
    }

    public void HideRoleSuggestions()
    {
        IsRoleSuggestionOpen = false;
        OnPropertyChanged(nameof(RoleSuggestionsVisible));
    }

    public void SetTargetSuggestions(IEnumerable<string> suggestions)
    {
        TargetSuggestions = new ObservableCollection<string>(suggestions);
        OnPropertyChanged(nameof(HasTargetSuggestions));
        OnPropertyChanged(nameof(TargetSuggestionsVisible));
        IsTargetSuggestionOpen = TargetSuggestions.Count > 0;
    }

    public void HideTargetSuggestions()
    {
        IsTargetSuggestionOpen = false;
        OnPropertyChanged(nameof(TargetSuggestionsVisible));
    }

    public EntityRelationship ToEntityRelationship()
        => new()
        {
            Role = Role.Trim(),
            Target = string.Join(", ", Targets.Select(target => target.Name))
        };

    partial void OnRoleSuggestionsChanged(ObservableCollection<string> value)
    {
        OnPropertyChanged(nameof(HasRoleSuggestions));
        OnPropertyChanged(nameof(RoleSuggestionsVisible));
    }

    partial void OnTargetSuggestionsChanged(ObservableCollection<string> value)
    {
        OnPropertyChanged(nameof(HasTargetSuggestions));
        OnPropertyChanged(nameof(TargetSuggestionsVisible));
    }

    partial void OnIsRoleSuggestionOpenChanged(bool value)
        => OnPropertyChanged(nameof(RoleSuggestionsVisible));

    partial void OnIsTargetSuggestionOpenChanged(bool value)
        => OnPropertyChanged(nameof(TargetSuggestionsVisible));
}

public sealed class ObservableRelationshipTarget
{
    public ObservableRelationshipTarget(string name, ObservableRelationship owner)
    {
        Name = name;
        Owner = owner;
    }

    public string Name { get; }
    public ObservableRelationship Owner { get; }
}
