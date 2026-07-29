using Novalist.Core.Services;
using Novalist.Core.Utilities;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Deterministic offline craft reports over a scene, a chapter, or the book.</summary>
public sealed class StyleRpc
{
    private readonly Workspace _workspace;

    public StyleRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private string Language => _workspace.Settings.Effective.AutoReplacementLanguage;

    [JsonRpcMethod("style/scene")]
    public async Task<StyleReportDto> SceneAsync(string chapterGuid, string sceneId)
    {
        var (chapter, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        var html = await _workspace.Projects.ReadSceneContentAsync(chapter, scene);
        return ToDto(ProseStyleAnalyzer.Analyze(TextDiff.StripHtml(html), Language));
    }

    /// <summary>
    /// Whole-book report. Scenes are concatenated so cross-scene repetition is
    /// visible, which is where the interesting habits show up.
    /// </summary>
    [JsonRpcMethod("style/book")]
    public async Task<StyleReportDto> BookAsync(string? chapterGuid = null)
    {
        var text = new System.Text.StringBuilder();
        foreach (var chapter in _workspace.Projects.GetChaptersOrdered())
        {
            if (!string.IsNullOrEmpty(chapterGuid)
                && !string.Equals(chapter.Guid, chapterGuid, StringComparison.Ordinal))
                continue;

            foreach (var scene in _workspace.Projects.GetScenesForChapter(chapter.Guid))
            {
                var html = await _workspace.Projects.ReadSceneContentAsync(chapter, scene);
                text.AppendLine(TextDiff.StripHtml(html));
            }
        }

        return ToDto(ProseStyleAnalyzer.Analyze(text.ToString(), Language));
    }

    private static StyleReportDto ToDto(ProseStyleReport r) =>
        new(
            r.Language,
            r.WordCount,
            r.SentenceCount,
            r.MeanSentenceWords,
            r.SentenceLengthStdDev,
            r.LongestSentenceWords,
            r.Findings
                .Select(f => new StyleFindingDto(
                    f.Key,
                    f.Count,
                    f.Per1000Words,
                    f.Supported,
                    f.Examples.Select(e => new StyleHitDto(e.Text, e.Offset, e.Context)).ToArray()))
                .ToArray());
}

public sealed record StyleHitDto(string Text, int Offset, string Context);

public sealed record StyleFindingDto(
    string Key, int Count, double Per1000Words, bool Supported, StyleHitDto[] Examples);

public sealed record StyleReportDto(
    string Language,
    int WordCount,
    int SentenceCount,
    double MeanSentenceWords,
    double SentenceLengthStdDev,
    int LongestSentenceWords,
    StyleFindingDto[] Findings);
