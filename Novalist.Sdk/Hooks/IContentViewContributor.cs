using Novalist.Sdk.Models;

namespace Novalist.Sdk.Hooks;

/// <summary>
/// Contributes full content area views (like Dashboard, Timeline, etc.).
/// </summary>
public interface IContentViewContributor
{
    /// <summary>
    /// Returns content view descriptors for full-area panels.
    /// </summary>
    IReadOnlyList<ContentViewDescriptor> GetContentViews();
}
