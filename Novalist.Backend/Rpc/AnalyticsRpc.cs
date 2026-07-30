using Novalist.Core.Models;
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
    /// Who drops out of the book, and for how long.
    ///
    /// The counts behind this have been drawn as a grid for a while. Reading
    /// forty rows of it to find the character who vanished in act two is the
    /// work the report exists to do.
    /// </summary>
    [JsonRpcMethod("analytics/castAbsence")]
    public async Task<AbsenceRowDto[]> CastAbsenceAsync(int minimumGap = 2)
    {
        var service = new BookAnalyticsService(
            _workspace.Projects, new EntityService(_workspace.Projects));
        var result = await service.ComputeAsync();

        return [.. CastAbsence
            .From(result.Characters, result.ChapterTitles.Count, minimumGap)
            .Select(r => new AbsenceRowDto(
                r.EntityId, r.Label, r.TotalScenes, r.LongestGap,
                ChapterName(result.ChapterTitles, r.GapStartChapter),
                ChapterName(result.ChapterTitles, r.GapEndChapter),
                ChapterName(result.ChapterTitles, r.FirstChapter),
                ChapterName(result.ChapterTitles, r.LastChapter),
                r.ChaptersSinceLastSeen))];
    }

    /// <summary>
    /// A chapter's title, or empty when there is no such chapter - a row with
    /// no gap has no gap to name.
    /// </summary>
    private static string ChapterName(IReadOnlyList<string> titles, int index)
        => index >= 0 && index < titles.Count ? titles[index] : string.Empty;

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

    /// <summary>
    /// One of the writer's own numeric scene fields, scene by scene in reading
    /// order.
    ///
    /// A number per scene only says something as a shape: a rating axis for
    /// stakes, or pace, or how much a viewpoint character knows, is the kind of
    /// thing that reads flat in a column and obvious as a curve. Which axes
    /// exist is the writer's business - these are their fields, not a fixed set
    /// of ours.
    /// </summary>
    [JsonRpcMethod("analytics/sceneFieldCurve")]
    public TensionPointDto[] SceneFieldCurve(string key)
    {
        var definition = new ManuscriptPropertyService(_workspace.Projects)
            .Definitions(ManuscriptPropertyScope.Scene)
            .FirstOrDefault(d =>
                string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase)
                && d.Type == CustomPropertyType.Int);
        if (definition == null) return [];

        var points = new List<TensionPointDto>();
        foreach (var chapter in _workspace.Projects.GetChaptersOrdered())
            foreach (var scene in _workspace.Projects.GetScenesForChapter(chapter.Guid))
            {
                // A value that will not parse is no value. It cannot get in
                // through the field editor, but a hand-edited file can hold one.
                int? value = scene.Properties != null
                    && scene.Properties.TryGetValue(definition.Key, out var raw)
                    && int.TryParse(raw, out var parsed)
                    ? parsed
                    : null;
                points.Add(new TensionPointDto(
                    chapter.Guid, chapter.Title, scene.Id, scene.Title, value, string.Empty));
            }

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

/// <summary>
/// One character's absence, with chapters named rather than numbered - a row
/// saying "gap of 4 from chapter index 7" is a row somebody has to go and
/// count against.
/// </summary>
public sealed record AbsenceRowDto(
    string EntityId,
    string Label,
    int TotalScenes,
    int LongestGap,
    string GapStart,
    string GapEnd,
    string FirstChapter,
    string LastChapter,
    int ChaptersSinceLastSeen);
