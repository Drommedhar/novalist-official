using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
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
public sealed class SceneAnalysisLexicon
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

    /// <summary>Every language tag that ships a lexicon.</summary>
    public static IReadOnlyList<string> AvailableLanguages { get; } = Assembly
        .GetExecutingAssembly()
        .GetManifestResourceNames()
        .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                       && name.EndsWith(ResourceSuffix, StringComparison.Ordinal))
        .Select(name => name[ResourcePrefix.Length..^ResourceSuffix.Length])
        .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>
    /// The lexicon for a writing language, or null when that language ships none
    /// (the caller then skips keyword-derived analysis rather than guessing with
    /// another language's words). A regional tag falls back to its base language,
    /// so "de-AT" uses "de".
    /// </summary>
    public static SceneAnalysisLexicon? For(string? language)
        => Cache.GetOrAdd(Resolve(language) ?? string.Empty, Load);

    /// <summary>Whether a writing language has a lexicon (exact tag or base
    /// language). Drives the "analysis unavailable for this language" note.</summary>
    public static bool Supports(string? language) => Resolve(language) != null;

    /// <summary>Picks the best available tag: exact match first, then the base
    /// language, then any tag sharing that base ("zh" -> "zh-CN").</summary>
    private static string? Resolve(string? language)
    {
        var tag = (language ?? string.Empty).Trim();
        if (tag.Length == 0) tag = "en";

        var exact = AvailableLanguages.FirstOrDefault(
            a => string.Equals(a, tag, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        var baseTag = tag.Split('-')[0];
        var baseMatch = AvailableLanguages.FirstOrDefault(
            a => string.Equals(a, baseTag, StringComparison.OrdinalIgnoreCase));
        if (baseMatch != null) return baseMatch;

        return AvailableLanguages.FirstOrDefault(
            a => a.Split('-')[0].Equals(baseTag, StringComparison.OrdinalIgnoreCase));
    }

    private static SceneAnalysisLexicon? Load(string tag)
    {
        if (tag.Length == 0) return null;

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream($"{ResourcePrefix}{tag}{ResourceSuffix}");
        if (stream == null) return null;

        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd(), tag);
    }

    /// <summary>Builds a lexicon from raw JSON. Separate from resource loading so
    /// the shape rules (blank filtering, key order, pronoun matching) are testable
    /// without shipping a fixture language. Null for unusable JSON.</summary>
    internal static SceneAnalysisLexicon? Parse(string json, string tag)
    {
        LexiconFile? file;
        try
        {
            file = JsonSerializer.Deserialize<LexiconFile>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        if (file == null) return null;

        var emotions = file.Emotions
            .Where(e => !string.IsNullOrWhiteSpace(e.Key))
            .ToArray();

        return new SceneAnalysisLexicon
        {
            Language = tag,
            Positive = Clean(file.Positive),
            Negative = Clean(file.Negative),
            Conflict = Clean(file.Conflict),
            Emotions = emotions,
            EmotionKeys = emotions.Select(e => e.Key).ToArray(),
            SpeechVerbs = Clean(file.SpeechVerbs),
            WordBoundaries = file.WordBoundaries,
            MalePronouns = BuildPronounRegex(Clean(file.PronounsMale), file.WordBoundaries),
            FemalePronouns = BuildPronounRegex(Clean(file.PronounsFemale), file.WordBoundaries),
            GenderMale = Clean(file.GenderMale),
            GenderFemale = Clean(file.GenderFemale),
            FirstPerson = BuildPronounRegex(Clean(file.FirstPerson), file.WordBoundaries)
        };
    }

    private static string[] Clean(IReadOnlyList<string>? words)
        => (words ?? [])
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Select(w => w.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static Regex BuildPronounRegex(IReadOnlyList<string> pronouns, bool wordBoundaries)
    {
        if (pronouns.Count == 0)
            return new Regex("(?!)", RegexOptions.CultureInvariant); // matches nothing

        // Longest first so "ourselves" wins over "our" at the same position.
        var alternation = string.Join(
            "|",
            pronouns.OrderByDescending(p => p.Length).Select(Regex.Escape));
        var pattern = wordBoundaries
            ? $@"(?<![\p{{L}}\p{{N}}])(?:{alternation})(?![\p{{L}}\p{{N}}])"
            : $"(?:{alternation})";
        return new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private sealed class LexiconFile
    {
        [JsonPropertyName("wordBoundaries")]
        public bool WordBoundaries { get; init; } = true;

        [JsonPropertyName("firstPerson")]
        public IReadOnlyList<string> FirstPerson { get; init; } = [];

        [JsonPropertyName("positive")]
        public IReadOnlyList<string> Positive { get; init; } = [];

        [JsonPropertyName("negative")]
        public IReadOnlyList<string> Negative { get; init; } = [];

        [JsonPropertyName("conflict")]
        public IReadOnlyList<string> Conflict { get; init; } = [];

        [JsonPropertyName("speechVerbs")]
        public IReadOnlyList<string> SpeechVerbs { get; init; } = [];

        [JsonPropertyName("pronounsMale")]
        public IReadOnlyList<string> PronounsMale { get; init; } = [];

        [JsonPropertyName("pronounsFemale")]
        public IReadOnlyList<string> PronounsFemale { get; init; } = [];

        [JsonPropertyName("genderMale")]
        public IReadOnlyList<string> GenderMale { get; init; } = [];

        [JsonPropertyName("genderFemale")]
        public IReadOnlyList<string> GenderFemale { get; init; } = [];

        [JsonPropertyName("emotions")]
        public IReadOnlyList<EmotionLexiconEntry> Emotions { get; init; } = [];
    }
}
