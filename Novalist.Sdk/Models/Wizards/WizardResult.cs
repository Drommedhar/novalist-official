using System.Text.Json.Serialization;

namespace Novalist.Sdk.Models.Wizards;

/// <summary>
/// Collected answers from a wizard run, keyed by step id.
/// </summary>
public sealed class WizardResult
{
    [JsonPropertyName("definitionId")]
    public string DefinitionId { get; set; } = string.Empty;

    [JsonPropertyName("answers")]
    public Dictionary<string, WizardAnswer> Answers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("currentStepIndex")]
    public int CurrentStepIndex { get; set; }

    [JsonPropertyName("completed")]
    public bool Completed { get; set; }

    public string GetText(string stepId)
        => Answers.TryGetValue(stepId, out var v) ? v.Text ?? string.Empty : string.Empty;

    public int GetNumber(string stepId, int fallback = 0)
        => Answers.TryGetValue(stepId, out var v) && v.Number.HasValue ? v.Number.Value : fallback;

    public List<string> GetMulti(string stepId)
        => Answers.TryGetValue(stepId, out var v) ? v.Multi ?? new List<string>() : new List<string>();

    public List<Dictionary<string, WizardAnswer>> GetList(string stepId)
        => Answers.TryGetValue(stepId, out var v) ? v.List ?? new List<Dictionary<string, WizardAnswer>>() : new List<Dictionary<string, WizardAnswer>>();
}

public sealed class WizardAnswer
{
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("number")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Number { get; set; }

    [JsonPropertyName("multi")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Multi { get; set; }

    [JsonPropertyName("list")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<Dictionary<string, WizardAnswer>>? List { get; set; }

    [JsonIgnore]
    public bool IsEmpty
        => string.IsNullOrWhiteSpace(Text)
           && Number == null
           && (Multi == null || Multi.Count == 0)
           && (List == null || List.Count == 0);
}
