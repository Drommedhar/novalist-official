using System.Text.RegularExpressions;

namespace Novalist.Core.Services;

/// <summary>What a scene's prose is written in, as far as it can be told.</summary>
public enum NarrativeReading
{
    /// <summary>Not enough evidence, or the language does not mark it.</summary>
    Unknown,
    First,
    Third,
    Past,
    Present
}

/// <summary>
/// One scene measured against what the book says it is.
///
/// <c>Declared</c> empty means the writer has not said, and nothing is reported:
/// a book with no declared voice cannot drift out of it.
/// </summary>
public sealed record VoiceCheck(
    string Declared,
    NarrativeReading Reading,
    bool Agrees,
    /// <summary>How strong the evidence was, 0-100. A short scene is weak
    /// evidence and says so rather than being reported as a violation.</summary>
    int Confidence);

/// <summary>
/// Reads a scene's narrative person and tense, and compares them with the
/// book's declaration.
///
/// Novalist has detected a point of view per scene for a long time, but nothing
/// declared what the book as a whole was meant to be - so a first-person novel
/// with one third-person scene in it had nothing to be wrong against. This is
/// the other half: the book says, and a scene can disagree.
///
/// Every reading is evidence-based and reports its own confidence, because the
/// failure mode that matters is telling a writer their scene is broken when it
/// is four sentences long. Below <see cref="MinimumWords"/> nothing is claimed
/// at all, and a language with no tense markers in its lexicon - Chinese marks
/// tense with particles rather than verb forms - reports Unknown rather than
/// counting the wrong thing.
/// </summary>
public static class NarrativeVoiceService
{
    /// <summary>Under this many words, no reading is offered.</summary>
    public const int MinimumWords = 60;

    /// <summary>
    /// The narrative person the prose reads as. First person needs a real share
    /// of first-person pronouns rather than one line of dialogue containing "I".
    /// </summary>
    public static (NarrativeReading Reading, int Confidence) ReadPerson(
        string text, SceneAnalysisLexicon? lexicon)
    {
        if (lexicon == null) return (NarrativeReading.Unknown, 0);
        var words = WordCount(text);
        if (words < MinimumWords) return (NarrativeReading.Unknown, 0);

        var first = lexicon.FirstPerson.Matches(text).Count;
        // Dialogue is first person by nature - every quoted line is somebody
        // saying "I" - so a scene needs first-person narration outside it, and
        // one in fifty words is roughly where that starts to show.
        var share = first / (double)words;
        if (share >= 0.02)
            return (NarrativeReading.First, Confidence(share, 0.02, 0.06));

        return (NarrativeReading.Third, Confidence(0.02 - share, 0.0, 0.02));
    }

    /// <summary>
    /// The tense the prose reads as, from the language's own marker lists.
    /// A language with neither list gets Unknown, which is the honest answer
    /// rather than a count of words that do not mark tense.
    /// </summary>
    public static (NarrativeReading Reading, int Confidence) ReadTense(
        string text, SceneAnalysisLexicon? lexicon)
    {
        if (lexicon == null
            || (lexicon.PastTenseMarkers.Count == 0 && lexicon.PresentTenseMarkers.Count == 0))
            return (NarrativeReading.Unknown, 0);

        var words = WordCount(text);
        if (words < MinimumWords) return (NarrativeReading.Unknown, 0);

        var past = Count(text, lexicon.PastTenseMarkers, lexicon.WordBoundaries);
        var present = Count(text, lexicon.PresentTenseMarkers, lexicon.WordBoundaries);
        var total = past + present;
        if (total < 5) return (NarrativeReading.Unknown, 0);

        var dominant = Math.Max(past, present) / (double)total;
        return (
            past >= present ? NarrativeReading.Past : NarrativeReading.Present,
            Confidence(dominant, 0.5, 0.85));
    }

    /// <summary>
    /// A scene against the book's declared person. Returns null when the book
    /// declares nothing - there is no such thing as drifting out of a mode
    /// nobody chose.
    /// </summary>
    public static VoiceCheck? CheckPerson(
        string declaredPerson, string text, SceneAnalysisLexicon? lexicon)
    {
        var expected = ParsePerson(declaredPerson);
        if (expected == NarrativeReading.Unknown) return null;

        var (reading, confidence) = ReadPerson(text, lexicon);
        return new VoiceCheck(
            declaredPerson,
            reading,
            reading == NarrativeReading.Unknown || reading == expected,
            confidence);
    }

    /// <summary>A scene against the book's declared tense, same rules.</summary>
    public static VoiceCheck? CheckTense(
        string declaredTense, string text, SceneAnalysisLexicon? lexicon)
    {
        var expected = ParseTense(declaredTense);
        if (expected == NarrativeReading.Unknown) return null;

        var (reading, confidence) = ReadTense(text, lexicon);
        return new VoiceCheck(
            declaredTense,
            reading,
            reading == NarrativeReading.Unknown || reading == expected,
            confidence);
    }

    /// <summary>
    /// The narrative person a declaration names. Second person and the two
    /// flavours of third all read as third here, because what the prose can be
    /// measured against is the pronoun, not the depth of interiority.
    /// </summary>
    internal static NarrativeReading ParsePerson(string? declared)
        => (declared ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "first" => NarrativeReading.First,
            "third" or "third-limited" or "third-omniscient" => NarrativeReading.Third,
            _ => NarrativeReading.Unknown
        };

    internal static NarrativeReading ParseTense(string? declared)
        => (declared ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "past" => NarrativeReading.Past,
            "present" => NarrativeReading.Present,
            _ => NarrativeReading.Unknown
        };

    /// <summary>
    /// A 0-100 reading of how far past the floor a measurement sits. Linear
    /// between floor and ceiling so the number means something rather than
    /// jumping between certain and silent.
    /// </summary>
    private static int Confidence(double value, double floor, double ceiling)
    {
        if (ceiling <= floor) return 0;
        var scaled = (value - floor) / (ceiling - floor);
        return (int)Math.Round(Math.Clamp(scaled, 0, 1) * 100);
    }

    private static int WordCount(string text)
        => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static int Count(string text, IReadOnlyList<string> markers, bool wordBoundaries)
    {
        var total = 0;
        foreach (var marker in markers)
        {
            if (marker.Length == 0) continue;
            var pattern = wordBoundaries
                ? $@"\b{Regex.Escape(marker)}\b"
                : Regex.Escape(marker);
            total += Regex.Matches(
                text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count;
        }
        return total;
    }
}
