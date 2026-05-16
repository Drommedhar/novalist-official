using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core.Models;
using Novalist.Desktop.Localization;

namespace Novalist.Desktop.ViewModels;

public partial class FocusPeekViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isPinned;

    [ObservableProperty]
    private bool _isPointerOverCard;

    [ObservableProperty]
    private double _left = 24;

    [ObservableProperty]
    private double _top = 24;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _typeLabel = string.Empty;

    [ObservableProperty]
    private string _typeBadgeBackground = "#45475A";

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _chapterInfo = string.Empty;

    [ObservableProperty]
    private string _aiStubText = Loc.T("focusPeek.aiStub");

    [ObservableProperty]
    private FocusPeekImageItem? _selectedImage;

    [ObservableProperty]
    private FocusPeekSectionItem? _selectedSection;

    public ObservableCollection<FocusPeekPillItem> Pills { get; } = [];
    public ObservableCollection<FocusPeekRelationshipItem> Relationships { get; } = [];
    public ObservableCollection<FocusPeekPropertyItem> AppearanceProperties { get; } = [];
    public ObservableCollection<FocusPeekPropertyItem> CustomProperties { get; } = [];
    public ObservableCollection<FocusPeekSectionItem> Sections { get; } = [];
    public ObservableCollection<FocusPeekImageItem> Images { get; } = [];
    public ObservableCollection<FocusPeekAiFindingItem> AiFindings { get; } = [];
    public ObservableCollection<FocusPeekMapPinItem> MapPins { get; } = [];

    public EntityType CurrentEntityType { get; private set; }
    public object? CurrentEntity { get; private set; }

    public Action? CloseRequested { get; set; }
    public Action? TogglePinRequested { get; set; }
    public Action? OpenRequested { get; set; }
    public Action? PointerExitedRequested { get; set; }
    public Func<bool>? HasOpenPopup { get; set; }

    public bool HasImage => SelectedImage != null;
    public bool HasMultipleImages => Images.Count > 1;
    public bool HasPills => Pills.Count > 0;
    public bool HasRelationships => Relationships.Count > 0;
    public bool HasAppearanceProperties => AppearanceProperties.Count > 0;
    public bool HasCustomProperties => CustomProperties.Count > 0;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public bool HasChapterInfo => !string.IsNullOrWhiteSpace(ChapterInfo);
    public bool HasSections => Sections.Count > 0;
    public bool HasAiFindings => AiFindings.Count > 0;
    public bool HasMapPins => MapPins.Count > 0;
    public bool ShowAiStub => !HasAiFindings;
    public bool HasSelectedSectionContent => !string.IsNullOrWhiteSpace(SelectedSectionContent);
    public string SelectedImagePath => SelectedImage?.Path ?? string.Empty;
    public string SelectedSectionContent => SelectedSection?.Content ?? string.Empty;
    public string PinButtonLabel => IsPinned ? Loc.T("focusPeek.unpin") : Loc.T("focusPeek.pin");
    public string PinButtonGlyph => IsPinned ? "\u25CF" : "\u25CB";

    partial void OnSelectedImageChanged(FocusPeekImageItem? value)
    {
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(SelectedImagePath));
    }

    partial void OnSelectedSectionChanged(FocusPeekSectionItem? value)
    {
        OnPropertyChanged(nameof(SelectedSectionContent));
        OnPropertyChanged(nameof(HasSelectedSectionContent));
    }

    public void Show(FocusPeekDisplayData data, double left, double top)
    {
        Title = data.Title;
        TypeLabel = data.TypeLabel;
        TypeBadgeBackground = data.TypeBadgeBackground;
        Description = data.Description;
        ChapterInfo = data.ChapterInfo;
        CurrentEntityType = data.EntityType;
        CurrentEntity = data.Entity;

        ReplaceCollection(Pills, data.Pills);
        ReplaceCollection(Relationships, data.Relationships);
        ReplaceCollection(AppearanceProperties, data.AppearanceProperties);
        ReplaceCollection(CustomProperties, data.CustomProperties);
        ReplaceCollection(Sections, data.Sections);
        ReplaceCollection(Images, data.Images);
        ReplaceCollection(AiFindings, data.AiFindings);
        ReplaceCollection(MapPins, data.MapPins);

        SelectedImage = Images.Count > 0 ? Images[0] : null;
        SelectedSection = Sections.Count > 0 ? Sections[0] : null;

        Left = left;
        Top = top;
        IsOpen = true;

        RaiseComputedPropertyNotifications();
    }

    public void Hide()
    {
        IsOpen = false;
        CurrentEntity = null;
        Relationships.Clear();
        Pills.Clear();
        AppearanceProperties.Clear();
        CustomProperties.Clear();
        Sections.Clear();
        Images.Clear();
        AiFindings.Clear();
        MapPins.Clear();
        SelectedImage = null;
        SelectedSection = null;
        Description = string.Empty;
        ChapterInfo = string.Empty;
        Title = string.Empty;
        TypeLabel = string.Empty;
        IsPointerOverCard = false;
        RaiseComputedPropertyNotifications();
    }

    public void SetPinned(bool isPinned)
    {
        IsPinned = isPinned;
        OnPropertyChanged(nameof(PinButtonLabel));
        OnPropertyChanged(nameof(PinButtonGlyph));
    }

    public void SetPointerOverCard(bool isPointerOverCard)
    {
        IsPointerOverCard = isPointerOverCard;
    }

    public void UpdatePosition(double left, double top)
    {
        Left = left;
        Top = top;
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void TogglePin()
    {
        TogglePinRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenEntity()
    {
        OpenRequested?.Invoke();
    }

    private void RaiseComputedPropertyNotifications()
    {
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(HasMultipleImages));
        OnPropertyChanged(nameof(HasPills));
        OnPropertyChanged(nameof(HasRelationships));
        OnPropertyChanged(nameof(HasAppearanceProperties));
        OnPropertyChanged(nameof(HasCustomProperties));
        OnPropertyChanged(nameof(HasDescription));
        OnPropertyChanged(nameof(HasChapterInfo));
        OnPropertyChanged(nameof(HasSections));
        OnPropertyChanged(nameof(HasAiFindings));
        OnPropertyChanged(nameof(HasMapPins));
        OnPropertyChanged(nameof(ShowAiStub));
        OnPropertyChanged(nameof(HasSelectedSectionContent));
        OnPropertyChanged(nameof(SelectedImagePath));
        OnPropertyChanged(nameof(SelectedSectionContent));
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> values)
    {
        target.Clear();
        foreach (var value in values)
            target.Add(value);
    }
}

