using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Novalist.Core.Services;

/// <summary>One emotion the scene analysis can detect. <see cref="Key"/> is a
/// stable identifier the renderer localizes (<c>emotion.&lt;key&gt;</c>); the
/// words are language-specific.</summary>
public sealed class EmotionLexiconEntry
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("words")]
    public IReadOnlyList<string> Words { get; init; } = [];
}

/// <summary>Grammatical gender, used to match a pronoun in a dialogue tag
/// against the cast.</summary>
public enum DialogueGender
{
    Unknown,
    Male,
    Female
}

/// <summary>
/// The keyword lists behind the Inspector's scene analysis — intensity, emotion,
/// conflict, tags, and first-person POV detection — for one writing language.
///
/// Each language ships a JSON file in <c>Resources/Analysis/analysis.&lt;tag&gt;.json</c>
/// as an embedded resource. The presence of that file is what makes a language
/// supported, and the file also declares the emotion keys (and their order) the
/// UI offers, so adding a language is a matter of adding one JSON file — no code
/// change. Keys are stable identifiers shared across languages; only the words
/// differ, so a scene's stored emotion stays valid if the writing language changes.
/// </summary>
public sealed partial class SceneAnalysisLexicon
{
    private const string ResourcePrefix = "Novalist.Core.Resources.Analysis.analysis.";
    private const string ResourceSuffix = ".json";

