
namespace Novalist.Sdk.Services;

/// <summary>
/// The book in one line, then one paragraph, then the answers a submission
/// form asks for.
/// </summary>
public sealed class BookPremiseInfo
{
    /// <summary>One sentence: somebody wants something, and something stops them.</summary>
    public string Logline { get; init; } = string.Empty;

    /// <summary>The premise opened out: world, stakes, inciting incident, rough climax.</summary>
    public string Paragraph { get; init; } = string.Empty;

    /// <summary>
    /// A summary per act, keyed by the act name the chapters carry - so the
    /// ladder stays attached to the book's own structure rather than assuming
    /// three acts.
    /// </summary>
    public IReadOnlyDictionary<string, string> Acts { get; init; }
        = new Dictionary<string, string>();

    /// <summary>Genre as a shop would file it.</summary>
    public string Genre { get; init; } = string.Empty;

    /// <summary>Who it is for: age band, readership, the shelf it sits on.</summary>
    public string Audience { get; init; } = string.Empty;

    /// <summary>Comparable titles for a submission query. Stored as free-form text without an enforced title count or delimiter format.</summary>
    public string Comparables { get; init; } = string.Empty;

    /// <summary>Where and when it is set.</summary>
    public string Setting { get; init; } = string.Empty;

    /// <summary>
    /// Back-cover copy: what a reader is told to make them open it. Not the
    /// synopsis - a blurb withholds the ending on purpose, and a prompt that
    /// confuses the two asks for the wrong thing.
    /// </summary>
    public string Blurb { get; init; } = string.Empty;

    /// <summary>The one-page synopsis, ending included.</summary>
    public string Synopsis { get; init; } = string.Empty;
}

/// <summary>
/// What a shop, a library and a distributor are told about the book.
/// </summary>
public sealed class BookPublishingInfo
{
    /// <summary>ISBN as the writer typed it, hyphens and all.</summary>
    public string Isbn { get; init; } = string.Empty;

    public string Publisher { get; init; } = string.Empty;

    /// <summary>Description written to publishing metadata during export, independently of the premise synopsis.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Subject headings: genre words, or BISAC codes where the writer has them.</summary>
    public IReadOnlyList<string> Subjects { get; init; } = [];

    public string Rights { get; init; } = string.Empty;

    /// <summary>Publication date as the writer entered it, ideally yyyy-mm-dd.</summary>
    public string PublicationDate { get; init; } = string.Empty;

    /// <summary>The series this book belongs to, or empty.</summary>
    public string SeriesName { get; init; } = string.Empty;

    /// <summary>Position in the series - "2", or "2.5" for a novella between
    /// two books, which is why it is not a number.</summary>
    public string SeriesPosition { get; init; } = string.Empty;
}

/// <summary>What the active book is, beyond the chapters in it.</summary>
public sealed class BookDetailInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Who wrote this book when that is not who wrote the project - an
    /// anthology whose volumes have different authors. Empty means the
    /// project's own author.
    /// </summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>
    /// "first", "second", "third limited", "third omniscient", or empty where
    /// the writer has not said. Declared rather than derived: it is the
    /// writer's intention, and reading it off the majority of scenes would make
    /// the outlier normal.
    /// </summary>
    public string NarrativePerson { get; init; } = string.Empty;

    /// <summary>"past", "present", or empty. Same reasoning as
    /// <see cref="NarrativePerson"/>.</summary>
    public string Tense { get; init; } = string.Empty;

    /// <summary>The story structure the book is written against, or empty.</summary>
    public string StructureTemplateId { get; init; } = string.Empty;

    /// <summary>Never null: a book with nothing filled in has empty fields
    /// rather than a missing object, so a caller reads it without a null check
    /// on every line.</summary>
    public BookPremiseInfo Premise { get; init; } = new();

    /// <summary>Never null, for the same reason as <see cref="Premise"/>.</summary>
    public BookPublishingInfo Publishing { get; init; } = new();
}
