using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// Supported data types for typed custom properties on entity templates.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CustomPropertyType
{
    String,
    Int,
    Bool,
    Date,
    Enum,
    Timespan
}

/// <summary>
/// Time interval unit for timespan and date-based age computation.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IntervalUnit
{
    Years,
    Months,
    Days
}

/// <summary>
/// Schema definition for one custom property on a template.
/// </summary>
public class CustomPropertyDefinition
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public CustomPropertyType Type { get; set; } = CustomPropertyType.String;

    [JsonPropertyName("defaultValue")]
    public string DefaultValue { get; set; } = string.Empty;

    [JsonPropertyName("enumOptions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? EnumOptions { get; set; }

    [JsonPropertyName("intervalUnit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IntervalUnit? IntervalUnit { get; set; }
}

/// <summary>
/// A named field with a default value, used for known entity fields in templates.
/// </summary>
public class TemplateField
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("defaultValue")]
    public string DefaultValue { get; set; } = string.Empty;
}

/// <summary>
/// A named section with default content for templates.
/// </summary>
public class TemplateSection
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("defaultContent")]
    public string DefaultContent { get; set; } = string.Empty;
}

/// <summary>
/// Template for character entities.
/// </summary>
public class CharacterTemplate
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("builtIn")]
    public bool BuiltIn { get; set; }

    [JsonPropertyName("fields")]
    public List<TemplateField> Fields { get; set; } = [];

    [JsonPropertyName("customPropertyDefs")]
    public List<CustomPropertyDefinition> CustomPropertyDefs { get; set; } = [];

    [JsonPropertyName("sections")]
    public List<TemplateSection> Sections { get; set; } = [];

    [JsonPropertyName("includeRelationships")]
    public bool IncludeRelationships { get; set; } = true;

    [JsonPropertyName("includeImages")]
    public bool IncludeImages { get; set; } = true;

    [JsonPropertyName("includeChapterOverrides")]
    public bool IncludeChapterOverrides { get; set; } = true;

    /// <summary>
    /// Whether the Age field uses a plain number or a date (birthdate) with computed interval.
    /// </summary>
    [JsonPropertyName("ageMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgeMode { get; set; }

    /// <summary>
    /// Interval unit when AgeMode is "date" (Years, Months, Days).
    /// </summary>
    [JsonPropertyName("ageIntervalUnit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IntervalUnit? AgeIntervalUnit { get; set; }

    public override string ToString() => Name;
}

/// <summary>
/// Template for location entities.
/// </summary>
public class LocationTemplate
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("builtIn")]
    public bool BuiltIn { get; set; }

    [JsonPropertyName("fields")]
    public List<TemplateField> Fields { get; set; } = [];

    [JsonPropertyName("customPropertyDefs")]
    public List<CustomPropertyDefinition> CustomPropertyDefs { get; set; } = [];

    [JsonPropertyName("sections")]
    public List<TemplateSection> Sections { get; set; } = [];

    [JsonPropertyName("includeImages")]
    public bool IncludeImages { get; set; } = true;

    public override string ToString() => Name;
}

/// <summary>
/// Template for item entities.
/// </summary>
public class ItemTemplate
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("builtIn")]
    public bool BuiltIn { get; set; }

    [JsonPropertyName("fields")]
    public List<TemplateField> Fields { get; set; } = [];

    [JsonPropertyName("customPropertyDefs")]
    public List<CustomPropertyDefinition> CustomPropertyDefs { get; set; } = [];

    [JsonPropertyName("sections")]
    public List<TemplateSection> Sections { get; set; } = [];

    [JsonPropertyName("includeImages")]
    public bool IncludeImages { get; set; } = true;

    public override string ToString() => Name;
}

/// <summary>
/// Template for lore entities.
/// </summary>
public class LoreTemplate
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("builtIn")]
    public bool BuiltIn { get; set; }

    [JsonPropertyName("fields")]
    public List<TemplateField> Fields { get; set; } = [];

    [JsonPropertyName("customPropertyDefs")]
    public List<CustomPropertyDefinition> CustomPropertyDefs { get; set; } = [];

    [JsonPropertyName("sections")]
    public List<TemplateSection> Sections { get; set; } = [];

    [JsonPropertyName("includeImages")]
    public bool IncludeImages { get; set; } = true;

    public override string ToString() => Name;
}

/// <summary>
/// Known built-in fields for each entity type used in the template editor.
/// </summary>
public static class TemplateKnownFields
{
    public static readonly string[] Character =
    [
        "Gender", "Age", "Role",
        "EyeColor", "HairColor", "HairLength",
        "Height", "Build", "SkinTone", "DistinguishingFeatures"
    ];

    public static readonly string[] Location = ["Type", "Description"];
    public static readonly string[] Item = ["Type", "Description", "Origin"];
    public static readonly string[] Lore = ["Category", "Description"];
}
