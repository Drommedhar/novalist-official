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

    /// <summary>
    /// Grades every sentence in the text the editor is showing. Offsets are
    /// into the string as passed, because the editor decorates that same string
    /// and any normalising here would shift every mark after it.
    /// </summary>
    [JsonRpcMethod("style/sentenceReadability")]
    public SentenceReadabilityDto[] SentenceReadabilityAsync(string? text)
        => [.. TextStatistics
            .GradeSentences(text ?? string.Empty, Language)
            .Select(s => new SentenceReadabilityDto(
                s.Offset, s.Length, s.Score, s.Level.ToString()))];

    /// <summary>The writer's own flagged words, counted alongside the bundled checks.</summary>
    private IReadOnlyCollection<string> WatchWords
        => _workspace.Settings.Settings.StyleWatchWords;

    /// <summary>The words the style report is watching for this writer.</summary>
    [JsonRpcMethod("style/watchWords")]
    public async Task<string[]> GetWatchWordsAsync()
    {
        await _workspace.Settings.LoadAsync();
        return [.. WatchWords];
    }

    /// <summary>
    /// Replaces the list. Blanks and repeats are dropped: an empty entry
    /// matches nothing and a repeat would count the same word twice.
    /// </summary>
    [JsonRpcMethod("style/setWatchWords")]
    public async Task<string[]> SetWatchWordsAsync(string[]? words)
    {
        await _workspace.Settings.LoadAsync();
        _workspace.Settings.Settings.StyleWatchWords = [.. (words ?? [])
            .Select(w => (w ?? string.Empty).Trim())
            .Where(w => w.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        await _workspace.Settings.SaveAsync();
        return [.. WatchWords];
    }

    [JsonRpcMethod("style/scene")]
    public async Task<StyleReportDto> SceneAsync(string chapterGuid, string sceneId)
    {
        var (chapter, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        var html = await _workspace.Projects.ReadSceneContentAsync(chapter, scene);
        return ToDto(ProseStyleAnalyzer.Analyze(TextDiff.StripHtml(html), Language, WatchWords));
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

        return ToDto(ProseStyleAnalyzer.Analyze(text.ToString(), Language, WatchWords));
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

/// <summary>One graded sentence: where it is, how it scored, and which band.</summary>
public sealed record SentenceReadabilityDto(int Offset, int Length, int Score, string Level);

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
