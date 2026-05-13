using Novalist.Core.Models;
using Novalist.Sdk.Models.Wizards;

namespace Novalist.Core.Wizards;

/// <summary>
/// Generic guided creation walker for entity types.
/// </summary>
public static class EntityGuidedWizard
{
    public static WizardDefinition BuildFor(EntityType type, CustomEntityTypeDefinition? customDef = null, Func<string, string>? loc = null)
    {
        return type switch
        {
            EntityType.Character => BuildForCharacter(loc),
            EntityType.Location => BuildForLocation(loc),
            EntityType.Item => BuildForItem(loc),
            EntityType.Lore => BuildForLore(loc),
            EntityType.Custom when customDef != null => BuildForCustom(customDef, loc),
            _ => BuildForCharacter(loc),
        };
    }

    public static string IdFor(EntityType type, CustomEntityTypeDefinition? customDef = null)
        => type switch
        {
            EntityType.Character => "entity.character.guided",
            EntityType.Location => "entity.location.guided",
            EntityType.Item => "entity.item.guided",
            EntityType.Lore => "entity.lore.guided",
            EntityType.Custom => $"entity.custom.{customDef?.TypeKey ?? "unknown"}.guided",
            _ => "entity.guided",
        };

    private static string T(Func<string, string>? loc, string key, string fallback)
        => loc?.Invoke(key) is { } v && v != key ? v : fallback;

    private static WizardDefinition BuildForCharacter(Func<string, string>? loc) => new()
    {
        Id = "entity.character.guided",
        DisplayName = T(loc, "wizard.entity.character.displayName", "Guided character creation"),
        Scope = WizardScope.Entity,
        EntityTypeKey = "character",
        Steps =
        [
            new TextStep { Id = "name", Title = T(loc, "wizard.entity.field.name", "Name"), Help = T(loc, "wizard.entity.character.nameHelp", "First name or sole name."), Skippable = false },
            new TextStep { Id = "surname", Title = T(loc, "wizard.entity.field.surname", "Surname"), Help = T(loc, "wizard.entity.character.surnameHelp", "Family name. Leave blank if not applicable.") },
            new TextStep { Id = "gender", Title = T(loc, "wizard.entity.field.gender", "Gender") },
            new TextStep { Id = "age", Title = T(loc, "wizard.entity.field.age", "Age"), Help = T(loc, "wizard.entity.character.ageHelp", "A literal value (e.g. \"23\") or descriptor (e.g. \"early 30s\"). Date-based ages can be set on the entity afterwards.") },
            new TextStep { Id = "role", Title = T(loc, "wizard.entity.field.role", "Role"), Help = T(loc, "wizard.entity.character.roleHelp", "Protagonist, antagonist, mentor, foil, …") },
            new TextStep { Id = "group", Title = T(loc, "wizard.entity.field.group", "Group"), Help = T(loc, "wizard.entity.character.groupHelp", "Family, organisation, faction — free text, used to cluster the cast.") },
            new TextStep { Id = "description", Title = T(loc, "wizard.entity.field.shortDescription", "Short description"), Multiline = true, Help = T(loc, "wizard.entity.character.descriptionHelp", "A one-paragraph thumbnail — surfaces in the focus peek.") },
        ],
    };

    private static WizardDefinition BuildForLocation(Func<string, string>? loc) => new()
    {
        Id = "entity.location.guided",
        DisplayName = T(loc, "wizard.entity.location.displayName", "Guided location creation"),
        Scope = WizardScope.Entity,
        EntityTypeKey = "location",
        Steps =
        [
            new TextStep { Id = "name", Title = T(loc, "wizard.entity.field.name", "Name"), Skippable = false },
            new TextStep { Id = "type", Title = T(loc, "wizard.entity.field.type", "Type"), Help = T(loc, "wizard.entity.location.typeHelp", "City, forest, building, continent, …") },
            new TextStep { Id = "parent", Title = T(loc, "wizard.entity.location.parent", "Parent location"), Help = T(loc, "wizard.entity.location.parentHelp", "If this place sits inside another (e.g. a city inside a country).") },
            new TextStep { Id = "description", Title = T(loc, "wizard.entity.field.description", "Description"), Multiline = true },
        ],
    };

