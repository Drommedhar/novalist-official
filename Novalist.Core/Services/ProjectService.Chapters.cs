using System.Text.Json;
using System.Text.RegularExpressions;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public partial class ProjectService
{
    // ── Chapter / Scene operations (delegate to active book) ────────

    /// <param name="insertAtOrder">
    /// Where the chapter goes, one-based. Null appends, which is what creating a
    /// chapter has always done.
    ///
    /// Without this the only way to put a chapter in the middle was to append it
    /// and drag it up past everything after it - and on a long book that is a
    /// dozen drags, each one a save.
    /// </param>
    public async Task<ChapterData> CreateChapterAsync(
        string title, string date = "", int? insertAtOrder = null)
    {
        if (ActiveBook == null || ActiveBookRoot == null || ScenesManifest == null)
            throw new InvalidOperationException("No book active.");

        var lastOrder = ActiveBook.Chapters.Count > 0
            ? ActiveBook.Chapters.Max(c => c.Order)
            : 0;
        var nextOrder = lastOrder + 1;

        if (insertAtOrder is { } at)
        {
            nextOrder = Math.Clamp(at, 1, lastOrder + 1);
            // Everything from that point on moves down one. The folder names
            // keep their old numbers on purpose: renaming folders would break
            // every snapshot path and every open editor for a cosmetic gain.
            foreach (var existing in ActiveBook.Chapters.Where(c => c.Order >= nextOrder))
                existing.Order++;
        }

        var folderName = $"{nextOrder:D2} - {SanitizeFileName(title)}";
        var chapter = new ChapterData
        {
            Title = title,
            Order = nextOrder,
            Date = date,
            FolderName = folderName
        };

        ActiveBook.Chapters.Add(chapter);
        ScenesManifest.Chapters[chapter.Guid] = new List<SceneData>();

        var chapterPath = GetChapterFolderPath(chapter);
        await _fileService.CreateDirectoryAsync(chapterPath);

        await SaveProjectAsync();
        await SaveScenesAsync();

        return chapter;
    }

    public async Task SetChapterDateAsync(string chapterGuid, string date)
    {
        if (ActiveBook == null) return;

        var chapter = ActiveBook.Chapters.FirstOrDefault(c => c.Guid == chapterGuid);
        if (chapter == null) return;

        chapter.Date = date.Trim();
        await SaveProjectAsync();
    }

    public async Task SetChapterFavoriteAsync(string chapterGuid, bool favorite)
    {
        if (ActiveBook == null) return;
        var chapter = ActiveBook.Chapters.FirstOrDefault(c => c.Guid == chapterGuid);
        if (chapter == null) return;
        chapter.IsFavorite = favorite;
        await SaveProjectAsync();
    }

    public async Task SetChapterDateRangeAsync(string chapterGuid, StoryDateRange? dateRange)
    {
        if (ActiveBook == null) return;
        var chapter = ActiveBook.Chapters.FirstOrDefault(c => c.Guid == chapterGuid);
        if (chapter == null) return;
        chapter.DateRange = dateRange?.HasValue == true ? dateRange.Clone() : null;
        if (dateRange?.HasValue == true && !string.IsNullOrWhiteSpace(dateRange.Start))
            chapter.Date = dateRange.Start;
        await SaveProjectAsync();
    }

    /// <summary>
    /// Moves a chapter and its scenes to the trash. Nothing is erased: the
    /// scenes go to the archive that already backs scene-level restore, and
    /// the chapter record is kept so it can come back with them. Deleting a
    /// chapter was the one structural action with no way back.
    /// </summary>
    public async Task DeleteChapterAsync(string chapterGuid)
    {
        if (ActiveBook == null) return;

        var chapter = ActiveBook.Chapters.FirstOrDefault(c => c.Guid == chapterGuid);
        if (chapter == null) return;

        var chapterFolderPath = GetChapterFolderPath(chapter);

        // Archive scenes before the chapter goes, or ArchiveSceneAsync cannot
        // find the folder their files are still sitting in.
        foreach (var scene in GetScenesForChapter(chapterGuid).ToList())
            await ArchiveSceneAsync(chapterGuid, scene.Id);

        ActiveBook.Chapters.Remove(chapter);
        ScenesManifest?.Chapters.Remove(chapterGuid);

        chapter.DeletedAt = DateTime.UtcNow;
        ActiveBook.Trash.Insert(0, chapter);

        var ordered = ActiveBook.Chapters.OrderBy(c => c.Order).ToList();
        for (int i = 0; i < ordered.Count; i++)
            ordered[i].Order = i + 1;

        await SaveProjectAsync();
        await SaveScenesAsync();
        await _fileService.DeleteDirectoryAsync(chapterFolderPath);
    }

    /// <summary>Chapters sitting in the trash, most recently deleted first.</summary>
    public IReadOnlyList<ChapterData> GetTrashedChapters()
        => ActiveBook?.Trash ?? [];

    /// <summary>
    /// Brings a chapter back from the trash, at the end of the manuscript, with
    /// every scene that was archived with it in the order it had. Returns false
    /// when the id is not in the trash.
    ///
    /// It lands at the end rather than at its old position because the numbers
    /// around it have moved on; putting it back where it was would renumber
    /// chapters the writer has since been working in.
    /// </summary>
    public async Task<bool> RestoreChapterAsync(string chapterGuid)
    {
        if (ActiveBook == null || ScenesManifest == null) return false;

        var chapter = ActiveBook.Trash.FirstOrDefault(c => c.Guid == chapterGuid);
        if (chapter == null) return false;

        ActiveBook.Trash.Remove(chapter);
        chapter.DeletedAt = null;
        chapter.Order = ActiveBook.Chapters.Count == 0
            ? 1
            : ActiveBook.Chapters.Max(c => c.Order) + 1;
        ActiveBook.Chapters.Add(chapter);
        ScenesManifest.Chapters.TryAdd(chapterGuid, []);

        await _fileService.CreateDirectoryAsync(GetChapterFolderPath(chapter));
        await SaveProjectAsync();

        var theirs = ScenesManifest.Archived
            .Where(s => s.OriginChapterGuid == chapterGuid)
            .OrderBy(s => s.Order)
            .ToList();
        // The index is passed rather than left to each scene's own slot: a
        // whole chapter coming back rebuilds its order, and the slots recorded
        // while it was being emptied one scene at a time were the shrinking
        // list's, not the order the writer had.
        for (var i = 0; i < theirs.Count; i++)
            await RestoreArchivedSceneAsync(theirs[i].Id, chapterGuid, i);

        await SaveScenesAsync();
        return true;
    }

    /// <summary>
    /// Erases a chapter in the trash and every scene archived with it, files
    /// and all. Returns false when the id is not in the trash. This is the only
    /// destructive path, and nothing calls it on the writer's behalf.
    /// </summary>
    public async Task<bool> PurgeChapterAsync(string chapterGuid)
    {
        if (ActiveBook == null || ScenesManifest == null) return false;

        var chapter = ActiveBook.Trash.FirstOrDefault(c => c.Guid == chapterGuid);
        if (chapter == null) return false;

        foreach (var scene in ScenesManifest.Archived
                     .Where(s => s.OriginChapterGuid == chapterGuid).ToList())
            await DeleteArchivedSceneAsync(scene.Id);

        ActiveBook.Trash.Remove(chapter);
        await SaveProjectAsync();
        return true;
    }

    public async Task ReorderChapterAsync(string chapterGuid, int newOrder)
    {
        if (ActiveBook == null) return;

        var chapter = ActiveBook.Chapters.FirstOrDefault(c => c.Guid == chapterGuid);
        if (chapter == null) return;

        var oldOrder = chapter.Order;
        if (oldOrder == newOrder) return;

        foreach (var c in ActiveBook.Chapters)
        {
            if (c.Guid == chapterGuid)
            {
                c.Order = newOrder;
            }
            else if (oldOrder < newOrder && c.Order > oldOrder && c.Order <= newOrder)
            {
                c.Order--;
            }
            else if (oldOrder > newOrder && c.Order >= newOrder && c.Order < oldOrder)
            {
                c.Order++;
            }
        }

        await SaveProjectAsync();
    }

    public async Task MoveChaptersAsync(IReadOnlyList<string> chapterGuids, int targetIndex)
    {
        if (ActiveBook == null || chapterGuids.Count == 0) return;

        var ordered = ActiveBook.Chapters.OrderBy(c => c.Order).ToList();
        var guidSet = new HashSet<string>(chapterGuids);
        var moving = ordered.Where(chapter => guidSet.Contains(chapter.Guid)).ToList();
        if (moving.Count == 0) return;

        var remaining = ordered.Where(chapter => !guidSet.Contains(chapter.Guid)).ToList();
        targetIndex = Math.Clamp(targetIndex, 0, remaining.Count);

        remaining.InsertRange(targetIndex, moving);
        for (int i = 0; i < remaining.Count; i++)
            remaining[i].Order = i + 1;

        ActiveBook.Chapters = remaining;
        await SaveProjectAsync();
    }

    public async Task RenameChapterAsync(string chapterGuid, string newTitle)
    {
        if (ActiveBook == null || ActiveBookRoot == null) return;

        var chapter = ActiveBook.Chapters.FirstOrDefault(c => c.Guid == chapterGuid);
        if (chapter == null) return;

        var oldFolderPath = GetChapterFolderPath(chapter);
        chapter.Title = newTitle;
        chapter.FolderName = $"{chapter.Order:D2} - {SanitizeFileName(newTitle)}";
        var newFolderPath = GetChapterFolderPath(chapter);

        if (await _fileService.DirectoryExistsAsync(oldFolderPath) && oldFolderPath != newFolderPath)
        {
            await _fileService.MoveDirectoryAsync(oldFolderPath, newFolderPath);
        }

        await SaveProjectAsync();
    }
}
