using System.Globalization;

namespace Novalist.Backend.Speech;

/// <summary>
/// The decisions about system voices, away from the COM interop so they can be
/// tested: what a voice is called, what language it speaks, how a rate maps, and
/// which voice a piece of prose should get.
/// </summary>
public static class VoiceCatalog
{
    /// <summary>
    /// SAPI reports a voice's language as one or more hex LCIDs, separated by
    /// semicolons - "407" for German, "809;409" for a voice that claims two.
    /// The first is the one to report; a voice claiming ten languages is still
    /// filed under the one it leads with.
    /// </summary>
    public static string LanguageFromLcidList(string? lcids)
    {
        foreach (var part in (lcids ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var lcid))
                continue;
            try
            {
                return CultureInfo.GetCultureInfo(lcid).Name;
            }
            catch (CultureNotFoundException)
            {
                // A voice pinned to a locale this machine does not know. Better
                // no language than a wrong one - the picker simply groups it
                // with the others rather than promising it speaks something.
            }
        }
        return string.Empty;
    }

    /// <summary>
    /// A speaking rate as a multiple of normal, mapped onto SAPI's -10..10.
    ///
    /// The renderer has always expressed rate as a multiplier because that is
    /// what the browser takes, and SAPI's scale is neither linear nor the same
    /// units. Ten steps either side of normal is close enough to feel like the
    /// same control, and clamping is the only sane answer to a rate outside it.
    /// </summary>
    public static int ToSapiRate(double multiplier)
    {
        if (double.IsNaN(multiplier) || multiplier <= 0) return 0;
        // Doubling the speed is +10, halving it is -10, and 1.0 is dead centre.
        var steps = Math.Log2(multiplier) * 10.0;
        return (int)Math.Round(Math.Clamp(steps, -10, 10));
    }

    /// <summary>
    /// The voice a piece of prose should be read in.
    ///
    /// An explicitly chosen voice always wins. Failing that, the first voice
    /// whose language matches the writing language - matched on the language
    /// part alone, because a German writer does not care whether the voice is
    /// filed as de-DE or de-AT. Failing that, nothing, and the engine picks:
    /// better its default than ours pretending to know.
    /// </summary>
    public static SystemVoice? Choose(
        IReadOnlyList<SystemVoice> voices, string? chosenId, string? writingLanguage)
    {
        if (!string.IsNullOrWhiteSpace(chosenId))
        {
            var exact = voices.FirstOrDefault(
                v => string.Equals(v.Id, chosenId, StringComparison.Ordinal));
            if (exact != null) return exact;
        }

        var wanted = Primary(writingLanguage);
        if (wanted.Length == 0) return null;
        return voices.FirstOrDefault(
            v => string.Equals(Primary(v.Language), wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The language part of a tag: "de" for both "de-DE" and "de".</summary>
    public static string Primary(string? tag)
    {
        var trimmed = (tag ?? string.Empty).Trim();
        var dash = trimmed.IndexOf('-');
        return dash < 0 ? trimmed : trimmed[..dash];
    }

    /// <summary>
    /// The voices worth showing, ordered so the useful ones are near the top.
    ///
    /// Voices that speak the writing language lead, then everything else by
    /// name. An adapter can expose several hundred voices at once, and a list
    /// of three hundred in no order is a list nobody reads to the end of.
    /// </summary>
    public static IReadOnlyList<SystemVoice> ForPicker(
        IReadOnlyList<SystemVoice> voices, string? writingLanguage)
    {
        var wanted = Primary(writingLanguage);
        return [.. voices
            .OrderByDescending(v => wanted.Length > 0
                && string.Equals(Primary(v.Language), wanted, StringComparison.OrdinalIgnoreCase))
            .ThenBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)];
    }
}
