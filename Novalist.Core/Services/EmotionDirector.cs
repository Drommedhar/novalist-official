using System.Text.RegularExpressions;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>
/// What a segment's direction was worked out from. Ordered weakest to
/// strongest, the same way <see cref="DialogueConfidence"/> is, so the view can
/// say how much to trust it and the writer can see when they are overruling a
/// guess rather than a fact.
/// </summary>
public enum DirectionSource
{
    /// <summary>Nothing in the prose or the scene said anything. Read plainly.</summary>
    None,

    /// <summary>The scene's own emotion field - the writer's summary of what
    /// this scene is, applied to a line that says nothing more specific.</summary>
    Scene,

    /// <summary>The speech verb in the dialogue tag. "She snapped" is a
    /// statement about delivery the writer already made.</summary>
    Verb,

    /// <summary>The writer directed this line by hand.</summary>
    Writer
}

/// <summary>
/// How one segment should be read: which of the lexicon's emotions, as a vector
/// a speech model can take, and on what evidence.
/// </summary>
/// <param name="Key">One of the emotion keys the writing language declares, so
/// the renderer localizes it through the same <c>emotion.*</c> strings the
/// Inspector uses.</param>
/// <param name="Vector">The emotion as engine input. Dimensions are
/// <see cref="Dimensions"/>; absent dimensions are zero.</param>
/// <param name="Evidence">The speech verb that produced this, when one did. The
/// view shows it so "angry" reads as "angry, because you wrote snapped".</param>
/// <param name="ReferenceClip">A clip to perform this line in the manner of,
/// named rather than carried - the audio lives in the cache. Null on almost
/// every line, which is what it should be.</param>
public sealed record VoiceDirection(
    string Key,
    IReadOnlyDictionary<string, double> Vector,
    DirectionSource Source,
    string? Evidence = null,
    string? ReferenceClip = null);

/// <summary>The verb matcher for one writing language, compiled once per scan.</summary>
public sealed record DirectionLanguage(Regex Verbs, IReadOnlyDictionary<string, string> Emotions);

/// <summary>
/// Decides how each segment of a scene should be performed.
///
/// Nothing here calls a model. A reading takes its direction from what the
/// writer already wrote down, in one order:
///
/// <list type="number">
/// <item>a direction the writer set on this line, which wins outright,</item>
/// <item>the speech verb in the dialogue tag - "she snapped", "er flüsterte",</item>
/// <item>the scene's own emotion field, scaled by its intensity,</item>
/// <item>nothing, which is neutral rather than a guess.</item>
/// </list>
///
/// The sixteen emotion keys are the lexicon's, shared with the Inspector's
/// scene analysis, so a scene marked "tense" and a line tagged "snapped" speak
/// the same vocabulary. <see cref="Vector"/> turns a key into the eight
/// dimensions speech models take, which is the only place the two ever have to
/// be reconciled.
/// </summary>
public static class EmotionDirector
{
    /// <summary>The reading with nothing said about it. Always a valid key: the
    /// lexicons all declare it, and a language that somehow does not still gets
    /// a usable direction rather than a null.</summary>
    public const string NeutralKey = "neutral";

    /// <summary>
    /// What a hand-pushed vector is called.
    ///
    /// Not a lexicon key and deliberately not one: the writer reached for the
    /// sliders because none of the sixteen names was the delivery they wanted,
    /// and labelling the result with the nearest name would be putting the word
    /// back that they had just refused.
    /// </summary>
    public const string CustomKey = "custom";

    /// <summary>
    /// The dimensions a speech model is directed in.
    ///
    /// This set is not ours - it is what emotion-controllable engines take, and
    /// the sixteen lexicon keys are projected onto it by <see cref="Vector"/>.
    /// Keeping the projection in one table means an engine never sees Novalist's
    /// vocabulary and the Inspector never sees the engine's.
    /// </summary>
    public static readonly IReadOnlyList<string> Dimensions =
    [
        "happy", "angry", "sad", "afraid", "disgusted", "melancholic", "surprised", "calm"
    ];

    /// <summary>
    /// The most an engine will take across every dimension at once, because a
    /// request for everything is a request for nothing.
    ///
    /// Held by the table rather than by a clamp at render time: every key's
    /// blend, at the largest scale intensity can apply, stays under this, and a
    /// test asserts it over the shipped emotion keys. A runtime clamp would let
    /// a bad edit through and quietly rescale it into a different emotion.
    /// </summary>
    public const double MaxVectorSum = 1.5;

    /// <summary>
    /// How much of a scene's emotion the narration carries.
    ///
    /// The prose describing a drowning should not be read flat, and it should
    /// not be acted either - a narrator performing every clause at the pitch of
    /// the dialogue is the thing that makes an audiobook exhausting. Dialogue
    /// takes the emotion whole; narration takes this much of it.
    /// </summary>
    public const double NarrationMagnitude = 0.6;

