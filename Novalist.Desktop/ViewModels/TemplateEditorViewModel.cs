using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core.Models;
using Novalist.Desktop.Localization;

namespace Novalist.Desktop.ViewModels;

public partial class TemplateEditorViewModel : ObservableObject
{
    private readonly string _entityType;

    [ObservableProperty] private string _templateName = string.Empty;
    [ObservableProperty] private bool _isBuiltIn;
    [ObservableProperty] private bool _isCharacterTemplate;

    // Character-specific age settings
    [ObservableProperty] private int _ageModeIndex; // 0 = number, 1 = date
    [ObservableProperty] private int _ageIntervalUnitIndex; // 0=Years, 1=Months, 2=Days
    [ObservableProperty] private bool _showAgeMode;
    [ObservableProperty] private bool _showAgeOptions;

    // Options
    [ObservableProperty] private bool _includeRelationships = true;
    [ObservableProperty] private bool _includeImages = true;
    [ObservableProperty] private bool _includeChapterOverrides = true;

    public ObservableCollection<KnownFieldRowViewModel> KnownFields { get; } = [];
    public ObservableCollection<CustomFieldRowViewModel> CustomFields { get; } = [];
    public ObservableCollection<CustomPropertyDefRowViewModel> PropertyDefs { get; } = [];
    public ObservableCollection<SectionRowViewModel> Sections { get; } = [];

    public string[] AgeModes { get; } = [Loc.T("template.ageMode.number"), Loc.T("template.ageMode.date")];
    public string[] IntervalUnits { get; } = [Loc.T("template.intervalUnit.years"), Loc.T("template.intervalUnit.months"), Loc.T("template.intervalUnit.days")];
    public string[] PropertyTypes { get; } =
    [
        Loc.T("template.propType.string"), Loc.T("template.propType.int"), Loc.T("template.propType.bool"),
        Loc.T("template.propType.date"), Loc.T("template.propType.enum"), Loc.T("template.propType.timespan")
    ];

    public TemplateEditorViewModel(string entityType)
    {
        _entityType = entityType;
        IsCharacterTemplate = string.Equals(entityType, "character", StringComparison.OrdinalIgnoreCase);
    }

