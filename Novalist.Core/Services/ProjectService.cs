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
        var book = new BookData
        {
            Id = bookId,
            Name = firstBookName,
            FolderName = SanitizeFileName(firstBookName),
            CreatedAt = DateTime.UtcNow
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

        // Load scenes manifest for the active book
        await LoadScenesManifestAsync();

        // Load project-level settings
        await LoadProjectSettingsAsync();

        return metadata;
    }

    public async Task SaveProjectAsync()
    {
        if (CurrentProject == null || ProjectRoot == null) return;

        var metadataPath = _fileService.CombinePath(ProjectRoot, ".novalist", "project.json");
        var json = JsonSerializer.Serialize(CurrentProject, JsonOptions);
        await _fileService.WriteTextAsync(metadataPath, json);
    }

    public async Task SaveScenesAsync()
    {
        if (ScenesManifest == null || ActiveBookRoot == null) return;

        var bookDir = _fileService.CombinePath(ActiveBookRoot, ".book");
        await _fileService.CreateDirectoryAsync(bookDir);
        var scenesPath = _fileService.CombinePath(bookDir, "scenes.json");
        var json = JsonSerializer.Serialize(ScenesManifest, JsonOptions);
        await _fileService.WriteTextAsync(scenesPath, json);
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

    public string GetChapterFolderPath(ChapterData chapter)
    {
        return _fileService.CombinePath(ActiveBookRoot!, ActiveBook!.ChapterFolder, chapter.FolderName);
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

        await _fileService.CreateDirectoryAsync(_fileService.CombinePath(bookRoot, book.ChapterFolder));
        await _fileService.CreateDirectoryAsync(_fileService.CombinePath(bookRoot, book.CharacterFolder));
        await _fileService.CreateDirectoryAsync(_fileService.CombinePath(bookRoot, book.LocationFolder));
        await _fileService.CreateDirectoryAsync(_fileService.CombinePath(bookRoot, book.ItemFolder));
        await _fileService.CreateDirectoryAsync(_fileService.CombinePath(bookRoot, book.LoreFolder));
        await _fileService.CreateDirectoryAsync(_fileService.CombinePath(bookRoot, book.ImageFolder));
        await _fileService.CreateDirectoryAsync(_fileService.CombinePath(bookRoot, book.SnapshotFolder));
    }

    private async Task LoadScenesManifestAsync()
    {
        if (ActiveBookRoot == null)
        {
            ScenesManifest = new ScenesManifest();
            return;
        }

        var scenesPath = _fileService.CombinePath(ActiveBookRoot, ".book", "scenes.json");
        if (await _fileService.ExistsAsync(scenesPath))
        {
            var scenesJson = await _fileService.ReadTextAsync(scenesPath);
            ScenesManifest = JsonSerializer.Deserialize<ScenesManifest>(scenesJson, JsonOptions) ?? new ScenesManifest();
        }
        else
        {
            ScenesManifest = new ScenesManifest();
        }
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
