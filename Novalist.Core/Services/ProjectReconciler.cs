using System.Text.Json;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public enum ChapterChangeKind { New, Renamed, Deleted }
public enum SceneChangeKind { New, Moved, Renamed, Deleted }

/// <summary>A detected chapter-level difference between disk and cache.</summary>
public sealed record ChapterChange(ChapterChangeKind Kind, string Guid, string FolderName, string? OldFolderName = null);

/// <summary>A detected scene-level difference between disk and cache.</summary>
public sealed record SceneChange(
    SceneChangeKind Kind,
    string Id,
    string FileName,
    string ChapterGuid,
    string? FromChapterGuid = null,
    string? OldFileName = null);

/// <summary>An ambiguity the reconciler cannot resolve automatically (surfaced for review).</summary>
public sealed record ReconciliationConflict(string Kind, string Detail);

/// <summary>Read-only summary of what changed on disk relative to the cached manifest.</summary>
public sealed class ReconciliationReport
{
    public List<ChapterChange> Chapters { get; } = new();
    public List<SceneChange> Scenes { get; } = new();
    public List<ReconciliationConflict> Conflicts { get; } = new();

    public bool HasChanges => Chapters.Count > 0 || Scenes.Count > 0 || Conflicts.Count > 0;
}

/// <summary>
/// Scans a draft's on-disk tree and diffs it against the cached chapter/scene manifest,
/// producing a <see cref="ReconciliationReport"/>. <b>Read-only</b> — it never mutates the
/// project; applying the report is a separate concern. Identity is resolved by, in order:
/// embedded scene id (survives move + rename), content hash vs a missing-file entry
/// (recovers id-less moves), then treated as new.
/// </summary>
public sealed class ProjectReconciler
{
    private const string ArchiveFolderName = "__Archive";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IFileService _fileService;

    public ProjectReconciler(IFileService fileService) => _fileService = fileService;

    public async Task<ReconciliationReport> ScanAsync(
        string draftRoot,
        string chapterFolderName,
        IReadOnlyList<ChapterData> chapters,
        ScenesManifest manifest,
        DraftIndex index)
    {
        var report = new ReconciliationReport();
        var chapterByGuid = new Dictionary<string, ChapterData>(StringComparer.Ordinal);
        foreach (var c in chapters) chapterByGuid[c.Guid] = c;

        // Active scenes flattened with their cached chapter.
        var sceneById = new Dictionary<string, (SceneData scene, string chapterGuid)>(StringComparer.Ordinal);
        foreach (var (guid, list) in manifest.Chapters)
            foreach (var s in list)
                sceneById[s.Id] = (s, guid);

        var chaptersRoot = _fileService.CombinePath(draftRoot, chapterFolderName);
        var diskFolders = await EnumerateChapterFoldersAsync(chaptersRoot);

        var folderGuid = await ScanChapterFoldersAsync(chaptersRoot, diskFolders, chapters, chapterByGuid, report);

        // ── Scenes ──
        // Precompute manifest scenes whose file is missing, with their cached hash, for
        // id-less hash rematch (move recovery).
        var missingByHash = new Dictionary<string, (SceneData scene, string chapterGuid)>(StringComparer.Ordinal);
        foreach (var (id, (scene, cachedGuid)) in sceneById)
        {
            if (!chapterByGuid.TryGetValue(cachedGuid, out var ch)) continue;
            var abs = _fileService.CombinePath(chaptersRoot, ch.FolderName, scene.FileName);
            if (await _fileService.ExistsAsync(abs)) continue;
            var rel = $"{chapterFolderName}/{ch.FolderName}/{scene.FileName}";
            if (index.Entries.TryGetValue(rel, out var entry) && !string.IsNullOrEmpty(entry.Hash))
                missingByHash[entry.Hash] = (scene, cachedGuid);
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var idFirstSeenIn = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var folder in diskFolders)
        {
            var guid = folderGuid[folder];
            var folderPath = _fileService.CombinePath(chaptersRoot, folder);
            var files = await _fileService.GetFilesAsync(folderPath, "*.novalist", recursive: false);
            foreach (var file in files.OrderBy(f => f, StringComparer.Ordinal))
            {
                var fileName = _fileService.GetFileName(file);
                var content = await _fileService.ReadTextAsync(file);

                if (FileFrontMatter.TryParse(content, out var parsed))
                {
                    if (!idFirstSeenIn.TryAdd(parsed.Id, fileName))
                    {
                        report.Conflicts.Add(new ReconciliationConflict("DuplicateSceneId", parsed.Id));
                        continue;
                    }
                    ClassifyKnownId(report, sceneById, seenIds, parsed.Id, fileName, guid);
                }
                else
                {
                    var hash = ContentHasher.Hash(content);
                    if (missingByHash.TryGetValue(hash, out var m) && !seenIds.Contains(m.scene.Id))
                    {
                        seenIds.Add(m.scene.Id);
                        report.Scenes.Add(new SceneChange(SceneChangeKind.Moved, m.scene.Id, fileName, guid ?? string.Empty, m.chapterGuid, m.scene.FileName));
                    }
                    else
                    {
                        report.Scenes.Add(new SceneChange(SceneChangeKind.New, string.Empty, fileName, guid ?? string.Empty));
                    }
                }
            }
        }

        // Deleted: active scenes never seen on disk and whose expected file is gone.
        foreach (var (id, (scene, cachedGuid)) in sceneById)
        {
            if (seenIds.Contains(id)) continue;
            if (!chapterByGuid.TryGetValue(cachedGuid, out var ch)) continue;
            var abs = _fileService.CombinePath(chaptersRoot, ch.FolderName, scene.FileName);
            if (!await _fileService.ExistsAsync(abs))
                report.Scenes.Add(new SceneChange(SceneChangeKind.Deleted, id, scene.FileName, cachedGuid));
        }

        return report;
    }

