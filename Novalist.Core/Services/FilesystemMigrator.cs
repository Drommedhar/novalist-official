using System.Text.Json;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>Progress tick reported while migrating a project to the filesystem-source-of-truth model.</summary>
public sealed record FilesystemMigrationProgress(int DraftsProcessed, int TotalDrafts, int ScenesStamped);

/// <summary>Summary of what a v2 to v3 migration changed.</summary>
public sealed record FilesystemMigrationResult(int DraftsProcessed, int ChaptersMarked, int ScenesStamped);

/// <summary>
/// Converts a pre-v3 project to the filesystem-source-of-truth layout: stamps every
/// scene file with its <see cref="SceneData.Id"/> front-matter, writes a
/// <c>.nvchapter.json</c> marker into each chapter folder, splits acts into
/// <c>acts.json</c>, and builds the per-draft <c>.nvindex.json</c> fingerprint cache.
///
/// Works off explicit on-disk paths (reading each draft's <c>draft.json</c> /
/// <c>scenes.json</c>), so it migrates <b>every</b> draft of <b>every</b> book without
/// disturbing the active-draft in-memory state. Idempotent: re-running skips files that
/// already carry front-matter and chapter folders that already have a marker, and never
/// prunes a manifest entry whose file is missing (pruning is the reconciler's job).
/// </summary>
public sealed class FilesystemMigrator
{
    /// <summary>Schema version a migrated project is stamped with.</summary>
    public const int FilesystemVersion = 3;

    private const string ArchiveFolderName = "__Archive";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IFileService _fileService;

    public FilesystemMigrator(IFileService fileService) => _fileService = fileService;

