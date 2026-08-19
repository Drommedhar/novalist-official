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
    /// A speaking rate as a multiple of normal, in words per minute.
    ///
    /// What both <c>say</c> and espeak take. Their defaults happen to agree at
    /// 175, and their ceilings do not: <c>say</c> will go as fast as it is
    /// asked, espeak refuses above 450 and speaks at its default instead - so a
    /// writer who pushed the Speed slider to 2x on Linux got a reading slower
    /// than the one they had at 1.5x, which reads as the control being broken.
    /// </summary>
    public static int ToWordsPerMinute(double multiplier, bool isSay)
    {
        const int Normal = 175;
        if (double.IsNaN(multiplier) || multiplier <= 0)
            return Normal;
        var wanted = (int)Math.Round(Normal * multiplier);
        return isSay ? Math.Clamp(wanted, 80, 720) : Math.Clamp(wanted, 80, 450);
    }

    /// <summary>
    /// The voices macOS lists, from <c>say -v '?'</c>.
    ///
    /// One per line, the locale second to last and a sample sentence behind a
    /// hash: <c>Alex                en_US    # Most people recognize me...</c>
    /// The name is read as everything before the locale rather than as the first
    /// word, because macOS ships voices called "Bad News" and
    /// "Eddy (English (UK))" - taking the first word alone would list four
    /// voices all called Eddy.
    /// </summary>
    public static IReadOnlyList<SystemVoice> ParseSayVoices(string output)
    {
        var voices = new List<SystemVoice>();
        foreach (var raw in output.Split('\n'))
        {
            // The sample sentence is prose in the voice's own language and can
            // contain anything at all, including another hash.
            var line = raw.Split('#', 2)[0].TrimEnd();
            if (line.Trim().Length == 0)
                continue;

            var at = line.LastIndexOf(' ');
            if (at <= 0)
                continue;

            var locale = line[(at + 1)..].Trim();
            var name = line[..at].Trim();
            if (name.Length == 0 || !LooksLikeLocale(locale))
                continue;

            // The id is the name: `say -v` takes the name, and there is nothing
            // else on offer to identify a voice by.
            voices.Add(new SystemVoice(name, name, locale.Replace('_', '-')));
        }
        return voices;
    }

    /// <summary>
    /// Whether a token is a language tag rather than the tail of a voice's name.
    ///
    /// macOS writes them with an underscore - "en_US", "zh_CN", "ar_001" - and
    /// occasionally bare, as "en". Shape rather than length: "any short run of
    /// letters" also accepts the last word of every English sentence in the
    /// file, which is how a heading became a voice called "line".
    /// </summary>
    private static bool LooksLikeLocale(string token)
    {
        var parts = token.Split('_', '-');
        if (parts.Length > 2)
            return false;
        if (parts[0].Length is < 2 or > 3 || !parts[0].All(char.IsAsciiLetter))
            return false;
        return parts.Length == 1
               || (parts[1].Length is >= 2 and <= 4 && parts[1].All(char.IsAsciiLetterOrDigit));
    }

    /// <summary>
    /// The voices espeak lists, from <c>--voices</c>.
    ///
    /// A table with a header, and the columns are read from the header rather
    /// than by splitting on whitespace: espeak names voices "English
    /// (Great Britain)" and "Chinese (Mandarin, latin as English)", so counting
    /// fields puts half of every name in the next column.
    ///
    /// The id is the language code, because that is what <c>-v</c> takes and it
    /// is the only column guaranteed to select the voice it names.
    /// </summary>
    public static IReadOnlyList<SystemVoice> ParseEspeakVoices(string output)
    {
        var lines = output.Split('\n');
        var header = Array.FindIndex(
            lines, l => l.Contains("Language", StringComparison.Ordinal)
                        && l.Contains("VoiceName", StringComparison.Ordinal));
        if (header < 0)
            return [];

        var language = lines[header].IndexOf("Language", StringComparison.Ordinal);
        var name = lines[header].IndexOf("VoiceName", StringComparison.Ordinal);
        if (language < 0 || name < 0 || name <= language)
            return [];

        var voices = new List<SystemVoice>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = header + 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Trim().Length == 0 || line.Length <= language)
                continue;

            var tag = Column(line, language, name).Split(' ')[0].Trim();
            var said = NameBeforeTheFile(Column(line, name, line.Length));
            if (tag.Length == 0 || said.Length == 0)
                continue;
            // espeak lists a language once per variant it knows. The picker wants
            // voices, and thirty entries all reading "-v en" are one voice.
            if (!seen.Add(tag))
                continue;

            voices.Add(new SystemVoice(tag, said, tag));
        }
        return voices;
    }

    /// <summary>
    /// The voice's name out of everything that follows the name column.
    ///
    /// Not a fixed width. espeak pads its columns but does not truncate, so
    /// "English (Great Britain)" simply pushes the File column to the right -
    /// and slicing at the header's own File offset cut the name to "English
    /// (Great Brit". The file is the first field carrying a slash, which is what
    /// a voice file always looks like and a voice name never does.
    /// </summary>
    private static string NameBeforeTheFile(string rest)
    {
        var said = new List<string>();
        foreach (var word in rest.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Contains('/'))
                break;
            said.Add(word);
        }
        return string.Join(' ', said);
    }

    /// <summary>One column of a row, clipped to what the row actually has. Rows
    /// are ragged: espeak stops writing at the last field a voice has.</summary>
    private static string Column(string line, int from, int to)
        => from >= line.Length ? string.Empty : line[from..Math.Min(to, line.Length)];

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
