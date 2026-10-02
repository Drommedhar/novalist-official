using System.Net;
using System.Security.Cryptography;
using System.Text;
using Markdig;
using Novalist.Core.Models;
using Novalist.Core.Utilities;

namespace Novalist.Core.Services;

public sealed record ImportFolder(string Path, int Files, int Total);
public sealed record FolderImportIssue(string Path, string Kind);
public sealed record FolderImportProgress(
    int Total, int Processed, int Imported, int Skipped, int Failed, bool Done,
    FolderImportIssue[] Issues);

/// <summary>
/// A folder import keeps only file paths during discovery and reads one body at
/// a time. Folder rules inherit down the tree; callers advance bounded batches
/// so the workspace queue and the interface stay available between requests.
/// </summary>
public sealed class FolderImportService(IProjectService projects, IFileService files)
{
    private static readonly MarkdownPipeline MarkdownPipeline =
        new MarkdownPipelineBuilder().DisableHtml().UseEmphasisExtras().Build();
    private readonly EntityService _entities = new(projects);
    private readonly List<string> _paths = [];
    private readonly List<FolderImportIssue> _issues = [];
    private readonly Dictionary<string, ChapterData> _chapters = [];
    private readonly Dictionary<string, int> _orders = [];
    private readonly Dictionary<string, string> _imagePaths = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private HashSet<string> _existing = [];
    private Dictionary<string, string> _rules = [];
    private string _root = string.Empty;
    private string? _bookRoot;
    private string? _draftRoot;
    private string _sectionTitle = string.Empty;
    private string _tag = string.Empty;
    private string[]? _targets;
    private FolderImportParser? _parser;
    private bool _projectDirty, _scenesDirty;
    private bool _structured;
    private int _processed, _imported, _skipped, _failed, _nextChapterOrder;

    public IReadOnlyList<ImportFolder> Folders { get; private set; } = [];
    public int Total => _paths.Count;
    public int Unsupported { get; private set; }

