using Novalist.Sdk.Models;

namespace Novalist.Sdk.Hooks;

/// <summary>
/// Contributes keyboard shortcuts to the application hotkey system.
/// </summary>
public interface IHotkeyContributor
{
    /// <summary>
    /// Returns hotkey descriptors to register. Called once during initialization.
    /// Each descriptor specifies an action ID, display name, default gesture, and callback.
    /// </summary>
    IReadOnlyList<HotkeyDescriptor> GetHotkeyBindings();
}
