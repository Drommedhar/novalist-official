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
/// concatenation at the call site: <b>the brief describes the instrument, never
/// the performance.</b> Age, gender, build, accent, distinguishing features, the
/// register they speak in when nothing is wrong - all of that is fixed and
/// belongs here. Everything the character <em>feels</em> belongs to the
/// direction, which is chosen fresh for every line.
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

        // The fixed facts first, in the order somebody would say them out loud.
        Add(parts, Fact("Age", character.Age));
        Add(parts, Fact("Gender", character.Gender));
        Add(parts, Fact("Build", character.Build));
        Add(parts, Fact("Height", character.Height));
        Add(parts, Fact("Distinguishing features", character.DistinguishingFeatures));

        // Custom properties the writer added that name the voice. Anything else
        // they invented is theirs and is not guessed at.
        foreach (var (key, value) in character.CustomProperties)
        {
            if (LooksLikeVoice(key))
                Add(parts, Fact(key, value));
        }

        // Then their own words about how this person speaks.
        foreach (var section in character.Sections)
        {
            // A section the writer withheld from AI stays withheld here. The
            // consent they gave was for the entry, not for the part of it they
            // separately marked private.
            if (section.AiHidden || !LooksLikeVoice(section.Title))
                continue;
            Add(parts, Fact(section.Title, section.Content));
        }

        var description = Strip(string.Join(". ", parts), lexicon);
        var samples = sampleLines
            .Select(Clean)
            .Where(line => line.Length > 0 && line.Length <= MaxSampleLength)
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
            if (!forbidden.Contains(candidate.Trim('.', ',', ';', ':', '!', '?', '"', '\'')))
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

    /// <summary>Every word no brief may contain: the language's emotion keys and
    /// the words that map to them.</summary>
    private static HashSet<string> BuildForbidden(SceneAnalysisLexicon? lexicon)
    {
        if (lexicon == null)
            return new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

        return lexicon.EmotionKeys
            .Concat(lexicon.Emotions.SelectMany(e => e.Words))
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Select(w => w.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
    }

    private static bool LooksLikeVoice(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;
        var lowered = title.ToLowerInvariant();
        return VoiceSectionHints.Any(hint => lowered.Contains(hint, StringComparison.Ordinal));
    }

    private static string? Fact(string label, string? value)
        => string.IsNullOrWhiteSpace(value) ? null : $"{label.Trim()}: {Clean(value)}";

    private static void Add(List<string> parts, string? part)
    {
        if (!string.IsNullOrWhiteSpace(part))
            parts.Add(part!);
    }

    private static string Clean(string? text)
        => Whitespace.Replace(text ?? string.Empty, " ").Trim();
}
