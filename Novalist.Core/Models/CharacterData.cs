using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

public class CharacterData : IEntityData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonIgnore]
    public bool IsWorldBible { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("surname")]
    public string Surname { get; set; } = string.Empty;

    [JsonPropertyName("aliases")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string> Aliases { get; set; } = [];

    [JsonPropertyName("gender")]
    public string Gender { get; set; } = string.Empty;

    [JsonPropertyName("age")]
    public string Age { get; set; } = string.Empty;

    [JsonPropertyName("birthDate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BirthDate { get; set; }

    [JsonPropertyName("ageMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgeMode { get; set; }

    [JsonPropertyName("ageIntervalUnit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IntervalUnit? AgeIntervalUnit { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("group")]
    public string Group { get; set; } = string.Empty;

    [JsonPropertyName("eyeColor")]
    public string EyeColor { get; set; } = string.Empty;

    [JsonPropertyName("hairColor")]
    public string HairColor { get; set; } = string.Empty;

    [JsonPropertyName("hairLength")]
    public string HairLength { get; set; } = string.Empty;

    [JsonPropertyName("height")]
    public string Height { get; set; } = string.Empty;

    [JsonPropertyName("build")]
    public string Build { get; set; } = string.Empty;

    [JsonPropertyName("skinTone")]
    public string SkinTone { get; set; } = string.Empty;

    [JsonPropertyName("distinguishingFeatures")]
    public string DistinguishingFeatures { get; set; } = string.Empty;

    [JsonPropertyName("images")]
    public List<EntityImage> Images { get; set; } = [];

    [JsonPropertyName("relationships")]
    public List<EntityRelationship> Relationships { get; set; } = [];

    [JsonPropertyName("customProperties")]
    public Dictionary<string, string> CustomProperties { get; set; } = [];

    [JsonPropertyName("sections")]
    public List<EntitySection> Sections { get; set; } = [];

    [JsonPropertyName("chapterOverrides")]
    public List<CharacterOverride> ChapterOverrides { get; set; } = [];

    [JsonPropertyName("templateId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TemplateId { get; set; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Surname) ? Name : $"{Name} {Surname}";
}

public class CharacterOverride
{
    [JsonPropertyName("act")]
    public string? Act { get; set; }

    [JsonPropertyName("chapter")]
    public string Chapter { get; set; } = string.Empty;

    [JsonPropertyName("scene")]
    public string? Scene { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("surname")]
    public string? Surname { get; set; }

    [JsonPropertyName("aliases")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Aliases { get; set; }

    [JsonPropertyName("gender")]
    public string? Gender { get; set; }

    [JsonPropertyName("age")]
    public string? Age { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("eyeColor")]
    public string? EyeColor { get; set; }

    [JsonPropertyName("hairColor")]
    public string? HairColor { get; set; }

    [JsonPropertyName("hairLength")]
    public string? HairLength { get; set; }

    [JsonPropertyName("height")]
    public string? Height { get; set; }

    [JsonPropertyName("build")]
    public string? Build { get; set; }

    [JsonPropertyName("skinTone")]
    public string? SkinTone { get; set; }

    [JsonPropertyName("distinguishingFeatures")]
    public string? DistinguishingFeatures { get; set; }

    [JsonPropertyName("images")]
    public List<EntityImage>? Images { get; set; }

    [JsonPropertyName("relationships")]
    public List<EntityRelationship>? Relationships { get; set; }

    [JsonPropertyName("customProperties")]
    public Dictionary<string, string>? CustomProperties { get; set; }

    [JsonPropertyName("sections")]
    public List<EntitySection>? Sections { get; set; }

    [JsonIgnore]
    public string ScopeLabel
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(Act)) parts.Add($"Act: {Act}");
            if (!string.IsNullOrEmpty(Chapter)) parts.Add($"Ch: {Chapter}");
            if (!string.IsNullOrEmpty(Scene)) parts.Add($"Sc: {Scene}");
            return string.Join(" → ", parts);
        }
    }
}
