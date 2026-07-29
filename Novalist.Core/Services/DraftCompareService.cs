using System.Text.Json;
using Novalist.Core.Models;
using Novalist.Core.Utilities;

namespace Novalist.Core.Services;

/// <summary>What happened to one scene between two drafts.</summary>
public enum DraftSceneState
{
    /// <summary>Present in both, with prose that matches word for word.</summary>
    Same,

    /// <summary>Present in both, with prose that does not.</summary>
    Changed,

    /// <summary>Only on the right - written after the drafts parted.</summary>
    Added,

    /// <summary>Only on the left - cut on the way to the right-hand draft.</summary>
    Removed
}

/// <summary>One scene as it stands in both drafts.</summary>
public sealed record DraftSceneComparison(
    string SceneId,
    string Title,
    string ChapterGuid,
    string ChapterTitle,
    DraftSceneState State,
    int LeftWords,
    int RightWords);

/// <summary>Two drafts of the same book, scene by scene.</summary>
public sealed record DraftComparison(
    string LeftDraftId,
    string LeftName,
    string RightDraftId,
    string RightName,
    IReadOnlyList<DraftSceneComparison> Scenes)
{
    public int SameCount => Scenes.Count(s => s.State == DraftSceneState.Same);
    public int ChangedCount => Scenes.Count(s => s.State == DraftSceneState.Changed);
    public int AddedCount => Scenes.Count(s => s.State == DraftSceneState.Added);
    public int RemovedCount => Scenes.Count(s => s.State == DraftSceneState.Removed);

    /// <summary>Words the right-hand draft has that the left does not, and the reverse.</summary>
    public int LeftWords => Scenes.Sum(s => s.LeftWords);
    public int RightWords => Scenes.Sum(s => s.RightWords);
}

