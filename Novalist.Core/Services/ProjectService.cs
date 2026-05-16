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
            Books = [book]
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

        // Load the active draft's chapters/acts from draft.json (if present).
        await LoadActiveDraftDataAsync();

        // Load scenes manifest for the active book/draft.
        await LoadScenesManifestAsync();

        // Load project-level settings
        await LoadProjectSettingsAsync();

        return metadata;
    }

    public async Task SaveProjectAsync()
    {
        if (CurrentProject == null || ProjectRoot == null) return;

        // Persist the active draft's chapter / act tree into draft.json.
        await SaveActiveDraftDataAsync();

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

    public async Task SaveProjectSettingsAsync()
    {
        if (ProjectRoot == null) return;

        var settingsPath = _fileService.CombinePath(ProjectRoot, ".novalist", "settings.json");
        var json = JsonSerializer.Serialize(ProjectSettings, JsonOptions);
        await _fileService.WriteTextAsync(settingsPath, json);
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

        var book = new BookData
        {
            Id = $"book-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            Name = bookName,
            FolderName = SanitizeFileName(bookName),
            CreatedAt = DateTime.UtcNow
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

        CurrentProject.ActiveBookId = bookId;
        ActiveBook = book;

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

    public async Task<ChapterData> CreateChapterAsync(string title, string date = "")
    {
        if (ActiveBook == null || ActiveBookRoot == null)
            throw new InvalidOperationException("No book active.");

        var nextOrder = ActiveBook.Chapters.Count > 0
            ? ActiveBook.Chapters.Max(c => c.Order) + 1
            : 1;

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

    public async Task<SceneData> CreateSceneAsync(string chapterGuid, string sceneTitle, string date = "")
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

        scenes.Add(scene);

        var scenePath = GetSceneFilePath(chapter, scene);
        await _fileService.WriteTextAsync(scenePath, string.Empty);

        await SaveScenesAsync();

        return scene;
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

    public async Task DeleteChapterAsync(string chapterGuid)
    {
        if (ActiveBook == null) return;

        var chapter = ActiveBook.Chapters.FirstOrDefault(c => c.Guid == chapterGuid);
        if (chapter == null) return;

        var chapterFolderPath = GetChapterFolderPath(chapter);

        ActiveBook.Chapters.Remove(chapter);
        ScenesManifest?.Chapters.Remove(chapterGuid);

        var ordered = ActiveBook.Chapters.OrderBy(c => c.Order).ToList();
        for (int i = 0; i < ordered.Count; i++)
            ordered[i].Order = i + 1;

        await SaveProjectAsync();
        await SaveScenesAsync();
        await _fileService.DeleteDirectoryAsync(chapterFolderPath);
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
            return await _fileService.ReadTextAsync(path);
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
        scene.ArchivedAt = DateTime.UtcNow;
        scene.ChapterGuid = string.Empty;

        scenes.Remove(scene);
        ReindexScenes(scenes);
        ScenesManifest.Archived.Add(scene);

        await SaveScenesAsync();
    }

    public async Task RestoreArchivedSceneAsync(string sceneId, string targetChapterGuid, int? targetIndex)
    {
        if (ScenesManifest == null || ActiveBook == null || ActiveBookRoot == null) return;

        var scene = ScenesManifest.Archived.FirstOrDefault(s => s.Id == sceneId);
        if (scene == null) return;

        var targetChapter = ActiveBook.Chapters.FirstOrDefault(c => c.Guid == targetChapterGuid);
        if (targetChapter == null) return;

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

        var targetPath = GetSceneFilePath(targetChapter, scene);

        await _fileService.CreateDirectoryAsync(GetChapterFolderPath(targetChapter));
        if (await _fileService.ExistsAsync(sourcePath))
            await _fileService.MoveFileAsync(sourcePath, targetPath);

        var insertAt = targetIndex.HasValue
            ? Math.Clamp(targetIndex.Value, 0, targetScenes.Count)
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
            return await _fileService.ReadTextAsync(path);
        return string.Empty;
    }

    public async Task WriteSceneContentAsync(ChapterData chapter, SceneData scene, string content)
    {
        var path = GetSceneFilePath(chapter, scene);
        await _fileService.WriteTextAsync(path, content);
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
            try { if (!Directory.EnumerateFileSystemEntries(oldChapters).Any()) Directory.Delete(oldChapters); }
            catch { /* fine — leave the empty folder if something is holding it */ }
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
        };
        await _fileService.WriteTextAsync(path, JsonSerializer.Serialize(data, JsonOptions));
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
