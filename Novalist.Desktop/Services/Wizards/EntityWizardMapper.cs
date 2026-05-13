using System;
using Novalist.Core.Models;
using Novalist.Sdk.Models.Wizards;

namespace Novalist.Desktop.Services.Wizards;

/// <summary>
/// Pours generic entity-wizard answers into a fresh entity record.
/// </summary>
public static class EntityWizardMapper
{
    public static CharacterData BuildCharacter(WizardResult result)
    {
        var c = new CharacterData
        {
            Name = result.GetText("name").Trim(),
            Surname = result.GetText("surname").Trim(),
            Gender = result.GetText("gender").Trim(),
            Age = result.GetText("age").Trim(),
            Role = result.GetText("role").Trim(),
            Group = result.GetText("group").Trim(),
        };
        var desc = result.GetText("description").Trim();
        if (!string.IsNullOrWhiteSpace(desc))
            c.Sections.Add(new EntitySection { Title = "Description", Content = desc });
        return c;
    }

    public static LocationData BuildLocation(WizardResult result)
    {
        return new LocationData
        {
            Name = result.GetText("name").Trim(),
            Type = result.GetText("type").Trim(),
            Parent = result.GetText("parent").Trim(),
            Description = result.GetText("description").Trim(),
        };
    }

    public static ItemData BuildItem(WizardResult result)
    {
        return new ItemData
        {
            Name = result.GetText("name").Trim(),
            Type = result.GetText("type").Trim(),
            Origin = result.GetText("origin").Trim(),
            Description = result.GetText("description").Trim(),
        };
    }

    public static LoreData BuildLore(WizardResult result)
    {
        return new LoreData
        {
            Name = result.GetText("name").Trim(),
            Category = string.IsNullOrWhiteSpace(result.GetText("category")) ? "Other" : result.GetText("category"),
            Description = result.GetText("description").Trim(),
        };
    }

    public static CustomEntityData BuildCustomEntity(WizardResult result, CustomEntityTypeDefinition def)
    {
        var entity = new CustomEntityData
        {
            EntityTypeKey = def.TypeKey,
            Name = result.GetText("name").Trim(),
        };
        foreach (var field in def.DefaultFields)
        {
            if (result.Answers.TryGetValue(field.Key, out var v) && !v.IsEmpty)
            {
                entity.Fields[field.Key] = v.Text ?? (v.Number?.ToString() ?? string.Empty);
            }
        }
        return entity;
    }
}
