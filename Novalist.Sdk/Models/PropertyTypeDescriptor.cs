namespace Novalist.Sdk.Models;

/// <summary>
/// Describes a custom property type contributed by an extension. Extends the
/// built-in set (String, Int, Bool, Date, Enum, Timespan) for use in templates.
/// </summary>
public sealed class PropertyTypeDescriptor
{
    /// <summary>Unique key for this property type (e.g. "color", "rating", "url").</summary>
    public string TypeKey { get; init; } = string.Empty;

    /// <summary>Display name shown in template editor dropdowns.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Validates a string value for this property type.
    /// Returns null if valid, or an error message string.
    /// </summary>
    public Func<string, string?>? Validate { get; init; }

    /// <summary>Default value for new properties of this type.</summary>
    public string DefaultValue { get; init; } = string.Empty;
}