    private static void ClassifyKnownId(
        ReconciliationReport report,
        Dictionary<string, (SceneData scene, string chapterGuid)> sceneById,
        HashSet<string> seenIds,
        string id,
        string fileName,
        string? folderGuid)
    {
        if (!sceneById.TryGetValue(id, out var cached))
        {
            // Embedded id with no manifest entry: a scene created externally with an id.
            report.Scenes.Add(new SceneChange(SceneChangeKind.New, id, fileName, folderGuid ?? string.Empty));
            return;
        }

        seenIds.Add(id);
        if (folderGuid != null && !string.Equals(cached.chapterGuid, folderGuid, StringComparison.Ordinal))
            report.Scenes.Add(new SceneChange(SceneChangeKind.Moved, id, fileName, folderGuid, cached.chapterGuid, cached.scene.FileName));
        else if (!string.Equals(cached.scene.FileName, fileName, StringComparison.Ordinal))
            report.Scenes.Add(new SceneChange(SceneChangeKind.Renamed, id, fileName, folderGuid ?? cached.chapterGuid, null, cached.scene.FileName));
    }

    /// <summary>
    /// Makes the in-memory <paramref name="chapters"/> + <paramref name="manifest"/> match the
    /// on-disk tree, carrying scene/chapter metadata across by id. New / id-less / duplicate
    /// files get a minted id and are stamped; survivors keep their order; deleted folders and
    /// files drop out. Rewrites <c>.nvindex.json</c>. Chapter markers + draft.json / scenes.json
    /// are persisted by the caller. This is the disk-authoritative rebuild — the report from
    /// <see cref="ScanAsync"/> is for surfacing/vetoing the change, not for driving the mutation.
    /// </summary>
    public async Task ApplyAsync(
        string draftRoot,
        string chapterFolderName,
        List<ChapterData> chapters,
        ScenesManifest manifest,
        DraftIndex index)
    {
        var chaptersRoot = _fileService.CombinePath(draftRoot, chapterFolderName);

        // Snapshot old state before mutating: scenes by id (metadata carry-over) and the
        // hashes of scenes whose file is now missing (id-less move reattach).
        var oldById = new Dictionary<string, SceneData>(StringComparer.Ordinal);
        foreach (var list in manifest.Chapters.Values)
            foreach (var s in list)
                oldById[s.Id] = s;
        var missingByHash = await BuildMissingByHashAsync(chaptersRoot, chapterFolderName, chapters, manifest, index);

        var oldChapterByGuid = new Dictionary<string, ChapterData>(StringComparer.Ordinal);
        foreach (var c in chapters) oldChapterByGuid[c.Guid] = c;
        var nextChapterOrder = chapters.Count > 0 ? chapters.Max(c => c.Order) + 1 : 1;

        var diskFolders = await EnumerateChapterFoldersAsync(chaptersRoot);
        var rebuiltChapters = new List<ChapterData>();
        var rebuiltManifest = new Dictionary<string, List<SceneData>>(StringComparer.Ordinal);
        var usedIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var folder in diskFolders)
        {
            var marker = await ReadMarkerAsync(chaptersRoot, folder);
            ChapterData chapter;
            if (marker != null && oldChapterByGuid.TryGetValue(marker.Guid, out var existingByGuid))
            {
                chapter = existingByGuid;       // keep metadata + order; honour the (possibly new) folder name
                chapter.FolderName = folder;
            }
            else if (marker != null)
            {
                chapter = marker.ToChapter(folder);   // new chapter created externally with a marker
                chapter.Order = nextChapterOrder++;
            }
            else if (chapters.FirstOrDefault(c => c.FolderName == folder) is { } existingByName)
            {
                chapter = existingByName;        // pre-migration folder, no marker yet
            }
            else
            {
                chapter = new ChapterData { FolderName = folder, Title = DeriveTitle(folder), Order = nextChapterOrder++ };
            }

            rebuiltChapters.Add(chapter);
            rebuiltManifest[chapter.Guid] = await RebuildSceneListAsync(chaptersRoot, folder, chapter.Guid, oldById, missingByHash, usedIds);
        }

