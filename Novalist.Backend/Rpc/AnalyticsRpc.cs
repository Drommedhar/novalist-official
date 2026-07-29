using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Where things sit across the whole book. Novalist computed POV and mentions
/// per scene and only ever showed them for the scene in view.
/// </summary>
public sealed class AnalyticsRpc
{
    private readonly Workspace _workspace;

    public AnalyticsRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    /// <summary>
    /// The whole-book distributions. Reads every scene file, so it is asked for
    /// when the Dashboard opens rather than kept live.
    /// </summary>
    [JsonRpcMethod("analytics/book")]
    public async Task<BookAnalyticsDto> BookAsync()
    {
        var service = new BookAnalyticsService(
            _workspace.Projects, new EntityService(_workspace.Projects));
        var result = await service.ComputeAsync();

        return new BookAnalyticsDto(
            [.. result.ChapterTitles],
            [.. result.Pov.Select(ToDto)],
            [.. result.Acts.Select(ToDto)],
            [.. result.Characters.Select(ToDto)],
            [.. result.Locations.Select(ToDto)],
            [.. result.Unused]);
    }

    private static DistributionDto ToDto(DistributionRow r)
        => new(r.Key, r.Label, r.SceneCount, r.WordCount, r.Percent);

    private static PresenceDto ToDto(PresenceRow r)
        => new(r.EntityId, r.Label, r.TotalScenes, [.. r.ScenesPerChapter]);
}

/// <summary>One slice of the book. An empty <c>Key</c> is the scenes with
/// nothing set - no POV, or no act.</summary>
public sealed record DistributionDto(
    string Key, string Label, int SceneCount, int WordCount, int Percent);

/// <summary>Which chapters an entity appears in. <c>ScenesPerChapter</c> is
/// parallel to the chapter title list.</summary>
public sealed record PresenceDto(
    string EntityId, string Label, int TotalScenes, int[] ScenesPerChapter);

public sealed record BookAnalyticsDto(
    string[] ChapterTitles,
    DistributionDto[] Pov,
    DistributionDto[] Acts,
    PresenceDto[] Characters,
    PresenceDto[] Locations,
    string[] Unused);
