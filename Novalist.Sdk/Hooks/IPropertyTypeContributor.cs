using Novalist.Sdk.Models;

namespace Novalist.Sdk.Hooks;

/// <summary>
/// Contributes custom property types that can be used in entity templates.
/// Extensions implement this to register new property types (e.g. Color, Rating, Url)
/// that appear alongside the built-in types (String, Int, Bool, Date, Enum, Timespan).
/// </summary>
public interface IPropertyTypeContributor
{
    /// <summary>
    /// Returns descriptors for custom property types managed by this extension.
    /// </summary>
    IReadOnlyList<PropertyTypeDescriptor> GetPropertyTypes();
}
