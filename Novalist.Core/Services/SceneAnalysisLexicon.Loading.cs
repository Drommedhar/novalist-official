using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Novalist.Core.Services;

public sealed partial class SceneAnalysisLexicon
{
    /// <summary>
    /// Points the lexicon loader at a folder of user-supplied
    /// <c>analysis.&lt;tag&gt;.json</c> files. A user file wins over a shipped
    /// one of the same tag, so a writer can correct or extend a bundled lexicon
    /// as well as add a language Novalist does not ship. Rescans and drops the
    /// cache, so it is safe to call more than once; pass null to go back to the
    /// shipped set only.
    /// </summary>
    public static void RegisterUserDirectory(string? directory)
    {
        _userDirectory = string.IsNullOrWhiteSpace(directory) ? null : directory;
        Cache.Clear();
        AvailableLanguages = BuiltInLanguages
            .Concat(DiscoverUserLanguages())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// A starting point for a new language's lexicon: the shipped English file,
    /// with a header naming the language and saying what to do with it.
    ///
    /// Seeded from English rather than left blank because the useful work is
    /// translating a real list of emotion and conflict words, not guessing which
    /// keys exist. Every value is meant to be replaced; the shape is the gift.
    /// </summary>
    public static string TemplateFor(string languageTag)
    {
        using var stream = Assembly
            .GetExecutingAssembly()
            .GetManifestResourceStream($"{ResourcePrefix}en{ResourceSuffix}")
            ?? throw new InvalidOperationException("The English lexicon is missing.");
        using var reader = new StreamReader(stream);

        // aislop-ignore-next-line ai-slop/csharp-null-forgiving -- The embedded English lexicon is a shipped JSON object, validated by the lexicon tests.
        var node = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
        node["_comment"] =
            $"Scene-analysis lexicon for '{languageTag}'. Every list below is the English one, "
            + "left here as a starting point - replace the words with the equivalents in your "
            + "language rather than translating them one for one, since what counts as a filter "
            + "word or a cliche differs. Delete a list to leave that detection off. Then press "
            + "Rescan in Settings; no restart is needed.";

        return node.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    /// <summary>Language tags with a lexicon file in the user directory. Empty
    /// when no directory is registered or it cannot be listed — a missing or
    /// unreadable folder degrades to the shipped set rather than throwing.</summary>
    private static IEnumerable<string> DiscoverUserLanguages()
    {
        if (_userDirectory == null) return [];
        try
        {
            return Directory
                .EnumerateFiles(_userDirectory, $"analysis.*{ResourceSuffix}", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Select(name => name["analysis.".Length..^ResourceSuffix.Length])
                .Where(tag => tag.Length > 0)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

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

        // The user directory is consulted first so a dropped file overrides the
        // shipped lexicon for the same tag.
        var userJson = ReadUserFile(tag);
        if (userJson != null) return Parse(userJson, tag);

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream($"{ResourcePrefix}{tag}{ResourceSuffix}");
        if (stream == null) return null;

        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd(), tag);
    }

    /// <summary>The user lexicon file's contents for a tag, or null when no user
    /// directory is registered, the file is absent, or it cannot be read.</summary>
    private static string? ReadUserFile(string tag)
    {
        if (_userDirectory == null) return null;
        var path = Path.Combine(_userDirectory, $"analysis.{tag}{ResourceSuffix}");
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
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
            // Only verbs the language actually ships, pointing at emotions it
            // actually declares. A map entry for a verb that is not a speech
            // verb could never be reached, and one naming an emotion outside
            // the file's own keys would resolve to a direction nothing can
            // localize - both are dropped rather than carried.
            SpeechVerbEmotions = BuildVerbEmotions(
                file.SpeechVerbEmotions, Clean(file.SpeechVerbs), emotions),
            WordBoundaries = file.WordBoundaries,
            MalePronouns = BuildPronounRegex(Clean(file.PronounsMale), file.WordBoundaries),
            FemalePronouns = BuildPronounRegex(Clean(file.PronounsFemale), file.WordBoundaries),
            GenderMale = Clean(file.GenderMale),
            GenderFemale = Clean(file.GenderFemale),
            FirstPerson = BuildPronounRegex(Clean(file.FirstPerson), file.WordBoundaries),
            AdverbSuffixes = Clean(file.AdverbSuffixes),
            AdverbExceptions = Clean(file.AdverbExceptions),
            FilterWords = Clean(file.FilterWords),
            GlueWords = Clean(file.GlueWords),
            Senses = (file.Senses ?? new Dictionary<string, IReadOnlyList<string>>())
                .Where(kv => kv.Value is { Count: > 0 })
                .ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)Clean(kv.Value),
                    StringComparer.OrdinalIgnoreCase),
            InteriorityVerbs = Clean(file.InteriorityVerbs),
            PassiveAuxiliaries = Clean(file.PassiveAuxiliaries),
            WeakVerbs = Clean(file.WeakVerbs),
            PastTenseMarkers = Clean(file.PastTenseMarkers),
            PresentTenseMarkers = Clean(file.PresentTenseMarkers),
            Cliches = Clean(file.Cliches),
            TimbreWords = Clean(file.TimbreWords),
            Abbreviations = Clean(file.Abbreviations)
                .Select(a => a.TrimEnd('.'))
                .Where(a => a.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            OrdinalPoint = file.OrdinalPoint
        };
    }

    /// <summary>
    /// Keeps the verb-to-emotion entries that can actually fire: the verb has
    /// to be one the language ships, and the emotion has to be one the file
    /// declares. Both sides are normalized the way <see cref="Clean"/>
    /// normalizes a word list, so a map written with capitals still matches.
    /// </summary>
    private static IReadOnlyDictionary<string, string> BuildVerbEmotions(
        IReadOnlyDictionary<string, string>? map,
        IReadOnlyList<string> speechVerbs,
        IReadOnlyList<EmotionLexiconEntry> emotions)
    {
        if (map == null || map.Count == 0)
            return new Dictionary<string, string>();

        var verbs = speechVerbs.ToHashSet(StringComparer.Ordinal);
        var keys = emotions.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);

        return map
            .Select(kv => (
                Verb: (kv.Key ?? string.Empty).Trim().ToLowerInvariant(),
                Emotion: (kv.Value ?? string.Empty).Trim()))
            .Where(e => verbs.Contains(e.Verb) && keys.Contains(e.Emotion))
            .DistinctBy(e => e.Verb, StringComparer.Ordinal)
            .ToDictionary(e => e.Verb, e => e.Emotion, StringComparer.OrdinalIgnoreCase);
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

        [JsonPropertyName("speechVerbEmotions")]
        public Dictionary<string, string>? SpeechVerbEmotions { get; init; }

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

        [JsonPropertyName("adverbSuffixes")]
        public IReadOnlyList<string> AdverbSuffixes { get; init; } = [];

        [JsonPropertyName("adverbExceptions")]
        public IReadOnlyList<string> AdverbExceptions { get; init; } = [];

        [JsonPropertyName("filterWords")]
        public IReadOnlyList<string> FilterWords { get; init; } = [];

        /// <summary>Sense words by sense key. Absent in a language nobody has
        /// written the lists for, which reports as unsupported rather than as
        /// a scene with no senses in it.</summary>
        [JsonPropertyName("interiorityVerbs")]
        public IReadOnlyList<string> InteriorityVerbs { get; init; } = [];

        [JsonPropertyName("senses")]
        public Dictionary<string, IReadOnlyList<string>>? Senses { get; init; }

        [JsonPropertyName("glueWords")]
        public IReadOnlyList<string> GlueWords { get; init; } = [];

        [JsonPropertyName("passiveAuxiliaries")]
        public IReadOnlyList<string> PassiveAuxiliaries { get; init; } = [];

        [JsonPropertyName("weakVerbs")]
        public IReadOnlyList<string> WeakVerbs { get; init; } = [];

        [JsonPropertyName("pastTenseMarkers")]
        public IReadOnlyList<string> PastTenseMarkers { get; init; } = [];

        [JsonPropertyName("presentTenseMarkers")]
        public IReadOnlyList<string> PresentTenseMarkers { get; init; } = [];

        [JsonPropertyName("cliches")]
        public IReadOnlyList<string> Cliches { get; init; } = [];

        [JsonPropertyName("timbreWords")]
        public IReadOnlyList<string> TimbreWords { get; init; } = [];

        [JsonPropertyName("abbreviations")]
        public IReadOnlyList<string> Abbreviations { get; init; } = [];

        [JsonPropertyName("ordinalPoint")]
        public bool OrdinalPoint { get; init; }
    }
}