    private static readonly ConcurrentDictionary<string, SceneAnalysisLexicon?> Cache = new(
        StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string Language { get; private init; } = "en";
    public IReadOnlyList<string> Positive { get; private init; } = [];
    public IReadOnlyList<string> Negative { get; private init; } = [];
    public IReadOnlyList<string> Conflict { get; private init; } = [];
    public IReadOnlyList<EmotionLexiconEntry> Emotions { get; private init; } = [];

    /// <summary>Verbs that introduce or follow a line of dialogue ("said",
    /// "flüsterte", "说"). Used by the Dialogue view to decide which name near a
    /// quote is the speaker rather than someone merely being talked about.</summary>
    public IReadOnlyList<string> SpeechVerbs { get; private init; } = [];

    /// <summary>
    /// The emotion a speech verb carries, for the verbs that carry one.
    ///
    /// The prose usually says how a line was said - "she snapped", "er
    /// flüsterte" - and that is a better direction for a reading than anything
    /// guessed from the scene around it. Values are <see cref="EmotionKeys"/>
    /// entries, so a verb and a scene resolve to the same vocabulary.
    ///
    /// Deliberately partial. Most speech verbs are neutral ("said", "asked"),
    /// and an ambiguous one is left out rather than guessed: English "cried" is
    /// as often a shout as a sob, so it maps to nothing and the line falls back
    /// to the scene's own emotion.
    /// </summary>
    public IReadOnlyDictionary<string, string> SpeechVerbEmotions { get; private init; }
        = new Dictionary<string, string>();

    /// <summary>Whether the language separates words with spaces. Chinese does
    /// not, so name and verb matching there is a plain substring test.</summary>
    public bool WordBoundaries { get; private init; } = true;

    /// <summary>Third-person singular pronouns, by grammatical gender. The
    /// Dialogue view uses these to resolve a tag like "brummte er" back to the
    /// character the narration last named.</summary>
    public Regex MalePronouns { get; private init; } = MatchNothing;

    public Regex FemalePronouns { get; private init; } = MatchNothing;

    /// <summary>Words a writer might put in a character's Gender field, so a
    /// pronoun can be matched against the cast. Free text, so both the formal
    /// word and the common shorthand are listed.</summary>
    public IReadOnlyList<string> GenderMale { get; private init; } = [];

    public IReadOnlyList<string> GenderFemale { get; private init; } = [];

    private static Regex MatchNothing => new("(?!)", RegexOptions.CultureInvariant);

    /// <summary>
    /// Classifies a character's free-text Gender field as male, female, or
    /// neither. Every shipped lexicon's word lists are consulted, not just the
    /// writing language's: the Gender field is typed in whatever language the
    /// writer uses for the interface, which need not be the manuscript's.
    /// </summary>
    public static DialogueGender ClassifyGender(string? gender)
    {
        var value = (gender ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Length == 0)
            return DialogueGender.Unknown;

        foreach (var tag in AvailableLanguages)
        {
            var lexicon = For(tag);
            if (lexicon == null) continue;
            if (lexicon.GenderMale.Contains(value, StringComparer.Ordinal))
                return DialogueGender.Male;
            if (lexicon.GenderFemale.Contains(value, StringComparer.Ordinal))
                return DialogueGender.Female;
        }
        return DialogueGender.Unknown;
    }

    /// <summary>The emotion keys, in the order the file declares them — this is
    /// what the UI's emotion dropdown offers.</summary>
    public IReadOnlyList<string> EmotionKeys { get; private init; } = [];

    /// <summary>Matches first-person pronouns for POV detection. Built from the
    /// language's pronoun list, with word boundaries only where the language is
    /// space-delimited (Chinese, for instance, is not).</summary>
    public Regex FirstPerson { get; private init; } = new("(?!)", RegexOptions.CultureInvariant);

    // ── Prose-style report inputs ───────────────────────────────────
    // Empty lists are meaningful: a language that does not mark adverbs with a
    // suffix, or for which no filter-word list exists, has that report reported
    // as unsupported rather than guessed at.

    /// <summary>Word endings that mark an adverb ("ly"). Empty where the
    /// language does not form adverbs by suffix, as German does not.</summary>
    public IReadOnlyList<string> AdverbSuffixes { get; private init; } = [];

    /// <summary>Words ending in an adverb suffix that are not adverbs
    /// ("only", "family"), so they are not counted.</summary>
    public IReadOnlyList<string> AdverbExceptions { get; private init; } = [];

    /// <summary>Verbs that put a narrator between the reader and the scene
    /// ("she saw the door open" over "the door opened").</summary>
    public IReadOnlyList<string> FilterWords { get; private init; } = [];

    /// <summary>Function words carrying no image. A high proportion makes a
    /// sentence read as sticky.</summary>
    public IReadOnlyList<string> GlueWords { get; private init; } = [];

    /// <summary>
    /// Words that put a reader in the room through one sense.
    ///
    /// Keyed by sense - sight, sound, smell, taste, touch - because the useful
    /// question is not how sensory a scene is but which senses it forgot.
    /// Nearly every writer defaults to sight and sound, and a total would hide
    /// exactly that.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Senses { get; private init; }
        = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>
    /// Verbs that report what somebody is thinking or feeling from inside.
    ///
    /// A scene written in one character's head that says "Tomas knew" has left
    /// that head. This is the shape of the slip; whether the writer meant it
    /// is not something a word list can decide.
    /// </summary>
    public IReadOnlyList<string> InteriorityVerbs { get; private init; } = [];

    /// <summary>Auxiliaries that can form the passive voice.</summary>
    public IReadOnlyList<string> PassiveAuxiliaries { get; private init; } = [];

    /// <summary>Verbs that usually have a more specific alternative.</summary>
    public IReadOnlyList<string> WeakVerbs { get; private init; } = [];

    /// <summary>Stock phrases, matched literally.</summary>
    public IReadOnlyList<string> Cliches { get; private init; } = [];

    /// <summary>
    /// Words that take a full stop without ending a sentence: titles, initials,
    /// and the short forms a language writes with a point in them.
    ///
    /// Read aloud rather than read. A reading is cut into utterances at a
    /// sentence ending, and a splitter that treats every point as one turns
    /// "The bell rang at 10 a.m. sharp." into three separate things for a model
    /// to say - which is not one breath, it is a stutter. Lower-cased and
    /// carrying no point of their own, so "Dr" matches "Dr." and "dr.".
    /// </summary>
    public IReadOnlyList<string> Abbreviations { get; private init; } = [];

    /// <summary>
    /// Words that describe how somebody <em>sounds</em> rather than how they
    /// feel, even where an emotion list also contains them.
    ///
    /// The emotion vocabulary is removed from a voice brief, because an emotion
    /// written into a design prompt is baked into the timbre. But the two lists
    /// overlap heavily - <em>quiet</em>, <em>soft</em>, <em>steady</em>,
    /// <em>heavy</em>, <em>low</em> are all emotion words and all of them are
    /// how a voice is described - so a blanket removal took the writer's most
    /// precise description of the instrument and left punctuation. These win.
    /// </summary>
    public IReadOnlyList<string> TimbreWords { get; private init; } = [];

    /// <summary>
    /// Whether this language writes ordinals with a full stop after the digits
    /// - German's <c>3. Mai</c>, where English writes <c>3rd May</c>.
    ///
    /// Where it does, a point straight after a number is not a sentence ending
    /// even when a capital follows it, which is the one case the surrounding
    /// evidence cannot tell apart on its own.
    /// </summary>
    public bool OrdinalPoint { get; private init; }

    /// <summary>
    /// Common verb forms that mark past-tense narration, and present-tense ones
    /// below.
    ///
    /// Empty on purpose for a language that does not inflect for tense - Chinese
    /// marks it with particles and context, so counting verb forms there would
    /// produce a confident wrong answer. A language with neither list has its
    /// tense reported as unknown rather than guessed.
    /// </summary>
    public IReadOnlyList<string> PastTenseMarkers { get; private init; } = [];

    /// <summary>Common verb forms that mark present-tense narration.</summary>
    public IReadOnlyList<string> PresentTenseMarkers { get; private init; } = [];

    /// <summary>Language tags shipped as embedded resources.</summary>
    public static IReadOnlyList<string> BuiltInLanguages { get; } = Assembly
        .GetExecutingAssembly()
        .GetManifestResourceNames()
        .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                       && name.EndsWith(ResourceSuffix, StringComparison.Ordinal))
        .Select(name => name[ResourcePrefix.Length..^ResourceSuffix.Length])
        .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string? _userDirectory;

    /// <summary>Every language tag with a lexicon — shipped, plus any dropped
    /// into the registered user directory.</summary>
    public static IReadOnlyList<string> AvailableLanguages { get; private set; } = BuiltInLanguages;
}
