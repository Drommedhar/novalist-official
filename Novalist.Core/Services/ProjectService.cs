using System.Text.Json;
using System.Text.RegularExpressions;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public partial class ProjectService : IProjectService
{
    private readonly IFileService _fileService;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ProjectMetadata? CurrentProject { get; private set; }
    public ProjectSettings ProjectSettings { get; private set; } = new();
    public BookData? ActiveBook { get; private set; }
    public ScenesManifest? ScenesManifest { get; private set; }
    public string? ProjectRoot { get; private set; }
    public string? ActiveBookRoot => ProjectRoot != null && ActiveBook != null
        ? _fileService.CombinePath(ProjectRoot, ActiveBook.FolderName)
        : null;
    public string? ActiveDraftRoot
    {
        get
        {
            if (ActiveBookRoot == null || ActiveBook?.ActiveDraft == null) return null;
            return _fileService.CombinePath(ActiveBookRoot, "Drafts", ActiveBook.ActiveDraft.FolderName);
        }
    }
    public string? WorldBibleRoot => ProjectRoot != null && CurrentProject != null
        ? _fileService.CombinePath(ProjectRoot, CurrentProject.WorldBibleFolder)
        : null;
    public bool IsProjectLoaded => CurrentProject != null && ActiveBook != null && ProjectRoot != null;

    /// <summary>
    /// Lets go of the loaded project, leaving the service as it was before one
    /// was opened.
    /// </summary>
    /// <remarks>
    /// There was no way back to no-project short of restarting: a project could
    /// be opened and swapped for another, never closed. That was survivable
    /// while the welcome screen was a separate window, and stopped being so once
    /// it became what the main window holds until a project is open - there was
    /// somewhere to go back to and no way to get there.
    /// Nothing is written here. Everything this drops is already on disk.
    /// </remarks>
    public void CloseProject()
    {
        CurrentProject = null;
        ProjectSettings = new ProjectSettings();
        ActiveBook = null;
        ScenesManifest = null;
        ProjectRoot = null;
    }

    /// <summary>
    /// Optional sink for v2 to v3 filesystem-migration progress, set by the UI before
    /// <see cref="LoadProjectAsync"/> so it can show a progress overlay. Null = no reporting.
    /// </summary>
    public IProgress<FilesystemMigrationProgress>? MigrationProgress { get; set; }

    /// <summary>
    /// Raised after <see cref="ReconcileActiveDraftAsync"/> detects external changes (on load or
    /// from the live watcher) so the UI can refresh + surface a summary. Carries the report.
    /// </summary>
    public event EventHandler<ReconciliationReport>? DraftReconciled;

    public ProjectService(IFileService fileService)
    {
        _fileService = fileService;
    }

    public async Task<ProjectMetadata> CreateProjectAsync(string parentDirectory, string projectName, string firstBookName)
    {
        var safeName = SanitizeFileName(projectName);
        var projectDir = _fileService.CombinePath(parentDirectory, safeName);

        await _fileService.CreateDirectoryAsync(projectDir);

        var bookId = $"book-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var defaultDraft = new BookDraftMetadata
        {
            Id = "draft-default",
            Name = "Draft 1",
            FolderName = "default",
            CreatedAt = DateTime.UtcNow,
        };
        var book = new BookData
        {
            Id = bookId,
            Name = firstBookName,
            FolderName = SanitizeFileName(firstBookName),
            CreatedAt = DateTime.UtcNow,
            Drafts = [defaultDraft],
            ActiveDraftId = defaultDraft.Id,
        };

        var metadata = new ProjectMetadata
        {
            Id = $"project-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            Name = projectName,
            CreatedAt = DateTime.UtcNow,
            ActiveBookId = bookId,
            Books = [book],
            // Born at the current schema version — no migration needed on first load.
            Version = FilesystemMigrator.FilesystemVersion,
        };

        // Create project-level folders
        var novalistDir = _fileService.CombinePath(projectDir, ".novalist");
        await _fileService.CreateDirectoryAsync(novalistDir);

        ProjectRoot = projectDir;
        CurrentProject = metadata;
        ActiveBook = book;
        ScenesManifest = new ScenesManifest();

        // Create book folder structure
        await CreateBookFolderStructureAsync(book);

        // Create world bible folder structure
        await InitializeWorldBibleAsync();

        await SaveProjectAsync();
        await SaveProjectSettingsAsync();
        await SaveScenesAsync();

        return metadata;
    }

    /// <summary>
    /// Creates a project on disk without adopting it, and returns its folder.
    ///
    /// Creating a project loads it, which is right when a person clicked New
    /// Project and wrong when an extension did: an importer building a binder
    /// would take the writer out of the book they were in the middle of a
    /// sentence of. The five fields that say which project is open are put
    /// back, so the only trace of the call is the folder it wrote.
    /// </summary>
    public async Task<string> CreateProjectDetachedAsync(
        string parentDirectory, string projectName, string firstBookName)
    {
        var project = CurrentProject;
        var settings = ProjectSettings;
        var book = ActiveBook;
        var scenes = ScenesManifest;
        var root = ProjectRoot;
        try
        {
            await CreateProjectAsync(parentDirectory, projectName, firstBookName);
            return ProjectRoot!;
        }
        finally
        {
            CurrentProject = project;
            ProjectSettings = settings;
            ActiveBook = book;
            ScenesManifest = scenes;
            ProjectRoot = root;
        }
    }

    public async Task<ProjectMetadata> LoadProjectAsync(string projectDirectory)
    {
        var metadataPath = _fileService.CombinePath(projectDirectory, ".novalist", "project.json");
        if (!await _fileService.ExistsAsync(metadataPath))
            throw new FileNotFoundException("No Novalist project found at this location.", metadataPath);

        var json = await _fileService.ReadTextAsync(metadataPath);
        var metadata = JsonSerializer.Deserialize<ProjectMetadata>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to parse project metadata.");

        ProjectRoot = projectDirectory;
        CurrentProject = metadata;
        ActiveBook = metadata.GetActiveBook();

        if (ActiveBook == null)
            throw new InvalidOperationException("Project has no books.");

        // Multi-draft migration: pre-multi-draft books need their chapter tree
        // moved under Drafts/default/. Books that already have a Drafts entry
        // but never had their files moved (interrupted migration, half-finished
        // upgrade) get the same fix-up applied.
        foreach (var book in metadata.Books)
        {
            var prevActive = ActiveBook;
            ActiveBook = book;
            if (book.Drafts.Count == 0)
                await MigrateToMultiDraftAsync(book);
            else
                await FixupLegacyDraftLayoutAsync(book);
            ActiveBook = prevActive;
        }

        // Filesystem-source-of-truth migration (v2 -> v3): stamp every scene file with
        // its id, write a .nvchapter.json marker per chapter folder, split acts.json, and
        // build the per-draft .nvindex.json. Runs for every draft of every book so the
        // whole project becomes reconcilable, not just the active draft. Idempotent.
        var migrator = new FilesystemMigrator(_fileService);
        if (await migrator.NeedsMigrationAsync(metadata, projectDirectory))
        {
            await migrator.MigrateAsync(metadata, projectDirectory, MigrationProgress);
            // Persist the version bump without flushing the not-yet-loaded draft data.
            await WriteProjectJsonAsync();
        }

        // Load the active draft's chapters/acts from draft.json (if present).
        await LoadActiveDraftDataAsync();

        // Load scenes manifest for the active book/draft.
        await LoadScenesManifestAsync();

        // Load project-level settings
        await LoadProjectSettingsAsync();

        // Reconcile the active draft against external filesystem edits made while the app was
        // closed (added / moved / renamed / deleted scenes + chapters). Auto-applies; the UI
        // surfaces the summary. A clean project produces no changes and no writes.
        await ReconcileActiveDraftAsync();

        return metadata;
    }

    /// <summary>
    /// Scans the active draft's on-disk tree and, when <paramref name="apply"/> is set and the
    /// scan found changes, reconciles the in-memory chapter/scene model + persists. Returns the
    /// report so callers can surface a summary. Safe to call on a clean project (no-op).
    /// </summary>
    public async Task<ReconciliationReport> ReconcileActiveDraftAsync(bool apply = true)
    {
        if (ActiveBook == null || ActiveDraftRoot == null || ScenesManifest == null)
            return new ReconciliationReport();

        var index = await LoadDraftIndexAsync();
        var reconciler = new ProjectReconciler(_fileService);
        var report = await reconciler.ScanAsync(ActiveDraftRoot, ActiveBook.ChapterFolder, ActiveBook.Chapters, ScenesManifest, index);

        if (apply && report.HasChanges)
        {
            await reconciler.ApplyAsync(ActiveDraftRoot, ActiveBook.ChapterFolder, ActiveBook.Chapters, ScenesManifest, index);
            await SaveActiveDraftDataAsync();   // draft.json + refreshed chapter markers
            await SaveScenesAsync();
        }

        if (report.HasChanges)
            DraftReconciled?.Invoke(this, report);

        return report;
    }

    private async Task<DraftIndex> LoadDraftIndexAsync()
    {
        if (ActiveDraftRoot == null) return new DraftIndex();
        var path = _fileService.CombinePath(ActiveDraftRoot, ".nvindex.json");
        if (!await _fileService.ExistsAsync(path)) return new DraftIndex();
        var json = await _fileService.ReadTextAsync(path);
        return JsonSerializer.Deserialize<DraftIndex>(json, JsonOptions) ?? new DraftIndex();
    }

    public async Task SaveProjectAsync()
    {
        if (CurrentProject == null || ProjectRoot == null) return;

        // Persist the active draft's chapter / act tree into draft.json.
        await SaveActiveDraftDataAsync();
        await WriteProjectJsonAsync();
    }

    /// <summary>
    /// Serializes <c>project.json</c> only (no draft.json flush). Used by the v3
    /// filesystem migration to persist the version bump before the active draft's data
    /// has been loaded — calling the full <see cref="SaveProjectAsync"/> there would write
    /// an empty draft.json over real chapter data.
    /// </summary>
    private async Task WriteProjectJsonAsync()
    {
        if (CurrentProject == null || ProjectRoot == null) return;

        // Temporarily clear chapters + acts on books that have multi-draft
        // storage so project.json doesn't duplicate the data.
        var snapshot = new List<(BookData Book, List<ChapterData> Chapters, List<ActData> Acts)>();
        foreach (var book in CurrentProject.Books)
        {
            if (book.Drafts.Count > 0)
            {
                snapshot.Add((book, book.Chapters, book.Acts));
                book.Chapters = new List<ChapterData>();
                book.Acts = new List<ActData>();
            }
        }

        var metadataPath = _fileService.CombinePath(ProjectRoot, ".novalist", "project.json");
        var json = JsonSerializer.Serialize(CurrentProject, JsonOptions);
        await _fileService.WriteTextAsync(metadataPath, json);

        // Restore in-memory state.
        foreach (var (book, chs, acts) in snapshot)
        {
            book.Chapters = chs;
            book.Acts = acts;
        }
    }

    public async Task SaveScenesAsync()
    {
        if (ScenesManifest == null) return;
        var path = GetActiveDraftScenesPath();
        if (path == null) return;

        var dir = _fileService.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            await _fileService.CreateDirectoryAsync(dir);
        var json = JsonSerializer.Serialize(ScenesManifest, JsonOptions);
        await _fileService.WriteTextAsync(path, json);
    }

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

    private string? GetActiveDraftScenesPath()
    {
        if (ActiveDraftRoot == null) return null;
        return _fileService.CombinePath(ActiveDraftRoot, "scenes.json");
    }

    private string? GetActiveDraftDataPath()
    {
        if (ActiveDraftRoot == null) return null;
        return _fileService.CombinePath(ActiveDraftRoot, "draft.json");
    }

    /// <summary>
    /// One save at a time. Two writes that overlap collide on the file rather
    /// than merging, and the change the losing one carried is gone with nothing
    /// to show for it - see <see cref="SettingsService.SaveAsync"/>.
    /// </summary>
    private readonly SemaphoreSlim _projectSettingsSaveLock = new(1, 1);

    public async Task SaveProjectSettingsAsync()
    {
        if (ProjectRoot == null) return;

        await _projectSettingsSaveLock.WaitAsync();
        try
        {
            var settingsPath = _fileService.CombinePath(ProjectRoot, ".novalist", "settings.json");
            var json = JsonSerializer.Serialize(ProjectSettings, JsonOptions);
            await _fileService.WriteTextAsync(settingsPath, json);
        }
        finally
        {
            _projectSettingsSaveLock.Release();
        }
    }

    private async Task LoadProjectSettingsAsync()
    {
        if (ProjectRoot == null) return;

        var settingsPath = _fileService.CombinePath(ProjectRoot, ".novalist", "settings.json");
        if (await _fileService.ExistsAsync(settingsPath))
        {
            var json = await _fileService.ReadTextAsync(settingsPath);
            ProjectSettings = JsonSerializer.Deserialize<ProjectSettings>(json, JsonOptions) ?? new ProjectSettings();
        }
        else
        {
            ProjectSettings = new ProjectSettings();
        }
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

    public async Task RenameProjectAsync(string newName)
    {
        if (CurrentProject == null || ProjectRoot == null) return;
        if (string.IsNullOrWhiteSpace(newName)) return;

        CurrentProject.Name = newName.Trim();
        await SaveProjectAsync();
    }

    // ── Draft management ────────────────────────────────────────────

    public async Task<BookDraftMetadata> CreateDraftAsync(string draftName, string? cloneFromDraftId = null)
    {
        if (ActiveBook == null || ProjectRoot == null)
            throw new InvalidOperationException("No active book.");

        var safeName = SanitizeFileName(draftName);
        var folderName = MakeUniqueDraftFolder(ActiveBook, safeName);
        var draft = new BookDraftMetadata
        {
            Id = $"draft-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            Name = draftName,
            FolderName = folderName,
            CreatedAt = DateTime.UtcNow,
            ParentDraftId = cloneFromDraftId,
        };
        ActiveBook.Drafts.Add(draft);

        var bookRoot = ActiveBookRoot!;
        var draftRoot = _fileService.CombinePath(bookRoot, "Drafts", draft.FolderName);
        await _fileService.CreateDirectoryAsync(draftRoot);
        await _fileService.CreateDirectoryAsync(_fileService.CombinePath(draftRoot, ActiveBook.ChapterFolder));
        await _fileService.CreateDirectoryAsync(_fileService.CombinePath(draftRoot, ActiveBook.SnapshotFolder));

        if (!string.IsNullOrEmpty(cloneFromDraftId))
        {
            var source = ActiveBook.Drafts.FirstOrDefault(d => d.Id == cloneFromDraftId);
            if (source != null)
            {
                var srcRoot = _fileService.CombinePath(bookRoot, "Drafts", source.FolderName);
                await CopyDraftTreeAsync(srcRoot, draftRoot);
            }
        }
        else
        {
            // Empty draft.
            var emptyData = new BookDraftData();
            await _fileService.WriteTextAsync(
                _fileService.CombinePath(draftRoot, "draft.json"),
                JsonSerializer.Serialize(emptyData, JsonOptions));
            await _fileService.WriteTextAsync(
                _fileService.CombinePath(draftRoot, "scenes.json"),
                JsonSerializer.Serialize(new ScenesManifest(), JsonOptions));
        }

        await SaveProjectAsync();
        return draft;
    }

    public async Task SwitchDraftAsync(string draftId)
    {
        if (ActiveBook == null || ProjectRoot == null) return;
        var target = ActiveBook.Drafts.FirstOrDefault(d => string.Equals(d.Id, draftId, StringComparison.OrdinalIgnoreCase));
        if (target == null || string.Equals(ActiveBook.ActiveDraftId, target.Id, StringComparison.OrdinalIgnoreCase))
            return;

        // Flush the outgoing draft to disk.
        await SaveActiveDraftDataAsync();
        await SaveScenesAsync();

        ActiveBook.ActiveDraftId = target.Id;

        // Reload incoming draft state into ActiveBook.Chapters / Acts + manifest.
        ActiveBook.Chapters = new List<ChapterData>();
        ActiveBook.Acts = new List<ActData>();
        await LoadActiveDraftDataAsync();
        await LoadScenesManifestAsync();
        await SaveProjectAsync();
    }

    public async Task RenameDraftAsync(string draftId, string newName)
    {
        if (ActiveBook == null) return;
        var draft = ActiveBook.Drafts.FirstOrDefault(d => d.Id == draftId);
        if (draft == null || string.IsNullOrWhiteSpace(newName)) return;
        draft.Name = newName.Trim();
        await SaveProjectAsync();
    }

    public async Task DeleteDraftAsync(string draftId)
    {
        if (ActiveBook == null || ProjectRoot == null) return;
        if (ActiveBook.Drafts.Count <= 1)
            throw new InvalidOperationException("Cannot delete the last draft.");

        var draft = ActiveBook.Drafts.FirstOrDefault(d => d.Id == draftId);
        if (draft == null) return;
        var wasActive = string.Equals(ActiveBook.ActiveDraftId, draftId, StringComparison.OrdinalIgnoreCase);

        ActiveBook.Drafts.Remove(draft);
        if (wasActive)
        {
            var next = ActiveBook.Drafts.First();
            ActiveBook.ActiveDraftId = next.Id;
            ActiveBook.Chapters = new List<ChapterData>();
            ActiveBook.Acts = new List<ActData>();
            await LoadActiveDraftDataAsync();
            await LoadScenesManifestAsync();
        }

        await SaveProjectAsync();

        var bookRoot = ActiveBookRoot!;
        var draftRoot = _fileService.CombinePath(bookRoot, "Drafts", draft.FolderName);
        if (await _fileService.DirectoryExistsAsync(draftRoot))
            await _fileService.DeleteDirectoryAsync(draftRoot);
    }

    private static string MakeUniqueDraftFolder(BookData book, string baseName)
    {
        var safe = string.IsNullOrEmpty(baseName) ? "draft" : baseName;
        if (!book.Drafts.Any(d => string.Equals(d.FolderName, safe, StringComparison.OrdinalIgnoreCase)))
            return safe;
        var i = 2;
        while (book.Drafts.Any(d => string.Equals(d.FolderName, safe + "-" + i, StringComparison.OrdinalIgnoreCase)))
            i++;
        return safe + "-" + i;
    }

    private async Task CopyDraftTreeAsync(string srcRoot, string dstRoot)
    {
        // Recursive copy. Files only — sub-dirs walked manually using IFileService.
        var dirs = await _fileService.GetDirectoriesAsync(srcRoot);
        var files = await _fileService.GetFilesAsync(srcRoot, "*");
        foreach (var f in files)
        {
            var name = _fileService.GetFileName(f);
            var target = _fileService.CombinePath(dstRoot, name);
            var content = await _fileService.ReadTextAsync(f);
            await _fileService.WriteTextAsync(target, content);
        }
        foreach (var d in dirs)
        {
            var name = _fileService.GetFileName(d);
            var targetDir = _fileService.CombinePath(dstRoot, name);
            await _fileService.CreateDirectoryAsync(targetDir);
            await CopyDraftTreeAsync(d, targetDir);
        }
    }

    public async Task RenameBookAsync(string bookId, string newName)
    {
        if (CurrentProject == null || ProjectRoot == null) return;

        var book = CurrentProject.Books.FirstOrDefault(b => b.Id == bookId);
        if (book == null) return;

        var oldFolderPath = _fileService.CombinePath(ProjectRoot, book.FolderName);
        var newFolderName = SanitizeFileName(newName);
        var newFolderPath = _fileService.CombinePath(ProjectRoot, newFolderName);

        if (await _fileService.DirectoryExistsAsync(oldFolderPath) && oldFolderPath != newFolderPath)
            Directory.Move(oldFolderPath, newFolderPath);

        book.Name = newName;
        book.FolderName = newFolderName;
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
        if (CurrentProject == null || ProjectRoot == null) return;

        var wbRoot = WorldBibleRoot!;
        await _fileService.CreateDirectoryAsync(wbRoot);
        await _fileService.CreateDirectoryAsync(_fileService.CombinePath(wbRoot, CurrentProject.CharacterFolder));
        await _fileService.CreateDirectoryAsync(_fileService.CombinePath(wbRoot, CurrentProject.LocationFolder));
        await _fileService.CreateDirectoryAsync(_fileService.CombinePath(wbRoot, CurrentProject.ItemFolder));
        await _fileService.CreateDirectoryAsync(_fileService.CombinePath(wbRoot, CurrentProject.LoreFolder));
        await _fileService.CreateDirectoryAsync(_fileService.CombinePath(wbRoot, CurrentProject.ImageFolder));
    }

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
        if (ActiveBook == null || ActiveBookRoot == null)
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
        ScenesManifest!.Chapters[chapter.Guid] = new List<SceneData>();

        var chapterPath = GetChapterFolderPath(chapter);
        await _fileService.CreateDirectoryAsync(chapterPath);

        await SaveProjectAsync();
        await SaveScenesAsync();

        return chapter;
    }

    /// <param name="template">
    /// A scene to start from. Everything it carries is copied onto the new
    /// scene - synopsis, prose, point of view, stage, label, tags, plotlines -
    /// which is the whole difference between a template and a note about one.
    /// </param>
    public async Task<SceneData> CreateSceneAsync(
        string chapterGuid, string sceneTitle, string date = "", SceneTemplate? template = null)
    {
        if (ActiveBook == null || ActiveBookRoot == null)
            throw new InvalidOperationException("No book active.");

        var chapter = ActiveBook.Chapters.FirstOrDefault(c => c.Guid == chapterGuid)
            ?? throw new ArgumentException($"Chapter not found: {chapterGuid}");

        if (!ScenesManifest!.Chapters.TryGetValue(chapterGuid, out var scenes))
        {
            scenes = new List<SceneData>();
            ScenesManifest.Chapters[chapterGuid] = scenes;
        }

        var nextOrder = scenes.Count > 0 ? scenes.Max(s => s.Order) + 1 : 1;
        var fileName = GetNextSceneFileName(scenes);

        var scene = new SceneData
        {
            Title = sceneTitle,
            Order = nextOrder,
            FileName = fileName,
            ChapterGuid = chapterGuid,
            Date = date
        };

        if (template != null) ApplyTemplate(scene, template);
        scenes.Add(scene);

        var scenePath = GetSceneFilePath(chapter, scene);
        // Born stamped with its identity front-matter, and with the template's
        // prose if there is one.
        await _fileService.WriteTextAsync(
            scenePath, FileFrontMatter.Build(scene.Id) + (template?.Content ?? string.Empty));

        await SaveScenesAsync();

        return scene;
    }

    /// <summary>
    /// Copies a template onto a new scene. Lists are copied rather than shared,
    /// or editing the scene's plotlines would rewrite the template.
    /// </summary>
    private static void ApplyTemplate(SceneData scene, SceneTemplate template)
    {
        scene.Synopsis = string.IsNullOrWhiteSpace(template.Synopsis) ? scene.Synopsis : template.Synopsis;
        scene.Stage = template.Stage;
        scene.LabelKey = template.LabelKey;
        if (template.PlotlineIds.Count > 0) scene.PlotlineIds = [.. template.PlotlineIds];

        if (template.Pov == null && template.Tags.Count == 0) return;
        scene.AnalysisOverrides ??= new SceneAnalysisOverrides();
        if (template.Pov != null) scene.AnalysisOverrides.Pov = template.Pov;
        if (template.Tags.Count > 0) scene.AnalysisOverrides.Tags = [.. template.Tags];
    }

    /// <summary>
    /// Captures a scene as a template: what it is, not what it says. The title
    /// is deliberately not copied - a template named after one scene would put
    /// that scene's name on every scene made from it.
    /// </summary>
    public async Task<SceneTemplate> SaveSceneAsTemplateAsync(
        string chapterGuid, string sceneId, string name)
    {
        if (ActiveBook == null) throw new InvalidOperationException("No book active.");

        var chapter = ActiveBook.Chapters.FirstOrDefault(c => c.Guid == chapterGuid)
            ?? throw new ArgumentException($"Chapter not found: {chapterGuid}");
        var scene = GetScenesForChapter(chapterGuid).FirstOrDefault(s => s.Id == sceneId)
            ?? throw new ArgumentException($"Scene not found: {sceneId}");

        var template = new SceneTemplate
        {
            Name = string.IsNullOrWhiteSpace(name) ? scene.Title : name.Trim(),
            Synopsis = scene.Synopsis ?? string.Empty,
            Content = await ReadSceneContentAsync(chapter, scene),
            Pov = scene.AnalysisOverrides?.Pov,
            Stage = scene.Stage,
            LabelKey = scene.LabelKey,
            Tags = [.. scene.AnalysisOverrides?.Tags ?? []],
            PlotlineIds = [.. scene.PlotlineIds ?? []]
        };

        ActiveBook.SceneTemplates.Add(template);
        await SaveProjectAsync();
        return template;
    }

    public async Task SetChapterDateAsync(string chapterGuid, string date)
    {
        if (ActiveBook == null) return;

        var chapter = ActiveBook.Chapters.FirstOrDefault(c => c.Guid == chapterGuid);
        if (chapter == null) return;

        chapter.Date = date.Trim();
        await SaveProjectAsync();
    }

    public async Task SetSceneDateAsync(string chapterGuid, string sceneId, string date)
    {
        if (ScenesManifest == null) return;

        if (!ScenesManifest.Chapters.TryGetValue(chapterGuid, out var scenes)) return;

        var scene = scenes.FirstOrDefault(s => s.Id == sceneId);
        if (scene == null) return;

        scene.Date = date.Trim();
        await SaveScenesAsync();
    }

    public async Task SetChapterFavoriteAsync(string chapterGuid, bool favorite)
    {
        if (ActiveBook == null) return;
        var chapter = ActiveBook.Chapters.FirstOrDefault(c => c.Guid == chapterGuid);
        if (chapter == null) return;
        chapter.IsFavorite = favorite;
        await SaveProjectAsync();
    }

    public async Task SetSceneFavoriteAsync(string chapterGuid, string sceneId, bool favorite)
    {
        if (ScenesManifest == null) return;
        if (!ScenesManifest.Chapters.TryGetValue(chapterGuid, out var scenes)) return;
        var scene = scenes.FirstOrDefault(s => s.Id == sceneId);
        if (scene == null) return;
        scene.IsFavorite = favorite;
        await SaveScenesAsync();
    }

    public async Task SetSceneLabelColorAsync(string chapterGuid, string sceneId, string? labelColor)
    {
        if (ScenesManifest == null) return;
        if (!ScenesManifest.Chapters.TryGetValue(chapterGuid, out var scenes)) return;
        var scene = scenes.FirstOrDefault(s => s.Id == sceneId);
        if (scene == null) return;
        scene.LabelColor = string.IsNullOrWhiteSpace(labelColor) ? null : labelColor.Trim();
        await SaveScenesAsync();
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

    public async Task SetSceneDateRangeAsync(string chapterGuid, string sceneId, StoryDateRange? dateRange)
    {
        if (ScenesManifest == null) return;
        if (!ScenesManifest.Chapters.TryGetValue(chapterGuid, out var scenes)) return;
        var scene = scenes.FirstOrDefault(s => s.Id == sceneId);
        if (scene == null) return;
        scene.DateRange = dateRange?.HasValue == true ? dateRange.Clone() : null;
        if (dateRange?.HasValue == true && !string.IsNullOrWhiteSpace(dateRange.Start))
            scene.Date = dateRange.Start;
        await SaveScenesAsync();
    }

    public async Task SetSceneAnalysisOverridesAsync(string chapterGuid, string sceneId, SceneAnalysisOverrides? overrides)
    {
        if (ScenesManifest == null) return;

        if (!ScenesManifest.Chapters.TryGetValue(chapterGuid, out var scenes)) return;

        var scene = scenes.FirstOrDefault(candidate => candidate.Id == sceneId);
        if (scene == null) return;

        scene.AnalysisOverrides = overrides?.HasValues == true ? overrides.Clone() : null;
        await SaveScenesAsync();
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

    public async Task DeleteSceneAsync(string chapterGuid, string sceneId)
    {
        if (ScenesManifest == null || ActiveBook == null) return;

        if (ScenesManifest.Chapters.TryGetValue(chapterGuid, out var scenes))
        {
            var scene = scenes.FirstOrDefault(s => s.Id == sceneId);
            if (scene != null)
            {
                var chapter = ActiveBook.Chapters.FirstOrDefault(c => c.Guid == chapterGuid);
                string? sceneFilePath = chapter != null ? GetSceneFilePath(chapter, scene) : null;

                scenes.Remove(scene);
                var ordered = scenes.OrderBy(s => s.Order).ToList();
                for (int i = 0; i < ordered.Count; i++)
                    ordered[i].Order = i + 1;

                if (sceneFilePath != null)
                    await _fileService.DeleteFileAsync(sceneFilePath);
            }
        }

        await SaveScenesAsync();
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

    public async Task ReorderSceneAsync(string chapterGuid, string sceneId, int newOrder)
    {
        if (ScenesManifest == null) return;

        if (!ScenesManifest.Chapters.TryGetValue(chapterGuid, out var scenes)) return;

        var scene = scenes.FirstOrDefault(s => s.Id == sceneId);
        if (scene == null) return;

        var oldOrder = scene.Order;
        if (oldOrder == newOrder) return;

        foreach (var s in scenes)
        {
            if (s.Id == sceneId)
            {
                s.Order = newOrder;
            }
            else if (oldOrder < newOrder && s.Order > oldOrder && s.Order <= newOrder)
            {
                s.Order--;
            }
            else if (oldOrder > newOrder && s.Order >= newOrder && s.Order < oldOrder)
            {
                s.Order++;
            }
        }

        await SaveScenesAsync();
    }

    public async Task MoveScenesAsync(IReadOnlyList<string> sceneIds, string targetChapterGuid, int targetIndex)
    {
        if (ScenesManifest == null || ActiveBook == null || ActiveBookRoot == null || sceneIds.Count == 0) return;

        var targetChapter = ActiveBook.Chapters.FirstOrDefault(chapter => chapter.Guid == targetChapterGuid);
        if (targetChapter == null) return;

        if (!ScenesManifest.Chapters.TryGetValue(targetChapterGuid, out var targetScenes))
        {
            targetScenes = new List<SceneData>();
            ScenesManifest.Chapters[targetChapterGuid] = targetScenes;
        }

        var sceneSet = new HashSet<string>(sceneIds);
        var moving = new List<(SceneData Scene, string SourceChapterGuid)>();

        foreach (var chapterEntry in ScenesManifest.Chapters)
        {
            foreach (var scene in chapterEntry.Value.Where(scene => sceneSet.Contains(scene.Id)).OrderBy(scene => scene.Order))
            {
                moving.Add((scene, chapterEntry.Key));
            }
        }

        if (moving.Count == 0) return;

        foreach (var chapterEntry in ScenesManifest.Chapters)
        {
            chapterEntry.Value.RemoveAll(scene => sceneSet.Contains(scene.Id));
            ReindexScenes(chapterEntry.Value);
        }

        targetIndex = Math.Clamp(targetIndex, 0, targetScenes.Count);

        foreach (var item in moving)
        {
            if (item.SourceChapterGuid != targetChapterGuid)
            {
                var sourceChapter = ActiveBook.Chapters.FirstOrDefault(chapter => chapter.Guid == item.SourceChapterGuid);
                if (sourceChapter != null)
                {
                    var oldPath = GetSceneFilePath(sourceChapter, item.Scene);
                    item.Scene.FileName = GetNextSceneFileName(targetScenes.Concat(moving.Select(m => m.Scene)).ToList());
                    item.Scene.ChapterGuid = targetChapterGuid;
                    var newPath = GetSceneFilePath(targetChapter, item.Scene);

                    if (await _fileService.ExistsAsync(oldPath))
                        await _fileService.MoveFileAsync(oldPath, newPath);
                }
            }
            else
            {
                item.Scene.ChapterGuid = targetChapterGuid;
            }
        }

        targetScenes.InsertRange(targetIndex, moving.Select(item => item.Scene));
        ReindexScenes(targetScenes);

        await SaveScenesAsync();
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
            Directory.Move(oldFolderPath, newFolderPath);
        }

        await SaveProjectAsync();
    }

    public async Task RenameSceneAsync(string chapterGuid, string sceneId, string newTitle)
    {
        if (ScenesManifest == null) return;

        if (ScenesManifest.Chapters.TryGetValue(chapterGuid, out var scenes))
        {
            var scene = scenes.FirstOrDefault(s => s.Id == sceneId);
            if (scene != null)
            {
                scene.Title = newTitle;
            }
        }

        await SaveScenesAsync();
    }

    // ── Scene archive ───────────────────────────────────────────────

    private const string ArchiveFolderName = "__Archive";

    private string GetArchiveFolderPath()
    {
        if (ActiveBook == null) throw new InvalidOperationException("No active book.");
        var root = ActiveDraftRoot ?? ActiveBookRoot
            ?? throw new InvalidOperationException("No active book root.");
        return _fileService.CombinePath(root, ActiveBook.ChapterFolder, ArchiveFolderName);
    }

    public string GetArchivedSceneFilePath(SceneData scene)
    {
        return _fileService.CombinePath(GetArchiveFolderPath(), scene.FileName);
    }

    public async Task<string> ReadArchivedSceneContentAsync(SceneData scene)
    {
        var path = GetArchivedSceneFilePath(scene);
        if (await _fileService.ExistsAsync(path))
            return FileFrontMatter.Strip(await _fileService.ReadTextAsync(path));
        return string.Empty;
    }

    public IReadOnlyList<SceneData> GetArchivedScenes()
        => ScenesManifest?.Archived ?? new List<SceneData>();

    public async Task ArchiveSceneAsync(string chapterGuid, string sceneId)
    {
        if (ScenesManifest == null || ActiveBook == null || ActiveBookRoot == null) return;
        if (!ScenesManifest.Chapters.TryGetValue(chapterGuid, out var scenes)) return;

        var scene = scenes.FirstOrDefault(s => s.Id == sceneId);
        if (scene == null) return;

        var chapter = ActiveBook.Chapters.FirstOrDefault(c => c.Guid == chapterGuid);
        if (chapter == null) return;

        var sourcePath = GetSceneFilePath(chapter, scene);
        var archiveFolder = GetArchiveFolderPath();
        await _fileService.CreateDirectoryAsync(archiveFolder);

        // Resolve filename collisions in the archive folder by suffixing with the scene id.
        var targetFileName = scene.FileName;
        var targetPath = _fileService.CombinePath(archiveFolder, targetFileName);
        if (await _fileService.ExistsAsync(targetPath))
        {
            var ext = Path.GetExtension(scene.FileName);
            var stem = Path.GetFileNameWithoutExtension(scene.FileName);
            targetFileName = $"{stem}-{scene.Id}{ext}";
            targetPath = _fileService.CombinePath(archiveFolder, targetFileName);
        }

        if (await _fileService.ExistsAsync(sourcePath))
            await _fileService.MoveFileAsync(sourcePath, targetPath);

        scene.FileName = targetFileName;
        scene.OriginChapterGuid = chapterGuid;
        // Recorded before the removal, or the index is the one it no longer has.
        scene.OriginIndex = scenes.IndexOf(scene);
        scene.ArchivedAt = DateTime.UtcNow;
        scene.ChapterGuid = string.Empty;

        scenes.Remove(scene);
        ReindexScenes(scenes);
        ScenesManifest.Archived.Add(scene);

        await SaveScenesAsync();
    }

    /// <summary>
    /// Brings an archived scene back.
    ///
    /// An empty <paramref name="targetChapterGuid"/> means "where it came
    /// from", which is what a writer restoring something almost always wants.
    /// Landing in the origin chapter with no index asked for puts the scene
    /// back in the slot it left, rather than at the end where every restore
    /// used to arrive.
    /// </summary>
    public async Task RestoreArchivedSceneAsync(
        string sceneId, string targetChapterGuid, int? targetIndex)
    {
        if (ScenesManifest == null || ActiveBook == null || ActiveBookRoot == null) return;

        var scene = ScenesManifest.Archived.FirstOrDefault(s => s.Id == sceneId);
        if (scene == null) return;

        var asked = string.IsNullOrWhiteSpace(targetChapterGuid)
            ? scene.OriginChapterGuid ?? string.Empty
            : targetChapterGuid;
        var origin = scene.OriginChapterGuid;

        var targetChapter = ActiveBook.Chapters.FirstOrDefault(c => c.Guid == asked);
        // The chapter it came from can have been deleted since. Falling back to
        // the first chapter beats refusing to restore: the scene exists and the
        // writer asked for it back.
        if (targetChapter == null && string.IsNullOrWhiteSpace(targetChapterGuid))
            targetChapter = ActiveBook.Chapters.OrderBy(c => c.Order).FirstOrDefault();
        if (targetChapter == null) return;

        // Captured here: the fields that say where the scene came from are
        // cleared further down, before the insert position is worked out.
        var homeIndex = targetChapter.Guid == origin ? scene.OriginIndex : null;
        targetChapterGuid = targetChapter.Guid;

        if (!ScenesManifest.Chapters.TryGetValue(targetChapterGuid, out var targetScenes))
        {
            targetScenes = new List<SceneData>();
            ScenesManifest.Chapters[targetChapterGuid] = targetScenes;
        }

        var sourcePath = GetArchivedSceneFilePath(scene);
        // Generate a fresh, non-colliding filename in the target chapter.
        var newFileName = GetNextSceneFileName(targetScenes);
        scene.FileName = newFileName;
        scene.ChapterGuid = targetChapterGuid;
        scene.ArchivedAt = null;
        scene.OriginChapterGuid = null;
        scene.OriginIndex = null;

        var targetPath = GetSceneFilePath(targetChapter, scene);

        await _fileService.CreateDirectoryAsync(GetChapterFolderPath(targetChapter));
        if (await _fileService.ExistsAsync(sourcePath))
            await _fileService.MoveFileAsync(sourcePath, targetPath);

        // Its own slot when it is going home and nobody named one; the end
        // otherwise, because a scene arriving in a chapter it never lived in
        // has no position of its own to claim.
        var wanted = targetIndex ?? homeIndex;
        var insertAt = wanted.HasValue
            ? Math.Clamp(wanted.Value, 0, targetScenes.Count)
            : targetScenes.Count;
        targetScenes.Insert(insertAt, scene);
        ReindexScenes(targetScenes);
        ScenesManifest.Archived.Remove(scene);

        await SaveScenesAsync();
    }

    public async Task DeleteArchivedSceneAsync(string sceneId)
    {
        if (ScenesManifest == null) return;

        var scene = ScenesManifest.Archived.FirstOrDefault(s => s.Id == sceneId);
        if (scene == null) return;

        var path = GetArchivedSceneFilePath(scene);
        if (await _fileService.ExistsAsync(path))
            await _fileService.DeleteFileAsync(path);

        ScenesManifest.Archived.Remove(scene);
        await SaveScenesAsync();
    }

    // ── Mention-span sync ───────────────────────────────────────────

    private static readonly Regex MentionSpanRegex = new(
        @"<span\s+([^>]*?)class\s*=\s*[""']nv-entity-mention[""']([^>]*)>([\s\S]*?)</span>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<int> SyncMentionDisplayTextAsync(string entityId, string newDisplayText)
    {
        if (ScenesManifest == null || ActiveBook == null || ActiveBookRoot == null) return 0;
        if (string.IsNullOrEmpty(entityId)) return 0;

        var modified = 0;
        var escapedDisplay = System.Net.WebUtility.HtmlEncode(newDisplayText);

        // Active scenes
        foreach (var entry in ScenesManifest.Chapters)
        {
            var chapter = ActiveBook.Chapters.FirstOrDefault(c => c.Guid == entry.Key);
            if (chapter == null) continue;

            foreach (var scene in entry.Value)
            {
                var path = GetSceneFilePath(chapter, scene);
                if (await TryRewriteSceneMentionsAsync(path, entityId, escapedDisplay))
                    modified++;
            }
        }

        // Archived scenes
        foreach (var scene in ScenesManifest.Archived)
        {
            var path = GetArchivedSceneFilePath(scene);
            if (await TryRewriteSceneMentionsAsync(path, entityId, escapedDisplay))
                modified++;
        }

        return modified;
    }

    private async Task<bool> TryRewriteSceneMentionsAsync(string path, string entityId, string newDisplayHtml)
    {
        if (!await _fileService.ExistsAsync(path)) return false;
        var content = await _fileService.ReadTextAsync(path);
        if (string.IsNullOrEmpty(content) || content.IndexOf("nv-entity-mention", StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        var changed = false;
        var rewritten = MentionSpanRegex.Replace(content, match =>
        {
            var attrsLeft = match.Groups[1].Value;
            var attrsRight = match.Groups[2].Value;
            var inner = match.Groups[3].Value;
            var attrs = attrsLeft + attrsRight;

            var idMatch = Regex.Match(attrs, @"data-entity-id\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (!idMatch.Success || !string.Equals(idMatch.Groups[1].Value, entityId, StringComparison.Ordinal))
                return match.Value;

            var sourceMatch = Regex.Match(attrs, @"data-mention-source\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
            if (sourceMatch.Success && string.Equals(sourceMatch.Groups[1].Value, "manual", StringComparison.OrdinalIgnoreCase))
                return match.Value;
            if (sourceMatch.Success && string.Equals(sourceMatch.Groups[1].Value, "alias", StringComparison.OrdinalIgnoreCase))
                return match.Value; // alias-sourced spans keep their text

            if (string.Equals(inner, newDisplayHtml, StringComparison.Ordinal))
                return match.Value;

            changed = true;
            // Reconstruct, preserving original attributes verbatim.
            return $"<span {attrsLeft}class=\"nv-entity-mention\"{attrsRight}>{newDisplayHtml}</span>";
        });

        if (!changed) return false;
        await _fileService.WriteTextAsync(path, rewritten);
        return true;
    }

    public string GetChapterFolderPath(ChapterData chapter)
    {
        var root = ActiveDraftRoot ?? ActiveBookRoot!;
        return _fileService.CombinePath(root, ActiveBook!.ChapterFolder, chapter.FolderName);
    }

    public string GetSceneFilePath(ChapterData chapter, SceneData scene)
    {
        return _fileService.CombinePath(GetChapterFolderPath(chapter), scene.FileName);
    }

    public async Task<string> ReadSceneContentAsync(ChapterData chapter, SceneData scene)
    {
        var path = GetSceneFilePath(chapter, scene);
        if (await _fileService.ExistsAsync(path))
            return FileFrontMatter.Strip(await _fileService.ReadTextAsync(path));
        return string.Empty;
    }

    public async Task WriteSceneContentAsync(ChapterData chapter, SceneData scene, string content)
    {
        var path = GetSceneFilePath(chapter, scene);
        // Re-stamp the identity front-matter: strip any echoed-back marker, then prepend the
        // canonical one. Keeps the scene's id durable across every save without leaking the
        // comment into the editor's content.
        var stamped = FileFrontMatter.Stamp(FileFrontMatter.Strip(content), scene.Id);
        await _fileService.WriteTextAsync(path, stamped);
    }

    public List<ChapterData> GetChaptersOrdered()
    {
        return ActiveBook?.Chapters.OrderBy(c => c.Order).ToList() ?? new List<ChapterData>();
    }

    public List<SceneData> GetScenesForChapter(string chapterGuid)
    {
        if (ScenesManifest?.Chapters.TryGetValue(chapterGuid, out var scenes) == true)
            return scenes.OrderBy(s => s.Order).ToList();
        return new List<SceneData>();
    }

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
            foreach (var sub in Directory.GetDirectories(oldChapters))
            {
                var name = Path.GetFileName(sub);
                var target = _fileService.CombinePath(newChapters, name);
                if (Directory.Exists(target))
                {
                    // Target chapter folder exists — only merge in if it has no
                    // scene files (i.e. an empty stub from a half-finished prior
                    // migration). If it already has scenes, leave both intact.
                    if (Directory.EnumerateFileSystemEntries(target).Any()) continue;
                    foreach (var file in Directory.EnumerateFiles(sub))
                        File.Move(file, _fileService.CombinePath(target, Path.GetFileName(file)));
                    try { Directory.Delete(sub); } catch { /* leave empty */ }
                }
                else
                {
                    Directory.Move(sub, target);
                }
            }
            // Drop the legacy chapters folder if it's now empty.
            TryDeleteEmptyDir(oldChapters);
        }

        // Snapshots: same pattern.
        var oldSnaps = _fileService.CombinePath(bookRoot, book.SnapshotFolder);
        var newSnaps = _fileService.CombinePath(draftRoot, book.SnapshotFolder);
        if (await _fileService.DirectoryExistsAsync(oldSnaps) && !await _fileService.DirectoryExistsAsync(newSnaps))
            Directory.Move(oldSnaps, newSnaps);

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
    private static void TryDeleteEmptyDir(string dir)
    {
        try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
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

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return sanitized.Trim();
    }
}
