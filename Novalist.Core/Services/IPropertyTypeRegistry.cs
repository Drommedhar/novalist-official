namespace Novalist.Core.Services;

/// <summary>
/// Registry for built-in and extension-contributed property types.
/// </summary>
public interface IPropertyTypeRegistry
{
    /// <summary>
    /// Returns all known property type keys (built-in + extension-contributed).
    /// </summary>
    IReadOnlyList<string> GetAllTypeKeys();

    /// <summary>
    /// Returns display names for all property types (index-matched with <see cref="GetAllTypeKeys"/>).
    /// </summary>
    IReadOnlyList<string> GetAllDisplayNames();

    /// <summary>
    /// Returns true if the given type key is a known built-in or extension type.
    /// </summary>
    bool IsKnownType(string typeKey);
}