/// <summary>
/// Reads and compares drafts without switching to them.
///
/// Cloning a draft has always been one click, and BookDraftMetadata has always
/// recorded which draft a clone came from, but nothing read it back: there was
/// no way to see what the rewrite actually changed, and no way to bring one
/// scene of it across. Everything here works on a draft the writer is not in,
/// because the whole point is to look at the other one.
/// </summary>
public sealed class DraftCompareService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IProjectService _projects;
    private readonly IFileService _files;
    private readonly ISnapshotService? _snapshots;

    public DraftCompareService(
        IProjectService projects, IFileService files, ISnapshotService? snapshots = null)
    {
        _projects = projects;
        _files = files;
        _snapshots = snapshots;
    }

    /// <summary>
    /// Compares two drafts scene by scene. Scenes are matched by id, which
    /// survives cloning, so a scene that was renamed and rewritten is still
    /// recognised as the same scene rather than one added and one removed.
    /// Returns null when either id is not a draft of the open book.
    /// </summary>
    public async Task<DraftComparison?> CompareAsync(string leftDraftId, string rightDraftId)
    {
        var book = _projects.ActiveBook;
        if (book == null) return null;

        var left = book.Drafts.FirstOrDefault(d => d.Id == leftDraftId);
        var right = book.Drafts.FirstOrDefault(d => d.Id == rightDraftId);
        if (left == null || right == null) return null;

        var leftSide = await LoadAsync(left).ConfigureAwait(false);
        var rightSide = await LoadAsync(right).ConfigureAwait(false);

        var rows = new List<DraftSceneComparison>();

        // The right-hand draft drives the order, because it is the one being
        // read as "the current state". Scenes only on the left are appended in
        // their own order afterwards, which is where a cut scene belongs.
        foreach (var (chapter, scene) in rightSide.Ordered)
        {
            var inLeft = leftSide.Scenes.TryGetValue(scene.Id, out var counterpart);
            var rightText = await ReadAsync(rightSide, chapter, scene).ConfigureAwait(false);
            var leftText = !inLeft
                ? string.Empty
                : await ReadAsync(leftSide, counterpart.Chapter, counterpart.Scene)
                    .ConfigureAwait(false);

            var state = !inLeft
                ? DraftSceneState.Added
                : Normalise(leftText) == Normalise(rightText)
                    ? DraftSceneState.Same
                    : DraftSceneState.Changed;

            rows.Add(new DraftSceneComparison(
                scene.Id, scene.Title, chapter.Guid, chapter.Title, state,
                WordCount(leftText), WordCount(rightText)));
        }

        foreach (var (chapter, scene) in leftSide.Ordered)
        {
            if (rightSide.Scenes.ContainsKey(scene.Id)) continue;
            var text = await ReadAsync(leftSide, chapter, scene).ConfigureAwait(false);
            rows.Add(new DraftSceneComparison(
                scene.Id, scene.Title, chapter.Guid, chapter.Title, DraftSceneState.Removed,
                WordCount(text), 0));
        }

        return new DraftComparison(left.Id, left.Name, right.Id, right.Name, rows);
    }

    /// <summary>
    /// One scene's prose as it stands in a draft, HTML stripped. Empty when the
    /// draft or the scene is unknown, which is what a diff of an added scene
    /// wants on its left-hand side anyway.
    /// </summary>
    public async Task<string> ReadSceneTextAsync(string draftId, string sceneId)
        => TextDiff.StripHtml(await ReadSceneHtmlAsync(draftId, sceneId).ConfigureAwait(false));

    /// <summary>One scene's prose as stored, markup intact.</summary>
    public async Task<string> ReadSceneHtmlAsync(string draftId, string sceneId)
    {
        var book = _projects.ActiveBook;
        var draft = book?.Drafts.FirstOrDefault(d => d.Id == draftId);
        if (draft == null) return string.Empty;

        var side = await LoadAsync(draft).ConfigureAwait(false);
        if (!side.Scenes.TryGetValue(sceneId, out var found)) return string.Empty;
        return await ReadAsync(side, found.Chapter, found.Scene).ConfigureAwait(false);
    }

    /// <summary>
    /// Brings one scene's prose from another draft into the active one. The
    /// scene it lands on is snapshotted first, so taking the wrong version is
    /// undoable from the scene's own history rather than being a straight
    /// overwrite.
    ///
    /// Returns false when the scene is not in the source draft, or when the
    /// active draft has neither the scene nor the chapter it would go in -
    /// inventing a chapter to hold it would be a structural change the writer
    /// did not ask for.
    /// </summary>
    public async Task<bool> TakeSceneAsync(string fromDraftId, string sceneId)
    {
        var book = _projects.ActiveBook;
        var source = book?.Drafts.FirstOrDefault(d => d.Id == fromDraftId);
        if (book == null || source == null || source.Id == book.ActiveDraft?.Id) return false;

        var side = await LoadAsync(source).ConfigureAwait(false);
        if (!side.Scenes.TryGetValue(sceneId, out var found)) return false;

        var html = await ReadAsync(side, found.Chapter, found.Scene).ConfigureAwait(false);

        var target = _projects.GetChaptersOrdered()
            .Select(c => (Chapter: c, Scene: _projects.GetScenesForChapter(c.Guid)
                .FirstOrDefault(s => s.Id == sceneId)))
            .FirstOrDefault(pair => pair.Scene != null);

        if (target.Scene != null)
        {
            if (_snapshots != null)
                await _snapshots.TakeAsync(target.Chapter, target.Scene, source.Name)
                    .ConfigureAwait(false);
            await _projects.WriteSceneContentAsync(target.Chapter, target.Scene, html)
                .ConfigureAwait(false);
            return true;
        }

        var chapter = _projects.GetChaptersOrdered()
            .FirstOrDefault(c => c.Guid == found.Chapter.Guid);
        if (chapter == null) return false;

        var created = await _projects.CreateSceneAsync(chapter.Guid, found.Scene.Title)
            .ConfigureAwait(false);
        await _projects.WriteSceneContentAsync(chapter, created, html).ConfigureAwait(false);
        return true;
    }

    /// <summary>A draft's structure and where its files are, read straight off disk.</summary>
    private sealed record DraftSide(
        string Root,
        string ChapterFolder,
        IReadOnlyList<(ChapterData Chapter, SceneData Scene)> Ordered,
        IReadOnlyDictionary<string, (ChapterData Chapter, SceneData Scene)> Scenes);

    private async Task<DraftSide> LoadAsync(BookDraftMetadata draft)
    {
        var book = _projects.ActiveBook!;
        var root = _files.CombinePath(_projects.ActiveBookRoot!, "Drafts", draft.FolderName);

        // The active draft is already in memory and may hold unsaved edits the
        // files do not; reading it back off disk would compare against a stale
        // copy of the very draft the writer is looking at.
        var isActive = draft.Id == book.ActiveDraft?.Id;
        var chapters = isActive
            ? book.Chapters
            : (await ReadJsonAsync<BookDraftData>(_files.CombinePath(root, "draft.json"))
                .ConfigureAwait(false))?.Chapters ?? [];
        var manifest = isActive
            ? _projects.ScenesManifest ?? new ScenesManifest()
            : await ReadJsonAsync<ScenesManifest>(_files.CombinePath(root, "scenes.json"))
                .ConfigureAwait(false) ?? new ScenesManifest();

        var ordered = new List<(ChapterData, SceneData)>();
        var index = new Dictionary<string, (ChapterData, SceneData)>();
        foreach (var chapter in chapters.OrderBy(c => c.Order))
        {
            if (!manifest.Chapters.TryGetValue(chapter.Guid, out var scenes)) continue;
            foreach (var scene in scenes.OrderBy(s => s.Order))
            {
                ordered.Add((chapter, scene));
                index[scene.Id] = (chapter, scene);
            }
        }

        return new DraftSide(root, book.ChapterFolder, ordered, index);
    }

    private async Task<T?> ReadJsonAsync<T>(string path) where T : class
    {
        if (!await _files.ExistsAsync(path).ConfigureAwait(false)) return null;
        var json = await _files.ReadTextAsync(path).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // A draft folder someone edited by hand should not take the compare
            // down; it reads as an empty draft, which is visibly wrong rather
            // than silently misleading.
            return null;
        }
    }

    private async Task<string> ReadAsync(DraftSide side, ChapterData chapter, SceneData scene)
    {
        var path = _files.CombinePath(
            side.Root, side.ChapterFolder, chapter.FolderName, scene.FileName);
        if (!await _files.ExistsAsync(path).ConfigureAwait(false)) return string.Empty;
        return FileFrontMatter.Strip(await _files.ReadTextAsync(path).ConfigureAwait(false));
    }

    /// <summary>
    /// Compares prose as words rather than as markup, so a change of formatting
    /// that leaves every word where it was does not read as a rewrite.
    /// </summary>
    private static string Normalise(string html)
        => string.Join(' ', TextDiff.StripHtml(html)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static int WordCount(string html)
        => TextDiff.StripHtml(html).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
