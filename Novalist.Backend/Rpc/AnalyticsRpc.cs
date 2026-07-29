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

    /// <summary>
    /// Each scene's tension in reading order.
    ///
    /// Intensity has been computed and hand-overridable per scene for a long
    /// time and shown only as one number in the Inspector, where a curve is the
    /// only shape it has ever had anything to say in.
    /// </summary>
    [JsonRpcMethod("analytics/tension")]
    public TensionPointDto[] Tension()
    {
        var points = new List<TensionPointDto>();
        foreach (var chapter in _workspace.Projects.GetChaptersOrdered())
            foreach (var scene in _workspace.Projects.GetScenesForChapter(chapter.Guid))
                points.Add(new TensionPointDto(
                    chapter.Guid, chapter.Title, scene.Id, scene.Title,
                    scene.AnalysisOverrides?.Intensity,
                    scene.AnalysisOverrides?.Emotion ?? string.Empty));

        return [.. points];
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

/// <summary>One scene's tension. A null intensity is a scene nobody has rated
/// yet, which is not the same as a flat one.</summary>
public sealed record TensionPointDto(
    string ChapterGuid, string ChapterTitle, string SceneId, string SceneTitle,
    int? Intensity, string Emotion);

public sealed record BookAnalyticsDto(
    string[] ChapterTitles,
    DistributionDto[] Pov,
    DistributionDto[] Acts,
    PresenceDto[] Characters,
    PresenceDto[] Locations,
    string[] Unused);
