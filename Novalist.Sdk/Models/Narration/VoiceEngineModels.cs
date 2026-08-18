using System;
using System.Collections.Generic;

namespace Novalist.Sdk.Models.Narration;

/// <summary>
/// What a voice engine can be asked for.
///
/// Consumers branch on these rather than on which engine is installed. The three
/// emotion flags are deliberately three and not one: engines take direction in
/// incompatible ways, and an engine that infers affect from the script is better
/// left to read the line than told about it.
/// </summary>
[Flags]
public enum VoiceEngineFeatures
{
    None = 0,

    /// <summary>A voice can be designed from a text description alone, with no
    /// reference recording. This is what lets a character's voice come from
    /// their Codex entry - and it is what keeps a real person's likeness out of
    /// the feature entirely.</summary>
    DesignFromDescription = 1 << 0,

    /// <summary>A voice can be cloned from a supplied recording.</summary>
    CloneFromSample = 1 << 1,

    /// <summary>Takes <see cref="VoiceDirection.Vector"/> - emotion as numbers
    /// across named dimensions.</summary>
    EmotionVector = 1 << 2,

    /// <summary>Takes <see cref="VoiceDirection.Instruction"/> - emotion as a
    /// sentence of plain language.</summary>
    EmotionInstruction = 1 << 3,

    /// <summary>Reads affect off the script itself and needs no direction at
    /// all. The host sends none rather than overriding what the model heard in
    /// the words.</summary>
    EmotionInferred = 1 << 4,

    /// <summary>Clips arrive before the whole run has finished, so a reading can
    /// start before it is fully rendered.</summary>
    Streaming = 1 << 5,

    /// <summary>Identity and prosody carry across the segments of one request.
    /// Without it the host crossfades the joins, because a chapter rendered line
    /// by line has audible seams.</summary>
    ContinuousContext = 1 << 6,

    /// <summary>Runs without a GPU. Slower, and the writer is told so before
    /// they wait rather than after.</summary>
    RunsOnCpu = 1 << 7,

    /// <summary>
    /// Takes <see cref="VoiceDirection.ReferenceAudio"/> - a clip to perform
    /// this line in the manner of.
    ///
    /// The input of last resort and the most precise one there is. Some
    /// deliveries have no name in any vocabulary, and the writer who has already
    /// heard the one they wanted can point at it instead of describing it.
    /// </summary>
    EmotionReference = 1 << 8
}

/// <summary>
/// A description of a voice, for designing one.
///
/// Everything here describes the <b>instrument</b>: what does not change about
/// how somebody sounds. Nothing here describes a mood. An emotional word in a
/// design prompt is baked into the timbre and cannot be got back out at render
/// time, which produces exactly the fixed-mood voice the two-stage design exists
/// to prevent - so the host strips them before a brief is ever built.
/// </summary>
public sealed class VoiceBrief
{
    /// <summary>The id the designed voice should be stored under. An engine that
    /// is handed an id it already knows replaces that voice.</summary>
    public string VoiceId { get; init; } = string.Empty;

    /// <summary>Who this is, for the engine's benefit and for diagnostics.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>The instrument in plain language: age, gender, build, accent,
    /// pitch, pace at rest, vocal habits, the register they speak in when
    /// nothing is wrong.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>A few lines this character actually speaks, taken from the
    /// manuscript. How somebody talks is a better description of their voice
    /// than any adjective, and the writer already wrote it.</summary>
    public IReadOnlyList<string> SampleLines { get; init; } = [];

    /// <summary>The book's language as a BCP-47 tag, so an engine that supports
    /// several does not have to guess.</summary>
    public string Language { get; init; } = "en";

    /// <summary>Optional seed, for an engine that can use one. Design is not
    /// deterministic even with a seed, which is why the result is stored as
    /// audio rather than re-derived.</summary>
    public int? Seed { get; init; }
}

/// <summary>The voice an engine designed.</summary>
public sealed class VoiceDesignResult
{
    /// <summary>The id the voice is known by from now on - normally the brief's,
    /// which the engine may normalize.</summary>
    public string VoiceId { get; init; } = string.Empty;

    /// <summary>The reference audio that <b>is</b> this voice. Stored by the
    /// host and handed back on every later render.</summary>
    public byte[] ReferenceAudio { get; init; } = [];

    /// <summary>Container of <see cref="ReferenceAudio"/> ("wav", "mp3", "opus"),
    /// so the host can name the file it writes without sniffing bytes.</summary>
    public string AudioFormat { get; init; } = "wav";

    /// <summary>Sample rate of the reference audio, in hertz.</summary>
    public int SampleRate { get; init; }

    /// <summary>What the engine understood it was asked for, where it says. Shown
    /// beside the voice so a re-design is an edit rather than a guess.</summary>
    public string ResolvedDescription { get; init; } = string.Empty;
}

/// <summary>
/// How one segment should be performed.
///
/// Carried beside the voice, never inside the text. A direction is a parameter:
/// concatenating it into the string handed to a model is one bad prompt away
/// from the word "angry" being read out in the middle of a sentence. An engine
/// that genuinely wants in-band tags adds them in its own adapter.
/// </summary>
public sealed class VoiceDirection
{
    /// <summary>The emotion's name in the book's own vocabulary ("angry",
    /// "melancholic"). Always set, so an engine taking any of the three input
    /// styles has something to work from.</summary>
    public string Key { get; init; } = "neutral";