    public void LoadCharacterTemplate(CharacterTemplate t)
    {
        TemplateName = t.Name;
        IsBuiltIn = t.BuiltIn;
        IncludeRelationships = t.IncludeRelationships;
        IncludeImages = t.IncludeImages;
        IncludeChapterOverrides = t.IncludeChapterOverrides;

        var ageMode = t.AgeMode ?? "number";
        AgeModeIndex = string.Equals(ageMode, "date", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        AgeIntervalUnitIndex = t.AgeIntervalUnit switch
        {
            IntervalUnit.Months => 1,
            IntervalUnit.Days => 2,
            _ => 0
        };

        LoadKnownFields(TemplateKnownFields.Character, t.Fields);
        LoadCustomFields(TemplateKnownFields.Character, t.Fields);
        LoadPropertyDefs(t.CustomPropertyDefs);
        LoadSections(t.Sections);
        UpdateShowAgeOptions();
    }

    public void LoadLocationTemplate(LocationTemplate t)
    {
        TemplateName = t.Name;
        IsBuiltIn = t.BuiltIn;
        IncludeImages = t.IncludeImages;

        LoadKnownFields(TemplateKnownFields.Location, t.Fields);
        LoadCustomFields(TemplateKnownFields.Location, t.Fields);
        LoadPropertyDefs(t.CustomPropertyDefs);
        LoadSections(t.Sections);
    }

    public void LoadItemTemplate(ItemTemplate t)
    {
        TemplateName = t.Name;
        IsBuiltIn = t.BuiltIn;
        IncludeImages = t.IncludeImages;

        LoadKnownFields(TemplateKnownFields.Item, t.Fields);
        LoadCustomFields(TemplateKnownFields.Item, t.Fields);
        LoadPropertyDefs(t.CustomPropertyDefs);
        LoadSections(t.Sections);
    }

    public void LoadLoreTemplate(LoreTemplate t)
    {
        TemplateName = t.Name;
        IsBuiltIn = t.BuiltIn;
        IncludeImages = t.IncludeImages;

        LoadKnownFields(TemplateKnownFields.Lore, t.Fields);
        LoadCustomFields(TemplateKnownFields.Lore, t.Fields);
        LoadPropertyDefs(t.CustomPropertyDefs);
        LoadSections(t.Sections);
    }

    public CharacterTemplate BuildCharacterTemplate(string id)
    {
        var template = new CharacterTemplate
        {
            Id = id,
            Name = TemplateName,
            BuiltIn = IsBuiltIn,
            IncludeRelationships = IncludeRelationships,
            IncludeImages = IncludeImages,
            IncludeChapterOverrides = IncludeChapterOverrides,
            AgeMode = AgeModeIndex == 1 ? "date" : null,
            AgeIntervalUnit = AgeModeIndex == 1 ? IndexToIntervalUnit(AgeIntervalUnitIndex) : null
        };
        BuildFields(template.Fields);
        BuildPropertyDefs(template.CustomPropertyDefs);
        BuildSections(template.Sections);
        return template;
    }

    public LocationTemplate BuildLocationTemplate(string id)
    {
        var template = new LocationTemplate { Id = id, Name = TemplateName, BuiltIn = IsBuiltIn, IncludeImages = IncludeImages };
        BuildFields(template.Fields);
        BuildPropertyDefs(template.CustomPropertyDefs);
        BuildSections(template.Sections);
        return template;
    }

    public ItemTemplate BuildItemTemplate(string id)
    {
        var template = new ItemTemplate { Id = id, Name = TemplateName, BuiltIn = IsBuiltIn, IncludeImages = IncludeImages };
        BuildFields(template.Fields);
        BuildPropertyDefs(template.CustomPropertyDefs);
        BuildSections(template.Sections);
        return template;
    }

    public LoreTemplate BuildLoreTemplate(string id)
    {
        var template = new LoreTemplate { Id = id, Name = TemplateName, BuiltIn = IsBuiltIn, IncludeImages = IncludeImages };
        BuildFields(template.Fields);
        BuildPropertyDefs(template.CustomPropertyDefs);
        BuildSections(template.Sections);
        return template;
    }

    [RelayCommand]
    private void AddCustomField()
    {
        var row = new CustomFieldRowViewModel { Key = string.Empty, DefaultValue = string.Empty };
        row.RemoveRequested += OnCustomFieldRemoveRequested;
        CustomFields.Add(row);
    }

    [RelayCommand]
    private void AddProperty()
    {
        var row = new CustomPropertyDefRowViewModel(PropertyTypes)
        {
            Key = $"prop{PropertyDefs.Count + 1}",
            TypeIndex = 0,
            DefaultValue = string.Empty
        };
        row.RemoveRequested += OnPropertyDefRemoveRequested;
        PropertyDefs.Add(row);
    }

    [RelayCommand]
    private void AddSection()
    {
        var row = new SectionRowViewModel { Title = string.Empty, DefaultContent = string.Empty };
        row.RemoveRequested += OnSectionRemoveRequested;
        Sections.Add(row);
    }

    partial void OnAgeModeIndexChanged(int value) => UpdateShowAgeOptions();

    private void UpdateShowAgeOptions()
    {
        var ageField = KnownFields.FirstOrDefault(f =>
            string.Equals(f.FieldKey, "Age", StringComparison.OrdinalIgnoreCase));
        var ageActive = IsCharacterTemplate && ageField is { IsActive: true };
        ShowAgeMode = ageActive;
        ShowAgeOptions = ageActive && AgeModeIndex == 1;
    }

    private void OnKnownFieldActiveChanged(object? sender, EventArgs e) => UpdateShowAgeOptions();

    private void OnCustomFieldRemoveRequested(object? sender, EventArgs e)
    {
        if (sender is CustomFieldRowViewModel row)
        {
            row.RemoveRequested -= OnCustomFieldRemoveRequested;
            CustomFields.Remove(row);
        }
    }

    private void OnPropertyDefRemoveRequested(object? sender, EventArgs e)
    {
        if (sender is CustomPropertyDefRowViewModel row)
        {
            row.RemoveRequested -= OnPropertyDefRemoveRequested;
            PropertyDefs.Remove(row);
        }
    }

    private void OnSectionRemoveRequested(object? sender, EventArgs e)
    {
        if (sender is SectionRowViewModel row)
        {
            row.RemoveRequested -= OnSectionRemoveRequested;
            Sections.Remove(row);
        }
    }

    private void LoadKnownFields(string[] knownKeys, System.Collections.Generic.List<TemplateField> fields)
    {
        KnownFields.Clear();
        var isNew = fields.Count == 0;
        var activeSet = fields.Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in knownKeys)
        {
            var field = fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
            var row = new KnownFieldRowViewModel
            {
                FieldKey = key,
                IsActive = isNew || activeSet.Contains(key),
                DefaultValue = field?.DefaultValue ?? string.Empty,
                IsAgeField = IsCharacterTemplate && string.Equals(key, "Age", StringComparison.OrdinalIgnoreCase)
            };
            row.ActiveChanged += OnKnownFieldActiveChanged;
            KnownFields.Add(row);
        }
    }

