using System.Text.RegularExpressions;

namespace Novalist.Core.Services;

/// <summary>One place the narration entered somebody else's head.</summary>
public sealed class PovSlip
{
    /// <summary>Who was named, as the prose named them.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The interiority verb that followed: "thought", "knew", "felt".</summary>
    public string Verb { get; init; } = string.Empty;

    /// <summary>Where in the text, so the editor can find it.</summary>
    public int Offset { get; init; }

    /// <summary>The sentence around it, so the writer can judge it without opening the scene.</summary>
    public string Context { get; init; } = string.Empty;
}

/// <summary>What a POV check found in one scene.</summary>
public sealed class PovReport
{
    /// <summary>The POV the scene is written in, or empty when none is recorded.</summary>
    public string Pov { get; init; } = string.Empty;

    /// <summary>
    /// False when the check could not run: no POV on the scene, no other cast
    /// to slip into, or a language with no verb list. A zero from a check that
    /// never ran reads as a clean scene, which is the worse failure.
    /// </summary>
    public bool Checked { get; init; }

    /// <summary>Why it did not run, as a key the UI localises. Empty when it ran.</summary>
    public string SkippedBecause { get; init; } = string.Empty;

    public IReadOnlyList<PovSlip> Slips { get; init; } = [];
}

/// <summary>
/// Head-hopping: a scene written in one character's point of view that reports
/// what somebody else is thinking.
///
/// Novalist detected and stored a POV per scene and let the writer override it,
/// and then nothing ever checked the prose against it - so a third-limited
/// scene marked Mira could describe what Tomas was thinking with no warning.
///
/// Deterministic and offline, like every other report here. It finds a named
/// character followed closely by an interiority verb, which is the shape of the
/// slip; it cannot judge whether the writer meant it, so everything it finds is
/// a question rather than an error.
/// </summary>
public static class PovConsistency
{
    /// <summary>How many characters may sit between the name and the verb.</summary>
    private const int Window = 24;

    /// <summary>Characters of context shown either side of a hit.</summary>
    private const int ContextRadius = 60;

    /// <summary>At most this many, so one runaway scene cannot flood the report.</summary>
    internal const int MaxSlips = 50;

    /// <summary>Why a check did not run.</summary>
    public const string NoPov = "noPov";
    public const string NoOtherCast = "noOtherCast";
    public const string NoVerbList = "noVerbList";

    /// <summary>
    /// Reads the prose against the POV the scene is written in.
    /// </summary>
    /// <param name="text">Plain prose, tags already stripped.</param>
    /// <param name="pov">The POV character's name.</param>
    /// <param name="otherNames">Every other name the scene could slip into.</param>
    /// <param name="language">Which lexicon supplies the interiority verbs.</param>
    public static PovReport Analyze(
        string? text, string? pov, IEnumerable<string>? otherNames, string language)
    {
        var lexicon = SceneAnalysisLexicon.For(language);
        var verbs = lexicon?.InteriorityVerbs ?? [];

        // Each of these is a reason the answer would be meaningless, and each
        // is reported rather than returned as a clean scene.
        if (string.IsNullOrWhiteSpace(pov))
            return new PovReport { SkippedBecause = NoPov };
        if (verbs.Count == 0)
            return new PovReport { Pov = pov.Trim(), SkippedBecause = NoVerbList };

        var others = (otherNames ?? [])
            .Select(n => (n ?? string.Empty).Trim())
            .Where(n => n.Length > 1)
            // The POV character thinking is the scene working as intended.
            .Where(n => !n.Equals(pov.Trim(), StringComparison.CurrentCultureIgnoreCase))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (others.Count == 0)
            return new PovReport { Pov = pov.Trim(), SkippedBecause = NoOtherCast };

        var prose = text ?? string.Empty;
        var slips = new List<PovSlip>();
        var boundaries = lexicon?.WordBoundaries ?? true;

        foreach (var name in others)
        {
            foreach (Match hit in NameRegex(name, boundaries).Matches(prose))
            {
                var from = hit.Index + hit.Length;
                var to = Math.Min(prose.Length, from + Window);
                if (from >= to) continue;

                var after = prose[from..to];
                var verb = verbs.FirstOrDefault(v =>
                    VerbRegex(v, boundaries).IsMatch(after));
                if (verb == null) continue;

                slips.Add(new PovSlip
                {
                    Name = hit.Value,
                    Verb = verb,
                    Offset = hit.Index,
                    Context = Context(prose, hit.Index)
                });
                if (slips.Count >= MaxSlips) break;
            }
            if (slips.Count >= MaxSlips) break;
        }

        return new PovReport
        {
            Pov = pov.Trim(),
            Checked = true,
            // In reading order, because the writer reads the scene in that
            // order and a list sorted by name sends them back and forth.
            Slips = [.. slips.OrderBy(s => s.Offset)]
        };
    }

    private static Regex NameRegex(string name, bool wordBoundaries)
        => new(wordBoundaries ? $@"\b{Regex.Escape(name)}\b" : Regex.Escape(name),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex VerbRegex(string verb, bool wordBoundaries)
        => new(wordBoundaries ? $@"\b{Regex.Escape(verb)}\b" : Regex.Escape(verb),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string Context(string text, int index)
    {
        var start = Math.Max(0, index - ContextRadius);
        var end = Math.Min(text.Length, index + ContextRadius);
        return text[start..end].Replace('\n', ' ').Trim();
    }
}
