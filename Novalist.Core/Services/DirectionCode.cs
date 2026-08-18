using System.Globalization;
using System.Text;

namespace Novalist.Core.Services;

/// <summary>
/// What the writer set on one line, taken apart.
/// </summary>
/// <param name="Key">A lexicon emotion, or empty for "read this plainly".</param>
/// <param name="Vector">Dimensions the writer pushed by hand, or null when they
/// picked a name and left the numbers alone.</param>
/// <param name="ReferenceClip">A clip to perform this line in the manner of, or
/// null. Named rather than carried: the audio lives in the cache.</param>
public sealed record DirectionCode(
    string Key,
    IReadOnlyDictionary<string, double>? Vector,
    string? ReferenceClip);

/// <summary>
/// The one string a hand-written direction is stored as.
///
/// Directions live in <c>SceneData.DialogueDirections</c>, a map of content hash
/// to string, beside the speaker overrides - so a direction survives a rewrite
/// of the paragraph next to it, for the same reason and by the same mechanism.
/// That map was a name and nothing else, which was enough until the writer
/// needed a delivery the sixteen names do not have.
///
/// Rather than a second side-car with its own lifetime, the extra forms are
/// encoded into the same string, prefixed so they cannot be mistaken for a
/// lexicon key:
///
/// <list type="bullet">
/// <item><c>angry</c> - a name from the lexicon, which is what most lines are.</item>
/// <item><c>v:happy=0.8,surprised=0.3</c> - the eight sliders, pushed by hand.</item>
/// <item><c>ref:a1b2c3.wav</c> - like that line, the one already rendered the
/// way it was wanted.</item>
/// <item><c>v:angry=0.9|ref:a1b2.wav</c> - both, for an engine that takes one
/// and not the other.</item>
/// <item>the empty string - read this plainly, which is a decision and not the
/// absence of one.</item>
/// </list>
///
/// Anything unrecognised is read as a lexicon key, which is what every direction
/// stored before this existed is.
/// </summary>
public static class DirectionCodec
{
    /// <summary>Marks a hand-pushed vector.</summary>
    private const string VectorPrefix = "v:";

    /// <summary>Marks an emotion-reference clip.</summary>
    private const string ReferencePrefix = "ref:";

    /// <summary>Separates the two when a line carries both.</summary>
    private const char Both = '|';

    /// <summary>
    /// Reads a stored direction.
    /// </summary>
    /// <returns>Null when nothing was stored - which is not the same as an empty
    /// string, and must not be: one means the writer said nothing and the prose
    /// decides, the other means the writer said "plainly".</returns>
    public static DirectionCode? Decode(string? stored)
    {
        if (stored == null)
            return null;

        var text = stored.Trim();
        if (text.Length == 0)
            return new DirectionCode(string.Empty, null, null);

        // Every form goes through the same split, including a bare name - which
        // is one piece carrying no prefix. Special-casing the bare name on the
        // way in is what made "angry|v:angry=0.9" decode as an emotion called
        // "angry|v:angry=0.9".
        string? key = null;
        Dictionary<string, double>? vector = null;
        string? clip = null;

        foreach (var part in text.Split(Both, StringSplitOptions.RemoveEmptyEntries))
        {
            var piece = part.Trim();
            if (piece.StartsWith(ReferencePrefix, StringComparison.Ordinal))
            {
                var name = piece[ReferencePrefix.Length..].Trim();
                if (name.Length > 0)
                    clip = name;
                continue;
            }
            if (!piece.StartsWith(VectorPrefix, StringComparison.Ordinal))
            {
                key = piece;
                continue;
            }

            vector = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var pair in piece[VectorPrefix.Length..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var at = pair.IndexOf('=');
                if (at <= 0)
                    continue;
                var dimension = pair[..at].Trim();
                if (!EmotionDirector.Dimensions.Contains(dimension))
                    continue;
                if (!double.TryParse(
                        pair[(at + 1)..].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var value))
                {
                    continue;
                }
                // Never a source of out-of-range input to an engine, whoever
                // wrote the file and however they wrote it.
                vector[dimension] = Math.Clamp(value, 0, 1);
            }
            if (vector.Count == 0)
                vector = null;
        }

        return new DirectionCode(key ?? string.Empty, vector, clip);
    }

    /// <summary>Writes a direction back out.</summary>
    public static string Encode(
        string? key,
        IReadOnlyDictionary<string, double>? vector = null,
        string? referenceClip = null)
    {
        var pieces = new List<string>(2);

        if (vector is { Count: > 0 })
        {
            var builder = new StringBuilder(VectorPrefix);
            // In the dimension order the engine declares, so two identical
            // directions are the same string and the render's fingerprint does
            // not change because a dictionary enumerated differently.
            var first = true;
            foreach (var dimension in EmotionDirector.Dimensions)
            {
                if (!vector.TryGetValue(dimension, out var value) || value <= 0)
                    continue;
                if (!first)
                    builder.Append(',');
                first = false;
                builder.Append(dimension).Append('=')
                    .Append(Math.Clamp(value, 0, 1).ToString("0.##", CultureInfo.InvariantCulture));
            }
            if (!first)
                pieces.Add(builder.ToString());
        }

        if (!string.IsNullOrWhiteSpace(referenceClip))
            pieces.Add(ReferencePrefix + referenceClip.Trim());

        if (pieces.Count == 0)
            return key?.Trim() ?? string.Empty;

        // The name rides along so a view has something to show, and so an engine
        // that takes neither of the two still gets a word.
        if (!string.IsNullOrWhiteSpace(key))
            pieces.Insert(0, key.Trim());
        return string.Join(Both, pieces);
    }
}
