using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// Words and phrases this book completes as you type.
///
/// The only completion Novalist had was the @-mention picker over Codex names,
/// in scene prose and nowhere else. That leaves out everything a secondary world
/// is full of and the Codex is not: a spelling of a place-name the writer has
/// settled on, a rank, a coined verb, a phrase that has to read the same way
/// every time. Those get retyped, and retyped slightly differently, and the
/// inconsistency shows up in copy-edit.
/// </summary>
public sealed class CompletionList
{
    /// <summary>
    /// How many characters must be typed before anything is offered.
    ///
    /// Two is too few - a two-letter prefix matches half the list and the popup
    /// becomes something to dismiss rather than something to use.
    /// </summary>
    public const int MinimumTrigger = 3;

    /// <summary>The most matches worth showing. A longer list is a menu to read
    /// rather than a completion to accept.</summary>
    public const int MaxSuggestions = 8;

    /// <summary>
    /// The entries, in the order the writer put them. Never sorted on save: a
    /// list somebody grouped by hand is a list they can find things in.
    /// </summary>
    [JsonPropertyName("words")]
    public List<string> Words { get; set; } = [];

    /// <summary>
    /// How many characters trigger the popup. Clamped rather than rejected -
    /// a completion list is not worth failing a save over.
    /// </summary>
    [JsonPropertyName("trigger")]
    public int Trigger { get; set; } = MinimumTrigger;

    /// <summary>The trigger length actually used.</summary>
    [JsonIgnore]
    public int EffectiveTrigger => Math.Clamp(Trigger, MinimumTrigger, 10);

    /// <summary>
    /// The entries that continue <paramref name="prefix"/>, best first.
    ///
    /// Prefix matching rather than substring: a writer typing "Aer" means a word
    /// starting that way, and offering "Kaeryn" for it is noise. Case-insensitive
    /// to match, but the stored spelling is what gets inserted - the whole point
    /// is that the word comes out the way it was decided.
    /// </summary>
    public IReadOnlyList<string> Suggest(string? prefix)
    {
        var typed = (prefix ?? string.Empty).Trim();
        if (typed.Length < EffectiveTrigger) return [];

        var hits = new List<string>();
        foreach (var word in Words)
        {
            if (hits.Count >= MaxSuggestions) break;
            if (string.IsNullOrWhiteSpace(word)) continue;
            if (!word.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) continue;
            // A word identical to what is already typed completes nothing.
            if (word.Length == typed.Length) continue;
            hits.Add(word);
        }
        return hits;
    }

    /// <summary>
    /// Cleans a submitted list: blanks dropped, duplicates folded, order kept.
    /// </summary>
    public static List<string> Clean(IEnumerable<string>? words)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cleaned = new List<string>();
        foreach (var word in words ?? [])
        {
            var trimmed = (word ?? string.Empty).Trim();
            if (trimmed.Length == 0 || !seen.Add(trimmed)) continue;
            cleaned.Add(trimmed);
        }
        return cleaned;
    }
}
