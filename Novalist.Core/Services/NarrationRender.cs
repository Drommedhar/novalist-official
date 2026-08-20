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
    /// <param name="placeAt">Where in the book each segment is, by its index in
    /// <paramref name="segments"/>, so a voice the writer set for part of the
    /// book wins over the character's standing one. Null - and a null answer -
    /// mean the standing voice, which is what every reading was before a
    /// character could sound like themselves at two different ages.</param>
    public static NarrationRequest Build(
        IReadOnlyList<NarrationSegment> segments,
        VoiceCastSheet sheet,
        IReadOnlyDictionary<string, byte[]> voices,
        VoiceEngineFeatures features,
        string language,
        double rate = 1.0,
        IReadOnlyDictionary<string, byte[]>? clips = null,
        Func<int, NarrationPlacement?>? placeAt = null,
        IReadOnlyDictionary<string, string>? voiceReferenceTexts = null)
    {
        var sendable = new List<SdkSegment>(segments.Count);
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            var voiceId = VoiceCast.Resolve(sheet, segment.SpeakerId, placeAt?.Invoke(index));
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
            VoiceReferenceTexts = voiceReferenceTexts == null
                ? new Dictionary<string, string>()
                : voiceReferenceTexts
                    .Where(pair => voices.ContainsKey(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            Language = language,
            Rate = rate
        };
    }

    /// <summary>
    /// Which voices a run needs.
    ///
    /// Every voice the cast names, rather than only the ones this window
    /// mentions. Once a character can sound different in chapter twenty, which
    /// voices a window needs depends on where the window is - and a caller that
    /// worked it out from the speakers alone would read the wrong ones and then
    /// leave those lines unspoken. A cast is a handful of short clips; reading
    /// all of them costs nothing and cannot be wrong.
    /// </summary>
    public static IReadOnlyList<string> VoicesNeeded(
        IReadOnlyList<NarrationSegment> segments, VoiceCastSheet sheet)
        => VoiceCast.AllVoices(sheet);

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

    /// <summary>
    /// One call to the engine, and how many lines of the reading it covers.
    /// </summary>
    /// <param name="Covers">Always at least one. The caller counts progress and
    /// unspoken lines in lines rather than in calls, so a joined run has to say
    /// how many it stands for.</param>
    public sealed record NarrationJoin(NarrationSegment Segment, int Covers);

    /// <summary>
    /// Joins consecutive sentences that one voice says in one breath.
    ///
    /// A sentence at a time is right for the reading: the highlight follows the
    /// voice, correcting a line costs a line, and hearing one line while you
    /// write it is a line. It is wrong for a recording. A cloning model starts
    /// each call afresh from the reference clip with no memory of the sentence
    /// before it, so pitch, pace and energy reset at every full stop - and a
    /// paragraph stitched from four of those sounds like four readings rather
    /// than one narrator. Read in one pass it has the cadence a paragraph
    /// actually has.
    ///
    /// It is slower, measured, not faster - there is no per-call overhead worth
    /// reclaiming. This buys continuity and nothing else.
    ///
    /// Joined only where every reason to keep them apart is absent:
    ///
    /// <list type="bullet">
    /// <item>the same speaker - two characters sharing one voice are still two
    /// people, so this is the speaker rather than the voice;</item>
    /// <item>the same direction for engines that accept explicit direction;
    /// inferred engines instead need the neighbouring sentences together so
    /// they have enough prose to discover the delivery themselves;</item>
    /// <item>narration with narration and dialogue with dialogue, never across
    /// the quote marks;</item>
    /// <item>and within <paramref name="maxCharacters"/>, because a line longer
    /// than a model will say in one breath comes back cut off mid-word - which
    /// is what splitting was for in the first place.</item>
    /// </list>
    /// </summary>
    /// <param name="maxCharacters">Nothing joined at or below zero, which is the
    /// reading's setting.</param>
    /// <param name="inferDirection">Whether the engine derives performance
    /// from the prose. When true, detected direction does not split a run because
    /// it is not sent to the engine.</param>
    public static IReadOnlyList<NarrationJoin> Joined(
        IReadOnlyList<NarrationSegment> segments,
        int maxCharacters,
        bool inferDirection = false)
    {
        var joined = new List<NarrationJoin>(segments.Count);
        var run = new List<NarrationSegment>();

        void Close()
        {
            if (run.Count == 0)
                return;
            joined.Add(new NarrationJoin(
                run.Count == 1
                    ? run[0]
                    // The first line's key, direction and speaker stand for the
                    // run: it is the line the recording reaches first, and every
                    // other line in the run agreed with it or would not be here.
                    : run[0] with { Text = string.Join(' ', run.Select(s => s.Text)) },
                run.Count));
            run.Clear();
        }

        foreach (var segment in segments)
        {
            if (run.Count > 0 && !Continues(run, segment, maxCharacters, inferDirection))
                Close();
            run.Add(segment);
        }
        Close();
        return joined;
    }

    /// <summary>Whether this line is still the same breath as the run before
    /// it.</summary>
    private static bool Continues(
        List<NarrationSegment> run,
        NarrationSegment next,
        int maxCharacters,
        bool inferDirection)
    {
        if (maxCharacters <= 0)
            return false;

        var first = run[0];
        if (first.Kind != next.Kind)
            return false;
        // The speaker, not the voice they resolve to. Two segments with one
        // speaker resolve to one voice by construction - the sheet and the place
        // in the book are the same for every line of a run - so comparing the
        // voices would be asking the same question twice.
        if (!string.Equals(first.SpeakerId ?? string.Empty, next.SpeakerId ?? string.Empty,
                StringComparison.Ordinal))
            return false;
        if (!inferDirection && !SameDirection(first.Direction, next.Direction))
            return false;

        // The joining space counts: it is a character the model is given.
        var length = run.Sum(s => s.Text.Length) + run.Count + next.Text.Length;
        return length <= maxCharacters;
    }

    /// <summary>Whether two lines are directed the same. One call performs one
    /// delivery, so anything less than identical has to stay its own call.</summary>
    private static bool SameDirection(VoiceDirection a, VoiceDirection b)
    {
        if (!string.Equals(a.Key, b.Key, StringComparison.Ordinal))
            return false;
        if (!string.Equals(a.ReferenceClip ?? string.Empty, b.ReferenceClip ?? string.Empty,
                StringComparison.Ordinal))
            return false;
        if (a.Vector.Count != b.Vector.Count)
            return false;
        foreach (var (name, weight) in a.Vector)
        {
            if (!b.Vector.TryGetValue(name, out var theirs) || Math.Abs(theirs - weight) > 0.0001)
                return false;
        }
        return true;
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
    /// <param name="lexicon">The writing language, so the writer's own words are
    /// filtered before the labels are put in front of them.
    ///
    /// The labels have to go on last. "Tense" is one of the sixteen emotion
    /// keys, so filtering the finished sentence deleted the word and left the
    /// value dangling after a colon - the brief read "Narration: third limited.
    /// : past." and the test that should have caught it asserted only that
    /// "past" was somewhere in the string.</param>
    public static string NarratorBrief(BookData? book, SceneAnalysisLexicon? lexicon = null)
    {
        if (book == null)
            return string.Empty;

        // Point of view, tense and plot describe the manuscript, not the
        // instrument reading it. A voice designer needs acoustic attributes.
        // This is deliberately a neutral editable baseline; the prose supplies
        // the changing performance later.
        return "Adult audiobook narrator. Balanced mid-range pitch. Clear natural timbre. "
            + "Precise articulation. Restrained, unhurried cadence. Neutral baseline. "
            + "Close, dry studio sound.";
    }
}