    public void Scan(string root)
    {
        if (!projects.IsProjectLoaded) throw new InvalidOperationException("Open a project before importing.");
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The selected folder is unavailable.");
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        _bookRoot = projects.ActiveBookRoot;
        _draftRoot = projects.ActiveDraftRoot;
        var counts = new Dictionary<string, (int Files, int Total)>(StringComparer.Ordinal);
        counts[string.Empty] = (0, 0);
        var pending = new Stack<string>();
        pending.Push(_root);
        var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", options))
            {
                var relative = Path.GetRelativePath(_root, entry.FullName).Replace('\\', '/');
                if (MarkdownVaultImport.IsSkipped(relative)) continue;
                if (entry is DirectoryInfo)
                {
                    pending.Push(entry.FullName);
                    continue;
                }
                if (relative.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase)) continue;
                if (!IsMarkdown(relative) && !Path.GetExtension(relative).Equals(".txt", StringComparison.OrdinalIgnoreCase)
                    && !Path.GetExtension(relative).Equals(".json", StringComparison.OrdinalIgnoreCase))
                {
                    Unsupported++;
                    continue;
                }
                _paths.Add(relative);
                var folder = Parent(relative);
                var direct = counts.GetValueOrDefault(folder);
                counts[folder] = (direct.Files + 1, direct.Total);
                while (true)
                {
                    var count = counts.GetValueOrDefault(folder);
                    counts[folder] = (count.Files, count.Total + 1);
                    if (folder.Length == 0) break;
                    folder = Parent(folder);
                }
            }
        }
        _paths.Sort(StringComparer.Ordinal);
        Folders = [.. counts.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ImportFolder(pair.Key, pair.Value.Files, pair.Value.Total))];
    }

    public FolderImportProgress Configure(
        string defaultTarget, Dictionary<string, string> overrides, string sectionTitle, string? tag = null)
    {
        EnsureScope();
        if (_targets != null) throw new InvalidOperationException("This import has already started.");
        var knownFolders = Folders.Select(folder => folder.Path).ToHashSet(StringComparer.Ordinal);
        var targets = _entities.GetCustomEntityTypes().Select(type => type.TypeKey)
            .Concat(["skip", "scene", "research", "character", "location", "item", "lore"])
            .ToHashSet(StringComparer.Ordinal);
        if (!targets.Contains(defaultTarget) || overrides.Any(pair =>
                !knownFolders.Contains(pair.Key) || !targets.Contains(pair.Value)))
            throw new ArgumentException("Choose valid folders and import targets.");
        _rules = new Dictionary<string, string>(overrides, StringComparer.Ordinal) { [string.Empty] = defaultTarget };
        _targets = [.. _paths.Select(path => ResolveTarget(Parent(path), _rules))];
        _sectionTitle = sectionTitle;
        _parser = new FolderImportParser(new FolderImportSchema(projects.ActiveBook, _entities.GetCustomEntityTypes()));
        _tag = (tag ?? string.Empty).Trim();
        PrepareWriting();
        return Progress();
    }

    internal void ConfigureStructured(string sectionTitle)
    {
        if (!projects.IsProjectLoaded) throw new InvalidOperationException("Open a project before importing.");
        _bookRoot = projects.ActiveBookRoot;
        _draftRoot = projects.ActiveDraftRoot;
        _root = "External import";
        _structured = true;
        _sectionTitle = sectionTitle;
        PrepareWriting();
    }

    private void PrepareWriting()
    {
        _existing = projects.ScenesManifest!.Chapters.Values.SelectMany(scenes => scenes)
            .Select(scene => scene.Id).Concat(projects.ScenesManifest.Archived.Select(scene => scene.Id))
            .Concat(projects.CurrentProject!.ResearchItems.Select(item => item.Id)).ToHashSet(StringComparer.Ordinal);
        foreach (var chapter in projects.ActiveBook!.Chapters)
        {
            _chapters[chapter.Guid] = chapter;
            _orders[chapter.Guid] = projects.GetScenesForChapter(chapter.Guid).Select(scene => scene.Order).DefaultIfEmpty().Max();
        }
        _nextChapterOrder = projects.ActiveBook.Chapters.Select(chapter => chapter.Order).DefaultIfEmpty().Max();
    }

    public async Task<FolderImportProgress> ImportBatchAsync(int batchSize = 40)
    {
        EnsureScope();
        if (_targets == null) throw new InvalidOperationException("Choose import targets first.");
        var end = Math.Min(Total, _processed + Math.Clamp(batchSize, 1, 100));
        for (; _processed < end; _processed++)
        {
            var relative = _paths[_processed];
            var target = _targets[_processed];
            var id = Identity(target, relative);
            if (target == "skip" || _existing.Contains(id) || await EntityExistsAsync(target, id))
            {
                _skipped++;
                continue;
            }
            string text;
            try
            {
                text = await files.ReadTextAsync(Path.Combine(_root, relative));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Fail(relative, "read");
                continue;
            }
            ParsedFolderImport document;
            try { document = _parser!.Parse(relative, text, target); }
            catch (FormatException)
            {
                Fail(relative, "parse");
                continue;
            }
            try
            {
                await WriteDocumentAsync(id, relative, target, document, Path.Combine(_root, relative), Parent(relative));
                _existing.Add(id);
                _imported++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Fail(relative, "write");
            }
        }
        await SavePendingAsync();
        return Progress();
    }

    internal async Task<StructuredImportResult> ImportStructuredAsync(StructuredImportEntry entry, ParsedFolderImport document)
    {
        EnsureScope();
        var target = document.Definition.Target;
        var id = Identity(target, entry.SourceId);
        if (_existing.Contains(id) || await EntityExistsAsync(target, id))
            return new(entry.SourceId, target, id, "skipped", null);
        await WriteDocumentAsync(id, entry.SourceId, target, document, entry.SourceId, entry.Folder ?? string.Empty);
        _existing.Add(id);
        return new(entry.SourceId, target, id, "imported", null);
    }

    internal void RefreshStructuredState()
    {
        EnsureScope();
        // The editor can change scene/chapter order between API batches.
        _chapters.Clear();
        _orders.Clear();
        PrepareWriting();
    }

    private async Task WriteDocumentAsync(string id, string relative, string target, ParsedFolderImport document, string origin, string folder)
    {
        if (!_structured) await ImportImagesAsync(document.Images, origin);
        document.Images.AddRange(await new ImportImageStore(projects, files).ResolveAsync(document.ImageReferences));
        var note = new VaultNote { RelativePath = relative, Title = document.Title, Body = document.Content };
        var tags = document.Tags.Concat(folder.Split('/', StringSplitOptions.RemoveEmptyEntries))
            .Concat(_tag.Length > 0 ? new[] { _tag } : [])
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var source = new Dictionary<string, string> { ["importedFrom"] = origin };
        if (target is "scene" or "research" && document.Metadata.Length > 0) source["importedMetadata"] = document.Metadata;
        switch (target)
        {
            case "scene":
                _projectDirty |= await ImportSceneAsync(id, note, tags, source, document, folder);
                _scenesDirty = true;
                break;
            case "research":
                var research = new ResearchItem
                {
                    Id = id, Title = note.Title, Content = note.Body, Tags = tags, Properties = source,
                    Order = projects.CurrentProject!.ResearchItems.Count, Type = ResearchItemType.Note
                };
                document.Apply(research, _sectionTitle);
                projects.CurrentProject.ResearchItems.Add(research);
                _projectDirty = true;
                break;
            case "character":
                var character = new CharacterData
                {
                    Id = id, Name = note.Title, Tags = tags, CustomProperties = source
                };
                document.Apply(character, _sectionTitle);
                await _entities.SaveCharacterAsync(character);
                break;
            case "location":
                var location = new LocationData
                    { Id = id, Name = note.Title, Description = note.Body, Tags = tags, CustomProperties = source };
                document.Apply(location, _sectionTitle);
                await _entities.SaveLocationAsync(location);
                break;
            case "item":
                var item = new ItemData
                    { Id = id, Name = note.Title, Description = note.Body, Tags = tags, CustomProperties = source };
                document.Apply(item, _sectionTitle);
                await _entities.SaveItemAsync(item);
                break;
            case "lore":
                var lore = new LoreData
                    { Id = id, Name = note.Title, Description = note.Body, Tags = tags, CustomProperties = source };
                document.Apply(lore, _sectionTitle);
                await _entities.SaveLoreAsync(lore);
                break;
            default:
                var custom = new CustomEntityData
                {
                    Id = id, EntityTypeKey = target, Name = note.Title, Tags = tags, CustomProperties = source
                };
                document.Apply(custom, _sectionTitle);
                await _entities.SaveCustomEntityAsync(custom);
                break;
        }
    }

    private async Task ImportImagesAsync(List<EntityImage> images, string sourceFile)
    {
        foreach (var image in images.ToArray())
        {
            var path = image.Path.Replace('\\', '/');
            // Embedded links are local image references, not arbitrary paths or URLs.
            if (Path.IsPathRooted(path) || path.Contains(':') || path.Split('/').Contains("..")
                || Path.GetExtension(path).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".svg" or ".webp"))
            {
                images.Remove(image);
                continue;
            }
            string? source = null;
            var boundary = Path.GetDirectoryName(_root) ?? _root;
            for (var directory = Path.GetDirectoryName(Path.GetFullPath(sourceFile)); directory != null; directory = Path.GetDirectoryName(directory))
            {
                var candidate = Path.Combine(directory, path);
                if (await files.ExistsAsync(candidate)) { source = candidate; break; }
                if (directory.Equals(boundary, StringComparison.OrdinalIgnoreCase)) break;
            }
            if (source == null)
            {
                // A relocated export can still reference an image already in this book.
                if (await files.ExistsAsync(Path.Combine(_bookRoot!, path))) image.Path = path;
                else images.Remove(image);
                continue;
            }
            if (!_imagePaths.TryGetValue(source, out var imported))
            {
                imported = Path.Combine(projects.ActiveBook!.ImageFolder, "Imported", Identity("image", source) + Path.GetExtension(source).ToLowerInvariant()).Replace('\\', '/');
                var destination = Path.Combine(_bookRoot!, imported);
                if (!await files.ExistsAsync(destination)) await files.WriteBytesAsync(destination, await files.ReadBytesAsync(source));
                _imagePaths[source] = imported;
            }
            image.Path = imported;
        }
    }

    internal async Task SavePendingAsync()
    {
        // Whole manifests are saved once per batch, rather than once per file.
        if (_projectDirty)
        {
            await projects.SaveProjectAsync();
            _projectDirty = false;
        }
        if (_scenesDirty)
        {
            await projects.SaveScenesAsync();
            _scenesDirty = false;
        }
    }

    public static string ResolveTarget(string folder, IReadOnlyDictionary<string, string> rules)
    {
        while (!rules.ContainsKey(folder) && folder.Length > 0) folder = Parent(folder);
        return rules.GetValueOrDefault(folder, "skip");
    }

    private async Task<bool> ImportSceneAsync(string id, VaultNote note, List<string> tags, Dictionary<string, string> source, ParsedFolderImport document, string folder)
    {
        var chapterId = Identity("chapter", folder);
        var isNew = !_chapters.TryGetValue(chapterId, out var chapter);
        if (isNew)
        {
            var title = folder.Length > 0 ? folder : Path.GetFileName(_root);
            var safeTitle = new string(title.Where(character => !Path.GetInvalidFileNameChars().Contains(character)).ToArray());
            chapter = new ChapterData
            {
                Guid = chapterId, Title = title, Order = _nextChapterOrder + 1,
                FolderName = $"{_nextChapterOrder + 1:D2} - {safeTitle[..Math.Min(safeTitle.Length, 60)]} - {chapterId[..8]}"
            };
        }
        var html = IsMarkdown(note.RelativePath) || document.IsJson ? Markdown.ToHtml(note.Body, MarkdownPipeline)
            : "<p>" + WebUtility.HtmlEncode(note.Body).Replace("\r\n", "\n").Replace("\n", "<br>") + "</p>";
        var scene = new SceneData
        {
            Id = id, Title = note.Title, ChapterGuid = chapterId, Order = _orders.GetValueOrDefault(chapterId) + 1,
            FileName = $"{id}.novalist", WordCount = TextStatistics.Calculate(html, "en").WordCount,
            Properties = source, AnalysisOverrides = new SceneAnalysisOverrides { Tags = tags }
        };
        document.Apply(scene, _sectionTitle);
        await files.CreateDirectoryAsync(projects.GetChapterFolderPath(chapter!));
        await projects.WriteSceneContentAsync(chapter!, scene, html);
        if (isNew)
        {
            projects.ActiveBook!.Chapters.Add(chapter!);
            projects.ScenesManifest!.Chapters[chapterId] = [];
            _chapters[chapterId] = chapter!;
            _nextChapterOrder++;
        }
        projects.ScenesManifest!.Chapters[chapterId].Add(scene);
        _orders[chapterId] = scene.Order;
        return isNew;
    }

    private async Task<bool> EntityExistsAsync(string target, string id)
    {
        var book = projects.ActiveBook!;
        var project = projects.CurrentProject!;
        var folders = target switch
        {
            "character" => (book.CharacterFolder, project.CharacterFolder),
            "location" => (book.LocationFolder, project.LocationFolder),
            "item" => (book.ItemFolder, project.ItemFolder),
            "lore" => (book.LoreFolder, project.LoreFolder),
            _ => (_entities.GetCustomEntityTypes().FirstOrDefault(type => type.TypeKey == target)?.FolderName, (string?)null)
        };
        return folders.Item1 != null && (await files.ExistsAsync(Path.Combine(_bookRoot!, folders.Item1, $"{id}.json"))
            || projects.WorldBibleRoot != null && await files.ExistsAsync(Path.Combine(
                projects.WorldBibleRoot, folders.Item2 ?? folders.Item1, $"{id}.json")));
    }

    private string Identity(string target, string relative)
    {
        var path = _structured ? "api:" + relative : Path.Combine(_root, relative);
        if (!_structured && OperatingSystem.IsWindows()) path = path.ToUpperInvariant();
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(target + "\n" + path)).AsSpan(0, 16)).ToString();
    }

    internal void EnsureScope()
    {
        if (_bookRoot == null || _bookRoot != projects.ActiveBookRoot || _draftRoot != projects.ActiveDraftRoot)
            throw new InvalidOperationException("The open book or draft changed. Choose the folder again.");
    }

    private void Fail(string path, string kind)
    {
        _failed++;
        if (_issues.Count < 10) _issues.Add(new FolderImportIssue(path, kind));
    }

    private FolderImportProgress Progress() => new(Total, _processed, _imported, _skipped, _failed, _processed == Total, [.. _issues]);
    private static string Parent(string path) => path.Contains('/') ? path[..path.LastIndexOf('/')] : string.Empty;
    private static bool IsMarkdown(string path) => Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(path).Equals(".markdown", StringComparison.OrdinalIgnoreCase);
}
