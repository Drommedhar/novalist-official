using System;
using System.Collections.Generic;
using System.Linq;
using Novalist.Core.Services;
using Novalist.Sdk.Models;

namespace Novalist.Desktop.Services;

/// <summary>
/// Manages hotkey registrations and user-overridden gesture bindings,
/// persisted through <see cref="ISettingsService"/>.
/// </summary>
public sealed class HotkeyService : IHotkeyService
{
    private readonly ISettingsService _settingsService;
    private readonly Dictionary<string, HotkeyDescriptor> _descriptors = new(StringComparer.Ordinal);

    public HotkeyService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public event Action? BindingsChanged;

    public void Register(HotkeyDescriptor descriptor)
    {
        _descriptors[descriptor.ActionId] = descriptor;
        BindingsChanged?.Invoke();
    }

    public void RegisterRange(IEnumerable<HotkeyDescriptor> descriptors)
    {
        foreach (var d in descriptors)
            _descriptors[d.ActionId] = d;
        BindingsChanged?.Invoke();
    }

    public void Unregister(string actionId)
    {
        if (_descriptors.Remove(actionId))
            BindingsChanged?.Invoke();
    }

    public string GetGesture(string actionId)
    {
        var overrides = _settingsService.Settings.HotkeyBindings;
        if (overrides.TryGetValue(actionId, out var gesture))
            return gesture;

        return _descriptors.TryGetValue(actionId, out var desc)
            ? desc.DefaultGesture
            : string.Empty;
    }

    public void SetGesture(string actionId, string gesture)
    {
        _settingsService.Settings.HotkeyBindings[actionId] = gesture;
        _ = _settingsService.SaveAsync();
        BindingsChanged?.Invoke();
    }

    public void ResetGesture(string actionId)
    {
        if (_settingsService.Settings.HotkeyBindings.Remove(actionId))
        {
            _ = _settingsService.SaveAsync();
            BindingsChanged?.Invoke();
        }
    }

    public void ResetAll()
    {
        _settingsService.Settings.HotkeyBindings.Clear();
        _ = _settingsService.SaveAsync();
        BindingsChanged?.Invoke();
    }

    public IReadOnlyList<HotkeyDescriptor> GetAllDescriptors()
    {
        return _descriptors.Values.ToList();
    }

    public string? FindConflict(string actionId, string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture))
            return null;

        var normalized = gesture.Trim();
        foreach (var (id, _) in _descriptors)
        {
            if (string.Equals(id, actionId, StringComparison.Ordinal))
                continue;

            var existing = GetGesture(id);
            if (string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))
                return id;
        }

        return null;
    }
}