    private void LoadCustomFields(string[] knownKeys, System.Collections.Generic.List<TemplateField> fields)
    {
        CustomFields.Clear();
        var knownSet = knownKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields.Where(f => !knownSet.Contains(f.Key)))
        {
            var row = new CustomFieldRowViewModel { Key = field.Key, DefaultValue = field.DefaultValue };
            row.RemoveRequested += OnCustomFieldRemoveRequested;
            CustomFields.Add(row);
        }
    }

    private void LoadPropertyDefs(System.Collections.Generic.List<CustomPropertyDefinition> defs)
    {
        PropertyDefs.Clear();
        foreach (var def in defs)
        {
            var row = new CustomPropertyDefRowViewModel(PropertyTypes)
            {
                Key = def.Key,
                TypeIndex = (int)def.Type,
                DefaultValue = def.DefaultValue,
                EnumOptionsText = def.EnumOptions != null ? string.Join(", ", def.EnumOptions) : string.Empty,
                IntervalUnitIndex = def.IntervalUnit switch
                {
                    IntervalUnit.Months => 1,
                    IntervalUnit.Days => 2,
                    _ => 0
                }
            };
            row.RemoveRequested += OnPropertyDefRemoveRequested;
            PropertyDefs.Add(row);
        }
    }

    private void LoadSections(System.Collections.Generic.List<TemplateSection> sections)
    {
        Sections.Clear();
        foreach (var section in sections)
        {
            var row = new SectionRowViewModel { Title = section.Title, DefaultContent = section.DefaultContent };
            row.RemoveRequested += OnSectionRemoveRequested;
            Sections.Add(row);
        }
    }

    private void BuildFields(System.Collections.Generic.List<TemplateField> target)
    {
        target.Clear();
        foreach (var row in KnownFields.Where(r => r.IsActive))
            target.Add(new TemplateField { Key = row.FieldKey, DefaultValue = row.DefaultValue });
        foreach (var row in CustomFields.Where(r => !string.IsNullOrWhiteSpace(r.Key)))
            target.Add(new TemplateField { Key = row.Key, DefaultValue = row.DefaultValue });
    }

    private void BuildPropertyDefs(System.Collections.Generic.List<CustomPropertyDefinition> target)
    {
        target.Clear();
        foreach (var row in PropertyDefs.Where(r => !string.IsNullOrWhiteSpace(r.Key)))
        {
            var def = new CustomPropertyDefinition
            {
                Key = row.Key,
                Type = (CustomPropertyType)row.TypeIndex,
                DefaultValue = row.DefaultValue
            };
            if (def.Type == CustomPropertyType.Enum && !string.IsNullOrWhiteSpace(row.EnumOptionsText))
                def.EnumOptions = row.EnumOptionsText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            if (def.Type == CustomPropertyType.Timespan)
                def.IntervalUnit = IndexToIntervalUnit(row.IntervalUnitIndex);
            target.Add(def);
        }
    }

    private void BuildSections(System.Collections.Generic.List<TemplateSection> target)
    {
        target.Clear();
        foreach (var row in Sections)
            target.Add(new TemplateSection { Title = row.Title, DefaultContent = row.DefaultContent });
    }

    private static IntervalUnit IndexToIntervalUnit(int index) => index switch
    {
        1 => IntervalUnit.Months,
        2 => IntervalUnit.Days,
        _ => IntervalUnit.Years
    };
}

