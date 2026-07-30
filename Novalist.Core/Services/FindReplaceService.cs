using System.Text.RegularExpressions;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public sealed class FindReplaceService : IFindReplaceService
{
    private readonly IProjectService _projectService;
    private readonly IEntityService? _entityService;

    /// <param name="entityService">
    /// Supplied when the caller wants Codex entries searchable. Optional so a
    /// caller that only ever searches prose does not have to build one.
    /// </param>
    public FindReplaceService(IProjectService projectService, IEntityService? entityService = null)
    {
        _projectService = projectService;
        _entityService = entityService;
    }

    public async Task<IReadOnlyList<FindMatch>> FindAsync(FindOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(options.Pattern))
            return Array.Empty<FindMatch>();

        var regex = BuildRegex(options);
        var results = new List<FindMatch>();

        await ForEachBookInScopeAsync(options, async () =>
        {
            foreach (var (chapter, scene) in EnumerateScopedScenes(options))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var html = await _projectService.ReadSceneContentAsync(chapter, scene).ConfigureAwait(false);
                var plain = StripHtml(html);
                var bookTitle = _projectService.ActiveBook?.Name ?? string.Empty;
                Collect(results, regex, plain, "prose", bookTitle, chapter, scene);

                // The places a writer leaves what they mean to come back to.
                if (options.IncludeSceneNotes)
                {
                    Collect(results, regex, scene.Synopsis ?? string.Empty, "synopsis",
                        bookTitle, chapter, scene);
                    Collect(results, regex, scene.Notes ?? string.Empty, "notes",
                        bookTitle, chapter, scene);
                    foreach (var comment in scene.Comments ?? [])
                        Collect(results, regex, comment.Text ?? string.Empty, "comment",
                            bookTitle, chapter, scene);
                }
            }
        }).ConfigureAwait(false);

        if (options.IncludeCodex) await CollectCodexAsync(results, regex).ConfigureAwait(false);
        return results;
    }

    /// <summary>
    /// Matches in Codex entries - a name, a description, a section. Reported
    /// only: renaming an entry has its own command, which carries the change
    /// through every reference to it, and a blind replace here would not.
    /// </summary>
    private async Task CollectCodexAsync(List<FindMatch> results, Regex regex)
    {
        if (_entityService == null) return;

        var bookTitle = _projectService.ActiveBook?.Name ?? string.Empty;
        void Add(string where, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (Match m in regex.Matches(text))
                results.Add(new FindMatch
                {
                    BookTitle = bookTitle,
                    Field = "codex",
                    ChapterTitle = where,
                    SceneTitle = where,
                    Index = m.Index,
                    Length = m.Length,
                    Before = SnippetBefore(text, m.Index),
                    MatchedText = m.Value,
                    After = SnippetAfter(text, m.Index + m.Length)
                });
        }

        foreach (var c in await _entityService.LoadCharactersAsync().ConfigureAwait(false))
        {
            var name = EntityResolveIndex.Compose(c.Name, c.Surname);
            Add(name, name);
            foreach (var section in c.Sections) Add(name, StripHtml(section.Content));
        }
        foreach (var l in await _entityService.LoadLocationsAsync().ConfigureAwait(false))
        {
            Add(l.Name, l.Name);
            foreach (var section in l.Sections) Add(l.Name, StripHtml(section.Content));
        }
        foreach (var i in await _entityService.LoadItemsAsync().ConfigureAwait(false))
        {
            Add(i.Name, i.Name);
            foreach (var section in i.Sections) Add(i.Name, StripHtml(section.Content));
        }
        foreach (var l in await _entityService.LoadLoreAsync().ConfigureAwait(false))
        {
            Add(l.Name, l.Name);
            foreach (var section in l.Sections) Add(l.Name, StripHtml(section.Content));
        }
    }

    public async Task<int> ReplaceAllAsync(FindOptions options, ISnapshotService? snapshotService = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(options.Pattern))
            return 0;

        var regex = BuildRegex(options);
        int totalReplacements = 0;

        // One label for this run, so its snapshots are one batch rather than
        // hundreds of identically-named ones that cannot be told from the last
        // run's. A project-wide replace on a long book is the case that matters.
        var batchLabel = SnapshotBatchLabel(DateTime.Now);

        await ForEachBookInScopeAsync(options, async () =>
        {
            var replacedHere = 0;
            foreach (var (chapter, scene) in EnumerateScopedScenes(options))
            {
                cancellationToken.ThrowIfCancellationRequested();

                // A synopsis and a note are plain text the writer owns, so a
                // replace can reach them. A comment is a conversation and a
                // Codex entry has a rename of its own that carries references
                // with it; neither is rewritten from here.
                if (options.IncludeSceneNotes)
                {
                    var (synopsis, synopsisCount) =
                        ReplaceWithCount(regex, scene.Synopsis ?? string.Empty, options.Replacement);
                    if (synopsisCount > 0) scene.Synopsis = synopsis;
                    var (notes, notesCount) =
                        ReplaceWithCount(regex, scene.Notes ?? string.Empty, options.Replacement);
                    if (notesCount > 0) scene.Notes = notes;
                    replacedHere += synopsisCount + notesCount;
                }

                var html = await _projectService.ReadSceneContentAsync(chapter, scene).ConfigureAwait(false);
                // Replace inside the raw HTML — patterns may inadvertently span tags
                // but for typical word-level edits this is safe enough.
                var (newHtml, count) = ReplaceWithCount(regex, html, options.Replacement);
                if (count == 0) continue;

                if (snapshotService != null)
                    await snapshotService.TakeAsync(chapter, scene, batchLabel).ConfigureAwait(false);

                await _projectService.WriteSceneContentAsync(chapter, scene, newHtml).ConfigureAwait(false);
                scene.WordCount = CountWords(StripHtml(newHtml));
                replacedHere += count;
            }

            // Saved per book: the manifest belongs to whichever book is open,
            // so leaving it until the end would write it into the wrong one.
            if (replacedHere > 0)
                await _projectService.SaveScenesAsync().ConfigureAwait(false);
            totalReplacements += replacedHere;
        }).ConfigureAwait(false);

        return totalReplacements;
    }

    /// <summary>
    /// Runs the body once per book the scope covers, restoring the book that
    /// was open before. Reaching another book's scenes means opening it: their
    /// paths hang off the active book's folder, so there is no read-only way in.
    /// </summary>
    private async Task ForEachBookInScopeAsync(FindOptions options, Func<Task> body)
    {
        var project = _projectService.CurrentProject;
        if (options.Scope != FindScope.Project || project == null || project.Books.Count <= 1)
        {
            await body().ConfigureAwait(false);
            return;
        }

        var openedWith = project.ActiveBookId;
        try
        {
            foreach (var book in project.Books.ToList())
            {
                await _projectService.SwitchBookAsync(book.Id).ConfigureAwait(false);
                await body().ConfigureAwait(false);
            }
        }
        finally
        {
            // Even if a book fails to open, the writer is left where they were
            // rather than in whichever book the sweep stopped on.
            if (_projectService.CurrentProject?.ActiveBookId != openedWith)
                await _projectService.SwitchBookAsync(openedWith).ConfigureAwait(false);
        }
    }

    /// <summary>Adds every match in one field, tagged with which field it was.</summary>
    private static void Collect(
        List<FindMatch> results, Regex regex, string text, string field,
        string bookTitle, ChapterData chapter, SceneData scene)
    {
        if (text.Length == 0) return;
        foreach (Match m in regex.Matches(text))
        {
            results.Add(new FindMatch
            {
                BookTitle = bookTitle,
                Field = field,
                ChapterGuid = chapter.Guid,
                ChapterTitle = chapter.Title,
                SceneId = scene.Id,
                SceneTitle = scene.Title,
                Index = m.Index,
                Length = m.Length,
                Before = SnippetBefore(text, m.Index),
                MatchedText = m.Value,
                After = SnippetAfter(text, m.Index + m.Length)
            });
        }
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
            // Project scope is handled a book at a time by the caller, so from
            // in here it is the same walk over whichever book is open.
            case FindScope.Project:
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

    /// <summary>
    /// The label every snapshot from one Replace All run carries.
    ///
    /// The prefix is what the snapshots dialog groups on, and the local
    /// timestamp is what separates this run from the one before it. Kept in one
    /// place so the two ends cannot drift apart.
    /// </summary>
    public const string SnapshotBatchPrefix = "Before find/replace";

    internal static string SnapshotBatchLabel(DateTime when)
        => $"{SnapshotBatchPrefix} {when:yyyy-MM-dd HH:mm:ss}";

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
