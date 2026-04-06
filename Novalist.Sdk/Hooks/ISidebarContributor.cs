using Novalist.Sdk.Models;

namespace Novalist.Sdk.Hooks;

/// <summary>
/// Contributes panels to the left or right sidebar.
/// </summary>
public interface ISidebarContributor
{
    /// <summary>
    /// Returns sidebar panel descriptors. The host creates tabs for these
    /// in the left or right sidebar area.
    /// </summary>
    IReadOnlyList<SidebarPanel> GetSidebarPanels();
}
