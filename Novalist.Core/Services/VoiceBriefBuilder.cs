using System.Text;
using System.Text.RegularExpressions;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>Why a voice cannot be designed for an entry, when it cannot.</summary>
public enum VoiceBriefRefusal
{
    /// <summary>Nothing in the way.</summary>
    None,

    /// <summary>The writer set this entry to <see cref="AiInclusion.Never"/>.
    /// A local model is still a model, so the answer is no until they say
    /// otherwise - deliberately, per entry, per design.</summary>
    WithheldFromAi
}

/// <summary>
/// A character described as an instrument, plus the reason not to, if there is
/// one.
/// </summary>
/// <param name="Description">The brief itself: plain text, no emotion in it.</param>
/// <param name="SampleLines">A few lines the character actually speaks.</param>
public sealed record VoiceBriefDraft(
    string Description,
    IReadOnlyList<string> SampleLines,
    VoiceBriefRefusal Refusal);

/// <summary>
/// Turns a Codex entry into a description of how somebody <em>sounds</em>.
///
/// The hard rule, and the reason this is a class rather than a string
/// concatenation at the call site: <b>the brief describes audible properties,
/// never biography or performance.</b> Approximate age, gender, accent, pitch,
/// timbre, articulation and resting cadence belong here. Build, height,
/// appearance, plot and mood do not reliably describe sound.
///
/// This is easy to get wrong because voice-design models happily accept
/// emotional words. Write "grief-stricken" or "perpetually furious" into a design
/// prompt and the emotion is baked into the timbre, where no amount of per-line
/// direction can get it back out - and the writer is handed a character who
/// sounds the same at the funeral and the wedding. So the emotion vocabulary the
/// rest of the app uses is filtered out of the brief on the way through, and a
/// test asserts that none of it survives in any of the shipped languages.
///
/// Nothing here calls a model. It reads what the writer already typed.
/// </summary>
public static class VoiceBriefBuilder
{
    /// <summary>How many of the character's own lines to include. Enough to show
    /// how they talk; few enough that the brief stays a description of a voice
    /// rather than a monologue.</summary>
    public const int MaxSampleLines = 6;

    /// <summary>The longest sample line worth carrying. A speech that runs to a
    /// paragraph says less about the voice than three short exchanges do.</summary>
    private const int MaxSampleLength = 220;

    /// <summary>
    /// How much of the writer's own prose the brief carries, across all
    /// sections.
    ///
    /// A brief is an instruction to a model, not a biography. Even explicitly
    /// voice-related notes can be long, so a bound keeps the acoustic traits
    /// prominent instead of turning the instruction into a monologue.
    /// </summary>
    private const int MaxProseLength = 500;

