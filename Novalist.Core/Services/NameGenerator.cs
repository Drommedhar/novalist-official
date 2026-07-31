namespace Novalist.Core.Services;

/// <summary>One name set: the sounds a naming tradition is built from.</summary>
/// <param name="Key">Stable identifier, used by the UI and never shown.</param>
/// <param name="Onsets">Syllable beginnings, commonest first.</param>
/// <param name="Nuclei">Syllable middles, commonest first.</param>
/// <param name="Codas">Syllable endings, commonest first. An empty entry is an
/// open syllable, which is what keeps a set from sounding uniformly clipped.</param>
/// <param name="Patterns">Syllable counts to build, commonest first.</param>
public sealed record NameSet(
    string Key,
    IReadOnlyList<string> Onsets,
    IReadOnlyList<string> Nuclei,
    IReadOnlyList<string> Codas,
    IReadOnlyList<int> Patterns);

/// <summary>
/// Invented names, offline and repeatable.
///
/// Naming is the highest-frequency thing that stops a draft, and every
/// worldbuilding tool in the field gives a generator away free. Novalist had
/// nothing: no generator, no lists, no assistance beyond aliases.
///
/// The sets are deliberately invented rather than labelled with real cultures.
/// A drop-down promising "Irish names" or "Japanese names" from a handful of
/// syllables misrepresents a real naming tradition, and gets it wrong in a way
/// the writer cannot see. These are sounds, named for how they sound.
/// </summary>
public static class NameGenerator
{
    /// <summary>
    /// The sets that ship. Each list is ordered commonest first, which is what
    /// the obscurity control reads: it is a position in these lists, not a
    /// separate rarity table to keep in step.
    /// </summary>
    public static readonly IReadOnlyList<NameSet> Sets =
    [
        new("soft",
            ["l", "m", "n", "s", "v", "r", "el", "an", "sel", "mir", "thal", "vel", "ys"],
            ["a", "e", "i", "ia", "ae", "io", "ea", "ei"],
            ["", "", "n", "l", "s", "ra", "th", "ne", "wyn", "riel"],
            [2, 2, 3]),

        new("hard",
            ["k", "g", "d", "t", "b", "br", "gr", "dr", "kr", "th", "sk", "vr"],
            ["a", "o", "u", "e", "au", "ou", "ai"],
            ["", "k", "g", "rd", "rn", "lk", "st", "gar", "mund", "rik"],
            [1, 2, 2]),

        new("coastal",
            ["m", "p", "t", "k", "h", "w", "n", "l", "s", "ma", "ka", "ta"],
            ["a", "i", "o", "u", "ai", "au", "oa"],
            ["", "", "", "na", "ka", "ra", "no", "mi"],
            [2, 3, 3]),

        new("old",
            ["v", "z", "kh", "gh", "sh", "th", "x", "zr", "vh", "qu"],
            ["a", "u", "o", "aa", "uu", "ao", "yi"],
            ["", "kh", "th", "r", "sh", "zar", "noth", "eth"],
            [2, 3, 2])
    ];

    /// <summary>
    /// Names from a set, repeatable for a given seed.
    /// </summary>
    /// <param name="setKey">A key from <see cref="Sets"/>. An unknown key uses
    /// the first set rather than failing: a picker out of step with this list
    /// should still produce names.</param>
    /// <param name="count">How many to return. Clamped to 1..100.</param>
    /// <param name="obscurity">
    /// 0 keeps to the commonest sounds in the set; 100 reaches the whole list.
    /// A slider rather than a switch because "unusual but not unpronounceable"
    /// is the setting people actually want.
    /// </param>
    /// <param name="seed">
    /// The same seed gives the same names. A generator that cannot be asked
    /// twice loses the name somebody liked and did not write down.
    /// </param>
    /// <param name="startsWith">Only names beginning with this, ignoring case.</param>
    public static IReadOnlyList<string> Generate(
        string setKey, int count, int obscurity, int seed, string? startsWith = null)
    {
        var set = Sets.FirstOrDefault(s => s.Key == setKey) ?? Sets[0];
        var wanted = Math.Clamp(count, 1, 100);
        var reach = Math.Clamp(obscurity, 0, 100);
        var random = new Random(seed);
        var prefix = (startsWith ?? string.Empty).Trim();

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // A filter can exclude nearly everything a set can make, so the attempts
        // are bounded. Returning four names is better than not returning.
        for (var attempt = 0; attempt < wanted * 200 && names.Count < wanted; attempt++)
        {
            var name = Build(set, random, reach);
            if (prefix.Length > 0
                && !name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase))
                continue;
            if (!seen.Add(name)) continue;
            names.Add(name);
        }

        return names;
    }

    private static string Build(NameSet set, Random random, int reach)
    {
        var syllables = Pick(set.Patterns, random, reach);
        var text = new System.Text.StringBuilder();

        for (var i = 0; i < syllables; i++)
        {
            text.Append(Pick(set.Onsets, random, reach));
            text.Append(Pick(set.Nuclei, random, reach));
            // A coda inside a word tends to collide with the next onset, so
            // only the last syllable closes. Without this the hard set produces
            // things like "Grokdrund".
            if (i == syllables - 1) text.Append(Pick(set.Codas, random, reach));
        }

        var name = text.ToString();
        return name.Length == 0
            ? name
            : char.ToUpper(name[0], System.Globalization.CultureInfo.CurrentCulture) + name[1..];
    }

    /// <summary>
    /// Picks from the front of a list at low obscurity and from all of it at
    /// high. The window is at least two entries wide, or a set would produce
    /// the same name every time at zero.
    /// </summary>
    private static T Pick<T>(IReadOnlyList<T> options, Random random, int reach)
    {
        var window = Math.Max(2, (int)Math.Round(options.Count * (reach / 100.0)));
        return options[random.Next(Math.Min(window, options.Count))];
    }
}
