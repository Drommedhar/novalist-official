using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Desktop.Localization;
using Novalist.Desktop.Services;

namespace Novalist.Desktop.ViewModels;

/// <summary>
/// Item representing a single hotkey binding in the settings UI.
/// </summary>
public partial class HotkeyBindingItem : ObservableObject
{
    public string ActionId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string DefaultGesture { get; init; } = string.Empty;

    [ObservableProperty]
    private string _currentGesture = string.Empty;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private bool _hasConflict;

    [ObservableProperty]
    private string _conflictDescription = string.Empty;

    public bool IsModified => !string.Equals(CurrentGesture, DefaultGesture, StringComparison.OrdinalIgnoreCase);

    /// <summary>Notify the UI that IsModified may have changed.</summary>
    public void NotifyIsModifiedChanged() =>
        OnPropertyChanged(nameof(IsModified));
}

/// <summary>
/// Group header for categorized hotkey display.
/// </summary>
public sealed class HotkeyGroupHeader
{
    public string Category { get; init; } = string.Empty;
}

/// <summary>
/// ViewModel for the keyboard shortcuts settings section.
/// </summary>
public partial class HotkeySettingsViewModel : ObservableObject
{
    private readonly IHotkeyService _hotkeyService;
    private HotkeyBindingItem? _recordingItem;

    [ObservableProperty]
    private string _filterText = string.Empty;

    /// <summary>Flat list of all binding items (for lookup).</summary>
    public List<HotkeyBindingItem> AllItems { get; } = [];

    /// <summary>Displayed list including group headers and filtered items.</summary>
    public ObservableCollection<object> DisplayItems { get; } = [];

    public HotkeySettingsViewModel(IHotkeyService hotkeyService)
    {
        _hotkeyService = hotkeyService;
        Refresh();
    }

    public void Refresh()
    {
        AllItems.Clear();

        foreach (var desc in _hotkeyService.GetAllDescriptors())
        {
            AllItems.Add(new HotkeyBindingItem
            {
                ActionId = desc.ActionId,
                DisplayName = desc.DisplayName,
                Category = desc.Category,
                DefaultGesture = desc.DefaultGesture,
                CurrentGesture = _hotkeyService.GetGesture(desc.ActionId),
            });
        }

        RebuildDisplayItems();
    }

    partial void OnFilterTextChanged(string value)
    {
        RebuildDisplayItems();
    }

    private void RebuildDisplayItems()
    {
        DisplayItems.Clear();

        var filtered = string.IsNullOrWhiteSpace(FilterText)
            ? AllItems
            : AllItems.Where(i =>
                i.DisplayName.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                i.Category.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                i.CurrentGesture.Contains(FilterText, StringComparison.OrdinalIgnoreCase)).ToList();

        string? lastCategory = null;
        foreach (var item in filtered.OrderBy(i => i.Category).ThenBy(i => i.DisplayName))
        {
            if (!string.Equals(item.Category, lastCategory, StringComparison.Ordinal))
            {
                DisplayItems.Add(new HotkeyGroupHeader { Category = item.Category });
                lastCategory = item.Category;
            }
            DisplayItems.Add(item);
        }
    }

    [RelayCommand]
    private void StartRecording(HotkeyBindingItem item)
    {
        // Cancel any existing recording
        if (_recordingItem != null)
            _recordingItem.IsRecording = false;

        item.IsRecording = true;
        _recordingItem = item;
    }

    [RelayCommand]
    private void CancelRecording()
    {
        if (_recordingItem != null)
        {
            _recordingItem.IsRecording = false;
            _recordingItem = null;
        }
    }

    /// <summary>
    /// Called from the view's KeyDown handler when recording mode is active.
    /// </summary>
    public bool HandleRecordingKeyDown(KeyEventArgs e)
    {
        if (_recordingItem == null)
            return false;

        // Ignore modifier-only presses
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return true;

        // Escape cancels recording
        if (e.Key == Key.Escape)
        {
            CancelRecording();
            return true;
        }

        var gesture = new KeyGesture(e.Key, e.KeyModifiers);
        var gestureStr = gesture.ToString();

        // Check for conflicts
        var conflict = _hotkeyService.FindConflict(_recordingItem.ActionId, gestureStr);
        if (conflict != null)
        {
            var conflictItem = AllItems.FirstOrDefault(i => i.ActionId == conflict);
            _recordingItem.HasConflict = true;
            _recordingItem.ConflictDescription = conflictItem != null
                ? Loc.T("hotkeys.conflict", conflictItem.DisplayName)
                : Loc.T("hotkeys.conflictGeneric");
        }
        else
        {
            _recordingItem.HasConflict = false;
            _recordingItem.ConflictDescription = string.Empty;
        }

        // Apply the new gesture
        _recordingItem.CurrentGesture = gestureStr;
        _hotkeyService.SetGesture(_recordingItem.ActionId, gestureStr);
        _recordingItem.IsRecording = false;
        _recordingItem.NotifyIsModifiedChanged();
        _recordingItem = null;

        return true;
    }

    public bool IsRecording => _recordingItem != null;

    [RelayCommand]
    private void ResetBinding(HotkeyBindingItem item)
    {
        _hotkeyService.ResetGesture(item.ActionId);
        item.CurrentGesture = item.DefaultGesture;
        item.HasConflict = false;
        item.ConflictDescription = string.Empty;
        item.NotifyIsModifiedChanged();
    }

    [RelayCommand]
    private void ResetAll()
    {
        _hotkeyService.ResetAll();
        foreach (var item in AllItems)
        {
            item.CurrentGesture = item.DefaultGesture;
            item.HasConflict = false;
            item.ConflictDescription = string.Empty;
            item.NotifyIsModifiedChanged();
        }
    }
}
