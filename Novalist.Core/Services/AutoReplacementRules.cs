using System.Text.RegularExpressions;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>
/// What a writer's own replacement rules are allowed to be.
///
/// A rule reaches two engines that do not share an implementation: the editor
/// substitutes as you type, in the browser's regex flavour, and the cleanup
/// pass rewrites prose already written, in .NET's. A rule that only one of them
/// accepts is worse than a rule neither does, because the manuscript then holds
/// both forms depending on when the words were written. So a rule is checked
/// once, here, before it is stored - and a rule that does not pass is refused
/// with a reason rather than saved and quietly skipped.
/// </summary>
public static class AutoReplacementRules
{
    /// <summary>
    /// How long a pattern is allowed to spend on one stretch of prose.
    ///
    /// The cleanup pass runs over every scene in a book, and a pattern that
    /// backtracks catastrophically would not fail so much as never come back.
    /// </summary>
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>Longer than any trigger or pattern a rule has cause to hold.</summary>
    public const int MaxPatternLength = 200;

    /// <summary>
    /// Why this rule cannot be stored, or null when it can.
    ///
    /// The strings are keys rather than prose: the renderer owns the wording,
    /// in the writer's own language.
    /// </summary>
    public static string? Validate(AutoReplacementPair rule)
    {
        if (rule is null) return "empty";
        if (string.IsNullOrEmpty(rule.Start)) return "empty";
        if (rule.Start.Length > MaxPatternLength) return "tooLong";
        if (rule.StartReplace.Length > MaxPatternLength) return "tooLong";
        if (!rule.IsRegex) return null;

        Regex compiled;
        try
        {
            compiled = new Regex(rule.Start, RegexOptions.None, MatchTimeout);
        }
        catch (ArgumentException)
        {
            return "badPattern";
        }

        // A pattern that matches nothing at all would fire on every keystroke
        // and replace the empty string before the caret, forever. Matched
        // against the empty string, which no pattern can spend time on, so the
        // timeout above cannot bite here - it is there for real prose.
        return compiled.IsMatch(string.Empty) ? "matchesNothing" : null;
    }

    /// <summary>The rules that may be stored, with the rest dropped.</summary>
    public static List<AutoReplacementPair> Sanitize(IEnumerable<AutoReplacementPair> rules)
        => [.. rules.Where(r => Validate(r) is null)];

    /// <summary>
    /// Applies a writer's own rules to a run of prose.
    ///
    /// Only used by the cleanup pass. Typing-time substitution happens in the
    /// editor page, which cannot call into here and carries its own port.
    /// </summary>
    public static string Apply(string text, IEnumerable<AutoReplacementPair> rules)
    {
        if (string.IsNullOrEmpty(text)) return text;

        foreach (var rule in rules)
        {
            if (Validate(rule) is not null) continue;
            if (rule.IsRegex)
            {
                try
                {
                    text = new Regex(rule.Start, RegexOptions.None, MatchTimeout)
                        .Replace(text, rule.StartReplace);
                }
                catch (RegexMatchTimeoutException)
                {
                    // One slow pattern must not cost the writer the rest of the
                    // pass, and it has already had a hundred milliseconds.
                }
            }
            else
            {
                text = text.Replace(rule.Start, rule.StartReplace, StringComparison.Ordinal);
            }
        }
        return text;
    }
}