    /// <summary>
    /// True if <paramref name="project"/> needs migration: either its schema version is
    /// pre-v3, or any draft is missing its <c>.nvindex.json</c> (a partial / interrupted
    /// migration). Cheap probe used to gate the (idempotent) migration on load.
    /// </summary>
    public async Task<bool> NeedsMigrationAsync(ProjectMetadata project, string projectRoot)
    {
        if (project.Version < FilesystemVersion) return true;
        foreach (var book in project.Books)
        {
            var bookRoot = _fileService.CombinePath(projectRoot, book.FolderName);
            foreach (var draft in book.Drafts)
            {
                var indexPath = _fileService.CombinePath(bookRoot, "Drafts", draft.FolderName, ".nvindex.json");
                if (!await _fileService.ExistsAsync(indexPath)) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Migrates every draft of every book and stamps <paramref name="project"/>.Version to
    /// <see cref="FilesystemVersion"/> in memory. Persisting <c>project.json</c> is the
    /// caller's responsibility (it owns the multi-draft serialization rules).
    /// </summary>
    public async Task<FilesystemMigrationResult> MigrateAsync(
        ProjectMetadata project,
        string projectRoot,
        IProgress<FilesystemMigrationProgress>? progress = null)
    {
        var total = project.Books.Sum(b => b.Drafts.Count);
        int draftsDone = 0, chaptersMarked = 0, scenesStamped = 0;

        foreach (var book in project.Books)
        {
            var bookRoot = _fileService.CombinePath(projectRoot, book.FolderName);
            foreach (var draft in book.Drafts)
            {
                var (chapters, scenes) = await MigrateDraftAsync(bookRoot, draft.FolderName, book.ChapterFolder);
                chaptersMarked += chapters;
                scenesStamped += scenes;
                draftsDone++;
                progress?.Report(new FilesystemMigrationProgress(draftsDone, total, scenesStamped));
            }
        }

        project.Version = FilesystemVersion;
        return new FilesystemMigrationResult(draftsDone, chaptersMarked, scenesStamped);
    }

    private async Task<(int chapters, int scenes)> MigrateDraftAsync(string bookRoot, string draftFolder, string chapterFolderName)
    {
        var draftRoot = _fileService.CombinePath(bookRoot, "Drafts", draftFolder);
        var draftData = await ReadJsonAsync<BookDraftData>(_fileService.CombinePath(draftRoot, "draft.json")) ?? new BookDraftData();
        var manifest = await ReadJsonAsync<ScenesManifest>(_fileService.CombinePath(draftRoot, "scenes.json")) ?? new ScenesManifest();

        int chapters = 0, scenes = 0;

        // Chapter markers: ensure each chapter folder exists and carries .nvchapter.json.
        foreach (var chapter in draftData.Chapters)
        {
            var folder = _fileService.CombinePath(draftRoot, chapterFolderName, chapter.FolderName);
            await _fileService.CreateDirectoryAsync(folder);
            var markerPath = _fileService.CombinePath(folder, ChapterMarker.FileName);
            if (!await _fileService.ExistsAsync(markerPath))
            {
                await WriteJsonAsync(markerPath, ChapterMarker.FromChapter(chapter));
                chapters++;
            }
        }

        var folderByGuid = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var chapter in draftData.Chapters)
            folderByGuid[chapter.Guid] = chapter.FolderName;

        var index = new DraftIndex();

        // Active scenes — locate via their chapter's folder.
        foreach (var (guid, list) in manifest.Chapters)
        {
            if (!folderByGuid.TryGetValue(guid, out var folder)) continue;
            foreach (var scene in list)
            {
                var rel = $"{chapterFolderName}/{folder}/{scene.FileName}";
                var abs = _fileService.CombinePath(draftRoot, chapterFolderName, folder, scene.FileName);
                if (await StampAndIndexAsync(abs, rel, scene.Id, index)) scenes++;
            }
        }

        // Archived scenes live under Chapters/__Archive/.
        foreach (var scene in manifest.Archived)
        {
            var rel = $"{chapterFolderName}/{ArchiveFolderName}/{scene.FileName}";
            var abs = _fileService.CombinePath(draftRoot, chapterFolderName, ArchiveFolderName, scene.FileName);
            if (await StampAndIndexAsync(abs, rel, scene.Id, index)) scenes++;
        }

        await WriteJsonAsync(_fileService.CombinePath(draftRoot, ".nvindex.json"), index);
        var actsPath = _fileService.CombinePath(draftRoot, "acts.json");
        if (!await _fileService.ExistsAsync(actsPath)) await WriteJsonAsync(actsPath, draftData.Acts);
        return (chapters, scenes);
    }

    /// <summary>
    /// Stamps the file with front-matter (if absent) and records its index entry. A file
    /// that is missing on disk is skipped — migration never deletes a manifest scene.
    /// Returns true only when the file was newly stamped.
    /// </summary>
    private async Task<bool> StampAndIndexAsync(string absPath, string relKey, string id, DraftIndex index)
    {
        if (!await _fileService.ExistsAsync(absPath)) return false;

        var content = await _fileService.ReadTextAsync(absPath);
        var stampedNow = false;
        if (!FileFrontMatter.HasFrontMatter(content))
        {
            content = FileFrontMatter.Stamp(content, id);
            await _fileService.WriteTextAsync(absPath, content);
            stampedNow = true;
        }

        // Content is guaranteed to carry front-matter now (pre-existing or just stamped),
        // so the parse always succeeds and yields the canonical id.
        FileFrontMatter.TryParse(content, out var parsed);
        index.Entries[relKey] = new DraftIndexEntry
        {
            Id = parsed.Id,
            Hash = ContentHasher.Hash(content),
            Size = await _fileService.GetFileSizeAsync(absPath),
            MtimeUtc = await _fileService.GetLastWriteTimeUtcAsync(absPath),
        };
        return stampedNow;
    }

    private async Task<T?> ReadJsonAsync<T>(string path) where T : class
    {
        if (!await _fileService.ExistsAsync(path)) return null;
        var json = await _fileService.ReadTextAsync(path);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private Task WriteJsonAsync<T>(string path, T value)
        => _fileService.WriteTextAsync(path, JsonSerializer.Serialize(value, JsonOptions));
}
