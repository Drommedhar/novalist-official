using System.Text.Json;
using System.Text.RegularExpressions;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public partial class ProjectService
{
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
        try
        {
            var json = JsonSerializer.Serialize(CurrentProject, JsonOptions);
            await _fileService.WriteTextAsync(metadataPath, json);
        }
        finally
        {
            // A failed write must restore the live draft as well.
            foreach (var (book, chs, acts) in snapshot)
            {
                book.Chapters = chs;
                book.Acts = acts;
            }
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
            ProjectSettings.Overrides ??= new SettingsOverrides();
            ProjectSettings.Overrides.ApplyCompatibilityDefaults();
        }
        else
        {
            ProjectSettings = new ProjectSettings();
        }
    }
}
