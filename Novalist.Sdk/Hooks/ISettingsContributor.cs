using Novalist.Sdk.Models;

namespace Novalist.Sdk.Hooks;

/// <summary>
/// Contributes custom settings pages to the Settings view.
/// </summary>
public interface ISettingsContributor
{
    /// <summary>
    /// Returns settings page descriptors. These appear as additional
    /// categories in the Settings sidebar.
    /// </summary>
    IReadOnlyList<SettingsPage> GetSettingsPages();
}
