using Novalist.Core.Models;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// What a shop, a library and a distributor need to know about the book. Stored
/// on the book and written into the EPUB metadata block on export.
/// </summary>
public sealed class PublishingRpc
{
    private readonly Workspace _workspace;

    public PublishingRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    [JsonRpcMethod("publishing/get")]
    public PublishingDto Get()
    {
        var meta = _workspace.Projects.ActiveBook?.Publishing ?? new PublishingMetadata();
        return ToDto(meta);
    }

    [JsonRpcMethod("publishing/set")]
    public async Task<PublishingDto> SetAsync(PublishingDto value)
    {
        var book = _workspace.Projects.ActiveBook
            ?? throw new InvalidOperationException("No active book.");

        book.Publishing = new PublishingMetadata
        {
            Isbn = value.Isbn ?? string.Empty,
            Publisher = value.Publisher ?? string.Empty,
            Description = value.Description ?? string.Empty,
            Rights = value.Rights ?? string.Empty,
            PublicationDate = value.PublicationDate ?? string.Empty,
            SeriesName = value.SeriesName ?? string.Empty,
            SeriesPosition = value.SeriesPosition ?? string.Empty,
            // Blank subjects would become empty dc:subject elements, which some
            // ingestion pipelines treat as a malformed record.
            Subjects = [.. (value.Subjects ?? [])
                .Select(sub => (sub ?? string.Empty).Trim())
                .Where(sub => sub.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)],
            // A store with no key cannot be asked for by a build, and one with
            // no link resolves to nothing in the back matter - both are a row
            // somebody started and did not finish.
            Retailers = [.. (value.Retailers ?? [])
                .Where(r => !string.IsNullOrWhiteSpace(r.Key))
                .Select(r => new RetailerLink
                {
                    Key = r.Key!.Trim(),
                    Name = string.IsNullOrWhiteSpace(r.Name) ? r.Key!.Trim() : r.Name!.Trim(),
                    Url = (r.Url ?? string.Empty).Trim(),
                    ProductId = (r.ProductId ?? string.Empty).Trim()
                })
                .GroupBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())]
        };

        await _workspace.Projects.SaveProjectAsync();
        return Get();
    }

    private static PublishingDto ToDto(PublishingMetadata meta)
        => new(
            meta.Isbn,
            meta.Publisher,
            meta.Description,
            meta.Rights,
            meta.PublicationDate,
            meta.SeriesName,
            meta.SeriesPosition,
            [.. meta.Subjects],
            [.. meta.Retailers.Select(
                r => new RetailerDto(r.Key, r.Name, r.Url, r.ProductId))],
            // The bare digits the file will carry, so the panel can show the
            // writer what a retailer will actually see - or that their typo
            // produced nothing usable.
            meta.NormalizedIsbn());
}

/// <summary>Publishing metadata. <c>NormalizedIsbn</c> is derived, and is empty
/// when what the writer typed is not a usable ISBN.</summary>
public sealed record PublishingDto(
    string Isbn,
    string Publisher,
    string Description,
    string Rights,
    string PublicationDate,
    string SeriesName,
    string SeriesPosition,
    string[] Subjects,
    /// <summary>Where the book can be bought, one entry per store.</summary>
    RetailerDto[] Retailers,
    string NormalizedIsbn);

/// <summary>One store's page for this book.</summary>
public sealed record RetailerDto(string? Key, string? Name, string? Url, string? ProductId);
