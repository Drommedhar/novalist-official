namespace Novalist.Core.Services;

/// <summary>Which part of a thing a term is asking about.</summary>
public enum SearchField
{
    /// <summary>Anywhere - the default when a term names no field.</summary>
    Any,
    Title,
    Text,
    Notes,
    Tag,

    /// <summary>The kind of result: scene, entity, research, timeline.</summary>
    Kind
}

/// <summary>
/// One term of a query: what to look for, where, whether it must be absent,
/// and whether it has to appear as written.
/// </summary>
public sealed record SearchTerm(string Value, SearchField Field, bool Negated, bool Exact)
{
    /// <summary>Whether <paramref name="haystack"/> satisfies this term.</summary>
    public bool Matches(string? haystack)
    {
        var found = !string.IsNullOrEmpty(haystack)
            && haystack.Contains(Value, StringComparison.OrdinalIgnoreCase);
        return Negated ? !found : found;
    }
}

/// <summary>
/// A search as the writer typed it.
///
/// One case-insensitive substring pass could not express "in the title", "not
/// this word", or "these words in this order", which are the three things
/// anyone looking for a half-remembered line actually needs. The syntax is
/// Scrivener's and Obsidian's, because a writer who knows one already knows
/// this: <c>title:bell -draft "the bell tolled"</c>.
/// </summary>
public sealed class SearchQuery
{
    private SearchQuery(IReadOnlyList<SearchTerm> terms)
    {
        Terms = terms;
    }

    public IReadOnlyList<SearchTerm> Terms { get; }

    /// <summary>A query with nothing to match on, which finds nothing.</summary>
    public bool IsEmpty => Terms.Count == 0;

    /// <summary>The kinds this query restricts itself to; empty means all of them.</summary>
    public IReadOnlyList<string> Kinds =>
        [.. Terms.Where(t => t.Field == SearchField.Kind && !t.Negated).Select(t => t.Value)];

    /// <summary>
    /// Parses a query. Anything that is not syntax is a term, so a stray colon
    /// or quote searches for itself rather than failing - a search box that
    /// rejects what you typed is worse than one that looks for it.
    /// </summary>
    public static SearchQuery Parse(string? query)
    {
        var terms = new List<SearchTerm>();
        var text = query ?? string.Empty;
        var i = 0;

        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i >= text.Length) break;

            var negated = text[i] == '-';
            if (negated) i++;

            var field = SearchField.Any;
            var colon = FieldEnd(text, i);
            if (colon > i)
            {
                var parsed = FieldOf(text[i..colon]);
                if (parsed != null)
                {
                    field = parsed.Value;
                    i = colon + 1;
                }
            }

            var exact = i < text.Length && text[i] == '"';
            string value;
            if (exact)
            {
                i++;
                var end = text.IndexOf('"', i);
                if (end < 0) end = text.Length;
                value = text[i..end];
                i = end < text.Length ? end + 1 : end;
            }
            else
            {
                var end = i;
                while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
                value = text[i..end];
                i = end;
            }

            if (value.Length > 0) terms.Add(new SearchTerm(value, field, negated, exact));
        }

        return new SearchQuery(terms);
    }

    /// <summary>Where a <c>field:</c> prefix ends, or -1 when there is none.</summary>
    private static int FieldEnd(string text, int from)
    {
        for (var i = from; i < text.Length; i++)
        {
            if (text[i] == ':') return i;
            if (!char.IsLetter(text[i])) return -1;
        }
        return -1;
    }

    private static SearchField? FieldOf(string name) => name.ToLowerInvariant() switch
    {
        "title" => SearchField.Title,
        "text" or "body" => SearchField.Text,
        "notes" or "note" or "synopsis" => SearchField.Notes,
        "tag" => SearchField.Tag,
        "kind" or "type" => SearchField.Kind,
        _ => null
    };

    /// <summary>
    /// Whether a result satisfies every term, given what each of its fields
    /// holds. A term naming a field this result does not have fails, which is
    /// what "title:bell" should do to a research note.
    /// </summary>
    public bool Matches(string? title, string? text, string? notes, IEnumerable<string>? tags, string kind)
    {
        foreach (var term in Terms)
        {
            var ok = term.Field switch
            {
                SearchField.Title => term.Matches(title),
                SearchField.Text => term.Matches(text),
                SearchField.Notes => term.Matches(notes),
                SearchField.Tag => tags != null && tags.Any(term.Matches) != term.Negated,
                SearchField.Kind => string.Equals(kind, term.Value, StringComparison.OrdinalIgnoreCase)
                    != term.Negated,
                _ => term.Negated
                    ? term.Matches(title) && term.Matches(text) && term.Matches(notes)
                    : term.Matches(title) || term.Matches(text) || term.Matches(notes)
            };
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>
    /// How well a result answers the query. A title match beats a body match
    /// because someone searching for a name is usually looking for the thing
    /// itself, and an earlier match beats a later one because a word in the
    /// first line is more likely the one meant.
    /// </summary>
    public int Score(string? title, string? text, string? notes)
    {
        var score = 0;
        foreach (var term in Terms.Where(t => !t.Negated))
        {
            if (Hit(title, term.Value, out var titleAt))
            {
                score += 100 - Math.Min(50, titleAt);
                if (string.Equals(title, term.Value, StringComparison.OrdinalIgnoreCase)) score += 100;
            }
            if (Hit(notes, term.Value, out var notesAt)) score += 40 - Math.Min(20, notesAt / 10);
            if (Hit(text, term.Value, out var textAt)) score += 20 - Math.Min(15, textAt / 100);
        }
        return score;
    }

    private static bool Hit(string? haystack, string needle, out int at)
    {
        at = string.IsNullOrEmpty(haystack)
            ? -1
            : haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        return at >= 0;
    }
}
