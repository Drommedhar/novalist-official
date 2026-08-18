using Novalist.Core.Models;
using Novalist.Sdk.Models.Narration;
using SdkSegment = Novalist.Sdk.Models.Narration.NarrationSegment;
using SdkDirection = Novalist.Sdk.Models.Narration.VoiceDirection;

namespace Novalist.Core.Services;

/// <summary>
/// Turns a directed reading into what a speech engine is actually sent.
///
/// The whole of the interesting decision is here: <b>how much to tell the
/// engine</b>. Engines take direction in incompatible ways, and the wrong
/// choice is not a degraded reading but a wrong one.
///
/// <list type="bullet">
/// <item>An engine that takes a <b>vector</b> gets the numbers.</item>
/// <item>One that takes an <b>instruction</b> gets a sentence naming the
/// emotion and, where the prose supplied one, the speech verb it came from -
/// "read this angrily, as though snapped" says more than "angry".</item>
/// <item>One that <b>infers</b> affect from the script gets nothing. It has
/// already read the line; telling it what to feel would override what it heard
/// in the words, which is worse than saying nothing.</item>
/// <item>One that takes <b>none</b> of the three reads flat, and the view says
/// so rather than showing direction controls that do nothing.</item>
/// </list>
///
/// The direction never enters the text. It travels beside the words as a
/// parameter, because concatenating it in is one bad prompt away from the word
/// "angry" being read out in the middle of a sentence. An engine that genuinely
/// wants in-band tags adds them in its own adapter, where it knows its own
/// syntax.
/// </summary>
public static class NarrationRender
{
    /// <summary>The three ways an engine can be told how to perform a line. An
    /// engine advertising none of them is read flat.</summary>
    private const VoiceEngineFeatures AnyDirection =
        VoiceEngineFeatures.EmotionVector
        | VoiceEngineFeatures.EmotionInstruction
        | VoiceEngineFeatures.EmotionInferred;

    /// <summary>
    /// Builds the request for a run of segments.
    /// </summary>
    /// <param name="segments">The reading, in order.</param>
    /// <param name="sheet">Who is read in which voice.</param>
    /// <param name="voices">Reference audio per voice id, for the voices this
    /// machine actually has.</param>
    /// <param name="features">What the engine says it can take.</param>
    /// <param name="language">The book's language, BCP-47.</param>
    /// <param name="rate">Reading pace, 1 being the engine's own.</param>
    /// <param name="clips">Emotion-reference clips by name, for the lines that
    /// point at one. Empty on almost every render.</param>
    public static NarrationRequest Build(
        IReadOnlyList<NarrationSegment> segments,
        VoiceCastSheet sheet,
        IReadOnlyDictionary<string, byte[]> voices,
        VoiceEngineFeatures features,
        string language,
        double rate = 1.0,
        IReadOnlyDictionary<string, byte[]>? clips = null)
    {
        var sendable = new List<SdkSegment>(segments.Count);
        foreach (var segment in segments)
        {
            var voiceId = VoiceCast.Resolve(sheet, segment.SpeakerId);
            // A segment with no voice, or one whose voice this machine does not
            // have, is left out rather than sent as something the engine will
            // refuse. The caller knows which keys it asked for and which came
            // back, so a gap is visible rather than silent.
            if (voiceId == null || !voices.ContainsKey(voiceId))
                continue;
            if (string.IsNullOrWhiteSpace(segment.Text))
                continue;

            sendable.Add(new SdkSegment
            {
                Key = segment.Key,
                Text = segment.Text,
                VoiceId = voiceId,
                IsDialogue = segment.Kind == NarrationSegmentKind.Dialogue,
                Direction = Direct(
                    segment.Direction,
                    features,
                    sheet.RegisterFor(segment.SpeakerId),
                    Reference(segment.Direction, clips))
            });
        }

        return new NarrationRequest
        {
            Segments = sendable,
            Voices = voices,
            Language = language,
            Rate = rate
        };
    }