public partial class KnownFieldRowViewModel : ObservableObject
{
    private static readonly Dictionary<string, string> FieldKeyToLocKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Gender"] = "entityEditor.gender",
        ["Age"] = "entityEditor.age",
        ["Role"] = "entityEditor.rolePlaceholder",
        ["EyeColor"] = "entityEditor.eyeColor",
        ["HairColor"] = "entityEditor.hairColor",
        ["HairLength"] = "entityEditor.hairLength",
        ["Height"] = "entityEditor.height",
        ["Build"] = "entityEditor.build",
        ["SkinTone"] = "entityEditor.skinTone",
        ["DistinguishingFeatures"] = "entityEditor.distinguishingFeatures",
        ["Type"] = "entityEditor.locationTypePlain",
        ["Description"] = "entityEditor.description",
        ["Origin"] = "entityEditor.origin",
        ["Category"] = "entityEditor.category",
    };

    [ObservableProperty] private string _fieldKey = string.Empty;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _defaultValue = string.Empty;
    [ObservableProperty] private bool _isAgeField;

    public string DisplayName => FieldKeyToLocKey.TryGetValue(FieldKey, out var key)
        ? Loc.T(key)
        : FieldKey;

    public bool ShowDefaultValue => IsActive && !IsAgeField;

    public event EventHandler? ActiveChanged;

    partial void OnIsActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowDefaultValue));
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }
}

public partial class CustomFieldRowViewModel : ObservableObject
{
    [ObservableProperty] private string _key = string.Empty;
    [ObservableProperty] private string _defaultValue = string.Empty;

    public event EventHandler? RemoveRequested;

    [RelayCommand]
    private void Remove() => RemoveRequested?.Invoke(this, EventArgs.Empty);
}

public partial class CustomPropertyDefRowViewModel : ObservableObject
{
    private readonly string[] _propertyTypes;

    [ObservableProperty] private string _key = string.Empty;
    [ObservableProperty] private int _typeIndex;
    [ObservableProperty] private string _defaultValue = string.Empty;
    [ObservableProperty] private string _enumOptionsText = string.Empty;
    [ObservableProperty] private int _intervalUnitIndex;
    [ObservableProperty] private bool _showEnumOptions;
    [ObservableProperty] private bool _showIntervalUnit;
    [ObservableProperty] private bool _isBoolType;

    public string[] PropertyTypes => _propertyTypes;
    public string[] IntervalUnits { get; } = [Loc.T("template.intervalUnit.years"), Loc.T("template.intervalUnit.months"), Loc.T("template.intervalUnit.days")];

    public event EventHandler? RemoveRequested;

    public CustomPropertyDefRowViewModel(string[] propertyTypes)
    {
        _propertyTypes = propertyTypes;
    }

    partial void OnTypeIndexChanged(int value)
    {
        var type = (CustomPropertyType)value;
        ShowEnumOptions = type == CustomPropertyType.Enum;
        ShowIntervalUnit = type == CustomPropertyType.Timespan;
        IsBoolType = type == CustomPropertyType.Bool;
    }

    [RelayCommand]
    private void Remove() => RemoveRequested?.Invoke(this, EventArgs.Empty);
}

public partial class SectionRowViewModel : ObservableObject
{
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _defaultContent = string.Empty;

    public event EventHandler? RemoveRequested;

    [RelayCommand]
    private void Remove() => RemoveRequested?.Invoke(this, EventArgs.Empty);
}
