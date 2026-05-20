using Novalist.Core.Models;
using Novalist.Core.Wizards;
using Novalist.Sdk.Models.Wizards;
using Xunit;

namespace Novalist.Core.Tests.Wizards;

public class CharacterInterviewWizardTests
{
    [Fact]
    public void Build_NullLoc_UsesFallbacks()
    {
        var def = CharacterInterviewWizard.Build();
        Assert.Equal(CharacterInterviewWizard.Id, def.Id);
        Assert.Equal("Character interview", def.DisplayName);
        Assert.Equal(WizardScope.Entity, def.Scope);
        Assert.Equal("character", def.EntityTypeKey);
        Assert.Equal(8, def.Steps.Count);
    }

    [Fact]
    public void Build_LocResolvesAndFallsBack()
    {
        // Returns a translation for one key, echoes the key (not found) for others.
        Func<string, string> loc = key => key == "wizard.interview.displayName" ? "Interview!" : key;
        var def = CharacterInterviewWizard.Build(loc);
        Assert.Equal("Interview!", def.DisplayName);                 // translated
        Assert.Equal("Walks the seven psychology pillars: wound, fear, lie, want, need, secret, voice.", def.Description); // key echoed -> fallback
    }
}

public class ProjectSnowflakeWizardTests
{
    [Fact]
    public void Build_NullLoc_HasExpectedShape()
    {
        var def = ProjectSnowflakeWizard.Build();
        Assert.Equal(ProjectSnowflakeWizard.Id, def.Id);
        Assert.Equal(WizardScope.Project, def.Scope);
        Assert.Contains(def.Steps, s => s is NumberStep);
        Assert.Contains(def.Steps, s => s is EntityListStep);
    }

    [Fact]
    public void Build_WithLoc_Translates()
    {
        Func<string, string> loc = key => key == "wizard.project.displayName" ? "Snowflake!" : key;
        Assert.Equal("Snowflake!", ProjectSnowflakeWizard.Build(loc).DisplayName);
    }
}

public class EntityGuidedWizardTests
{
    [Theory]
    [InlineData(EntityType.Character, "entity.character.guided", "character")]
    [InlineData(EntityType.Location, "entity.location.guided", "location")]
    [InlineData(EntityType.Item, "entity.item.guided", "item")]
    [InlineData(EntityType.Lore, "entity.lore.guided", "lore")]
    public void BuildFor_BuiltInTypes(EntityType type, string expectedId, string expectedKey)
    {
        var def = EntityGuidedWizard.BuildFor(type);
        Assert.Equal(expectedId, def.Id);
        Assert.Equal(expectedKey, def.EntityTypeKey);
    }

    [Fact]
    public void BuildFor_CustomWithDefinition()
    {
        var custom = new CustomEntityTypeDefinition { TypeKey = "faction", DisplayName = "Faction" };
        var def = EntityGuidedWizard.BuildFor(EntityType.Custom, custom);
        Assert.Equal("entity.custom.faction.guided", def.Id);
        Assert.Equal("faction", def.EntityTypeKey);
    }

    [Fact]
    public void BuildFor_CustomWithoutDefinition_FallsBackToCharacter()
    {
        var def = EntityGuidedWizard.BuildFor(EntityType.Custom, null);
        Assert.Equal("entity.character.guided", def.Id);
    }

    [Fact]
    public void BuildFor_UndefinedEnum_FallsBackToCharacter()
    {
        var def = EntityGuidedWizard.BuildFor((EntityType)999);
        Assert.Equal("entity.character.guided", def.Id);
    }

    [Fact]
    public void Build_WithLoc_Translates()
    {
        Func<string, string> loc = key => key == "wizard.entity.character.displayName" ? "X" : key;
        Assert.Equal("X", EntityGuidedWizard.BuildFor(EntityType.Character, loc: loc).DisplayName);
    }

    [Theory]
    [InlineData(EntityType.Character, "entity.character.guided")]
    [InlineData(EntityType.Location, "entity.location.guided")]
    [InlineData(EntityType.Item, "entity.item.guided")]
    [InlineData(EntityType.Lore, "entity.lore.guided")]
    public void IdFor_BuiltIns(EntityType type, string expected)
        => Assert.Equal(expected, EntityGuidedWizard.IdFor(type));

    [Fact]
    public void IdFor_Custom_WithAndWithoutDef()
    {
        Assert.Equal("entity.custom.faction.guided",
            EntityGuidedWizard.IdFor(EntityType.Custom, new CustomEntityTypeDefinition { TypeKey = "faction" }));
        Assert.Equal("entity.custom.unknown.guided", EntityGuidedWizard.IdFor(EntityType.Custom, null));
    }

    [Fact]
    public void IdFor_UndefinedEnum_ReturnsGeneric()
        => Assert.Equal("entity.guided", EntityGuidedWizard.IdFor((EntityType)999));

    [Fact]
    public void BuildForCustom_MapsEachFieldType()
    {
        var def = new CustomEntityTypeDefinition
        {
            TypeKey = "thing",
            DisplayName = "Thing",
            DefaultFields =
            {
                new CustomEntityFieldDefinition { Key = "count", DisplayName = "Count", Type = CustomPropertyType.Int, DefaultValue = "5" },
                new CustomEntityFieldDefinition { Key = "bad", DisplayName = "Bad", Type = CustomPropertyType.Int, DefaultValue = "xx" },
                new CustomEntityFieldDefinition { Key = "flag", DisplayName = "Flag", Type = CustomPropertyType.Bool },
                new CustomEntityFieldDefinition { Key = "kind", DisplayName = "Kind", Type = CustomPropertyType.Enum, EnumOptions = new() { "a", "b" } },
                new CustomEntityFieldDefinition { Key = "kind2", DisplayName = "Kind2", Type = CustomPropertyType.Enum, EnumOptions = null },
                new CustomEntityFieldDefinition { Key = "when", DisplayName = "When", Type = CustomPropertyType.Date },
                new CustomEntityFieldDefinition { Key = "ref", DisplayName = "Ref", Type = CustomPropertyType.EntityRef, TypeKey = "character" },
                new CustomEntityFieldDefinition { Key = "note", DisplayName = "Note", Type = CustomPropertyType.String },
            }
        };

        var wizard = EntityGuidedWizard.BuildFor(EntityType.Custom, def);
        var steps = wizard.Steps;

        Assert.IsType<NumberStep>(steps[1]);
        Assert.Equal(5, ((NumberStep)steps[1]).DefaultValue);
        Assert.Equal(0, ((NumberStep)steps[2]).DefaultValue);     // unparseable -> 0
        Assert.IsType<ChoiceStep>(steps[3]);                       // bool
        Assert.Equal(2, ((ChoiceStep)steps[4]).Choices.Count);     // enum with options
        Assert.Empty(((ChoiceStep)steps[5]).Choices);              // enum, null options
        Assert.IsType<DateStep>(steps[6]);
        Assert.IsType<EntityRefStep>(steps[7]);
        Assert.Equal("character", ((EntityRefStep)steps[7]).TargetEntityTypeKey);
        Assert.IsType<TextStep>(steps[8]);                         // string/default
    }

    [Fact]
    public void BuildForLore_UsesCategoryChoices()
    {
        var def = EntityGuidedWizard.BuildFor(EntityType.Lore);
        var choice = Assert.IsType<ChoiceStep>(def.Steps[1]);
        Assert.NotEmpty(choice.Choices);
    }
}