    private static WizardDefinition BuildForItem(Func<string, string>? loc) => new()
    {
        Id = "entity.item.guided",
        DisplayName = T(loc, "wizard.entity.item.displayName", "Guided item creation"),
        Scope = WizardScope.Entity,
        EntityTypeKey = "item",
        Steps =
        [
            new TextStep { Id = "name", Title = T(loc, "wizard.entity.field.name", "Name"), Skippable = false },
            new TextStep { Id = "type", Title = T(loc, "wizard.entity.field.type", "Type"), Help = T(loc, "wizard.entity.item.typeHelp", "Weapon, artifact, vehicle, …") },
            new TextStep { Id = "origin", Title = T(loc, "wizard.entity.item.origin", "Origin"), Help = T(loc, "wizard.entity.item.originHelp", "Where the item came from in-world.") },
            new TextStep { Id = "description", Title = T(loc, "wizard.entity.field.description", "Description"), Multiline = true },
        ],
    };

    private static WizardDefinition BuildForLore(Func<string, string>? loc) => new()
    {
        Id = "entity.lore.guided",
        DisplayName = T(loc, "wizard.entity.lore.displayName", "Guided lore creation"),
        Scope = WizardScope.Entity,
        EntityTypeKey = "lore",
        Steps =
        [
            new TextStep { Id = "name", Title = T(loc, "wizard.entity.field.name", "Name"), Skippable = false },
            new ChoiceStep
            {
                Id = "category",
                Title = T(loc, "wizard.entity.lore.category", "Category"),
                Choices = LoreData.Categories
                    .Select(c => new WizardChoice { Value = c, Label = c })
                    .ToList(),
            },
            new TextStep { Id = "description", Title = T(loc, "wizard.entity.field.description", "Description"), Multiline = true },
        ],
    };

    private static WizardDefinition BuildForCustom(CustomEntityTypeDefinition def, Func<string, string>? loc)
    {
        var steps = new List<WizardStep>
        {
            new TextStep { Id = "name", Title = T(loc, "wizard.entity.field.name", "Name"), Skippable = false },
        };

        foreach (var field in def.DefaultFields)
        {
            steps.Add(BuildStepForField(field));
        }

        return new WizardDefinition
        {
            Id = $"entity.custom.{def.TypeKey}.guided",
            DisplayName = T(loc, $"wizard.entity.custom.{def.TypeKey}.displayName", $"Guided {def.DisplayName} creation"),
            Scope = WizardScope.Entity,
            EntityTypeKey = def.TypeKey,
            Steps = steps,
        };
    }

    private static WizardStep BuildStepForField(CustomEntityFieldDefinition field)
    {
        var title = field.DisplayName;
        return field.Type switch
        {
            CustomPropertyType.Int => new NumberStep
            {
                Id = field.Key,
                Title = title,
                DefaultValue = int.TryParse(field.DefaultValue, out var n) ? n : 0,
            },
            CustomPropertyType.Bool => new ChoiceStep
            {
                Id = field.Key,
                Title = title,
                Choices =
                [
                    new WizardChoice { Value = "true", Label = "Yes" },
                    new WizardChoice { Value = "false", Label = "No" },
                ],
            },
            CustomPropertyType.Enum => new ChoiceStep
            {
                Id = field.Key,
                Title = title,
                Choices = (field.EnumOptions ?? new List<string>())
                    .Select(v => new WizardChoice { Value = v, Label = v })
                    .ToList(),
            },
            CustomPropertyType.Date => new DateStep { Id = field.Key, Title = title },
            CustomPropertyType.EntityRef => new EntityRefStep
            {
                Id = field.Key,
                Title = title,
                TargetEntityTypeKey = field.TypeKey ?? string.Empty,
            },
            _ => new TextStep { Id = field.Key, Title = title, Multiline = false },
        };
    }
}
