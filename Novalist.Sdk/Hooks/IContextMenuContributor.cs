using Novalist.Sdk.Models;

namespace Novalist.Sdk.Hooks;

/// <summary>
/// Contributes items to explorer and entity context menus.
/// </summary>
public interface IContextMenuContributor
{
    /// <summary>
    /// Returns context menu items to add to explorer/entity context menus.
    /// </summary>
    IReadOnlyList<ContextMenuItem> GetContextMenuItems();
}
