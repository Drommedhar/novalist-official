using Novalist.Backend.Extensions;
using Novalist.Sdk.Models.Wizards;
using Xunit;

namespace Novalist.Backend.Tests;

public class WizardMapperTests
{
    [Fact]
    public void ToDto_MapsDefinitionAndConditionAndFlags()
    {
        var def = new WizardDefinition
        {
            Id = "w", DisplayName = "Setup", Description = "desc",
            Scope = WizardScope.Reference, EntityTypeKey = "character",
            Steps =
            {
                new TextStep
                {
                    Id = "url", Title = "URL", Help = "help", Skippable = false,
                    Multiline = true, MaxLength = 10, Placeholder = "http://", ExampleValue = "ex",
                    VisibleWhen = new WizardCondition { StepId = "enabled", Operator = "equals", Value = "true" },
                    Validator = _ => Task.FromResult<string?>(null),
                },
            }
        };

        var dto = WizardMapper.ToDto(def);
        Assert.Equal("w", dto.Id);
        Assert.Equal("Reference", dto.Scope);
        Assert.Equal("character", dto.EntityTypeKey);
        var step = Assert.Single(dto.Steps);
        Assert.Equal("text", step.Kind);
        Assert.True(step.Multiline);
        Assert.Equal(10, step.MaxLength);
        Assert.True(step.HasValidator);
        Assert.False(step.Skippable);
        Assert.NotNull(step.VisibleWhen);
        Assert.Equal("enabled", step.VisibleWhen!.StepId);
    }

    [Fact]
    public void ToDto_MapsAllStepKinds()
    {
        Assert.Equal("choice", WizardMapper.ToDto(new ChoiceStep
        {
            Id = "c",
            Choices = { new WizardChoice { Value = "v", Label = "L", Description = "d" } },
            MultiSelect = true,
            AutoSkipIfChoicesEmpty = true,
            DynamicChoicesProvider = _ => Task.FromResult<IReadOnlyList<WizardChoice>>([]),
        }).Kind);

        var choice = WizardMapper.ToDto(new ChoiceStep { Id = "c", DynamicChoicesProvider = _ => Task.FromResult<IReadOnlyList<WizardChoice>>([]) });
        Assert.True(choice.HasDynamicChoices);

        var number = WizardMapper.ToDto(new NumberStep { Id = "n", Min = 1, Max = 9, DefaultValue = 3, Unit = "x" });
        Assert.Equal("number", number.Kind);
        Assert.Equal(3, number.DefaultNumber);

        Assert.Equal("date", WizardMapper.ToDto(new DateStep { Id = "d", AllowInWorld = true }).Kind);
        Assert.Equal("entityRef", WizardMapper.ToDto(new EntityRefStep { Id = "e", TargetEntityTypeKey = "character" }).Kind);

        var list = WizardMapper.ToDto(new EntityListStep
        {
            Id = "el", TargetEntityTypeKey = "location", MinCount = 1, MaxCount = 3,
            SubSteps = { new TextStep { Id = "sub" } }
        });
        Assert.Equal("entityList", list.Kind);
        Assert.Single(list.SubSteps!);

        var compound = WizardMapper.ToDto(new CompoundStep { Id = "cmp", SubSteps = { new TextStep { Id = "s2" } } });
        Assert.Equal("compound", compound.Kind);
        Assert.Single(compound.SubSteps!);
    }

    [Fact]
    public void ToDto_UnknownStepKind_FallsBackToText()
    {
        Assert.Equal("text", WizardMapper.ToDto(new CustomStep { Id = "x" }).Kind);
    }

    [Fact]
    public void FindStep_FindsTopLevelAndNested()
    {
        var steps = new List<WizardStep>
        {
            new TextStep { Id = "a" },
            new CompoundStep { Id = "b", SubSteps = { new TextStep { Id = "b1" } } },
            new EntityListStep { Id = "c", SubSteps = { new TextStep { Id = "c1" } } },
        };

        Assert.Equal("a", WizardMapper.FindStep(steps, "a")!.Id);
        Assert.Equal("b1", WizardMapper.FindStep(steps, "b1")!.Id);
        Assert.Equal("c1", WizardMapper.FindStep(steps, "c1")!.Id);
        Assert.Null(WizardMapper.FindStep(steps, "missing"));
    }

    private sealed class CustomStep : WizardStep;
}