    /// <summary>Section titles that describe how somebody speaks. The writer's
    /// own words about the voice are the best material there is, and they are
    /// usually under a heading like this.</summary>
    private static readonly string[] VoiceSectionHints =
    [
        "voice", "speech", "accent", "dialect", "manner", "mannerism", "how they speak",
        "stimme", "sprache", "akzent", "dialekt", "sprechweise",
        "声音", "口音", "说话"
    ];

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Builds the brief for one character.
    /// </summary>
    /// <param name="character">The Codex entry.</param>
    /// <param name="sampleLines">Lines this character speaks, newest first or in
    /// story order - the order is the caller's business.</param>
    /// <param name="lexicon">The writing language. Its emotion vocabulary is what
    /// gets filtered out, and its <see cref="SceneAnalysisLexicon.WordBoundaries"/>
    /// decides how: a language that separates words is filtered word by word, and
    /// one that does not is filtered by substring, because in Chinese the emotion
    /// sits inside a run of characters with no space to find it by.</param>
    /// <param name="consentOverride">True when the writer has been asked about an
    /// entry they withheld from AI and said to go ahead anyway. The host does not
    /// decide this for them and does not quietly ignore what they set.</param>
    public static VoiceBriefDraft Build(
        CharacterData character,
        IReadOnlyList<string> sampleLines,
        SceneAnalysisLexicon? lexicon,
        bool consentOverride = false)
    {
        if (character.Ai == AiInclusion.Never && !consentOverride)
            return new VoiceBriefDraft(string.Empty, [], VoiceBriefRefusal.WithheldFromAi);

        var parts = new List<string>();

        // Only structured facts with a defensible acoustic meaning. A model can
        // infer a vocal age range from age and a broad register from gender;
        // build, height, a scar or a broken nose are visual facts and caused it
        // to invent stereotypes rather than follow a voice description.
        Add(parts, Fact("Age", character.Age));
        Add(parts, Fact("Gender", character.Gender));

        // Custom properties the writer added that name the voice. Anything else
        // they invented is theirs and is not guessed at.
        foreach (var (key, value) in character.CustomProperties)
        {
            if (LooksLikeVoice(key))
                Add(parts, Fact(key, value));
        }

        // Then only sections the writer explicitly made about speech. General
        // appearance and personality prose may be useful character context,
        // but it is not a voice-design instruction and was burying the actual
        // acoustic cues under almost a thousand characters of biography.
        var written = character.Sections
            // A section the writer withheld from AI stays withheld here. The
            // consent they gave was for the entry, not for the part of it they
            // separately marked private.
            .Where(section => !section.AiHidden
                && !string.IsNullOrWhiteSpace(section.Content)
                && LooksLikeVoice(section.Title));

        var room = MaxProseLength;
        foreach (var section in written)
        {
            if (room <= 0)
                break;
            var said = Clean(section.Content);
            if (said.Length > room)
                said = Trimmed(said, room);
            if (said.Length == 0)
                continue;
            room -= said.Length;
            Add(parts, Fact(section.Title, said));
        }

        var description = Strip(string.Join(". ", parts), lexicon);

        // The sample lines are not description - they are the words the designed
        // voice actually speaks - so they cannot be filtered word by word
        // without turning a line of dialogue into gibberish. A line carrying a
        // mood is dropped whole instead.
        //
        // It matters more than it looks. The clip a voice is designed as is what
        // every later line is cloned from, so a character designed speaking
        // their worst scene has that scene's delivery baked into their timbre
        // for the whole book - which is the exact thing the emotion filter on
        // the description exists to prevent, walking in through the other door.
        // Six consecutive lines from a character's worst scene went through at
        // full charge while the writer's own word "quiet" was scrubbed out of
        // the description beside them.
        var forbidden = BuildForbidden(lexicon);
        var samples = sampleLines
            .Select(Clean)
            .Where(line => line.Length > 0 && line.Length <= MaxSampleLength)
            .Where(line => IsPlainlySaid(line, forbidden, lexicon))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxSampleLines)
            .ToArray();

