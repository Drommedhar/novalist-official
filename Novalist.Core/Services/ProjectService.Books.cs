using System.Text.Json;
using System.Text.RegularExpressions;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public partial class ProjectService
{
    // ── Reading a book that is not the open one ─────────────────────
    //
    // Every path above resolves against ActiveBook, which is right for
    // everything that edits and wrong for anything that reads across a
    // multi-book project. An export that stitches two volumes together, or a
    // report over a series, could otherwise only be had by switching the active
    // book mid-run - which mutates state the watcher and the UI are both
    // reading, and leaves the app on the wrong book if anything throws.
    //
    // These are read-only and derive every path from the book handed in, so
    // nothing about the open book changes.

    /// <summary>A given book's folder, or null with no project open.</summary>
    public string? BookRootFor(BookData book)
        => ProjectRoot == null || book == null
            ? null
            : _fileService.CombinePath(ProjectRoot, book.FolderName);

    /// <summary>A given book's active draft folder.</summary>
    public string? DraftRootFor(BookData book)
    {
        var root = BookRootFor(book);
        if (root == null || book.ActiveDraft == null) return null;
        return _fileService.CombinePath(root, "Drafts", book.ActiveDraft.FolderName);
    }

    /// <summary>Where a chapter of a given book keeps its scenes.</summary>
    public string? ChapterFolderPathFor(BookData book, ChapterData chapter)
    {
        var root = DraftRootFor(book) ?? BookRootFor(book);
        return root == null
            ? null
            : _fileService.CombinePath(root, book.ChapterFolder, chapter.FolderName);
    }

    /// <summary>
    /// A given book's scene manifest, read fresh rather than cached: this is
    /// for a pass over a book nobody is editing, so a stale copy would be worse
    /// than the read.
    /// </summary>
    /// <summary>
    /// A book's chapters as its draft has them on disk, in order.
    ///
    /// The active draft's chapters live in memory on the book; every other
    /// draft's live only in its own draft.json, so comparing two drafts - the
    /// most obvious thing to want a second draft for - had no way to read the
    /// one that is not open.
    /// </summary>
    public async Task<List<ChapterData>> LoadChaptersForAsync(BookData book)
    {
        var draftRoot = DraftRootFor(book);
        if (draftRoot == null) return [];
        var path = _fileService.CombinePath(draftRoot, "draft.json");
        if (!await _fileService.ExistsAsync(path)) return [];

        var data = JsonSerializer.Deserialize<BookDraftData>(
            await _fileService.ReadTextAsync(path), JsonOptions);
        return [.. (data?.Chapters ?? []).OrderBy(c => c.Order)];
    }

    public async Task<ScenesManifest?> LoadScenesManifestForAsync(BookData book)
    {
        var draftRoot = DraftRootFor(book);
        if (draftRoot != null)
        {
            var path = _fileService.CombinePath(draftRoot, "scenes.json");
            if (await _fileService.ExistsAsync(path))
            {
                return JsonSerializer.Deserialize<ScenesManifest>(
                    await _fileService.ReadTextAsync(path), JsonOptions) ?? new ScenesManifest();
            }
        }

        // The same legacy layout the active-book loader falls back to.
        var bookRoot = BookRootFor(book);
        if (bookRoot != null)
        {
            var legacy = _fileService.CombinePath(bookRoot, ".book", "scenes.json");
            if (await _fileService.ExistsAsync(legacy))
            {
                return JsonSerializer.Deserialize<ScenesManifest>(
                    await _fileService.ReadTextAsync(legacy), JsonOptions) ?? new ScenesManifest();
            }
        }

        return null;
    }

    /// <summary>One scene of a given book, or empty when the file is not there.</summary>
    public async Task<string> ReadSceneContentForAsync(
        BookData book, ChapterData chapter, SceneData scene)
    {
        var folder = ChapterFolderPathFor(book, chapter);
        if (folder == null) return string.Empty;
        var path = _fileService.CombinePath(folder, scene.FileName);
        return await _fileService.ExistsAsync(path)
            ? FileFrontMatter.Strip(await _fileService.ReadTextAsync(path))
            : string.Empty;
    }

    // ── Book management ─────────────────────────────────────────────

    public async Task<BookData> CreateBookAsync(string bookName)
    {
        if (CurrentProject == null || ProjectRoot == null)
            throw new InvalidOperationException("No project loaded.");

        var defaultDraft = new BookDraftMetadata
        {
            Id = "draft-default",
            Name = "Draft 1",
            FolderName = "default",
            CreatedAt = DateTime.UtcNow,
        };
        var book = new BookData
        {
            Id = $"book-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            Name = bookName,
            FolderName = SanitizeFileName(bookName),
            CreatedAt = DateTime.UtcNow,
            Drafts = [defaultDraft],
            ActiveDraftId = defaultDraft.Id,
        };

        CurrentProject.Books.Add(book);
        await CreateBookFolderStructureAsync(book);
        await SaveProjectAsync();

        return book;
    }

    public async Task SwitchBookAsync(string bookId)
    {
        if (CurrentProject == null || ProjectRoot == null)
            throw new InvalidOperationException("No project loaded.");

        var book = CurrentProject.Books.FirstOrDefault(b => b.Id == bookId)
            ?? throw new ArgumentException($"Book not found: {bookId}");

        // Same book: refresh manifest from disk and exit (legacy callers rely
        // on this to re-read scenes.json without changing the active book).
        if (ActiveBook != null && string.Equals(ActiveBook.Id, bookId, StringComparison.OrdinalIgnoreCase))
        {
            await LoadScenesManifestAsync();
            await SaveProjectAsync();
            return;
        }

        // Flush outgoing book's draft + scenes manifest so its in-memory state
        // is durably persisted before we swap.
        if (ActiveBook != null)
        {
            await SaveActiveDraftDataAsync();
            await SaveScenesAsync();
        }

        CurrentProject.ActiveBookId = bookId;
        ActiveBook = book;

        // Reload incoming book's chapter / act tree + manifest from disk.
        book.Chapters = new List<ChapterData>();
        book.Acts = new List<ActData>();
        await LoadActiveDraftDataAsync();
        await LoadScenesManifestAsync();
        await SaveProjectAsync();
    }

    public async Task RenameBookAsync(string bookId, string newName)
    {
        if (CurrentProject == null || ProjectRoot == null) return;
        if (string.IsNullOrWhiteSpace(newName)) return;

        var book = CurrentProject.Books.FirstOrDefault(b => b.Id == bookId);
        if (book == null) return;

        // A display name is not a storage path. Keeping the folder stable also
        // allows duplicate titles and preserves paths held by open editors.
        book.Name = newName.Trim();
        await SaveProjectAsync();
    }

    public async Task DeleteBookAsync(string bookId)
    {
        if (CurrentProject == null || ProjectRoot == null) return;
        if (CurrentProject.Books.Count <= 1)
            throw new InvalidOperationException("Cannot delete the last book.");

        var book = CurrentProject.Books.FirstOrDefault(b => b.Id == bookId);
        if (book == null) return;

        var bookFolderPath = _fileService.CombinePath(ProjectRoot, book.FolderName);

        CurrentProject.Books.Remove(book);

        if (CurrentProject.ActiveBookId == bookId)
        {
            var nextBook = CurrentProject.Books.First();
            CurrentProject.ActiveBookId = nextBook.Id;
            ActiveBook = nextBook;
            await LoadScenesManifestAsync();
        }

        await SaveProjectAsync();
        await _fileService.DeleteDirectoryAsync(bookFolderPath);
    }

    // ── World Bible ─────────────────────────────────────────────────

    public async Task InitializeWorldBibleAsync()
    {
        var wbRoot = WorldBibleRoot;
        if (CurrentProject == null || wbRoot == null) return;
        await _fileService.CreateDirectoryAsync(wbRoot);
        await _fileService.CreateDirectoryAsync(_fileService.CombinePath(wbRoot, CurrentProject.CharacterFolder));
        await _fileService.CreateDirectoryAsync(_fileService.CombinePath(wbRoot, CurrentProject.LocationFolder));
        await _fileService.CreateDirectoryAsync(_fileService.CombinePath(wbRoot, CurrentProject.ItemFolder));
        await _fileService.CreateDirectoryAsync(_fileService.CombinePath(wbRoot, CurrentProject.LoreFolder));
        await _fileService.CreateDirectoryAsync(_fileService.CombinePath(wbRoot, CurrentProject.ImageFolder));
    }
}
