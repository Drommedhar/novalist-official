using Novalist.Sdk.Models;

namespace Novalist.Sdk.Hooks;

/// <summary>
/// Contributes items to the application status bar.
/// </summary>
public interface IStatusBarContributor
{
    /// <summary>Returns status bar items to display.</summary>
    IReadOnlyList<StatusBarItem> GetStatusBarItems();
}
