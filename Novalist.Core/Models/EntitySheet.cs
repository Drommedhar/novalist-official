using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// How one entry type's sheet is laid out in this project.
///
/// The built-in field sets are fixed and were always all shown, so a project
/// that never records eye colour carried the field on every character for
/// ever. Hiding one keeps its value - the field is not deleted, it is just not
/// in the way - because a hidden field that threw its contents away would be a
/// trap rather than a preference.
/// </summary>
public sealed class EntitySheet
{
    /// <summary>Which entry type this describes: character, location, or a custom key.</summary>
    [JsonPropertyName("typeKey")]
    public string TypeKey { get; set; } = string.Empty;

    /// <summary>Field keys this project does not want to see. Values are kept.</summary>
    [JsonPropertyName("hidden")]
    public List<string> Hidden { get; set; } = [];

    /// <summary>
    /// Field keys in the order the sheet shows them. A field missing from the
    /// list keeps its natural place after the ordered ones, so adding a field
    /// to Novalist later does not make it invisible in an existing project.
    /// </summary>
    [JsonPropertyName("order")]
    public List<string> Order { get; set; } = [];
}
