using System.Text.RegularExpressions;
using Novalist.Core.Models;
using Novalist.Core.Services;

namespace Novalist.Backend;

/// <summary>
/// Owns the Core service graph for the running app: one project open at a time,
/// app settings (recents), and the word-history journal. RPC facades are thin
/// wrappers over this class so behavior stays testable without an RPC pair.
/// </summary>
public sealed partial class Workspace
{
    public Workspace(string? settingsDirectory = null)
    {
        FileService = new FileService();
        Projects = new ProjectService(FileService);
        Settings = new SettingsService(settingsDirectory);
        WordHistory = new WordHistoryService(FileService, Projects);
    }

    public FileService FileService { get; }
    public ProjectService Projects { get; }
    public SettingsService Settings { get; }
    public WordHistoryService WordHistory { get; }

    private Extensions.ExtensionManager? _extensions;
    private Extensions.HostServices? _hostServices;

    /// <summary>Test seam: overrides the extension discovery directory.</summary>
    public Extensions.ExtensionLoader? ExtensionsLoaderOverride { get; set; }

    /// <summary>The headless extension host, created on first use.</summary>
    public Extensions.ExtensionManager ExtensionsHost
    {
        get
        {
            if (_extensions == null)
            {
                _hostServices = new Extensions.HostServices(
                    FileService, Projects, new EntityService(Projects), Settings);
                _extensions = new Extensions.ExtensionManager(Settings, _hostServices, ExtensionsLoaderOverride);
            }
            return _extensions;
        }
    }

    public async Task<ProjectStateDto> OpenProjectAsync(string projectDirectory)
    {
        await Settings.LoadAsync();
        var metadata = await Projects.LoadProjectAsync(projectDirectory);
        await Projects.ReconcileActiveDraftAsync();
        Settings.SetActiveOverrides(Projects.ProjectSettings.Overrides);
        Settings.AddRecentProject(metadata.Name, projectDirectory);
        await Settings.SaveAsync();
        return BuildState();
    }

    public ProjectStateDto BuildState()
    {
        var project = Projects.CurrentProject;
        var book = Projects.ActiveBook;
        if (project == null || book == null)
        {
            return new ProjectStateDto(false, null, null, null, Array.Empty<BookDto>(), Array.Empty<ChapterDto>());
        }

        var chapters = book.Chapters
            .OrderBy(c => c.Order)
            .Select(c => new ChapterDto(
                c.Guid,
                c.Title,
                c.Order,
                c.Status.ToString(),
                c.Act,
                c.IsFavorite,
                ScenesOf(c.Guid)))
            .ToArray();

        return new ProjectStateDto(
            true,
            project.Name,
            Projects.ProjectRoot,
            book.Id,
            project.Books.Select(b => new BookDto(b.Id, b.Name)).ToArray(),
            chapters);
    }

    private SceneDto[] ScenesOf(string chapterGuid)
    {
        var manifest = Projects.ScenesManifest;
        if (manifest == null || !manifest.Chapters.TryGetValue(chapterGuid, out var scenes))
        {
            return Array.Empty<SceneDto>();
        }
        return scenes
            .Where(s => s.ArchivedAt == null)
            .OrderBy(s => s.Order)
            .Select(s => new SceneDto(
                s.Id, s.Title, s.Order, s.WordCount, s.LabelColor, s.IsFavorite, s.Synopsis))
            .ToArray();
    }

    public async Task<RecentProjectDto[]> GetRecentProjectsAsync()
    {
        await Settings.LoadAsync();
        return Settings.Settings.RecentProjects
            .Select(r => new RecentProjectDto(r.Name, r.Path))
            .ToArray();
    }

    public ChapterData ResolveChapter(string chapterGuid)
    {
        var book = Projects.ActiveBook ?? throw new InvalidOperationException("No project open.");
        return book.Chapters.FirstOrDefault(c => c.Guid == chapterGuid)
            ?? throw new InvalidOperationException("Unknown chapter.");
    }

    public (ChapterData chapter, SceneData scene) ResolveScene(string chapterGuid, string sceneId)
    {
        var chapter = ResolveChapter(chapterGuid);
        var scene = Projects.ScenesManifest?.Chapters.GetValueOrDefault(chapterGuid)?.FirstOrDefault(s => s.Id == sceneId)
            ?? throw new InvalidOperationException("Unknown scene.");
        return (chapter, scene);
    }

    public async Task<int> WriteSceneAsync(string chapterGuid, string sceneId, string html, string plainText)
    {
        var (chapter, scene) = ResolveScene(chapterGuid, sceneId);
        await Projects.WriteSceneContentAsync(chapter, scene, html);
        var wordCount = CountWords(plainText.Length > 0 ? plainText : StripHtml(html));
        scene.WordCount = wordCount;
        await WordHistory.RecordSaveAsync(Projects.ActiveBook?.Id ?? string.Empty, scene.Id, wordCount);
        await Projects.SaveScenesAsync();
        return wordCount;
    }

    // Same regex the Avalonia EditorViewModel uses, so persisted word counts stay identical.
    internal static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return WordRegex().Matches(text).Count;
    }

    internal static string StripHtml(string content)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;
        if (!content.TrimStart().StartsWith('<')) return content;
        var text = Regex.Replace(content, "<[^>]+>", string.Empty);
        return System.Net.WebUtility.HtmlDecode(text);
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['’-][\p{L}\p{N}]+)*", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}

public sealed record ProjectStateDto(
    bool IsLoaded,
    string? ProjectName,
    string? ProjectPath,
    string? ActiveBookId,
    IReadOnlyList<BookDto> Books,
    IReadOnlyList<ChapterDto> Chapters);

public sealed record BookDto(string Id, string Name);

public sealed record ChapterDto(
    string Guid,
    string Title,
    int Order,
    string Status,
    string Act,
    bool IsFavorite,
    IReadOnlyList<SceneDto> Scenes);

public sealed record SceneDto(
    string Id,
    string Title,
    int Order,
    int WordCount,
    string? LabelColor,
    bool IsFavorite,
    string? Synopsis);

public sealed record RecentProjectDto(string Name, string Path);
