
namespace Novalist.Sdk.Services;

/// <summary>One condition in a saved list.</summary>
public sealed class SmartListRuleInfo
{
    /// <summary>Case-sensitive attribute key, such as title or chapterStatus; prop: followed by a field name selects a custom scene property.</summary>
    public string Field { get; init; } = string.Empty;

    /// <summary>"Is", "Contains", "GreaterThan", "LessThan", "IsSet" or "IsNotSet".</summary>
    public string Op { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;
}

public sealed class SmartListInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    /// <summary>"All" when every rule must hold, "Any" when one is enough.</summary>
    public string Match { get; init; } = string.Empty;

    public IReadOnlyList<SmartListRuleInfo> Rules { get; init; } = [];
}