        chapters.Clear();
        chapters.AddRange(rebuiltChapters);
        manifest.Chapters = rebuiltManifest;

        await WriteIndexAsync(draftRoot, chaptersRoot, chapterFolderName, rebuiltChapters, manifest);
    }

    private async Task<List<SceneData>> RebuildSceneListAsync(
        string chaptersRoot,
        string folder,
        string chapterGuid,
        Dictionary<string, SceneData> oldById,
        Dictionary<string, (SceneData scene, string chapterGuid)> missingByHash,
        HashSet<string> usedIds)
    {
        var folderPath = _fileService.CombinePath(chaptersRoot, folder);
        var files = await _fileService.GetFilesAsync(folderPath, "*.novalist", recursive: false);
        var list = new List<SceneData>();

        foreach (var file in files.OrderBy(f => f, StringComparer.Ordinal))
        {
            var content = await _fileService.ReadTextAsync(file);
            SceneData scene;

            var hasUsableId = FileFrontMatter.TryParse(content, out var parsed) && !usedIds.Contains(parsed.Id);
            if (hasUsableId && oldById.TryGetValue(parsed.Id, out var carried))
            {
                scene = carried;   // known scene — preserve all metadata
            }
            else if (hasUsableId)
            {
                scene = new SceneData { Id = parsed.Id };   // externally created with its own id
            }
            else
            {
                // No (usable) id: try to recover a moved id-less file by content hash, else mint.
                var hash = ContentHasher.Hash(content);
                scene = missingByHash.TryGetValue(hash, out var m) && !usedIds.Contains(m.scene.Id)
                    ? m.scene
                    : new SceneData { Id = Guid.NewGuid().ToString() };
                // Force the canonical id onto disk: strip any existing (id-less or duplicate)
                // front-matter first, since Stamp() is a no-op when a marker is already present.
                await _fileService.WriteTextAsync(file, FileFrontMatter.Stamp(FileFrontMatter.Strip(content), scene.Id));
            }

            usedIds.Add(scene.Id);
            scene.ChapterGuid = chapterGuid;
            scene.FileName = _fileService.GetFileName(file);
            scene.Order = list.Count + 1;
            list.Add(scene);
        }

        return list;
    }

    private async Task<Dictionary<string, (SceneData scene, string chapterGuid)>> BuildMissingByHashAsync(
        string chaptersRoot, string chapterFolderName, IReadOnlyList<ChapterData> chapters, ScenesManifest manifest, DraftIndex index)
    {
        var chapterByGuid = new Dictionary<string, ChapterData>(StringComparer.Ordinal);
        foreach (var c in chapters) chapterByGuid[c.Guid] = c;

        var map = new Dictionary<string, (SceneData, string)>(StringComparer.Ordinal);
        foreach (var (guid, list) in manifest.Chapters)
        {
            if (!chapterByGuid.TryGetValue(guid, out var ch)) continue;
            foreach (var scene in list)
            {
                var abs = _fileService.CombinePath(chaptersRoot, ch.FolderName, scene.FileName);
                if (await _fileService.ExistsAsync(abs)) continue;
                var rel = $"{chapterFolderName}/{ch.FolderName}/{scene.FileName}";
                if (index.Entries.TryGetValue(rel, out var entry) && !string.IsNullOrEmpty(entry.Hash))
                    map[entry.Hash] = (scene, guid);
            }
        }
        return map;
    }

    private async Task WriteIndexAsync(
        string draftRoot, string chaptersRoot, string chapterFolderName, IReadOnlyList<ChapterData> chapters, ScenesManifest manifest)
    {
        var index = new DraftIndex();
        foreach (var chapter in chapters)
            foreach (var scene in manifest.Chapters[chapter.Guid])
                await IndexFileAsync(index, _fileService.CombinePath(chaptersRoot, chapter.FolderName, scene.FileName),
                    $"{chapterFolderName}/{chapter.FolderName}/{scene.FileName}", scene.Id);

        foreach (var scene in manifest.Archived)
            await IndexFileAsync(index, _fileService.CombinePath(chaptersRoot, ArchiveFolderName, scene.FileName),
                $"{chapterFolderName}/{ArchiveFolderName}/{scene.FileName}", scene.Id);

        await _fileService.WriteTextAsync(_fileService.CombinePath(draftRoot, ".nvindex.json"),
            JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task IndexFileAsync(DraftIndex index, string abs, string rel, string id)
    {
        if (!await _fileService.ExistsAsync(abs)) return;
        var content = await _fileService.ReadTextAsync(abs);
        index.Entries[rel] = new DraftIndexEntry
        {
            Id = id,
            Hash = ContentHasher.Hash(content),
            Size = await _fileService.GetFileSizeAsync(abs),
            MtimeUtc = await _fileService.GetLastWriteTimeUtcAsync(abs),
        };
    }

    private static string DeriveTitle(string folderName)
        => System.Text.RegularExpressions.Regex.Replace(folderName, @"^\d+\s*-\s*", string.Empty).Trim() is { Length: > 0 } t
            ? t
            : folderName;

    private async Task<List<string>> EnumerateChapterFoldersAsync(string chaptersRoot)
    {
        var dirs = await _fileService.GetDirectoriesAsync(chaptersRoot);
        return dirs
            .Select(_fileService.GetFileName)
            .Where(name => !string.Equals(name, ArchiveFolderName, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<ChapterMarker?> ReadMarkerAsync(string chaptersRoot, string folder)
    {
        var path = _fileService.CombinePath(chaptersRoot, folder, ChapterMarker.FileName);
        if (!await _fileService.ExistsAsync(path)) return null;
        var json = await _fileService.ReadTextAsync(path);
        return JsonSerializer.Deserialize<ChapterMarker>(json, JsonOptions);
    }

    private async Task<Dictionary<string, string?>> ScanChapterFoldersAsync(string chaptersRoot, IReadOnlyList<string> diskFolders, IReadOnlyList<ChapterData> chapters, Dictionary<string, ChapterData> chapterByGuid, ReconciliationReport report)
    {
        // ── Chapters ──
        var matchedGuids = new HashSet<string>(StringComparer.Ordinal);
        // Effective guid per disk folder (marker guid, matched cached guid, or null = brand new).
        var folderGuid = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var folder in diskFolders)
        {
            var marker = await ReadMarkerAsync(chaptersRoot, folder);
            if (marker != null && chapterByGuid.TryGetValue(marker.Guid, out var cachedByGuid))
            {
                matchedGuids.Add(marker.Guid);
                folderGuid[folder] = marker.Guid;
                if (!string.Equals(cachedByGuid.FolderName, folder, StringComparison.Ordinal))
                    report.Chapters.Add(new ChapterChange(ChapterChangeKind.Renamed, marker.Guid, folder, cachedByGuid.FolderName));
            }
            else if (marker == null && chapters.FirstOrDefault(c => c.FolderName == folder) is { } cachedByName)
            {
                // Existing chapter with no marker yet (pre-migration leftover) — matched, no change.
                matchedGuids.Add(cachedByName.Guid);
                folderGuid[folder] = cachedByName.Guid;
            }
            else
            {
                // New chapter: a marker with an unknown guid, or no marker and no name match.
                var newGuid = marker?.Guid ?? string.Empty;
                folderGuid[folder] = marker?.Guid;
                report.Chapters.Add(new ChapterChange(ChapterChangeKind.New, newGuid, folder));
            }
        }

        foreach (var c in chapters)
            if (!matchedGuids.Contains(c.Guid))
                report.Chapters.Add(new ChapterChange(ChapterChangeKind.Deleted, c.Guid, c.FolderName));

        return folderGuid;
    }
}
