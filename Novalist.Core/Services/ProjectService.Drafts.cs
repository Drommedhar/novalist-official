using System.Text.Json;
using System.Text.RegularExpressions;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public partial class ProjectService
{
    // ── Draft management ────────────────────────────────────────────

    public async Task<BookDraftMetadata> CreateDraftAsync(string draftName, string? cloneFromDraftId = null)
    {
        var bookRoot = ActiveBookRoot;
        if (ActiveBook == null || bookRoot == null)
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

    public async Task SetDraftNotesAsync(string draftId, string? notes)
    {
        if (ActiveBook == null) return;
        var draft = ActiveBook.Drafts.FirstOrDefault(d => d.Id == draftId);
        if (draft == null) return;
        var trimmed = notes?.Trim();
        draft.Notes = string.IsNullOrEmpty(trimmed) ? null : trimmed;
        await SaveProjectAsync();
    }

    /// <summary>
    /// The list itself is the order - project.json stores the drafts as an
    /// array, so there is no second place for the order to disagree with.
    /// </summary>
    public async Task ReorderDraftsAsync(IReadOnlyList<string> orderedDraftIds)
    {
        if (ActiveBook == null) return;

        var byId = ActiveBook.Drafts.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        var reordered = new List<BookDraftMetadata>();
        foreach (var id in orderedDraftIds)
        {
            if (byId.TryGetValue(id, out var draft) && !reordered.Contains(draft))
                reordered.Add(draft);
        }

        // Anything the caller did not name keeps its place at the end rather
        // than disappearing: a list built from a stale view of the drafts must
        // not be able to delete one.
        foreach (var draft in ActiveBook.Drafts)
            if (!reordered.Contains(draft))
                reordered.Add(draft);

        ActiveBook.Drafts = reordered;
        await SaveProjectAsync();
    }

    public Task FlushActiveDraftAsync() => SaveActiveDraftAndScenesAsync();

    public async Task ReloadActiveDraftAsync()
    {
        if (ActiveBook == null) return;
        ActiveBook.Chapters = new List<ChapterData>();
        ActiveBook.Acts = new List<ActData>();
        await LoadActiveDraftDataAsync();
        await LoadScenesManifestAsync();
    }

    private async Task SaveActiveDraftAndScenesAsync()
    {
        await SaveActiveDraftDataAsync();
        await SaveScenesAsync();
    }

    public async Task DeleteDraftAsync(string draftId)
    {
        var bookRoot = ActiveBookRoot;
        if (ActiveBook == null || bookRoot == null) return;
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
}
