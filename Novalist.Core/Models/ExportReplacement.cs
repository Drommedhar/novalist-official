using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Novalist.Core.Models;

/// <summary>
/// One substitution applied to an export and never to the manuscript.
///
/// Novalist's Replace All writes to the source scenes and snapshots each one it
/// touches, which is the right tool for fixing a name and the wrong tool
/// entirely for "the submission copy spells it out and the ebook uses the
/// glyph". A compile-time replacement leaves the prose alone: it runs on the way
/// out, every time, and can be turned off without undoing anything.
/// </summary>
public sealed class ExportReplacement
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>What to look for. A regular expression when <see cref="IsRegex"/>.</summary>
    [JsonPropertyName("find")]
    public string Find { get; set; } = string.Empty;

    /// <summary>
    /// What to put there. With <see cref="IsRegex"/>, <c>$1</c> and friends
    /// refer to captured groups.
    /// </summary>
    [JsonPropertyName("replace")]
    public string Replace { get; set; } = string.Empty;

    [JsonPropertyName("isRegex")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsRegex { get; set; }

    [JsonPropertyName("matchCase")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool MatchCase { get; set; }

    /// <summary>
    /// Off keeps the rule without running it. A rule for one submission that is
    /// wrong for the next is worth keeping and worth not deleting.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Order matters: an earlier rule's output is a later rule's input.</summary>
    [JsonPropertyName("order")]
    public int Order { get; set; }
}

/// <summary>Applies a book's compile-time replacements, in order.</summary>
public static class ExportReplacements
{
    /// <summary>
    /// How long a single replacement is allowed to run before it is abandoned.
    ///
    /// A regular expression a writer types can backtrack catastrophically, and
    /// an export that hangs looks like a crash. A rule that times out is skipped
    /// and the text passes through unchanged, which is the safe direction.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Runs every enabled rule over <paramref name="text"/>, in order. A rule
    /// whose pattern does not compile is skipped rather than failing the export:
    /// a half-typed regular expression should not cost somebody their file.
    /// </summary>
    public static string Apply(string text, IEnumerable<ExportReplacement>? rules)
    {
        if (rules == null || string.IsNullOrEmpty(text)) return text;

        foreach (var rule in rules.Where(r => r.Enabled).OrderBy(r => r.Order))
        {
            if (string.IsNullOrEmpty(rule.Find)) continue;
            try
            {
                var options = rule.MatchCase
                    ? RegexOptions.None
                    : RegexOptions.IgnoreCase;
                var pattern = rule.IsRegex ? rule.Find : Regex.Escape(rule.Find);
                var replacement = rule.IsRegex ? rule.Replace : rule.Replace.Replace("$", "$$");
                text = Regex.Replace(text, pattern, replacement, options, Timeout);
            }
            catch (Exception e) when (e is ArgumentException or RegexMatchTimeoutException)
            {
                // A pattern that does not compile, or one that runs away: the
                // text passes through untouched rather than the export failing.
            }
        }
        return text;
    }
}
