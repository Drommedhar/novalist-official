using System.Linq;
using Novalist.Core.Models;
using Novalist.Sdk.Models.Wizards;

namespace Novalist.Desktop.Services.Wizards;

/// <summary>
/// Pours the character-interview wizard's seven psychology answers into the
/// character's Sections. Existing sections with the same title are replaced;
/// new sections are appended.
/// </summary>
public static class CharacterInterviewMapper
{
    private static readonly (string StepId, string SectionTitle)[] Mapping =
    [
        ("wound", "Wound"),
        ("fear", "Fear"),
        ("lie", "Lie they believe"),
        ("want", "Want"),
        ("need", "Need"),
        ("secret", "Secret"),
        ("voice", "Voice"),
    ];

    public static void Apply(CharacterData character, WizardResult result, bool overwriteName = false)
    {
        var name = result.GetText("name");
        if (overwriteName && !string.IsNullOrWhiteSpace(name))
            character.Name = name.Trim();

        foreach (var (stepId, title) in Mapping)
        {
            var value = result.GetText(stepId);
            if (string.IsNullOrWhiteSpace(value)) continue;

            var existing = character.Sections.FirstOrDefault(s =>
                string.Equals(s.Title, title, System.StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Content = value.Trim();
            }
            else
            {
                character.Sections.Add(new EntitySection { Title = title, Content = value.Trim() });
            }
        }
    }
}
