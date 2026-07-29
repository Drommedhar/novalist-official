using System.Text;
using System.Text.RegularExpressions;

namespace Novalist.Core.Utilities;

/// <summary>Whether a tracked change adds text or takes it away.</summary>
public enum ChangeKind
{
    Insertion,
    Deletion
}

/// <summary>One suggested edit, as it stands in the prose.</summary>
public sealed record TrackedChange(
    string Id,
    ChangeKind Kind,
    string Text,
    string Author,
    string At);

/// <summary>
/// Suggested edits, stored in the prose itself as <c>&lt;ins&gt;</c> and
/// <c>&lt;del&gt;</c>.
///
/// Keeping them in the HTML rather than beside it means they travel wherever
/// the scene travels - a snapshot, a draft clone, a git diff, a copy of the
/// file opened in a text editor - and cannot come adrift from the words they
/// are about. The cost is that everything which reads prose has to know what
/// they mean, which is what <see cref="Final"/> and
/// <see cref="TextDiff.StripHtml"/> are for.
///
/// A pending insertion counts as part of the document, the way a word
/// processor treats one: it is in the word count and in an export until
/// somebody rejects it. A pending deletion does not.
/// </summary>
public static partial class TrackedChanges
{
    /// <summary>Marks the element as a tracked change and carries its id.</summary>
    public const string IdAttribute = "data-nl-change";

    /// <summary>
    /// Only a tag carrying our marker is a suggestion.
    ///
    /// A bare <c>&lt;del&gt;</c> is strikethrough - a writer struck those words
    /// on purpose and wants them printed struck. Reading it as a suggested cut
    /// would quietly drop them from every export, which is the same shape of
    /// bug as losing the prose outright. Prose pasted from a word processor
    /// keeps its formatting rather than arriving as unanswered edits, and that
    /// is the right way round: an untracked change still reads correctly, a
    /// silently deleted sentence does not.
    /// </summary>
    [GeneratedRegex(
        @"<(ins|del)\b((?=[^>]*\bdata-nl-change\s*=)[^>]*)>(.*?)</\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ChangeRegex();

    [GeneratedRegex(
        @"([\w-]+)\s*=\s*(?:""([^""]*)""|'([^']*)')",
        RegexOptions.IgnoreCase)]
    private static partial Regex AttributeRegex();

    /// <summary>Whether the prose carries any suggested edit at all.</summary>
    public static bool HasChanges(string? html)
        => !string.IsNullOrEmpty(html) && ChangeRegex().IsMatch(html);

    /// <summary>
    /// Every suggested edit in the prose, in the order it appears. Only tags
    /// carrying our marker count; see <see cref="ChangeRegex"/> for why.
    /// </summary>
    public static IReadOnlyList<TrackedChange> Pending(string? html)
    {
        if (string.IsNullOrEmpty(html)) return [];

        var changes = new List<TrackedChange>();
        var index = 0;
        foreach (Match match in ChangeRegex().Matches(html))
        {
            var attributes = Attributes(match.Groups[2].Value);
            changes.Add(new TrackedChange(
                attributes.GetValueOrDefault(IdAttribute, $"change-{index}"),
                match.Groups[1].Value.Equals("del", StringComparison.OrdinalIgnoreCase)
                    ? ChangeKind.Deletion
                    : ChangeKind.Insertion,
                TextDiff.StripHtml(match.Groups[3].Value).Trim(),
                attributes.GetValueOrDefault("data-nl-author", string.Empty),
                attributes.GetValueOrDefault("data-nl-at", string.Empty)));
            index++;
        }
        return changes;
    }

    /// <summary>How many edits are still waiting on somebody.</summary>
    public static int Count(string? html) => Pending(html).Count;

    /// <summary>
    /// The prose as it reads if every suggestion is taken. This is the version
    /// that counts: an export writes it, a word count measures it, and a search
    /// looks through it.
    /// </summary>
    public static string Final(string? html) => Resolve(html, accept: true, only: null);

    /// <summary>The prose as it read before anybody suggested anything.</summary>
    public static string Original(string? html) => Resolve(html, accept: false, only: null);

    /// <summary>Takes one suggestion, leaving the rest pending.</summary>
    public static string Accept(string? html, string changeId)
        => Resolve(html, accept: true, only: changeId);

    /// <summary>Turns one suggestion down, leaving the rest pending.</summary>
    public static string Reject(string? html, string changeId)
        => Resolve(html, accept: false, only: changeId);

    /// <summary>Takes every suggestion. Same as <see cref="Final"/>, named for what it does.</summary>
    public static string AcceptAll(string? html) => Final(html);

    /// <summary>Turns every suggestion down.</summary>
    public static string RejectAll(string? html) => Original(html);

    /// <summary>
    /// Resolves suggestions in the prose.
    ///
    /// Accepting an insertion keeps its words and drops the tag; accepting a
    /// deletion drops both. Rejecting is the mirror. With <paramref name="only"/>
    /// set, every other change is left exactly as it was, which is what makes
    /// accepting one edit out of twenty possible.
    /// </summary>
    private static string Resolve(string? html, bool accept, string? only)
    {
        if (string.IsNullOrEmpty(html)) return html ?? string.Empty;

        var index = 0;
        return ChangeRegex().Replace(html, match =>
        {
            var attributes = Attributes(match.Groups[2].Value);
            var id = attributes.GetValueOrDefault(IdAttribute, $"change-{index}");
            index++;

            if (only != null && !string.Equals(id, only, StringComparison.Ordinal))
                return match.Value;

            var isInsertion = !match.Groups[1].Value
                .Equals("del", StringComparison.OrdinalIgnoreCase);

            // Keep the words when the answer is yes to an insertion or no to a
            // deletion; drop them otherwise.
            return accept == isInsertion ? match.Groups[3].Value : string.Empty;
        });
    }

    /// <summary>
    /// Attributes off a tag, matched case-insensitively by name and decoded -
    /// an author called <c>Ada &amp; Co</c> is stored escaped and has to come
    /// back as they wrote it.
    /// </summary>
    private static Dictionary<string, string> Attributes(string raw)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in AttributeRegex().Matches(raw))
        {
            var value = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
            attributes[match.Groups[1].Value] = System.Net.WebUtility.HtmlDecode(value);
        }
        return attributes;
    }

    /// <summary>
    /// Wraps text as a suggested insertion. The id has to be stable and unique
    /// within the scene, because it is how one edit out of twenty is named.
    /// </summary>
    public static string Insertion(string id, string text, string author, string at)
        => $"<ins {IdAttribute}=\"{Escape(id)}\" data-nl-author=\"{Escape(author)}\" " +
           $"data-nl-at=\"{Escape(at)}\">{text}</ins>";

    /// <summary>Marks text as a suggested deletion, leaving the words in place.</summary>
    public static string Deletion(string id, string text, string author, string at)
        => $"<del {IdAttribute}=\"{Escape(id)}\" data-nl-author=\"{Escape(author)}\" " +
           $"data-nl-at=\"{Escape(at)}\">{text}</del>";

    /// <summary>
    /// An attribute value that cannot break out of its quotes. An author called
    /// <c>"&gt;</c> would otherwise rewrite the document.
    /// </summary>
    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(c switch
            {
                '&' => "&amp;",
                '"' => "&quot;",
                '<' => "&lt;",
                '>' => "&gt;",
                _ => c.ToString()
            });
        }
        return builder.ToString();
    }
}