        return new VoiceBriefDraft(description, samples, VoiceBriefRefusal.None);
    }

    /// <summary>
    /// Removes every emotion word from a piece of prose, leaving the rest
    /// readable.
    ///
    /// Public because the design dialog lets the writer edit the brief before it
    /// is sent, and what they type has to go through the same filter. A rule the
    /// UI can talk its way around is not a rule.
    /// </summary>
    public static string Strip(string? text, SceneAnalysisLexicon? lexicon)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var forbidden = BuildForbidden(lexicon);
        if (forbidden.Count == 0)
            return Clean(text);

        return lexicon?.WordBoundaries == false
            ? Clean(StripBySubstring(text, forbidden))
            : Clean(StripByWord(text, forbidden));
    }

    /// <summary>
    /// Removes whole words only, so a word that merely spells an emotion inside
    /// itself survives: cutting "sad" out of the middle of a word would leave
    /// the writer's own prose mangled rather than filtered.
    /// </summary>
    private static string StripByWord(string text, HashSet<string> forbidden)
    {
        var kept = new StringBuilder(text.Length);
        var word = new StringBuilder(32);

        void Flush()
        {
            if (word.Length == 0)
                return;
            var candidate = word.ToString();
            if (!Forbidden(candidate, forbidden))
                kept.Append(candidate);
            word.Clear();
        }

        foreach (var ch in text)
        {
            if (char.IsLetter(ch) || ch == '-' || ch == '\'')
            {
                word.Append(ch);
                continue;
            }
            Flush();
            kept.Append(ch);
        }
        Flush();
        return kept.ToString();
    }

    /// <summary>
    /// Removes the vocabulary wherever it appears, for a language that does not
    /// put spaces between words. Longest first, so a longer emotion is not left
    /// half-removed by a shorter one it contains.
    /// </summary>
    private static string StripBySubstring(string text, HashSet<string> forbidden)
    {
        var stripped = text;
        foreach (var word in forbidden.OrderByDescending(w => w.Length))
        {
            if (word.Length > 0)
                stripped = stripped.Replace(word, " ", StringComparison.CurrentCultureIgnoreCase);
        }
        return stripped;
    }

    /// <summary>
    /// Every word no brief may contain: the language's emotion keys and the
    /// words that map to them - minus the ones that describe how somebody
    /// <em>sounds</em>.
    ///
    /// The subtraction is the whole point. The two lists overlap heavily, and
    /// without it the filter removed exactly the words a voice is described
    /// with: "A quiet, gentle voice; low and steady, soft at the edges, with a
    /// heavy northern burr" came back as "A , voice; low and , at the edges,
    /// with a northern burr" - and that wreckage was shown to the writer as
    /// their brief and sent to the model as the description.
    /// </summary>
    private static HashSet<string> BuildForbidden(SceneAnalysisLexicon? lexicon)
    {
        if (lexicon == null)
            return new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

        var timbre = lexicon.TimbreWords
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Select(w => w.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        return lexicon.EmotionKeys
            .Concat(lexicon.Emotions.SelectMany(e => e.Words))
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Select(w => w.Trim().ToLowerInvariant())
            .Where(w => !timbre.Contains(w))
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Whether one token has to go.
    ///
    /// The token itself, and then its parts either side of a hyphen - because
    /// whole-token matching let "grief-stricken" straight through, which is the
    /// exact string this class's own documentation names as the thing that must
    /// never reach a design prompt. "grief" was forbidden; the compound built
    /// out of it was not.
    /// </summary>
    private static bool Forbidden(string token, HashSet<string> forbidden)
    {
        var trimmed = token.Trim(TokenEdges);
        if (trimmed.Length == 0)
            return false;
        if (forbidden.Contains(trimmed))
            return true;

        return trimmed.Contains('-', StringComparison.Ordinal)
            && trimmed.Split('-').Any(part => part.Length > 0 && forbidden.Contains(part));
    }

    /// <summary>Punctuation that can sit on either end of a word without being
    /// part of it.</summary>
    private static readonly char[] TokenEdges =
        ['.', ',', ';', ':', '!', '?', '"', '\u0027'];

    /// <summary>
    /// Whether a line is said plainly enough to design a voice from.
    ///
    /// No emotion vocabulary in it, and no shout. Both are cheap proxies and
    /// both are the right way round: a line that fails this is not lost, it is
    /// simply not the one the voice is built on, and a character with nothing
    /// left falls back to a neutral sentence in the book's own language.
    /// </summary>
    private static bool IsPlainlySaid(
        string line, HashSet<string> forbidden, SceneAnalysisLexicon? lexicon)
    {
        if (line.Contains('!', StringComparison.Ordinal)
            || line.Contains('\uFF01', StringComparison.Ordinal))
        {
            return false;
        }
        if (forbidden.Count == 0)
            return true;

        // A language that puts no spaces between words has to be searched by
        // substring, exactly as the description filter searches it.
        if (lexicon?.WordBoundaries == false)
            return !forbidden.Any(word => line.Contains(word, StringComparison.CurrentCultureIgnoreCase));

        return !Words(line).Any(word => Forbidden(word, forbidden));
    }

    /// <summary>The words of a line, by the same reckoning the filter uses.</summary>
    private static IEnumerable<string> Words(string line)
    {
        var word = new StringBuilder(32);
        foreach (var ch in line)
        {
            if (char.IsLetter(ch) || ch == '-' || ch == '\u0027')
            {
                word.Append(ch);
                continue;
            }
            if (word.Length > 0)
            {
                yield return word.ToString();
                word.Clear();
            }
        }
        if (word.Length > 0)
            yield return word.ToString();
    }

    private static bool LooksLikeVoice(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;
        var lowered = title.ToLowerInvariant();
        return VoiceSectionHints.Any(hint => lowered.Contains(hint, StringComparison.Ordinal));
    }

    /// <summary>
    /// One labelled statement, or the bare words where there is no label to put
    /// in front of them - a section the writer never got round to naming is
    /// still their description of the person, and ": Speaks slowly." is not a
    /// sentence anybody wrote.
    /// </summary>
    private static string? Fact(string? label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var said = Clean(value);
        return string.IsNullOrWhiteSpace(label) ? said : $"{label.Trim()}: {said}";
    }

    private static void Add(List<string> parts, string? part)
    {
        if (!string.IsNullOrWhiteSpace(part))
            parts.Add(part!);
    }

    private static string Clean(string? text)
        => Whitespace.Replace(text ?? string.Empty, " ").Trim();

    /// <summary>
    /// As much of a section as there is room for, cut at a word rather than
    /// through one.
    ///
    /// Only ever called with room to spare - the loop stops before it runs out -
    /// so there is no guard here for none. A section whose first words are one
    /// unbroken run longer than what is left comes back empty rather than cut
    /// through the middle of it.
    /// </summary>
    private static string Trimmed(string text, int room)
    {
        var cut = text.LastIndexOf(' ', Math.Min(room, text.Length - 1));
        return cut > 0 ? text[..cut] : string.Empty;
    }
}
