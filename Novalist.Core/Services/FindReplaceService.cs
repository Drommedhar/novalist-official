using System.Text.RegularExpressions;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public sealed class FindReplaceService : IFindReplaceService
{
    private readonly IProjectService _projectService;

    public FindReplaceService(IProjectService projectService)
    {
        _projectService = projectService;
    }

    public async Task<IReadOnlyList<FindMatch>> FindAsync(FindOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(options.Pattern))
            return Array.Empty<FindMatch>();

        var regex = BuildRegex(options);
        var results = new List<FindMatch>();

        foreach (var (chapter, scene) in EnumerateScopedScenes(options))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var html = await _projectService.ReadSceneContentAsync(chapter, scene).ConfigureAwait(false);
            var plain = StripHtml(html);
            foreach (Match m in regex.Matches(plain))
            {
                results.Add(new FindMatch
                {
                    ChapterGuid = chapter.Guid,
                    ChapterTitle = chapter.Title,
                    SceneId = scene.Id,
                    SceneTitle = scene.Title,
                    Index = m.Index,
                    Length = m.Length,
                    Before = SnippetBefore(plain, m.Index),
                    MatchedText = m.Value,
                    After = SnippetAfter(plain, m.Index + m.Length)
                });
            }
        }

        return results;
    }

    public async Task<int> ReplaceAllAsync(FindOptions options, ISnapshotService? snapshotService = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(options.Pattern))
            return 0;

        var regex = BuildRegex(options);
        int totalReplacements = 0;

        foreach (var (chapter, scene) in EnumerateScopedScenes(options))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var html = await _projectService.ReadSceneContentAsync(chapter, scene).ConfigureAwait(false);
            // Replace inside the raw HTML — patterns may inadvertently span tags
            // but for typical word-level edits this is safe enough.
            var (newHtml, count) = ReplaceWithCount(regex, html, options.Replacement);
            if (count == 0) continue;

            if (snapshotService != null)
                await snapshotService.TakeAsync(chapter, scene, "Auto-snapshot before find/replace").ConfigureAwait(false);

            await _projectService.WriteSceneContentAsync(chapter, scene, newHtml).ConfigureAwait(false);
            scene.WordCount = CountWords(StripHtml(newHtml));
            totalReplacements += count;
        }

        if (totalReplacements > 0)
            await _projectService.SaveScenesAsync().ConfigureAwait(false);

        return totalReplacements;
    }

    private IEnumerable<(ChapterData Chapter, SceneData Scene)> EnumerateScopedScenes(FindOptions options)
    {
        var chapters = _projectService.GetChaptersOrdered();
        switch (options.Scope)
        {
            case FindScope.CurrentScene:
            {
                if (string.IsNullOrEmpty(options.AnchorChapterGuid) || string.IsNullOrEmpty(options.AnchorSceneId))
                    yield break;
                var chapter = chapters.FirstOrDefault(c => c.Guid == options.AnchorChapterGuid);
                if (chapter == null) yield break;
                var scene = _projectService.GetScenesForChapter(chapter.Guid)
                    .FirstOrDefault(s => s.Id == options.AnchorSceneId);
                if (scene != null) yield return (chapter, scene);
                break;
            }
            case FindScope.CurrentChapter:
            {
                if (string.IsNullOrEmpty(options.AnchorChapterGuid)) yield break;
                var chapter = chapters.FirstOrDefault(c => c.Guid == options.AnchorChapterGuid);
                if (chapter == null) yield break;
                foreach (var scene in _projectService.GetScenesForChapter(chapter.Guid))
                    yield return (chapter, scene);
                break;
            }
            case FindScope.ActiveBook:
            case FindScope.Project: // No multi-book reach yet — same as ActiveBook.
            {
                foreach (var chapter in chapters)
                {
                    foreach (var scene in _projectService.GetScenesForChapter(chapter.Guid))
                        yield return (chapter, scene);
                }
                break;
            }
        }
    }

    private static Regex BuildRegex(FindOptions options)
    {
        var pattern = options.UseRegex ? options.Pattern : Regex.Escape(options.Pattern);
        if (options.WholeWord)
            pattern = $@"(?<![\p{{L}}\p{{N}}_]){pattern}(?![\p{{L}}\p{{N}}_])";
        var opts = RegexOptions.CultureInvariant;
        if (!options.MatchCase) opts |= RegexOptions.IgnoreCase;
        return new Regex(pattern, opts);
    }

    private static (string Replaced, int Count) ReplaceWithCount(Regex regex, string input, string replacement)
    {
        int count = 0;
        var replaced = regex.Replace(input, _ => { count++; return replacement; });
        return (replaced, count);
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        var withBreaks = Regex.Replace(html, "</p>|<br ?/?>", "\n", RegexOptions.IgnoreCase);
        return Regex.Replace(withBreaks, "<[^>]+>", string.Empty);
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return Regex.Matches(text, @"[\p{L}\p{N}]+(?:['’-][\p{L}\p{N}]+)*", RegexOptions.CultureInvariant).Count;
    }

    private const int SnippetWidth = 40;

    private static string SnippetBefore(string text, int index)
    {
        if (index <= 0) return string.Empty;
        var start = Math.Max(0, index - SnippetWidth);
        return text.Substring(start, index - start).Replace('\n', ' ');
    }

    private static string SnippetAfter(string text, int after)
    {
        if (after >= text.Length) return string.Empty;
        var len = Math.Min(SnippetWidth, text.Length - after);
        return text.Substring(after, len).Replace('\n', ' ');
    }
}
