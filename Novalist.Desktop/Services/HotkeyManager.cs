using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Novalist.Sdk.Models;

namespace Novalist.Desktop.Services;

/// <summary>
/// Intercepts keyboard events and dispatches them to registered hotkey actions.
/// Attached to the main window's OnKeyDown handler.
/// </summary>
public sealed class HotkeyManager
{
    private readonly IHotkeyService _hotkeyService;
    private Dictionary<string, List<HotkeyDescriptor>> _gestureMap = new(StringComparer.OrdinalIgnoreCase);

    public HotkeyManager(IHotkeyService hotkeyService)
    {
        _hotkeyService = hotkeyService;
        _hotkeyService.BindingsChanged += RebuildGestureMap;
        RebuildGestureMap();
    }

    /// <summary>
    /// Call from MainWindow.OnKeyDown. If a matching hotkey action is found
    /// and its CanExecute passes, the action is invoked and e.Handled is set.
    /// </summary>
    public void HandleKeyDown(KeyEventArgs e)
    {
        if (e.Handled)
            return;

        var gesture = new KeyGesture(e.Key, e.KeyModifiers);
        if (TryExecute(gesture.ToString()))
            e.Handled = true;
    }

    /// <summary>
    /// Tries to execute a hotkey action matching the given key and modifiers.
    /// Used by WebView-hosted editors that forward key events via JS messages.
    /// </summary>
    public bool TryExecute(Key key, KeyModifiers modifiers)
    {
        var gesture = new KeyGesture(key, modifiers);
        return TryExecute(gesture.ToString());
    }

    private bool TryExecute(string gestureStr)
    {
        if (!_gestureMap.TryGetValue(gestureStr, out var candidates))
            return false;

        foreach (var descriptor in candidates)
        {
            if (descriptor.CanExecute != null && !descriptor.CanExecute())
                continue;

            descriptor.OnExecute?.Invoke();
            return true;
        }
        return false;
    }

    private void RebuildGestureMap()
    {
        var map = new Dictionary<string, List<HotkeyDescriptor>>(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in _hotkeyService.GetAllDescriptors())
        {
            var gestureStr = _hotkeyService.GetGesture(descriptor.ActionId);
            if (string.IsNullOrWhiteSpace(gestureStr))
                continue;

            // Validate the gesture string parses correctly
            try
            {
                var parsed = KeyGesture.Parse(gestureStr);
                var normalized = parsed.ToString();

                if (!map.TryGetValue(normalized, out var list))
                {
                    list = [];
                    map[normalized] = list;
                }
                list.Add(descriptor);
            }
            catch
            {
                // Skip invalid gesture strings
            }
        }

        _gestureMap = map;
    }
}
