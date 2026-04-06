using Novalist.Sdk.Models;

namespace Novalist.Sdk.Hooks;

/// <summary>
/// Contributes custom entity types to the entity panel and editor.
/// </summary>
public interface IEntityTypeContributor
{
    /// <summary>
    /// Returns descriptors for custom entity types managed by this extension.
    /// </summary>
    IReadOnlyList<EntityTypeDescriptor> GetEntityTypes();
}