public sealed class FocusPeekDisplayData
{
    public required EntityType EntityType { get; init; }
    public required object Entity { get; init; }
    public required string Title { get; init; }
    public required string TypeLabel { get; init; }
    public required string TypeBadgeBackground { get; init; }
    public string Description { get; init; } = string.Empty;
    public string ChapterInfo { get; init; } = string.Empty;
    public IReadOnlyList<FocusPeekPillItem> Pills { get; init; } = [];
    public IReadOnlyList<FocusPeekRelationshipItem> Relationships { get; init; } = [];
    public IReadOnlyList<FocusPeekPropertyItem> AppearanceProperties { get; init; } = [];
    public IReadOnlyList<FocusPeekPropertyItem> CustomProperties { get; init; } = [];
    public IReadOnlyList<FocusPeekSectionItem> Sections { get; init; } = [];
    public IReadOnlyList<FocusPeekImageItem> Images { get; init; } = [];
    public IReadOnlyList<FocusPeekAiFindingItem> AiFindings { get; set; } = [];
    public IReadOnlyList<FocusPeekMapPinItem> MapPins { get; set; } = [];
}

public sealed class FocusPeekMapPinItem
{
    public FocusPeekMapPinItem(string mapId, string mapName, string pinId, string pinLabel, Func<string, string, Task> navigateAsync)
    {
        MapId = mapId;
        MapName = mapName;
        PinId = pinId;
        PinLabel = pinLabel;
        NavigateCommand = new AsyncRelayCommand(() => navigateAsync(MapId, PinId));
    }

    public string MapId { get; }
    public string MapName { get; }
    public string PinId { get; }
    public string PinLabel { get; }
    /// <summary>Shown in the peek button — "Pin label · Map name" when both set, else whichever exists.</summary>
    public string DisplayText =>
        string.IsNullOrWhiteSpace(PinLabel) ? MapName
        : (string.IsNullOrWhiteSpace(MapName) ? PinLabel : $"{PinLabel} · {MapName}");
    public IAsyncRelayCommand NavigateCommand { get; }
}

public sealed class FocusPeekPillItem
{
    public required string Text { get; init; }
    public string IconPath { get; init; } = string.Empty;
    public string Background { get; init; } = "#1E1E2E";
    public string Foreground { get; init; } = "#CDD6F4";
    public bool Dim { get; init; }
    public bool HasIcon => !string.IsNullOrWhiteSpace(IconPath);
    public double DisplayOpacity => Dim ? 0.75 : 1.0;
}

public sealed class FocusPeekPropertyItem
{
    public required string Key { get; init; }
    public required string Value { get; init; }
}

public sealed class FocusPeekSectionItem
{
    public required string Title { get; init; }
    public required string Content { get; init; }

    public override string ToString() => Title;
}

public sealed class FocusPeekImageItem
{
    public required string Name { get; init; }
    public required string Path { get; init; }

    public override string ToString() => Name;
}

public sealed class FocusPeekRelationshipItem
{
    public FocusPeekRelationshipItem(string role, IReadOnlyList<FocusPeekRelationshipTarget> targets)
    {
        Role = role;
        Targets = targets;
    }

    public string Role { get; }
    public IReadOnlyList<FocusPeekRelationshipTarget> Targets { get; }
}

public sealed class FocusPeekRelationshipTarget
{
    public FocusPeekRelationshipTarget(string name, Func<string, Task> navigateAsync, bool canNavigate, bool showSeparator)
    {
        Name = name;
        CanNavigate = canNavigate;
        ShowSeparator = showSeparator;
        NavigateCommand = new AsyncRelayCommand(() => navigateAsync(Name), () => CanNavigate);
    }

    public string Name { get; }
    public bool CanNavigate { get; }
    public bool ShowSeparator { get; }
    public IAsyncRelayCommand NavigateCommand { get; }
}

public sealed class FocusPeekAiFindingItem
{
    public required string Type { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public string Excerpt { get; init; } = string.Empty;
    public bool HasExcerpt => !string.IsNullOrWhiteSpace(Excerpt);

    public string TypeIcon => Type switch
    {
        "reference" => "\u2192",
        "inconsistency" => "\u26A0",
        "suggestion" => "\u2022",
        _ => "\u2022",
    };

    public string TypeColor => Type switch
    {
        "reference" => "#3498db",
        "inconsistency" => "#e74c3c",
        "suggestion" => "#f39c12",
        _ => "#95a5a6",
    };
}