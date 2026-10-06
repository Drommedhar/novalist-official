using System.Text.Json;
using System.Text.RegularExpressions;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public partial class ProjectService : IProjectService
{
    private readonly IFileService _fileService;
    /// <summary>The host's file access policy, shared by services writing this project.</summary>
    public IFileService FileService => _fileService;
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
    [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(true, nameof(CurrentProject), nameof(ActiveBook), nameof(ProjectRoot))]
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
        var projectDir = ProjectDirectory(parentDirectory, projectName);

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
            var createdRoot = ProjectDirectory(parentDirectory, projectName);
            await CreateProjectAsync(parentDirectory, projectName, firstBookName);
            return createdRoot;
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

    private string ProjectDirectory(string parentDirectory, string projectName)
        => _fileService.CombinePath(parentDirectory, SanitizeFileName(projectName));

    /// <summary>
    /// Whether a folder still holds a Novalist project.
    ///
    /// The same file <see cref="LoadProjectAsync"/> would open, so a path that
    /// answers <see cref="ProjectPresence.Present"/> is one that can actually be
    /// opened. Used to keep the recent-projects list honest: a folder the writer
    /// has since deleted, or one whose project was moved out from under it, is
    /// not somewhere to offer to go back to.
    ///
    /// The third answer is the point of this method. "The project file is not
    /// there" and "I was not allowed to look" arrive identically - as a false
    /// from the file service, or as an exception this swallows - and treating
    /// the second as the first is how the iOS build erased its own recent
    /// projects: the folders live behind a security-scoped grant that is not
    /// active while the list is being built, every entry read as deleted, and
    /// the list pruned itself out of existence on every launch. So absence is
    /// only reported when the folder's PARENT can be read and the project is
    /// genuinely not in it. Anything else is <see cref="ProjectPresence.Unknown"/>:
    /// come back later, and keep the entry meanwhile.
    /// </summary>
    public async Task<ProjectPresence> ProbeProjectAsync(string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory)) return ProjectPresence.Absent;

        try
        {
            var metadataPath = _fileService.CombinePath(projectDirectory, ".novalist", "project.json");
            if (await _fileService.ExistsAsync(metadataPath)) return ProjectPresence.Present;

            var parent = _fileService.GetDirectoryName(projectDirectory);
            if (!string.IsNullOrEmpty(parent) && await _fileService.DirectoryExistsAsync(parent))
                return ProjectPresence.Absent;

            return ProjectPresence.Unknown;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            return ProjectPresence.Unknown;
        }
    }

    public Task<ProjectMetadata> LoadProjectAsync(string projectDirectory) => LoadProjectAsync(projectDirectory, null);

    /// <summary>Open a library book directly, without loading the previously active draft first.</summary>
    public async Task<ProjectMetadata> LoadProjectAsync(string projectDirectory, string? bookId)
    {
        var metadataPath = _fileService.CombinePath(projectDirectory, ".novalist", "project.json");
        if (!await _fileService.ExistsAsync(metadataPath))
            throw new FileNotFoundException("No Novalist project found at this location.", metadataPath);

        var json = await _fileService.ReadTextAsync(metadataPath);
        var metadata = JsonSerializer.Deserialize<ProjectMetadata>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to parse project metadata.");

        if (bookId != null)
        {
            if (!metadata.Books.Any(book => book.Id == bookId))
                throw new ArgumentException("Book not found.", nameof(bookId));
            metadata.ActiveBookId = bookId;
        }

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

        if (bookId != null) await WriteProjectJsonAsync();
        return metadata;
    }

    /// <summary>
    /// One save at a time. Two writes that overlap collide on the file rather
    /// than merging, and the change the losing one carried is gone with nothing
    /// to show for it - see <see cref="SettingsService.SaveAsync"/>.
    /// </summary>
    private readonly SemaphoreSlim _projectSettingsSaveLock = new(1, 1);

    public async Task RenameProjectAsync(string newName)
    {
        if (CurrentProject == null || ProjectRoot == null) return;
        if (string.IsNullOrWhiteSpace(newName)) return;

        CurrentProject.Name = newName.Trim();
        await SaveProjectAsync();
    }

    // ── Scene archive ───────────────────────────────────────────────

    private const string ArchiveFolderName = "__Archive";

    // ── Mention-span sync ───────────────────────────────────────────

    private static readonly Regex MentionSpanRegex = new(
        @"<span\s+([^>]*?)class\s*=\s*[""']nv-entity-mention[""']([^>]*)>([\s\S]*?)</span>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
}
