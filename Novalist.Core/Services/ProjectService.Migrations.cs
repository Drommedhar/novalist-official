using System.Text.Json;
using System.Text.RegularExpressions;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public partial class ProjectService
{
    // ── Private helpers ─────────────────────────────────────────────

    private async Task CreateBookFolderStructureAsync(BookData book)
    {
        if (ProjectRoot == null) return;

        var bookRoot = _fileService.CombinePath(ProjectRoot, book.FolderName);
        await _fileService.CreateDirectoryAsync(bookRoot);

        var bookMetaDir = _fileService.CombinePath(bookRoot, ".book");
        await _fileService.CreateDirectoryAsync(bookMetaDir);

        // Codex folders (per-book, shared across drafts).
        await _fileService.CreateDirectoryAsync(_fileService.CombinePath(bookRoot, book.CharacterFolder));
        await _fileService.CreateDirectoryAsync(_fileService.CombinePath(bookRoot, book.LocationFolder));
        await _fileService.CreateDirectoryAsync(_fileService.CombinePath(bookRoot, book.ItemFolder));
        await _fileService.CreateDirectoryAsync(_fileService.CombinePath(bookRoot, book.LoreFolder));
        await _fileService.CreateDirectoryAsync(_fileService.CombinePath(bookRoot, book.ImageFolder));

        // Active draft folder structure (chapters + snapshots).
        var activeDraft = book.ActiveDraft;
        if (activeDraft != null)
        {
            var draftRoot = _fileService.CombinePath(bookRoot, "Drafts", activeDraft.FolderName);
            await _fileService.CreateDirectoryAsync(draftRoot);
            await _fileService.CreateDirectoryAsync(_fileService.CombinePath(draftRoot, book.ChapterFolder));
            await _fileService.CreateDirectoryAsync(_fileService.CombinePath(draftRoot, book.SnapshotFolder));
        }
    }

    private async Task LoadScenesManifestAsync()
    {
        var draftScenesPath = GetActiveDraftScenesPath();
        if (draftScenesPath != null && await _fileService.ExistsAsync(draftScenesPath))
        {
            var scenesJson = await _fileService.ReadTextAsync(draftScenesPath);
            ScenesManifest = JsonSerializer.Deserialize<ScenesManifest>(scenesJson, JsonOptions) ?? new ScenesManifest();
            return;
        }

        // Legacy fallback — old layout had scenes.json under .book/.
        if (ActiveBookRoot != null)
        {
            var legacyPath = _fileService.CombinePath(ActiveBookRoot, ".book", "scenes.json");
            if (await _fileService.ExistsAsync(legacyPath))
            {
                var scenesJson = await _fileService.ReadTextAsync(legacyPath);
                ScenesManifest = JsonSerializer.Deserialize<ScenesManifest>(scenesJson, JsonOptions) ?? new ScenesManifest();
                return;
            }
        }

        ScenesManifest = new ScenesManifest();
    }

    /// <summary>
    /// Migrates pre-multi-draft books: creates a default draft, moves chapter
    /// content + snapshots + scenes.json under <c>Drafts/default/</c>, and
    /// flushes chapters/acts into <c>draft.json</c>. Called from
    /// <see cref="LoadProjectAsync"/> when a book has no drafts.
    /// </summary>
    private async Task MigrateToMultiDraftAsync(BookData book)
    {
        if (book.Drafts.Count > 0) return;
        if (ProjectRoot == null) return;

        var bookRoot = _fileService.CombinePath(ProjectRoot, book.FolderName);
        var defaultDraft = new BookDraftMetadata
        {
            Id = "draft-default",
            Name = "Draft 1",
            FolderName = "default",
            CreatedAt = DateTime.UtcNow,
        };
        book.Drafts.Add(defaultDraft);
        book.ActiveDraftId = defaultDraft.Id;

        var draftRoot = _fileService.CombinePath(bookRoot, "Drafts", defaultDraft.FolderName);
        await _fileService.CreateDirectoryAsync(draftRoot);

        // Move chapter / snapshot folders + scenes.json. Delegated to the
        // fixup so partially-migrated layouts (e.g. an earlier interrupted run
        // left an empty Drafts/default/Chapters folder) still merge correctly.
        await FixupLegacyDraftLayoutAsync(book);

        // Flush chapters + acts into draft.json, then clear them on BookData
        // so project.json doesn't duplicate the data.
        var draftData = new BookDraftData
        {
            Chapters = book.Chapters,
            Acts = book.Acts,
        };
        var draftJson = JsonSerializer.Serialize(draftData, JsonOptions);
        await _fileService.WriteTextAsync(_fileService.CombinePath(draftRoot, "draft.json"), draftJson);

        // Keep chapters/acts in memory for the active session; project.json will
        // still serialize them empty after migration (legacy fields stay for
        // older readers). Subsequent saves go to draft.json.
        await SaveProjectAsync();
    }

    /// <summary>
    /// Safety-net for half-migrated projects: a book already has a draft entry
    /// (so MigrateToMultiDraftAsync skips it) but the chapter / snapshot folders
    /// still live at the legacy book-root location and the draft folder is
    /// missing them. Moves the legacy folders into the active draft so scene
    /// loads + manuscript reads find the files.
    /// </summary>
    private async Task FixupLegacyDraftLayoutAsync(BookData book)
    {
        if (book.Drafts.Count == 0 || ProjectRoot == null) return;
        var bookRoot = _fileService.CombinePath(ProjectRoot, book.FolderName);
        // Legacy files belong to the migration-target draft, not the currently-
        // active one. Prefer the "draft-default" record left by MigrateToMulti-
        // DraftAsync; otherwise the first draft (insertion order).
        var draft = book.Drafts.FirstOrDefault(d => d.Id == "draft-default") ?? book.Drafts[0];
        var draftRoot = _fileService.CombinePath(bookRoot, "Drafts", draft.FolderName);
        await _fileService.CreateDirectoryAsync(draftRoot);

        // Chapters: move every legacy chapter folder into the draft if the
        // matching draft folder doesn't already have it.
        var oldChapters = _fileService.CombinePath(bookRoot, book.ChapterFolder);
        var newChapters = _fileService.CombinePath(draftRoot, book.ChapterFolder);
        if (await _fileService.DirectoryExistsAsync(oldChapters))
        {
            await _fileService.CreateDirectoryAsync(newChapters);
            foreach (var sub in await _fileService.GetDirectoriesAsync(oldChapters))
            {
                var name = Path.GetFileName(sub);
                var target = _fileService.CombinePath(newChapters, name);
                if (await _fileService.DirectoryExistsAsync(target))
                {
                    // Target chapter folder exists — only merge in if it has no
                    // scene files (i.e. an empty stub from a half-finished prior
                    // migration). If it already has scenes, leave both intact.
                    if ((await _fileService.GetFilesAsync(target)).Count > 0 ||
                        (await _fileService.GetDirectoriesAsync(target)).Count > 0) continue;
                    foreach (var file in await _fileService.GetFilesAsync(sub))
                        await _fileService.MoveFileAsync(file, _fileService.CombinePath(target, Path.GetFileName(file)));
                    try { await _fileService.DeleteDirectoryAsync(sub, recursive: false); } catch { /* leave empty */ }
                }
                else
                {
                    await _fileService.MoveDirectoryAsync(sub, target);
                }
            }
            // Drop the legacy chapters folder if it's now empty.
            await TryDeleteEmptyDirAsync(oldChapters);
        }

        // Snapshots: same pattern.
        var oldSnaps = _fileService.CombinePath(bookRoot, book.SnapshotFolder);
        var newSnaps = _fileService.CombinePath(draftRoot, book.SnapshotFolder);
        if (await _fileService.DirectoryExistsAsync(oldSnaps) && !await _fileService.DirectoryExistsAsync(newSnaps))
            await _fileService.MoveDirectoryAsync(oldSnaps, newSnaps);

        // scenes.json: legacy under .book/, draft expects it at draft root.
        var oldScenes = _fileService.CombinePath(bookRoot, ".book", "scenes.json");
        var newScenes = _fileService.CombinePath(draftRoot, "scenes.json");
        if (await _fileService.ExistsAsync(oldScenes) && !await _fileService.ExistsAsync(newScenes))
            await _fileService.MoveFileAsync(oldScenes, newScenes);
    }

    private async Task LoadActiveDraftDataAsync()
    {
        if (ActiveBook == null) return;
        var path = GetActiveDraftDataPath();
        if (path == null || !await _fileService.ExistsAsync(path))
            return;

        var raw = await _fileService.ReadTextAsync(path);
        var data = JsonSerializer.Deserialize<BookDraftData>(raw, JsonOptions) ?? new BookDraftData();
        ActiveBook.Chapters = data.Chapters;
        ActiveBook.Acts = data.Acts;
        ActiveBook.Trash = data.Trash;
    }

    private async Task SaveActiveDraftDataAsync()
    {
        if (ActiveBook == null) return;
        var path = GetActiveDraftDataPath();
        if (path == null) return;

        var dir = _fileService.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            await _fileService.CreateDirectoryAsync(dir);

        var data = new BookDraftData
        {
            Chapters = ActiveBook.Chapters,
            Acts = ActiveBook.Acts,
            Trash = ActiveBook.Trash,
        };
        await _fileService.WriteTextAsync(path, JsonSerializer.Serialize(data, JsonOptions));

        await WriteChapterMarkersAsync();
    }

    /// <summary>
    /// Refreshes the <c>.nvchapter.json</c> marker in every active-draft chapter folder so
    /// the on-disk identity stays the source of truth after any chapter edit (create,
    /// rename, reorder, status/date change). Cheap and centralised — every chapter mutation
    /// flows through <see cref="SaveActiveDraftDataAsync"/>.
    /// </summary>
    private async Task WriteChapterMarkersAsync()
    {
        if (ActiveBook == null) return;
        foreach (var chapter in ActiveBook.Chapters)
        {
            var folder = GetChapterFolderPath(chapter);
            await _fileService.CreateDirectoryAsync(folder);
            var markerPath = _fileService.CombinePath(folder, ChapterMarker.FileName);
            await _fileService.WriteTextAsync(markerPath, JsonSerializer.Serialize(ChapterMarker.FromChapter(chapter), JsonOptions));
        }
    }

    // Best-effort cleanup of a now-empty legacy folder. Excluded from coverage:
    // the catch only fires when the OS refuses to delete an empty directory
    // (another process holding a handle), which is not reproducible in a test.
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private async Task TryDeleteEmptyDirAsync(string dir)
    {
        try
        {
            if ((await _fileService.GetFilesAsync(dir)).Count == 0 &&
                (await _fileService.GetDirectoriesAsync(dir)).Count == 0)
                await _fileService.DeleteDirectoryAsync(dir, recursive: false);
        }
        catch { /* leave the empty folder if something is holding it */ }
    }

    private static void ReindexScenes(List<SceneData> scenes)
    {
        for (int i = 0; i < scenes.Count; i++)
            scenes[i].Order = i + 1;
    }

    private static string GetNextSceneFileName(IEnumerable<SceneData> scenes)
    {
        var existing = new HashSet<string>(scenes.Select(scene => scene.FileName), StringComparer.OrdinalIgnoreCase);
        var order = 1;
        while (true)
        {
            var fileName = $"scene-{order:D2}.novalist";
            if (!existing.Contains(fileName))
                return fileName;
            order++;
        }
    }

    /// <summary>
    /// A folder name for something the writer titled.
    ///
    /// Windows drops trailing dots and spaces from a name as it creates it, so a
    /// chapter called "In The Beginning..." produced a folder called "In The
    /// Beginning" while the project went on addressing it with the dots - and
    /// the very next write failed with the folder reported missing. Trimming
    /// them here means the name on disk is the name we recorded.
    /// </summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return sanitized.Trim().TrimEnd('.', ' ');
    }
}
