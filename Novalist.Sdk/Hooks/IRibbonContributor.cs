using Novalist.Sdk.Models;

namespace Novalist.Sdk.Hooks;

/// <summary>
/// Contributes buttons and groups to the application ribbon bar.
/// </summary>
public interface IRibbonContributor
{
    /// <summary>
    /// Returns ribbon items to add. Called once during initialization.
    /// Items specify which tab and group they belong to.
    /// </summary>
    IReadOnlyList<RibbonItem> GetRibbonItems();
}
