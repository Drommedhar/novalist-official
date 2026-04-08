namespace Novalist.Core.Models;

/// <summary>
/// String constants for the built-in property types.
/// Used for the string-based type system that extends the <see cref="CustomPropertyType"/> enum.
/// </summary>
public static class WellKnownPropertyTypes
{
    public const string String = "String";
    public const string Int = "Int";
    public const string Bool = "Bool";
    public const string Date = "Date";
    public const string Enum = "Enum";
    public const string Timespan = "Timespan";
    public const string EntityRef = "EntityRef";

    public static readonly string[] All = [String, Int, Bool, Date, Enum, Timespan, EntityRef];

    /// <summary>
    /// Resolves a <see cref="CustomPropertyType"/> enum to its string key.
    /// </summary>
    public static string FromEnum(CustomPropertyType type) => type.ToString();

    /// <summary>
    /// Tries to parse a string key to a <see cref="CustomPropertyType"/> enum.
    /// Returns false for extension-provided type keys.
    /// </summary>
    public static bool TryToEnum(string typeKey, out CustomPropertyType type)
        => System.Enum.TryParse(typeKey, ignoreCase: true, out type);
}
