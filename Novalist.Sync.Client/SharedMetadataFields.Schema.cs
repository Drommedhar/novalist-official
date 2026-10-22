using System.Text.Json.Nodes;

namespace Novalist.Sync.Client;

public static partial class SharedMetadataFields
{
    private static readonly IReadOnlyDictionary<string, string> Codex = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["aliases"] = "strings", ["relationships"] = "relationships", ["tags"] = "strings",
        ["group"] = "text", ["ai"] = "aiInclusion", ["readerHidden"] = "boolean", ["locked"] = "boolean", ["isWorld"] = "boolean",
        ["match.caseSensitive"] = "boolean", ["match.matchPlurals"] = "boolean", ["match.exclusions"] = "strings", ["match.ignoredSceneIds"] = "strings"
    };
    private static readonly string[] OverrideText = ["name", "surname", "gender", "age", "role", "eyeColor", "hairColor", "hairLength", "height", "build", "skinTone"];
    private static readonly IReadOnlyDictionary<string, string> Scene = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["title"] = "text", ["date"] = "text", ["dateRange"] = "dateRange", ["relativeTime"] = "relativeTime",
        ["narrativeMode"] = "text", ["strand"] = "text", ["stage"] = "text", ["label"] = "text",
        ["wordTarget"] = "integer", ["beat"] = "text", ["excludeFromExport"] = "boolean", ["isFavorite"] = "boolean",
        ["inactive"] = "boolean", ["cast"] = "strings", ["focusEntityId"] = "text",
        ["analysisOverrides.pov"] = "text", ["analysisOverrides.emotion"] = "text", ["analysisOverrides.intensity"] = "intensity",
        ["analysisOverrides.conflict"] = "text", ["analysisOverrides.tags"] = "strings"
    };
    private static readonly IReadOnlyDictionary<string, string> Research = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["title"] = "text", ["type"] = "researchType", ["tags"] = "strings", ["status"] = "researchStatus", ["rating"] = "rating",
        ["entityRefs"] = "strings", ["relatedIds"] = "strings", ["content"] = "text"
    };

    private static string? ValueKind(SharedFieldBinding binding) => binding.Metadata?.Kind switch
    {
        "codex" or "override" when binding.Key.StartsWith("customProperties:", StringComparison.Ordinal) && binding.Key.Length > 17 => "text",
        "scene" or "research" when binding.Key.StartsWith("properties:", StringComparison.Ordinal) && binding.Key.Length > 11 => "text",
        "codex" => Codex.GetValueOrDefault(binding.Key),
        "override" => OverrideText.Contains(binding.Key, StringComparer.Ordinal) ? "text" : Codex.GetValueOrDefault(binding.Key),
        "section" => binding.Key == "title" ? "text" : binding.Key is "aiHidden" or "readerHidden" ? "boolean" : null,
        "state" => binding.Key is "act" or "chapter" or "scene" or "name" ? "text" : binding.Key == "gone" ? "boolean" : null,
        "image" => binding.Key is "name" or "alt" ? "text" : null,
        "attachment" => binding.Key == "name" ? "text" : null,
        "scene" => Scene.GetValueOrDefault(binding.Key),
        "research" => Research.GetValueOrDefault(binding.Key),
        _ => null
    };

    private static bool ValidValue(string kind, JsonNode value) => kind switch
    {
        "text" => IsText(value),
        "strings" => value is JsonArray strings && strings.All(IsText),
        "relationships" => value is JsonArray rows && rows.All(IsRelationship),
        "integer" => IsInteger(value, 0, int.MaxValue),
        "intensity" => IsInteger(value, -10, 10),
        "rating" => IsInteger(value, 0, 5),
        "boolean" => value is JsonValue boolean && boolean.TryGetValue<bool>(out _),
        "researchType" => IsChoice(value, ["Note", "Link", "File", "Image", "Pdf", "Audio", "Video"]),
        "researchStatus" => IsChoice(value, ["None", "Open", "InProgress", "Resolved"]),
        "aiInclusion" => IsInteger(value, 0, 2),
        "relativeTime" => value is JsonObject relative && relative.Count == 2 && IsInteger(relative["amount"], int.MinValue, int.MaxValue) &&
            IsChoice(relative["unit"], ["Minutes", "Hours", "Days", "Weeks"]),
        "dateRange" => value is JsonObject range && range.All(property => property.Key is "start" or "end" or "note" or "startTime" or "endTime" && (property.Value is null || IsText(property.Value))),
        _ => false
    };

    private static bool IsInteger(JsonNode? value, int minimum, int maximum) => value is JsonValue scalar && scalar.TryGetValue<int>(out var number) && number >= minimum && number <= maximum;
    private static bool IsChoice(JsonNode? value, string[] choices) => value is JsonValue scalar && scalar.TryGetValue<string>(out var text) && choices.Contains(text, StringComparer.Ordinal);
}