    /// <summary>Intensity runs -10 to 10 on the scene. It scales a direction
    /// between these, so a scene the writer called calm is read less emphatically
    /// than one they called unbearable, without either becoming a different
    /// emotion.</summary>
    private const double MinIntensityScale = 0.7;
    private const double MaxIntensityScale = 1.3;

    /// <summary>
    /// The sixteen lexicon keys as vectors.
    ///
    /// Blends rather than single primaries, because most of the sixteen are
    /// blends: "desperate" is fear and grief together, and rendering it as
    /// either alone loses the half that makes it desperate. Values are a
    /// starting point tuned by ear, not a measurement - they are here, in one
    /// table, so tuning them is one edit.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string Dimension, double Weight)[]> Table =
        new Dictionary<string, (string, double)[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["neutral"] = [("calm", 0.6)],
            ["tense"] = [("afraid", 0.4), ("angry", 0.2), ("calm", 0.1)],
            ["joyful"] = [("happy", 0.9)],
            ["melancholic"] = [("melancholic", 0.8)],
            ["angry"] = [("angry", 0.9)],
            ["fearful"] = [("afraid", 0.9)],
            ["romantic"] = [("calm", 0.4), ("happy", 0.3)],
            ["mysterious"] = [("calm", 0.5), ("afraid", 0.2)],
            ["humorous"] = [("happy", 0.6), ("surprised", 0.2)],
            ["hopeful"] = [("happy", 0.4), ("calm", 0.3)],
            ["desperate"] = [("afraid", 0.5), ("sad", 0.4)],
            ["peaceful"] = [("calm", 0.9)],
            ["chaotic"] = [("surprised", 0.5), ("angry", 0.3), ("afraid", 0.2)],
            ["sorrowful"] = [("sad", 0.9)],
            ["triumphant"] = [("happy", 0.7), ("surprised", 0.3)],
            ["somber"] = [("sad", 0.5), ("calm", 0.4)]
        };

    /// <summary>
    /// Compiles the verb matcher for a language. A language that ships no
    /// verb-to-emotion map matches nothing, so its lines fall through to the
    /// scene's emotion - which is exactly right, and is what every language did
    /// before the map existed.
    /// </summary>
    public static DirectionLanguage BuildLanguage(SceneAnalysisLexicon? lexicon)
    {
        if (lexicon == null || lexicon.SpeechVerbEmotions.Count == 0)
            return new DirectionLanguage(MatchNothing(), new Dictionary<string, string>());

        // Longest first, so "murmured back" is not matched as "murmured" and
        // given the wrong half of its meaning.
        var alternation = string.Join("|", lexicon.SpeechVerbEmotions.Keys
            .OrderByDescending(v => v.Length)
            .Select(Regex.Escape));

        var pattern = lexicon.WordBoundaries
            ? $@"(?<![\p{{L}}\p{{N}}])({alternation})(?![\p{{L}}\p{{N}}])"
            : $"({alternation})";

        return new DirectionLanguage(
            new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
            lexicon.SpeechVerbEmotions);
    }

    private static Regex MatchNothing() => new("(?!)", RegexOptions.CultureInvariant);

    /// <summary>
    /// The direction for one segment.
    /// </summary>
    /// <param name="writerKey">A direction set by hand on this line. Wins
    /// outright; a blank string means the writer explicitly cleared the line and
    /// wants it read plainly, which is different from never having said.</param>
    /// <param name="contextAfter">The prose after the quote - where a dialogue
    /// tag usually sits, so it is read first.</param>
    /// <param name="contextBefore">The prose before it, read second.</param>
    /// <param name="sceneEmotion">The scene's emotion field, or null.</param>
    /// <param name="sceneIntensity">The scene's -10..10 intensity, or null.</param>
    /// <param name="magnitude">Scales the whole vector. <see cref="NarrationMagnitude"/>
    /// for narration; 1 for a spoken line.</param>
    public static VoiceDirection Resolve(
        string? writerKey,
        string? contextAfter,
        string? contextBefore,
        string? sceneEmotion,
        int? sceneIntensity,
        DirectionLanguage language,
        double magnitude = 1.0)
    {
        // The writer had the last word, including when the word was "plainly".
        if (writerKey != null)
        {
            var code = DirectionCodec.Decode(writerKey)!;

            // Sliders they pushed themselves are the numbers, untouched. Not
            // scaled by the scene's intensity and not reduced for narration:
            // both would move a value the writer set by hand while the screen
            // went on showing what they set.
            if (code.Vector != null)
            {
                return new VoiceDirection(
                    code.Key.Length == 0 ? CustomKey : code.Key,
                    Held(code.Vector),
                    DirectionSource.Writer,
                    null,
                    code.ReferenceClip);
            }

            return code.Key.Length == 0
                ? new VoiceDirection(
                    NeutralKey, Vector(NeutralKey, null, magnitude), DirectionSource.Writer,
                    null, code.ReferenceClip)
                : new VoiceDirection(
                    code.Key, Vector(code.Key, sceneIntensity, magnitude), DirectionSource.Writer,
                    null, code.ReferenceClip);
        }

        // The tag follows the quote far more often than it precedes it, and when
        // both carry a verb the following one is the one attached to this line.
        var verb = MatchVerb(contextAfter, language) ?? MatchVerb(contextBefore, language);
        if (verb != null)
        {
            return new VoiceDirection(
                verb.Value.Emotion,
                Vector(verb.Value.Emotion, sceneIntensity, magnitude),
                DirectionSource.Verb,
                verb.Value.Verb);
        }

        var scene = sceneEmotion?.Trim();
        if (!string.IsNullOrEmpty(scene))
            return new VoiceDirection(scene, Vector(scene, sceneIntensity, magnitude), DirectionSource.Scene);

        return new VoiceDirection(NeutralKey, Vector(NeutralKey, null, magnitude), DirectionSource.None);
    }

