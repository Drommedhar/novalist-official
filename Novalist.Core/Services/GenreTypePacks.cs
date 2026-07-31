using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>
/// Starting points for the entity types a worldbuilder ends up needing.
///
/// The custom type builder is an empty form, so every writer who wants species,
/// a magic system, factions or a language reconstructs the same field list by
/// hand, and reconstructs it differently in every project. Campfire ships
/// modules for exactly these; World Anvil ships around twenty-eight templates.
///
/// These are starting points and not fixtures. A pack fills the builder in and
/// then gets out of the way: the fields are the writer's to rename, reorder or
/// throw out before the type is created, because a magic system that runs on
/// debt rather than on energy needs different questions.
///
/// Every field carries the question it is for. A field labelled "Cost" answers
/// nothing on its own; "What does using it take out of the user, and who pays
/// if they cannot?" is the thing that makes the field worth filling in, and it
/// stays on the entry rather than vanishing after creation.
/// </summary>
public static class GenreTypePacks
{
    /// <summary>The packs that ship, in the order the picker lists them.</summary>
    public static readonly IReadOnlyList<CustomEntityTypeDefinition> All =
    [
        Pack("species", "Species", "Species",
            ("Habitat", "Where do they live, and what does that cost them?"),
            ("Lifespan", "How long do they live, and how does that change what they care about?"),
            ("Physiology", "What can their bodies do that a human's cannot, and what can they not do?"),
            ("Culture", "What do they value, and what do they find unforgivable?"),
            ("Standing", "How do the other peoples of this world treat them, and why?")),

        Pack("magic_system", "Magic system", "Magic systems",
            ("Source", "Where does the power come from, and does it run out?"),
            ("Cost", "What does using it take out of the user, and who pays if they cannot?"),
            ("Limits", "What can it not do? This is the field that keeps the ending earned."),
            ("Who can use it", "Is it inherited, learned, granted, or stolen?"),
            ("How it is learned", "Who teaches it, and what do they want in return?"),
            ("What it looks like", "What does somebody in the room see, hear and smell?")),

        Pack("faction", "Faction", "Factions",
            ("Purpose", "What does it exist to do, and what would finishing look like?"),
            ("Leadership", "Who decides, and how did they get to decide?"),
            ("Membership", "Who joins, how, and what happens to somebody who leaves?"),
            ("Resources", "What does it actually have - money, people, force, information?"),
            ("Rivals", "Who wants it gone, and what is the fight really about?"),
            ("Public face", "What does everyone believe about it that is not quite true?")),

        Pack("language", "Language", "Languages",
            ("Speakers", "Who speaks it, and who used to?"),
            ("Sound", "What does it sound like to somebody who does not speak it?"),
            ("Script", "Is it written, and by whom?"),
            ("Grammar notes", "The two or three rules you need to keep names consistent."),
            ("Useful phrases", "The handful you will actually put in the prose.")),

        Pack("religion", "Religion", "Religions",
            ("Belief", "What does it say the world is, and what is it for?"),
            ("Practice", "What does a follower do daily, weekly, yearly?"),
            ("Authority", "Who speaks for it, and who checks them?"),
            ("Heresy", "What belief gets somebody thrown out, and who currently holds it?"),
            ("Reach", "Who follows it, where, and who is quietly done with it?"))
    ];

    private static CustomEntityTypeDefinition Pack(
        string key, string name, string plural,
        params (string Field, string Prompt)[] fields)
        => new()
        {
            TypeKey = key,
            DisplayName = name,
            DisplayNamePlural = plural,
            FolderName = plural,
            Source = "user",
            DefaultFields =
            [
                .. fields.Select(f => new CustomEntityFieldDefinition
                {
                    Key = Slug(f.Field),
                    DisplayName = f.Field,
                    Type = CustomPropertyType.String,
                    Prompt = f.Prompt
                })
            ],
            // Sections and relationships on, images off: these are things a
            // world runs on rather than things with a face, and an empty image
            // strip on every one of them is noise.
            Features = new CustomEntityFeatures
            {
                IncludeImages = false,
                IncludeRelationships = true,
                IncludeSections = true
            }
        };

    /// <summary>lowercase_snake, matching the keys the builder generates.</summary>
    internal static string Slug(string label)
        => new(label.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray());
}
