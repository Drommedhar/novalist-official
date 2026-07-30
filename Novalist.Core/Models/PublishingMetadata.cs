using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// What a shop, a library and a distributor need to know about a book, beyond
/// its title and its author.
///
/// Novalist wrote four fields into an EPUB's metadata block. A retailer
/// ingesting one had no ISBN to key on, no publisher, no rights statement, and
/// no way to learn that a book is the second of a trilogy — which is the
/// difference between a series that shelves together and three unrelated books.
/// </summary>
public sealed class PublishingMetadata
{
    /// <summary>
    /// ISBN-13 or ISBN-10, as the writer typed it. Stored verbatim; hyphens are
    /// stripped only on the way into an identifier, because the writer's copy is
    /// the one they will check against their registration.
    /// </summary>
    [JsonPropertyName("isbn")]
    public string Isbn { get; set; } = string.Empty;

    [JsonPropertyName("publisher")]
    public string Publisher { get; set; } = string.Empty;

    /// <summary>Blurb or description. Goes into dc:description.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Subject headings — genre words, or BISAC codes if the writer has them.
    /// One dc:subject each; shops use these to shelve the book.
    /// </summary>
    [JsonPropertyName("subjects")]
    public List<string> Subjects { get; set; } = [];

    /// <summary>
    /// Where the book can be bought, one entry per store.
    ///
    /// A build made for one retailer resolves its back-matter links to that
    /// store's page; without this every copy carries the same link, which for a
    /// book in five shops sends four of them to a competitor - and Amazon will
    /// refuse a book whose back matter links to a rival.
    /// </summary>
    [JsonPropertyName("retailers")]
    public List<RetailerLink> Retailers { get; set; } = [];

    /// <summary>Copyright line. Goes into dc:rights.</summary>
    [JsonPropertyName("rights")]
    public string Rights { get; set; } = string.Empty;

    /// <summary>Publication date as the writer entered it, ideally ISO
    /// (yyyy-mm-dd). Goes into dc:date.</summary>
    [JsonPropertyName("publicationDate")]
    public string PublicationDate { get; set; } = string.Empty;

    /// <summary>The series this book belongs to, if any.</summary>
    [JsonPropertyName("seriesName")]
    public string SeriesName { get; set; } = string.Empty;

    /// <summary>Position in the series — "2", or "2.5" for a novella between
    /// two books, which is why this is a string rather than a number.</summary>
    [JsonPropertyName("seriesPosition")]
    public string SeriesPosition { get; set; } = string.Empty;

    /// <summary>Whether anything here is worth writing out.</summary>
    [JsonIgnore]
    public bool HasAny =>
        !string.IsNullOrWhiteSpace(Isbn)
        || !string.IsNullOrWhiteSpace(Publisher)
        || !string.IsNullOrWhiteSpace(Description)
        || Subjects.Any(s => !string.IsNullOrWhiteSpace(s))
        || !string.IsNullOrWhiteSpace(Rights)
        || !string.IsNullOrWhiteSpace(PublicationDate)
        || !string.IsNullOrWhiteSpace(SeriesName);

    /// <summary>
    /// The ISBN as an identifier: digits and a trailing X only.
    ///
    /// Writers type ISBNs with hyphens because that is how they are printed,
    /// and a retailer keying on the identifier wants the bare number. Empty when
    /// nothing usable is left, so a half-typed value never becomes a broken
    /// identifier in the file.
    /// </summary>
    public string NormalizedIsbn()
    {
        var digits = new string([.. Isbn.Where(c => char.IsAsciiDigit(c) || c is 'X' or 'x')]);
        return digits.Length is 10 or 13 ? digits.ToUpperInvariant() : string.Empty;
    }
}