    /// <summary>The mapped speech verb in a stretch of prose, with the emotion it
    /// carries. Null when the prose has no verb the language maps.</summary>
    private static (string Verb, string Emotion)? MatchVerb(string? context, DirectionLanguage language)
    {
        if (string.IsNullOrEmpty(context))
            return null;

        var match = language.Verbs.Match(context);
        if (!match.Success)
            return null;

        var verb = match.Groups[1].Value.ToLowerInvariant();
        return language.Emotions.TryGetValue(verb, out var emotion) ? (verb, emotion) : null;
    }

    /// <summary>
    /// An emotion key as engine input, scaled by the scene's intensity and by
    /// <paramref name="magnitude"/>, then held under <see cref="MaxVectorSum"/>.
    /// An unknown key reads as neutral rather than as nothing.
    /// </summary>
    public static IReadOnlyDictionary<string, double> Vector(
        string? key, int? intensity, double magnitude = 1.0)
    {
        if (key == null || !Table.TryGetValue(key, out var parts))
            parts = Table[NeutralKey];

        var scale = IntensityScale(intensity) * Math.Clamp(magnitude, 0, 1);
        return parts.ToDictionary(
            p => p.Dimension, p => Math.Round(p.Weight * scale, 3), StringComparer.Ordinal);
    }

    /// <summary>
    /// Adds a character's standing register to a line's direction.
    ///
    /// For somebody who is always more clipped, or warmer, or wearier than the
    /// prose bothers to say every time - a standing note to the actor rather
    /// than a direction on any one line. Added rather than replacing, so a
    /// furious line from a habitually flat character is still furious, and held
    /// under the same ceiling every other vector is.
    /// </summary>
    public static IReadOnlyDictionary<string, double> WithRegister(
        IReadOnlyDictionary<string, double> vector,
        IReadOnlyDictionary<string, double>? register)
    {
        if (register == null || register.Count == 0)
            return vector;

        var combined = new Dictionary<string, double>(vector, StringComparer.Ordinal);
        foreach (var (dimension, offset) in register)
        {
            if (!Dimensions.Contains(dimension) || offset == 0)
                continue;
            combined.TryGetValue(dimension, out var was);
            combined[dimension] = was + offset;
        }
        return Held(combined);
    }

    /// <summary>
    /// A vector an engine will take: every dimension inside 0 to 1, and the
    /// whole under <see cref="MaxVectorSum"/>.
    ///
    /// Scaled down whole rather than truncated dimension by dimension, because
    /// a blend clipped unevenly is a different emotion - two parts grief to one
    /// part fear becomes equal parts, and the line stops being desperate.
    /// </summary>
    internal static IReadOnlyDictionary<string, double> Held(
        IReadOnlyDictionary<string, double> vector)
    {
        var held = vector
            .Where(p => p.Value > 0)
            .ToDictionary(p => p.Key, p => Math.Min(p.Value, 1.0), StringComparer.Ordinal);

        var sum = held.Values.Sum();
        if (sum > MaxVectorSum)
        {
            var scale = MaxVectorSum / sum;
            foreach (var dimension in held.Keys.ToArray())
                held[dimension] *= scale;
        }

        foreach (var dimension in held.Keys.ToArray())
            held[dimension] = Math.Round(held[dimension], 3);
        return held;
    }

    /// <summary>Maps the scene's -10..10 intensity onto a magnitude multiplier.
    /// An unrated scene reads at 1 rather than at nothing.</summary>
    private static double IntensityScale(int? intensity)
    {
        if (intensity is not { } value)
            return 1.0;

        var clamped = Math.Clamp(value, -10, 10);
        var fraction = (clamped + 10) / 20.0;
        return MinIntensityScale + fraction * (MaxIntensityScale - MinIntensityScale);
    }
}
