using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core.Models;

namespace Novalist.Desktop.ViewModels;

public partial class EntityTypeManagerViewModel : ObservableObject
{
    [ObservableProperty] private string _typeKey = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _displayNamePlural = string.Empty;
    [ObservableProperty] private string _icon = "📋";
    [ObservableProperty] private bool _includeImages = true;
    [ObservableProperty] private bool _includeRelationships;
    [ObservableProperty] private bool _includeSections = true;

    public ObservableCollection<EntityTypeFieldRowViewModel> Fields { get; } = [];

    public bool IsEditing { get; private set; }

    private string[] _allEntityRefTargets = ["Character", "Location", "Item", "Lore"];

    public EntityTypeManagerViewModel()
    {
    }

    public void SetCustomEntityTypes(IEnumerable<CustomEntityTypeDefinition> customTypes)
    {
        var targets = new[] { "Character", "Location", "Item", "Lore" }.ToList();
        foreach (var ct in customTypes)
        {
            if (!targets.Contains(ct.DisplayName, StringComparer.OrdinalIgnoreCase))
                targets.Add(ct.DisplayName);
        }
        _allEntityRefTargets = targets.ToArray();
        foreach (var field in Fields)
            field.EntityRefTargets = _allEntityRefTargets;
    }

    public void LoadDefinition(CustomEntityTypeDefinition def)
    {
        IsEditing = true;
        TypeKey = def.TypeKey;
        DisplayName = def.DisplayName;
        DisplayNamePlural = def.DisplayNamePlural;
        Icon = def.Icon;
        IncludeImages = def.Features.IncludeImages;
        IncludeRelationships = def.Features.IncludeRelationships;
        IncludeSections = def.Features.IncludeSections;

        Fields.Clear();
        foreach (var field in def.DefaultFields)
        {
            Fields.Add(new EntityTypeFieldRowViewModel(RemoveField)
            {
                EntityRefTargets = _allEntityRefTargets,
                Key = field.Key,
                DisplayName = field.DisplayName,
                TypeIndex = (int)field.Type,
                DefaultValue = field.DefaultValue,
                EnumOptionsText = field.EnumOptions != null ? string.Join(", ", field.EnumOptions) : string.Empty,
                EntityRefTarget = field.Type == CustomPropertyType.EntityRef && field.EnumOptions is { Count: > 0 }
                    ? field.EnumOptions[0] : "Character",
                Required = field.Required
            });
        }
    }

    public CustomEntityTypeDefinition BuildDefinition()
    {
        var key = IsEditing ? TypeKey : GenerateTypeKey(DisplayName);

        return new CustomEntityTypeDefinition
        {
            TypeKey = key,
            DisplayName = DisplayName.Trim(),
            DisplayNamePlural = string.IsNullOrWhiteSpace(DisplayNamePlural) ? DisplayName.Trim() + "s" : DisplayNamePlural.Trim(),
            Icon = string.IsNullOrWhiteSpace(Icon) ? "📋" : Icon.Trim(),
            FolderName = key,
            Source = "user",
            DefaultFields = Fields.Select(f => new CustomEntityFieldDefinition
            {
                Key = string.IsNullOrWhiteSpace(f.Key)
                    ? f.DisplayName.Replace(" ", "", StringComparison.Ordinal)
                    : f.Key,
                DisplayName = f.DisplayName,
                Type = (CustomPropertyType)f.TypeIndex,
                DefaultValue = f.DefaultValue,
                EnumOptions = (CustomPropertyType)f.TypeIndex switch
                {
                    CustomPropertyType.Enum when !string.IsNullOrWhiteSpace(f.EnumOptionsText) =>
                        f.EnumOptionsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                    CustomPropertyType.EntityRef => [f.EntityRefTarget],
                    _ => null
                },
                Required = f.Required
            }).ToList(),
            Features = new CustomEntityFeatures
            {
                IncludeImages = IncludeImages,
                IncludeRelationships = IncludeRelationships,
                IncludeSections = IncludeSections
            }
        };
    }

    [RelayCommand]
    private void AddField()
    {
        Fields.Add(new EntityTypeFieldRowViewModel(RemoveField) { EntityRefTargets = _allEntityRefTargets });
    }

    private void RemoveField(EntityTypeFieldRowViewModel field)
    {
        Fields.Remove(field);
    }

    private static string GenerateTypeKey(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return "custom_" + Guid.NewGuid().ToString("N")[..8];

        return string.Concat(displayName.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_'))
            .Trim('_');
    }
}

public partial class EntityTypeFieldRowViewModel : ObservableObject
{
    private readonly Action<EntityTypeFieldRowViewModel> _removeAction;

    [ObservableProperty] private string _key = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private int _typeIndex; // maps to CustomPropertyType enum
    [ObservableProperty] private string _defaultValue = string.Empty;
    [ObservableProperty] private string _enumOptionsText = string.Empty;
    [ObservableProperty] private string _entityRefTarget = "Character";
    [ObservableProperty] private bool _required;

    public bool ShowEnumOptions => TypeIndex == (int)CustomPropertyType.Enum;
    public bool ShowEntityRefTarget => TypeIndex == (int)CustomPropertyType.EntityRef;

    public string[] PropertyTypes { get; } = ["String", "Int", "Bool", "Date", "Enum", "Timespan", "EntityRef"];
    [ObservableProperty] private string[] _entityRefTargets = ["Character", "Location", "Item", "Lore"];

    public EntityTypeFieldRowViewModel(Action<EntityTypeFieldRowViewModel> removeAction)
    {
        _removeAction = removeAction;
    }

    partial void OnTypeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ShowEnumOptions));
        OnPropertyChanged(nameof(ShowEntityRefTarget));
    }

    [RelayCommand]
    private void Remove() => _removeAction(this);
}
