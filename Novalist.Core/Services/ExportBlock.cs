namespace Novalist.Core.Services;

/// <summary>Whether a block is a list item, and which kind of list.</summary>
internal enum ListKind
{
    None,
    Bullet,
    Number
}

/// <summary>
/// One block of a scene as the export writers see it: its inline runs, the named
/// paragraph style the writer applied (null for body text), and whether it is a
/// list item.
///
/// Every format parses from this rather than from raw HTML, so a style added in
/// the editor is either handled everywhere or visibly missing in one place —
/// not silently honoured by whichever writer happened to grow a case for it.
/// </summary>
internal sealed record ExportBlock(
    List<InlineSegment> Segments,
    string? StyleId,
    ListKind List)
{
    /// <summary>The block's text with no formatting, for writers that only need
    /// the words.</summary>
    public string Text => string.Concat(Segments.Select(s => s.Text));
}
