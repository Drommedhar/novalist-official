using System.Text.Json;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>One scene of a draft, as the picker needs to list it.</summary>
public sealed record DraftSceneRef(string Id, string Title);

/// <summary>One chapter of a draft, with the scenes under it.</summary>
public sealed record DraftChapterRef(string Guid, string Title, IReadOnlyList<DraftSceneRef> Scenes);

/// <summary>A draft's shape, read without switching to it.</summary>
public sealed record DraftStructure(string DraftId, string Name, IReadOnlyList<DraftChapterRef> Chapters);

/// <summary>What a transfer did.</summary>
/// <param name="Chapters">Chapters created in the target draft.</param>
/// <param name="Scenes">Scenes written into the target draft.</param>
/// <param name="Replaced">Scenes the target already had, whose prose was overwritten.</param>
/// <param name="Moved">Whether the source draft gave them up.</param>
public sealed record DraftTransferResult(int Chapters, int Scenes, int Replaced, bool Moved);

/// <summary>
/// Moves and copies chapters and scenes between the drafts of a book.
///
/// A draft owns its own chapter tree and its own scene files, and until now the
/// only way content crossed between two of them was one scene at a time through
/// the compare dialog. A writer who kept a chapter in the wrong draft - or who
/// wanted the rewritten chapter 12 in the draft they are actually submitting -
/// had to retype it.
///
/// Scene ids survive the crossing. Drafts are usually clones of one another, so
/// the same scene exists on both sides under the same id; keeping it is what
/// lets the compare dialog still recognise the two as one scene afterwards, and
/// it is why sending a chapter that is already there rewrites its prose rather
/// than leaving a second copy beside it.
/// </summary>
public sealed class DraftTransferService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private readonly IProjectService _projects;
    private readonly IFileService _files;

    public DraftTransferService(IProjectService projects, IFileService files)
    {
        _projects = projects;
        _files = files;
    }

    /// <summary>
    /// A draft's chapters and scenes in reading order. Null when no project is
    /// open or the id is not a draft of the open book.
    /// </summary>
    public async Task<DraftStructure?> ReadStructureAsync(string draftId)
    {
        var book = _projects.ActiveBook;
        var draft = book?.Drafts.FirstOrDefault(d => d.Id == draftId);
        if (book == null || draft == null) return null;

        // The active draft may hold edits the folder does not, so it is read
        // from memory. Every other draft is only ever on disk.
        await _projects.FlushActiveDraftAsync().ConfigureAwait(false);
        var side = await ReadSideAsync(book, draft).ConfigureAwait(false);

        var chapters = side.Chapters
            .OrderBy(c => c.Order)
            .Select(c => new DraftChapterRef(
                c.Guid,
                c.Title,
                ScenesOf(side.Manifest, c.Guid)
                    .Select(s => new DraftSceneRef(s.Id, s.Title))
                    .ToList()))
            .ToList();

        return new DraftStructure(draft.Id, draft.Name, chapters);
    }

    /// <summary>
    /// Sends chapters and scenes from one draft to another.
    ///
    /// A selected chapter takes every scene under it. A selected scene lands in
    /// the target's chapter of the same identity, and that chapter is created
    /// there when it is missing - a scene has to live somewhere, and the writer
    /// asked for this one by name.
    ///
    /// With <paramref name="move"/> the source gives the content up afterwards.
    /// Copying is the default everywhere it is offered, because a draft is a
    /// version of the book and taking a chapter out of one rewrites history the
    /// writer may not have meant to rewrite.
    /// </summary>
    public async Task<DraftTransferResult> TransferAsync(
        string fromDraftId,
        string toDraftId,
        IReadOnlyList<string> chapterGuids,
        IReadOnlyList<string> sceneIds,
        bool move)
    {
        var book = _projects.ActiveBook;
        if (book == null) return new DraftTransferResult(0, 0, 0, move);

        var source = book.Drafts.FirstOrDefault(d => d.Id == fromDraftId);
        var target = book.Drafts.FirstOrDefault(d => d.Id == toDraftId);
        if (source == null || target == null || source.Id == target.Id)
            return new DraftTransferResult(0, 0, 0, move);

        // Both sides are edited as folders, so whichever of them the writer is
        // in has to be on disk first and re-read afterwards.
        await _projects.FlushActiveDraftAsync().ConfigureAwait(false);

        var from = await ReadSideAsync(book, source).ConfigureAwait(false);
        var to = await ReadSideAsync(book, target).ConfigureAwait(false);

        var wantedChapters = new HashSet<string>(chapterGuids, StringComparer.OrdinalIgnoreCase);
        var wantedScenes = new HashSet<string>(sceneIds, StringComparer.OrdinalIgnoreCase);

        var chaptersCreated = 0;
        var scenesWritten = 0;
        var scenesReplaced = 0;
        var sentScenes = new List<(ChapterData Chapter, SceneData Scene)>();

        foreach (var chapter in from.Chapters.OrderBy(c => c.Order))
        {
            var whole = wantedChapters.Contains(chapter.Guid);
            var scenes = ScenesOf(from.Manifest, chapter.Guid)
                .Where(s => whole || wantedScenes.Contains(s.Id))
                .ToList();
            if (scenes.Count == 0 && !whole) continue;

            var landing = to.Chapters.FirstOrDefault(c => c.Guid == chapter.Guid);
            if (landing == null)
            {
                landing = CloneChapter(chapter, to);
                to.Chapters.Add(landing);
                to.Manifest.Chapters[landing.Guid] = new List<SceneData>();
                await WriteChapterFolderAsync(to, landing).ConfigureAwait(false);
                chaptersCreated++;
            }

            var landingScenes = ScenesOf(to.Manifest, landing.Guid);
            foreach (var scene in scenes)
            {
                var raw = await ReadSceneAsync(from, chapter, scene).ConfigureAwait(false);
                var existing = landingScenes.FirstOrDefault(s => s.Id == scene.Id);
                if (existing != null)
                {
                    await WriteSceneAsync(to, landing, existing, raw).ConfigureAwait(false);
                    scenesReplaced++;
                }
                else
                {
                    var copy = CloneScene(scene, landing, landingScenes);
                    landingScenes.Add(copy);
                    await WriteSceneAsync(to, landing, copy, raw).ConfigureAwait(false);
                }
                scenesWritten++;
                sentScenes.Add((chapter, scene));
            }

            to.Manifest.Chapters[landing.Guid] = landingScenes;
        }

        await SaveSideAsync(to).ConfigureAwait(false);

        if (move && scenesWritten > 0)
            await TakeFromSourceAsync(from, sentScenes, wantedChapters).ConfigureAwait(false);

        // The writer is standing in one of these folders, and it just changed
        // underneath them.
        if (IsActive(book, source) || IsActive(book, target))
            await _projects.ReloadActiveDraftAsync().ConfigureAwait(false);

        return new DraftTransferResult(chaptersCreated, scenesWritten, scenesReplaced, move);
    }

    /// <summary>
    /// Removes what was sent from the draft it came from. Scene files go, and a
    /// chapter goes with them only when nothing of it is left - a chapter that
    /// still holds scenes the writer did not send stays where it is.
    /// </summary>
    private async Task TakeFromSourceAsync(
        DraftSide from,
        IReadOnlyList<(ChapterData Chapter, SceneData Scene)> sent,
        HashSet<string> wholeChapters)
    {
        foreach (var (chapter, scene) in sent)
        {
            var path = _files.CombinePath(
                from.Root, from.ChapterFolder, chapter.FolderName, scene.FileName);
            if (await _files.ExistsAsync(path).ConfigureAwait(false))
                await _files.DeleteFileAsync(path).ConfigureAwait(false);

            var scenes = ScenesOf(from.Manifest, chapter.Guid);
            scenes.RemoveAll(s => s.Id == scene.Id);
            for (var i = 0; i < scenes.Count; i++) scenes[i].Order = i + 1;
            from.Manifest.Chapters[chapter.Guid] = scenes;
        }

        foreach (var guid in wholeChapters)
        {
            var chapter = from.Chapters.FirstOrDefault(c => c.Guid == guid);
            if (chapter == null || ScenesOf(from.Manifest, guid).Count > 0) continue;

            from.Chapters.Remove(chapter);
            from.Manifest.Chapters.Remove(guid);
            var folder = _files.CombinePath(from.Root, from.ChapterFolder, chapter.FolderName);
            if (await _files.DirectoryExistsAsync(folder).ConfigureAwait(false))
                await _files.DeleteDirectoryAsync(folder, true).ConfigureAwait(false);
        }

        var order = 1;
        foreach (var chapter in from.Chapters.OrderBy(c => c.Order))
            chapter.Order = order++;

        await SaveSideAsync(from).ConfigureAwait(false);
    }

    /// <summary>A draft's records and where its files live.</summary>
    private sealed record DraftSide(
        BookDraftMetadata Draft,
        string Root,
        string ChapterFolder,
        List<ChapterData> Chapters,
        List<ActData> Acts,
        List<ChapterData> Trash,
        ScenesManifest Manifest);

    private async Task<DraftSide> ReadSideAsync(BookData book, BookDraftMetadata draft)
    {
        var root = _files.CombinePath(_projects.ActiveBookRoot!, "Drafts", draft.FolderName);
        var data = await ReadJsonAsync<BookDraftData>(_files.CombinePath(root, "draft.json"))
            .ConfigureAwait(false) ?? new BookDraftData();
        var manifest = await ReadJsonAsync<ScenesManifest>(_files.CombinePath(root, "scenes.json"))
            .ConfigureAwait(false) ?? new ScenesManifest();

        return new DraftSide(
            draft, root, book.ChapterFolder, data.Chapters, data.Acts, data.Trash, manifest);
    }

    private async Task SaveSideAsync(DraftSide side)
    {
        var data = new BookDraftData
        {
            Chapters = side.Chapters,
            Acts = side.Acts,
            Trash = side.Trash,
        };
        await _files.WriteTextAsync(
            _files.CombinePath(side.Root, "draft.json"),
            JsonSerializer.Serialize(data, JsonOptions)).ConfigureAwait(false);
        await _files.WriteTextAsync(
            _files.CombinePath(side.Root, "scenes.json"),
            JsonSerializer.Serialize(side.Manifest, JsonOptions)).ConfigureAwait(false);
    }

    private async Task WriteChapterFolderAsync(DraftSide side, ChapterData chapter)
    {
        var folder = _files.CombinePath(side.Root, side.ChapterFolder, chapter.FolderName);
        await _files.CreateDirectoryAsync(folder).ConfigureAwait(false);
        await _files.WriteTextAsync(
            _files.CombinePath(folder, ChapterMarker.FileName),
            JsonSerializer.Serialize(ChapterMarker.FromChapter(chapter), JsonOptions))
            .ConfigureAwait(false);
    }

    private async Task<string> ReadSceneAsync(DraftSide side, ChapterData chapter, SceneData scene)
    {
        var path = _files.CombinePath(
            side.Root, side.ChapterFolder, chapter.FolderName, scene.FileName);
        return await _files.ExistsAsync(path).ConfigureAwait(false)
            ? await _files.ReadTextAsync(path).ConfigureAwait(false)
            // A manifest entry whose file is gone still carries the scene's
            // title and place, so it crosses as an empty scene rather than
            // taking the whole transfer down.
            : FileFrontMatter.Build(scene.Id);
    }

    private async Task WriteSceneAsync(
        DraftSide side, ChapterData chapter, SceneData scene, string raw)
    {
        var folder = _files.CombinePath(side.Root, side.ChapterFolder, chapter.FolderName);
        await _files.CreateDirectoryAsync(folder).ConfigureAwait(false);
        await _files.WriteTextAsync(_files.CombinePath(folder, scene.FileName), raw)
            .ConfigureAwait(false);
    }

    private async Task<T?> ReadJsonAsync<T>(string path) where T : class
    {
        if (!await _files.ExistsAsync(path).ConfigureAwait(false)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(
                await _files.ReadTextAsync(path).ConfigureAwait(false), JsonOptions);
        }
        catch (JsonException)
        {
            // A draft folder someone edited by hand reads as an empty draft,
            // which is visibly wrong rather than silently destructive.
            return null;
        }
    }

    private static List<SceneData> ScenesOf(ScenesManifest manifest, string chapterGuid)
        => manifest.Chapters.TryGetValue(chapterGuid, out var scenes)
            ? scenes.OrderBy(s => s.Order).ToList()
            : new List<SceneData>();

    /// <summary>
    /// The same chapter in the other draft: same identity, its own folder name.
    /// The folder is renumbered to where it lands, because a folder called
    /// "03 - " sitting eleventh is what makes a project folder unreadable.
    /// </summary>
    private static ChapterData CloneChapter(ChapterData chapter, DraftSide to)
    {
        var order = to.Chapters.Count == 0 ? 1 : to.Chapters.Max(c => c.Order) + 1;
        var baseName = $"{order:D2} - {SanitizeFileName(chapter.Title)}";
        var taken = new HashSet<string>(
            to.Chapters.Select(c => c.FolderName), StringComparer.OrdinalIgnoreCase);
        var folderName = baseName;
        var suffix = 2;
        while (taken.Contains(folderName))
            folderName = $"{baseName} ({suffix++})";

        var copy = Duplicate(chapter);
        copy.Order = order;
        copy.FolderName = folderName;
        return copy;
    }

    /// <summary>
    /// The same scene in the other draft. The id crosses unchanged - it is what
    /// makes this the same scene in both drafts rather than two of them - while
    /// the file name is the next free one in the chapter it lands in.
    /// </summary>
    private static SceneData CloneScene(
        SceneData scene, ChapterData landing, IReadOnlyList<SceneData> siblings)
    {
        var taken = new HashSet<string>(
            siblings.Select(s => s.FileName), StringComparer.OrdinalIgnoreCase);
        var index = 1;
        while (taken.Contains($"scene-{index:D2}.novalist")) index++;

        var copy = Duplicate(scene);
        copy.ChapterGuid = landing.Guid;
        copy.FileName = $"scene-{index:D2}.novalist";
        copy.Order = siblings.Count == 0 ? 1 : siblings.Max(s => s.Order) + 1;
        return copy;
    }

    /// <summary>
    /// A record's twin, through the same serializer that stores it. Copying
    /// field by field means every field added later is silently dropped on the
    /// way between drafts, which is the kind of loss nobody notices for months.
    /// </summary>
    private static T Duplicate<T>(T value) where T : class
        => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!;

    private static bool IsActive(BookData book, BookDraftMetadata draft)
        => string.Equals(book.ActiveDraftId, draft.Id, StringComparison.OrdinalIgnoreCase);

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "Untitled" : cleaned;
    }
}
