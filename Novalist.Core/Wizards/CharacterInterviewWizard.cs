using Novalist.Sdk.Models.Wizards;

namespace Novalist.Core.Wizards;

/// <summary>
/// Character-interview wizard. Walks the seven psychology fields the writer
/// is most likely to want explicit: wound, fear, lie, want, need, secret,
/// voice. Maps the answers into the character's Sections.
/// </summary>
public static class CharacterInterviewWizard
{
    public const string Id = "entity.character.interview";

    public static WizardDefinition Build(Func<string, string>? loc = null)
    {
        string T(string key, string fallback) => loc?.Invoke(key) is { } v && v != key ? v : fallback;

        return new WizardDefinition
        {
            Id = Id,
            DisplayName = T("wizard.interview.displayName", "Character interview"),
            Description = T("wizard.interview.description", "Walks the seven psychology pillars: wound, fear, lie, want, need, secret, voice."),
            Scope = WizardScope.Entity,
            EntityTypeKey = "character",
            Steps =
            [
                new TextStep
                {
                    Id = "name",
                    Title = T("wizard.interview.name.title", "Name"),
                    Help = T("wizard.interview.name.help", "The character's primary name. You can add aliases later."),
                    Skippable = false,
                },
                new TextStep
                {
                    Id = "wound",
                    Title = T("wizard.interview.wound.title", "Wound"),
                    Help = T("wizard.interview.wound.help", "A formative event from the character's past that still shapes them. Often the source of the lie they believe."),
                    Multiline = true,
                },
                new TextStep
                {
                    Id = "fear",
                    Title = T("wizard.interview.fear.title", "Fear"),
                    Help = T("wizard.interview.fear.help", "What does the character most fear happening to them? It's often a re-occurrence of the wound."),
                    Multiline = true,
                },
                new TextStep
                {
                    Id = "lie",
                    Title = T("wizard.interview.lie.title", "Lie they believe"),
                    Help = T("wizard.interview.lie.help", "The false belief about themselves or the world the character has internalised — \"I'm unlovable\", \"strength = isolation\", \"the world is fair\"."),
                    Multiline = true,
                },
                new TextStep
                {
                    Id = "want",
                    Title = T("wizard.interview.want.title", "Want (external goal)"),
                    Help = T("wizard.interview.want.help", "The thing the character is consciously chasing. Tangible, in-world, can be pointed at."),
                    Multiline = true,
                },
                new TextStep
                {
                    Id = "need",
                    Title = T("wizard.interview.need.title", "Need (internal truth)"),
                    Help = T("wizard.interview.need.help", "The lesson they must learn — usually the inverse of the lie. Often realised in act three."),
                    Multiline = true,
                },
                new TextStep
                {
                    Id = "secret",
                    Title = T("wizard.interview.secret.title", "Secret"),
                    Help = T("wizard.interview.secret.help", "Something the character hides from other characters (and maybe themselves)."),
                    Multiline = true,
                },
                new TextStep
                {
                    Id = "voice",
                    Title = T("wizard.interview.voice.title", "Voice — sample lines"),
                    Help = T("wizard.interview.voice.help", "A few sentences in this character's voice so their dialogue stays consistent across the manuscript."),
                    Multiline = true,
                },
            ],
        };
    }
}
