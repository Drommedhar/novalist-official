namespace Novalist.Core.Models;

/// <summary>
/// The page a book is printed on, as a print shop describes it.
///
/// A manuscript PDF is one page size with one margin all round, because it is
/// read on a screen or in a ream on a desk. A book is not: the inner margin has
/// to clear the binding, the outer one has to survive trimming, the two swap on
/// facing pages, and anything meant to run to the edge has to be drawn past the
/// cut and then cut off. None of that is a preference - a file that gets it
/// wrong is rejected by the printer or comes back with text in the gutter.
/// </summary>
public sealed record PrintSpec
{
    /// <summary>Finished page width after trimming, in inches.</summary>
    public double TrimWidthInches { get; init; } = 8.5;

    /// <summary>Finished page height after trimming, in inches.</summary>
    public double TrimHeightInches { get; init; } = 11.0;

    /// <summary>
    /// Margin at the binding edge. On a mirrored layout this is the left margin
    /// of a right-hand page and the right margin of a left-hand one.
    /// </summary>
    public double MarginInsideInches { get; init; } = 1.0;

    /// <summary>Margin at the outer, trimmed edge.</summary>
    public double MarginOutsideInches { get; init; } = 1.0;

    public double MarginTopInches { get; init; } = 1.0;
    public double MarginBottomInches { get; init; } = 1.0;

    /// <summary>
    /// Whether inside and outside swap on facing pages. On for a bound book,
    /// off for anything read on a screen or printed single-sided.
    /// </summary>
    public bool MirrorMargins { get; init; } = true;

    /// <summary>
    /// Extra width added at the binding edge, beyond the inside margin. A thick
    /// book curves more at the spine and needs more of it.
    /// </summary>
    public double GutterInches { get; init; }

    /// <summary>
    /// Sizes the gutter from the finished page count instead of the fixed
    /// value, following the table print-on-demand services publish. On by
    /// default because the right gutter is a fact about the book, not a taste.
    /// </summary>
    public bool GutterFromPageCount { get; init; } = true;

    /// <summary>
    /// How far artwork runs past the trim, in inches. Zero for a text-only
    /// interior. A cover or a full-page image needs it, or a trim that lands a
    /// hair inside the line leaves a white sliver.
    /// </summary>
    public double BleedInches { get; init; }

    /// <summary>
    /// Whether to keep paragraphs from breaking to leave one line stranded at
    /// the top or bottom of a page.
    /// </summary>
    public bool AvoidWidowsAndOrphans { get; init; } = true;

    /// <summary>
    /// The smallest number of lines allowed alone at the foot of a page (an
    /// orphan) or at the head of the next (a widow). Two is the usual rule.
    /// </summary>
    public int MinLinesTogether { get; init; } = 2;

    /// <summary>
    /// The gutter this book actually gets, given how many pages it runs to.
    ///
    /// The steps are the ones print-on-demand services publish: a thin book
    /// barely curves at the spine, a 700-page one swallows half an inch.
    /// </summary>
    public double EffectiveGutterInches(int pageCount)
    {
        if (!GutterFromPageCount) return Math.Max(0, GutterInches);

        return pageCount switch
        {
            < 151 => 0.375,
            < 301 => 0.5,
            < 501 => 0.625,
            < 701 => 0.75,
            _ => 0.875
        };
    }

    /// <summary>
    /// The left margin of a given page. Page one is a right-hand page, so odd
    /// pages bind on the left.
    /// </summary>
    public double LeftMarginInches(int pageNumber, int pageCount)
    {
        var gutter = EffectiveGutterInches(pageCount);
        if (!MirrorMargins) return MarginInsideInches + gutter;
        return IsRightHandPage(pageNumber)
            ? MarginInsideInches + gutter
            : MarginOutsideInches;
    }

    /// <summary>The right margin of a given page.</summary>
    public double RightMarginInches(int pageNumber, int pageCount)
    {
        var gutter = EffectiveGutterInches(pageCount);
        if (!MirrorMargins) return MarginOutsideInches;
        return IsRightHandPage(pageNumber)
            ? MarginOutsideInches
            : MarginInsideInches + gutter;
    }

    /// <summary>
    /// Odd pages are right-hand pages, which is the convention every bound book
    /// follows and what makes page one a recto.
    /// </summary>
    public static bool IsRightHandPage(int pageNumber) => pageNumber % 2 == 1;

    /// <summary>Full media width including bleed on both edges.</summary>
    public double MediaWidthInches => TrimWidthInches + BleedInches * 2;

    /// <summary>Full media height including bleed on both edges.</summary>
    public double MediaHeightInches => TrimHeightInches + BleedInches * 2;

    /// <summary>
    /// A named trim, or null when the name is not one we know. Names are the
    /// ones printers and writers use, so a writer who was told "five by eight"
    /// does not have to convert anything.
    /// </summary>
    public static (double Width, double Height)? NamedTrim(string? name) =>
        (name ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "us-letter" or "letter" => (8.5, 11.0),
            "us-trade" or "trade" => (6.0, 9.0),
            "digest" => (5.5, 8.5),
            "pocket" or "mass-market" => (4.25, 6.87),
            "a4" => (8.27, 11.69),
            "a5" => (5.83, 8.27),
            "royal" => (6.14, 9.21),
            "crown-quarto" => (7.44, 9.69),
            _ => null
        };

    /// <summary>Every trim we know by name, for a picker.</summary>
    public static IReadOnlyList<string> TrimNames =>
    [
        "us-letter", "us-trade", "digest", "pocket", "a4", "a5", "royal", "crown-quarto"
    ];

    /// <summary>
    /// This spec with a named trim applied. An unknown name leaves the size
    /// alone rather than resetting it, so a custom trim survives a bad string.
    /// </summary>
    public PrintSpec WithTrim(string? name)
    {
        var trim = NamedTrim(name);
        return trim == null
            ? this
            : this with { TrimWidthInches = trim.Value.Width, TrimHeightInches = trim.Value.Height };
    }
}
