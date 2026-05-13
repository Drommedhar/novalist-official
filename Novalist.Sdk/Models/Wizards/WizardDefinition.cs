using System.Text.Json.Serialization;

namespace Novalist.Sdk.Models.Wizards;

public enum WizardScope
{
    Project,
    Entity,
    Reference,
}

/// <summary>
/// A wizard is an ordered list of <see cref="WizardStep"/>s with optional
/// branching. The runner walks the user through it; the result is mapped into a
/// real domain object by a scope-specific mapper.
/// </summary>
public sealed class WizardDefinition
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public WizardScope Scope { get; set; }
    public string? EntityTypeKey { get; set; }
    public List<WizardStep> Steps { get; set; } = [];
}

/// <summary>Condition gating step visibility against the answer map. </summary>
public sealed class WizardCondition
{
    public string StepId { get; set; } = string.Empty;
    public string Operator { get; set; } = "equals"; // equals|notEquals|contains|present
    public string? Value { get; set; }
}

public abstract class WizardStep
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Help { get; set; }
    public bool Skippable { get; set; } = true;
    public WizardCondition? VisibleWhen { get; set; }

    /// <summary>
    /// Optional async validation callback fired before advancing past this
    /// step. Receives the current <see cref="WizardResult"/> and returns
    /// either <c>null</c> on success or an error message that the dialog
    /// surfaces inline; advance is blocked when a message is returned.
    /// Runtime-only, not serialized.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Func<WizardResult, Task<string?>>? Validator { get; set; }
}

public sealed class TextStep : WizardStep
{
    public bool Multiline { get; set; }
    public int? MaxLength { get; set; }
    public string? Placeholder { get; set; }
    public string? ExampleValue { get; set; }
}

public sealed class ChoiceStep : WizardStep
{
    public List<WizardChoice> Choices { get; set; } = [];
    public bool MultiSelect { get; set; }

    /// <summary>
    /// Optional async provider invoked when the step is entered. Replaces
    /// <see cref="Choices"/> with whatever the provider returns. Runtime-only.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Func<WizardResult, Task<IReadOnlyList<WizardChoice>>>? DynamicChoicesProvider { get; set; }

    /// <summary>
    /// When <c>true</c> and <see cref="DynamicChoicesProvider"/> returns an
    /// empty list, the host auto-advances past this step instead of showing
    /// an empty radio group.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool AutoSkipIfChoicesEmpty { get; set; }
}

public sealed class WizardChoice
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public sealed class NumberStep : WizardStep
{
    public int? Min { get; set; }
    public int? Max { get; set; }
    public int DefaultValue { get; set; }
    public string? Unit { get; set; }
}

public sealed class DateStep : WizardStep
{
    public bool AllowInWorld { get; set; }
}

public sealed class EntityRefStep : WizardStep
{
    public string TargetEntityTypeKey { get; set; } = string.Empty;
}

public sealed class EntityListStep : WizardStep
{
    public string TargetEntityTypeKey { get; set; } = string.Empty;
    public int? MinCount { get; set; }
    public int? MaxCount { get; set; }
    /// <summary>Sub-prompts asked for each entry the user adds.</summary>
    public List<WizardStep> SubSteps { get; set; } = [];
}

public sealed class CompoundStep : WizardStep
{
    public List<WizardStep> SubSteps { get; set; } = [];
}