    /// <summary>Which voices a run needs, so the caller can read exactly those
    /// and no more off disk.</summary>
    public static IReadOnlyList<string> VoicesNeeded(
        IReadOnlyList<NarrationSegment> segments, VoiceCastSheet sheet)
        => [.. segments
            .Select(s => VoiceCast.Resolve(sheet, s.SpeakerId))
            .Where(v => v != null)
            .Select(v => v!)
            .Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// One segment's direction, in whichever form the engine understands.
    /// </summary>
    /// <param name="register">The speaker's standing register, added to the
    /// line's own direction. Null when they have none.</param>
    /// <param name="reference">The audio of the clip this line points at, when
    /// it points at one and the engine can take it.</param>
    public static SdkDirection Direct(
        VoiceDirection direction,
        VoiceEngineFeatures features,
        IReadOnlyDictionary<string, double>? register = null,
        byte[]? reference = null)
    {
        // A clip the writer pointed at is the most precise thing they can say,
        // so it goes wherever the engine can take it - including to an engine
        // that would otherwise be told nothing.
        var clip = features.HasFlag(VoiceEngineFeatures.EmotionReference) ? reference : null;

        // Nothing to say, or an engine that would rather listen. Both get a
        // direction naming the emotion and carrying no instruction, so an
        // engine that ignores it loses nothing and one that logs it can still
        // say what the host thought.
        if ((features & AnyDirection) == 0
            || features.HasFlag(VoiceEngineFeatures.EmotionInferred))
        {
            return new SdkDirection
            {
                Key = direction.Key,
                Source = direction.Source.ToString(),
                ReferenceAudio = clip
            };
        }

        return new SdkDirection
        {
            Key = direction.Key,
            Vector = features.HasFlag(VoiceEngineFeatures.EmotionVector)
                ? EmotionDirector.WithRegister(direction.Vector, register)
                : new Dictionary<string, double>(),
            Instruction = features.HasFlag(VoiceEngineFeatures.EmotionInstruction)
                ? Instruct(direction)
                : string.Empty,
            Source = direction.Source.ToString(),
            ReferenceAudio = clip
        };
    }

    /// <summary>Which reference clips a run points at, so the caller reads
    /// exactly those off disk and no more.</summary>
    public static IReadOnlyList<string> ClipsNeeded(IReadOnlyList<NarrationSegment> segments)
        => [.. segments
            .Select(s => s.Direction.ReferenceClip)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!)
            .Distinct(StringComparer.Ordinal)];

    private static byte[]? Reference(
        VoiceDirection direction, IReadOnlyDictionary<string, byte[]>? clips)
        => direction.ReferenceClip is { Length: > 0 } name && clips != null
            ? clips.GetValueOrDefault(name)
            : null;

    /// <summary>
    /// The direction as a sentence.
    ///
    /// The speech verb goes in where the prose supplied one, because the
    /// writer's own word is more precise than the emotion it was mapped to:
    /// "snapped", "whispered" and "hissed" all land on the same key and are
    /// three different performances.
    /// </summary>
    private static string Instruct(VoiceDirection direction)
    {
        var key = string.IsNullOrWhiteSpace(direction.Key)
            ? EmotionDirector.NeutralKey
            : direction.Key.Trim();

        return string.IsNullOrWhiteSpace(direction.Evidence)
            ? $"Read this {key}."
            : $"Read this {key}, as though {direction.Evidence!.Trim()}.";
    }

    /// <summary>
    /// The narrator's brief, from the book rather than from a Codex entry.
    ///
    /// A narrator is not a character and has no entry to read: what decides how
    /// a book should be narrated is what kind of book it is and who is telling
    /// it. Person and tense are the writer's own declarations, and the logline
    /// says what the book is - all three are things they already wrote down.
    ///
    /// As with a character, this describes the instrument. The story's mood
    /// belongs to the per-line direction, so nothing here is taken from the
    /// premise paragraph, which is where the drama lives.
    /// </summary>
    public static string NarratorBrief(BookData? book)
    {
        if (book == null)
            return string.Empty;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(book.NarrativePerson))
            parts.Add($"Narration: {book.NarrativePerson.Trim()}");
        if (!string.IsNullOrWhiteSpace(book.Tense))
            parts.Add($"Tense: {book.Tense.Trim()}");
        if (!string.IsNullOrWhiteSpace(book.Premise?.Logline))
            parts.Add($"The book: {book.Premise!.Logline.Trim()}");

        return string.Join(". ", parts);
    }
}
