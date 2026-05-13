using Novalist.Sdk.Models.Wizards;

namespace Novalist.Core.Wizards;

/// <summary>
/// Snowflake-method-inspired project setup wizard. Walks premise → one
/// sentence → paragraph → acts → chapters → cast seed.
/// </summary>
public static class ProjectSnowflakeWizard
{
    public const string Id = "project.snowflake";

    public static WizardDefinition Build(Func<string, string>? loc = null)
    {
        string T(string key, string fallback) => loc?.Invoke(key) is { } v && v != key ? v : fallback;

        return new WizardDefinition
        {
            Id = Id,
            DisplayName = T("wizard.project.displayName", "Snowflake project setup"),
            Description = T("wizard.project.description", "Walks you from a one-line premise to an outlined manuscript with seed cast."),
            Scope = WizardScope.Project,
            Steps =
            [
                new TextStep
                {
                    Id = "projectName",
                    Title = T("wizard.project.projectName.title", "What's the project called?"),
                    Help = T("wizard.project.projectName.help", "The display name for the project; also the folder name on disk."),
                    Skippable = false,
                    Placeholder = T("wizard.project.projectName.placeholder", "My Novel"),
                },
                new TextStep
                {
                    Id = "bookName",
                    Title = T("wizard.project.bookName.title", "Working title of the first book"),
                    Help = T("wizard.project.bookName.help", "You can rename this later."),
                    Skippable = false,
                    Placeholder = T("wizard.project.bookName.placeholder", "Book 1"),
                },
                new TextStep
                {
                    Id = "premise",
                    Title = T("wizard.project.premise.title", "Premise — one sentence"),
                    Help = T("wizard.project.premise.help", "What is your story about in a single line? Try the form: A [character] wants [goal] but [obstacle]."),
                    Placeholder = T("wizard.project.premise.placeholder", "A retired thief must rob the man who framed her."),
                    MaxLength = 240,
                },
                new TextStep
                {
                    Id = "paragraph",
                    Title = T("wizard.project.paragraph.title", "Expand the premise into a paragraph"),
                    Help = T("wizard.project.paragraph.help", "Set up the world, stakes, the inciting incident, and the rough shape of the climax."),
                    Multiline = true,
                    MaxLength = 1500,
                },
                new TextStep
                {
                    Id = "actOne",
                    Title = T("wizard.project.actOne.title", "Act 1 — Setup"),
                    Help = T("wizard.project.actOne.help", "What's the status quo? Who's the protagonist before they're forced to change?"),
                    Multiline = true,
                },
                new TextStep
                {
                    Id = "actTwo",
                    Title = T("wizard.project.actTwo.title", "Act 2 — Confrontation"),
                    Help = T("wizard.project.actTwo.help", "The middle. Rising tension, complications, the protagonist tries and fails."),
                    Multiline = true,
                },
                new TextStep
                {
                    Id = "actThree",
                    Title = T("wizard.project.actThree.title", "Act 3 — Resolution"),
                    Help = T("wizard.project.actThree.help", "Climax + new equilibrium. What's the cost? What changed?"),
                    Multiline = true,
                },
                new NumberStep
                {
                    Id = "chaptersPerAct",
                    Title = T("wizard.project.chaptersPerAct.title", "Roughly how many chapters per act?"),
                    Help = T("wizard.project.chaptersPerAct.help", "We'll create placeholder chapters under each act. Aim for 5–12. You can add and remove chapters freely later."),
                    Min = 1, Max = 30, DefaultValue = 7,
                },
                new EntityListStep
                {
                    Id = "protagonists",
                    Title = T("wizard.project.protagonists.title", "Who is this story about? (protagonists)"),
                    Help = T("wizard.project.protagonists.help", "Add one or more main characters. Just names for now — develop them later in the Codex."),
                    TargetEntityTypeKey = "character",
                    MinCount = 0, MaxCount = 5,
                    SubSteps =
                    [
                        new TextStep { Id = "name", Title = T("wizard.project.cast.name", "Name"), Skippable = false, Placeholder = T("wizard.project.cast.namePlaceholder", "First name") },
                        new TextStep { Id = "role", Title = T("wizard.project.cast.role", "Role / archetype"), Placeholder = T("wizard.project.cast.protagonistPlaceholder", "Protagonist, mentor, …") },
                    ],
                },
                new EntityListStep
                {
                    Id = "antagonists",
                    Title = T("wizard.project.antagonists.title", "Who stands in the way? (antagonists)"),
                    Help = T("wizard.project.antagonists.help", "Add the people, factions, or forces opposing the protagonist."),
                    TargetEntityTypeKey = "character",
                    MinCount = 0, MaxCount = 5,
                    SubSteps =
                    [
                        new TextStep { Id = "name", Title = T("wizard.project.cast.name", "Name"), Skippable = false, Placeholder = T("wizard.project.cast.namePlaceholderAlt", "Name or label") },
                        new TextStep { Id = "role", Title = T("wizard.project.cast.role", "Role / archetype"), Placeholder = T("wizard.project.cast.antagonistPlaceholder", "Antagonist, foil, …") },
                    ],
                },
            ],
        };
    }
}
