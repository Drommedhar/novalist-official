using System.Collections.Generic;
using System.Linq;
using Novalist.Sdk.Models.Wizards;

namespace Novalist.Backend.Extensions;

/// <summary>
/// Flattens the SDK's polymorphic <see cref="WizardStep"/> hierarchy into a
/// single serializable DTO the renderer's wizard runner understands, and finds a
/// live step by id so the host can invoke its runtime-only callbacks
/// (validators, dynamic-choice providers) which cannot cross the RPC boundary.
/// </summary>
public static class WizardMapper
{
    public static WizardDefinitionDto ToDto(WizardDefinition def) => new(
        def.Id,
        def.DisplayName,
        def.Description,
        def.Scope.ToString(),
        def.EntityTypeKey,
        def.Steps.Select(ToDto).ToList());

    public static WizardStepDto ToDto(WizardStep step)
    {
        var condition = step.VisibleWhen is { } c
            ? new WizardConditionDto(c.StepId, c.Operator, c.Value)
            : null;

        return step switch
        {
            TextStep t => Base("text", step, condition) with
            {
                Multiline = t.Multiline,
                MaxLength = t.MaxLength,
                Placeholder = t.Placeholder,
                ExampleValue = t.ExampleValue,
            },
            ChoiceStep ch => Base("choice", step, condition) with
            {
                Choices = ch.Choices.Select(x => new WizardChoiceDto(x.Value, x.Label, x.Description)).ToList(),
                MultiSelect = ch.MultiSelect,
                HasDynamicChoices = ch.DynamicChoicesProvider != null,
                AutoSkipIfChoicesEmpty = ch.AutoSkipIfChoicesEmpty,
            },
            NumberStep n => Base("number", step, condition) with
            {
                Min = n.Min,
                Max = n.Max,
                DefaultNumber = n.DefaultValue,
                Unit = n.Unit,
            },
            DateStep d => Base("date", step, condition) with { AllowInWorld = d.AllowInWorld },
            EntityRefStep e => Base("entityRef", step, condition) with { TargetEntityTypeKey = e.TargetEntityTypeKey },
            EntityListStep el => Base("entityList", step, condition) with
            {
                TargetEntityTypeKey = el.TargetEntityTypeKey,
                MinCount = el.MinCount,
                MaxCount = el.MaxCount,
                SubSteps = el.SubSteps.Select(ToDto).ToList(),
            },
            CompoundStep cs => Base("compound", step, condition) with
            {
                SubSteps = cs.SubSteps.Select(ToDto).ToList(),
            },
            _ => Base("text", step, condition),
        };
    }

    private static WizardStepDto Base(string kind, WizardStep step, WizardConditionDto? condition) => new(
        Kind: kind,
        Id: step.Id,
        Title: step.Title,
        Help: step.Help,
        Skippable: step.Skippable,
        VisibleWhen: condition,
        HasValidator: step.Validator != null);

    /// <summary>Depth-first search for a step (including compound / entity-list
    /// sub-steps) by id.</summary>
    public static WizardStep? FindStep(IEnumerable<WizardStep> steps, string stepId)
    {
        foreach (var step in steps)
        {
            if (step.Id == stepId)
                return step;
            var children = step switch
            {
                CompoundStep cs => cs.SubSteps,
                EntityListStep el => el.SubSteps,
                _ => null,
            };
            if (children != null && FindStep(children, stepId) is { } found)
                return found;
        }
        return null;
    }
}

/// <summary>Serializable wizard definition sent to the renderer.</summary>
public sealed record WizardDefinitionDto(
    string Id,
    string DisplayName,
    string Description,
    string Scope,
    string? EntityTypeKey,
    IReadOnlyList<WizardStepDto> Steps);

/// <summary>Serializable, flattened wizard step. Only the fields relevant to the
/// step's <see cref="Kind"/> are populated.</summary>
public sealed record WizardStepDto(
    string Kind,
    string Id,
    string Title,
    string? Help,
    bool Skippable,
    WizardConditionDto? VisibleWhen,
    bool HasValidator,
    bool Multiline = false,
    int? MaxLength = null,
    string? Placeholder = null,
    string? ExampleValue = null,
    IReadOnlyList<WizardChoiceDto>? Choices = null,
    bool MultiSelect = false,
    bool HasDynamicChoices = false,
    bool AutoSkipIfChoicesEmpty = false,
    int? Min = null,
    int? Max = null,
    int DefaultNumber = 0,
    string? Unit = null,
    bool AllowInWorld = false,
    string? TargetEntityTypeKey = null,
    int? MinCount = null,
    int? MaxCount = null,
    IReadOnlyList<WizardStepDto>? SubSteps = null);

public sealed record WizardConditionDto(string StepId, string Operator, string? Value);

public sealed record WizardChoiceDto(string Value, string Label, string? Description);
