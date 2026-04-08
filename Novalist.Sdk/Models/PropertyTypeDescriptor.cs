using Avalonia.Controls;

namespace Novalist.Sdk.Models;

/// <summary>
/// Describes a custom property type contributed by an extension.
/// Custom property types extend the built-in set (String, Int, Bool, Date, Enum, Timespan)
/// and can be used in templates for both built-in and custom entity types.
/// </summary>
public sealed class PropertyTypeDescriptor
{
    /// <summary>Unique key for this property type (e.g. "color", "rating", "url").</summary>
    public string TypeKey { get; init; } = string.Empty;

    /// <summary>Display name shown in template editor dropdowns.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Factory that creates an Avalonia editor control for this property type.
    /// Receives the current string value and a callback to invoke when the value changes.
    /// </summary>
    public Func<string, Action<string>, Control>? CreateEditor { get; init; }

    /// <summary>
    /// Validates a string value for this property type.
    /// Returns null if valid, or an error message string.
    /// </summary>
    public Func<string, string?>? Validate { get; init; }

    /// <summary>Default value for new properties of this type.</summary>
    public string DefaultValue { get; init; } = string.Empty;
}