    /// <summary>The same emotion as numbers, for
    /// <see cref="VoiceEngineFeatures.EmotionVector"/>. Dimension names are the
    /// engine's own vocabulary; absent dimensions are zero.</summary>
    public IReadOnlyDictionary<string, double> Vector { get; init; }
        = new Dictionary<string, double>();

    /// <summary>The same emotion as a sentence, for
    /// <see cref="VoiceEngineFeatures.EmotionInstruction"/> - "read this angrily,
    /// as though snapped".</summary>
    public string Instruction { get; init; } = string.Empty;

    /// <summary>Where the direction came from: "Writer", "Verb", "Scene" or
    /// "None". An engine may use it to weight how firmly to take the direction -
    /// a scene-wide default is a weaker statement than a line the writer set.</summary>
    public string Source { get; init; } = "None";

    /// <summary>
    /// A clip to perform this line in the manner of, for
    /// <see cref="VoiceEngineFeatures.EmotionReference"/>. Null when the writer
    /// pointed at nothing, which is almost always.
    ///
    /// The audio itself rather than a path: the engine may be in another
    /// process, and a file name only means something on the machine that wrote
    /// it. It carries emotion only - the voice is still the designed one.
    /// </summary>
    public byte[]? ReferenceAudio { get; init; }

    /// <summary>The format of <see cref="ReferenceAudio"/>, when there is any.</summary>
    public string ReferenceFormat { get; init; } = "wav";
}

/// <summary>One stretch of the book to be spoken.</summary>
public sealed class NarrationSegment
{
    /// <summary>Identity within the request, echoed back on the clip so the host
    /// can match audio to text without relying on ordering.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>The words to speak. Never carries direction markup.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>The designed voice to speak them in.</summary>
    public string VoiceId { get; init; } = string.Empty;

    /// <summary>True when this is somebody speaking, false for the prose around
    /// it. An engine may perform the two differently.</summary>
    public bool IsDialogue { get; init; }

    /// <summary>How to perform it.</summary>
    public VoiceDirection Direction { get; init; } = new();
}

/// <summary>A run of the book to be spoken in one go.</summary>
public sealed class NarrationRequest
{
    /// <summary>The segments, in reading order.</summary>
    public IReadOnlyList<NarrationSegment> Segments { get; init; } = [];

    /// <summary>The reference audio for every voice the segments name, keyed by
    /// voice id. Passed with the request so an engine holds no state between
    /// calls and a cast assembled on another machine still works.</summary>
    public IReadOnlyDictionary<string, byte[]> Voices { get; init; }
        = new Dictionary<string, byte[]>();

    /// <summary>The book's language as a BCP-47 tag.</summary>
    public string Language { get; init; } = "en";

    /// <summary>Reading pace, 1 being the engine's own. The host applies nothing
    /// itself, so an engine that cannot vary pace should say so rather than
    /// silently ignoring this.</summary>
    public double Rate { get; init; } = 1.0;
}

/// <summary>One rendered segment.</summary>
public sealed class NarrationClip
{
    /// <summary>The key of the segment this is the audio for.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>The audio.</summary>
    public byte[] Audio { get; init; } = [];

    /// <summary>Container of <see cref="Audio"/> ("wav", "mp3", "opus").</summary>
    public string AudioFormat { get; init; } = "wav";

    /// <summary>Sample rate, in hertz.</summary>
    public int SampleRate { get; init; }

    /// <summary>How long the clip runs, in milliseconds.</summary>
    public double DurationMs { get; init; }

    /// <summary>Why this segment could not be spoken, when it could not. The
    /// host stops the reading at the last good clip and marks this one, rather
    /// than reading on in the wrong voice.</summary>
    public string? Error { get; init; }
}

/// <summary>Where an engine is right now.</summary>
public sealed class VoiceEngineStatus
{
    /// <summary>Ready to design and to speak.</summary>
    public bool IsReady { get; init; }

    /// <summary>Preparing - a download or a first load is in flight.</summary>
    public bool IsPreparing { get; init; }

    /// <summary>Why it cannot run, in language the writer can act on. Null when
    /// nothing is wrong.</summary>
    public string? Error { get; init; }

    /// <summary>What it is, for diagnostics and for the settings page - the model
    /// and the device it is on.</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>What preparing will cost, in bytes, when the engine knows and has
    /// not already paid it. Shown before the download rather than during.</summary>
    public long? DownloadBytes { get; init; }
}

/// <summary>Progress while an engine gets itself ready.</summary>
public sealed class VoiceEnginePrepare
{
    /// <summary>Coarse step name ("downloading", "loading-model"). A key rather
    /// than a sentence, so the host can localize it.</summary>
    public string Step { get; init; } = string.Empty;

    /// <summary>How far through, 0 to 1, where the engine knows.</summary>
    public double? Fraction { get; init; }

    /// <summary>Free detail for the diagnostics log. Never shown as the only
    /// explanation, because it is not translated.</summary>
    public string Detail { get; init; } = string.Empty;
}
