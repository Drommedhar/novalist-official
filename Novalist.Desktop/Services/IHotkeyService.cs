using System;
using System.Collections.Generic;
using Novalist.Sdk.Models;

namespace Novalist.Desktop.Services;

/// <summary>
/// Manages hotkey action registrations and user-overridden key gesture bindings.
/// </summary>
public interface IHotkeyService
{
    /// <summary>Register a single hotkey action.</summary>
    void Register(HotkeyDescriptor descriptor);

    /// <summary>Register multiple hotkey actions at once.</summary>
    void RegisterRange(IEnumerable<HotkeyDescriptor> descriptors);

    /// <summary>Unregister an action by its ID. Used when disabling extensions.</summary>
    void Unregister(string actionId);

    /// <summary>
    /// Returns the current key gesture string for the given action.
    /// Returns the user override if set, otherwise the default from the descriptor.
    /// </summary>
    string GetGesture(string actionId);

    /// <summary>Set a user override gesture for an action.</summary>
    void SetGesture(string actionId, string gesture);

    /// <summary>Remove the user override for an action, reverting to its default gesture.</summary>
    void ResetGesture(string actionId);

    /// <summary>Remove all user overrides, reverting everything to defaults.</summary>
    void ResetAll();

    /// <summary>Returns all registered action descriptors.</summary>
    IReadOnlyList<HotkeyDescriptor> GetAllDescriptors();

    /// <summary>
    /// Checks whether the given gesture conflicts with another action.
    /// Returns the conflicting action ID, or null if no conflict.
    /// </summary>
    string? FindConflict(string actionId, string gesture);

    /// <summary>Fires when any binding is added, removed, or changed.</summary>
    event Action? BindingsChanged;
}
