using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// Where this book can be bought, at one store.
///
/// Novalist had no retailer, build-variant or store-link concept anywhere: one
/// format, one path, one file. So the back matter of every copy carried the same
/// link, which for a book sold in five shops means four of them are sent to a
/// competitor - and Amazon in particular will refuse a book whose back matter
/// links to a rival store.
/// </summary>
public sealed class RetailerLink
{
    /// <summary>
    /// A stable key for the store, used to pick a build: "amazon", "kobo",
    /// "apple". Free text so a shop nobody anticipated still works.
    /// </summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>What the store is called in the prose: "Amazon", "Kobo".</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The book's page at that store.</summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// The store's own identifier for the book - an ASIN, an Apple id. Kept
    /// beside the link because a retailer's ingestion form asks for it and it
    /// otherwise lives in somebody's spreadsheet.
    /// </summary>
    [JsonPropertyName("productId")]
    public string ProductId { get; set; } = string.Empty;
}
