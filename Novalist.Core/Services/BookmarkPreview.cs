namespace Novalist.Core.Services;

/// <summary>
/// A few lines of whatever a bookmark points at.
///
/// A bookmark that only navigates makes a writer go and look to remember why
/// they kept it, and for a list of thirty that is thirty trips. The extract is
/// what turns the list into something readable at a glance.
/// </summary>
public static class BookmarkPreview
{
    /// <summary>Characters of prose worth showing. Enough to recognise a
    /// passage, short enough that thirty of them still read as a list.</summary>
    public const int Length = 240;

    /// <summary>How much of the extract sits before the marked passage, so the
    /// sentence it belongs to is visible rather than starting mid-clause.</summary>
    private const int Lead = 60;

    /// <summary>
    /// The passage around <paramref name="anchor"/>, or the opening when there
    /// is no anchor or the prose it named has been rewritten since.
    ///
    /// Falling back to the opening rather than to nothing is deliberate: a
    /// bookmark whose anchor has been edited away still points at a scene worth
    /// recognising, and an empty preview reads as a broken bookmark.
    /// </summary>
    public static string Extract(string? text, string? anchor)
    {
        var prose = Collapse(text);
        if (prose.Length == 0) return string.Empty;

        var start = 0;
        var trimmedAnchor = (anchor ?? string.Empty).Trim();
        if (trimmedAnchor.Length > 0)
        {
            var at = prose.IndexOf(trimmedAnchor, StringComparison.OrdinalIgnoreCase);
            if (at >= 0) start = Math.Max(0, at - Lead);
        }

        var take = Math.Min(Length, prose.Length - start);
        var extract = prose.Substring(start, take);

        // An ellipsis either side says the passage continues, so nobody reads a
        // truncated sentence as the whole of what is there.
        if (start > 0) extract = "…" + extract;
        if (start + take < prose.Length) extract += "…";
        return extract;
    }

    /// <summary>
    /// Runs of whitespace become single spaces.
    ///
    /// Prose arrives with the paragraph breaks the scene had, and a preview
    /// three lines tall in a list of thirty is a wall. One line each is what
    /// makes the list scannable.
    /// </summary>
    private static string Collapse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var builder = new System.Text.StringBuilder(text.Length);
        var lastWasSpace = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace && builder.Length > 0) builder.Append(' ');
                lastWasSpace = true;
                continue;
            }
            builder.Append(ch);
            lastWasSpace = false;
        }
        return builder.ToString().TrimEnd();
    }
}
