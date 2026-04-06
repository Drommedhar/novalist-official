using Novalist.Sdk.Models;

namespace Novalist.Sdk.Hooks;

/// <summary>
/// Contributes theme/style overrides to the application.
/// </summary>
public interface IThemeContributor
{
    /// <summary>
    /// Returns theme overrides to merge into the application styles.
    /// </summary>
    IReadOnlyList<ThemeOverride> GetThemeOverrides();
}
