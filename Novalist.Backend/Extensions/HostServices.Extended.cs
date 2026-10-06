using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;
using Novalist.Sdk.Services;

namespace Novalist.Backend.Extensions;

/// <summary>
/// The surfaces added so that work the audit placed outside core could actually
/// be written outside core: research items, review remarks and suggested edits,
/// scene metadata, story structure, a command bus, and structural editing.
///
/// Everything here is deliberately narrow. An extension gets the operation it
/// needs and not the project file: nothing hands out a live model it could
/// mutate behind the host's back, and nothing erases anything - the destructive
/// verbs are trash and archive, both of which the writer can undo.
/// </summary>
public sealed partial class HostServices
{

    // ── Structural editing (IExtensionProjectService) ──────────────

    async Task<bool> IExtensionProjectService.RenameChapterAsync(string chapterGuid, string title)
    {
        if (!ChapterExists(chapterGuid)) return false;
        await _projectService.RenameChapterAsync(chapterGuid, title ?? string.Empty);
        ProjectStructureChanged?.Invoke();
        return true;
    }

    async Task<bool> IExtensionProjectService.RenameSceneAsync(
        string chapterGuid, string sceneId, string title)
    {
        if (!SceneExists(chapterGuid, sceneId)) return false;
        await _projectService.RenameSceneAsync(chapterGuid, sceneId, title ?? string.Empty);
        ProjectStructureChanged?.Invoke();
        return true;
    }

    async Task<bool> IExtensionProjectService.MoveSceneAsync(
        string sceneId, string targetChapterGuid, int index)
    {
        if (!ChapterExists(targetChapterGuid)) return false;

        // The scene may be in any chapter, so it is found across the book
        // rather than in the target - moving between chapters is the case that
        // matters and would otherwise be the one that failed.
        var owner = _projectService.GetChaptersOrdered()
            .FirstOrDefault(c => _projectService.GetScenesForChapter(c.Guid)
                .Any(s => s.Id == sceneId));
        if (owner == null) return false;

        await ChangeWorkspaceAsync("project/moveScenes", () => _projectService.MoveScenesAsync([sceneId], targetChapterGuid, Math.Max(0, index)));
        ProjectStructureChanged?.Invoke();
        return true;
    }

    async Task<bool> IExtensionProjectService.MoveChapterAsync(string chapterGuid, int order)
    {
        if (!ChapterExists(chapterGuid)) return false;
        await _projectService.ReorderChapterAsync(chapterGuid, Math.Max(1, order));
        ProjectStructureChanged?.Invoke();
        return true;
    }

    async Task<bool> IExtensionProjectService.SetChapterActAsync(string chapterGuid, string act)
    {
        var chapter = _projectService.GetChaptersOrdered().FirstOrDefault(c => c.Guid == chapterGuid);
        if (chapter == null) return false;

        chapter.Act = (act ?? string.Empty).Trim();
        await _projectService.SaveProjectAsync();
        ProjectStructureChanged?.Invoke();
        return true;
    }

    async Task<bool> IExtensionProjectService.TrashChapterAsync(string chapterGuid)
    {
        if (!ChapterExists(chapterGuid)) return false;
        // The core delete is already a move to the trash, so an extension gets
        // the recoverable verb without a second implementation.
        await ChangeWorkspaceAsync("project/deleteChapter", () => _projectService.DeleteChapterAsync(chapterGuid));
        ProjectStructureChanged?.Invoke();
        return true;
    }

    async Task<bool> IExtensionProjectService.ArchiveSceneAsync(string chapterGuid, string sceneId)
    {
        if (!SceneExists(chapterGuid, sceneId)) return false;
        await ChangeWorkspaceAsync("sceneBulk/archive", () => _projectService.ArchiveSceneAsync(chapterGuid, sceneId));
        ProjectStructureChanged?.Invoke();
        return true;
    }

    private bool ChapterExists(string chapterGuid)
        => _projectService.GetChaptersOrdered().Any(c => c.Guid == chapterGuid);

    private bool SceneExists(string chapterGuid, string sceneId)
        => ChapterExists(chapterGuid)
            && _projectService.GetScenesForChapter(chapterGuid).Any(s => s.Id == sceneId);

    // ── Books and drafts (IExtensionProjectService) ────────────────

    async Task<string?> IExtensionProjectService.CreateProjectAsync(
        string parentDirectory, string projectName, string firstBookName)
    {
        if (string.IsNullOrWhiteSpace(parentDirectory) || string.IsNullOrWhiteSpace(projectName))
            return null;
        // No structure-changed signal: nothing the interface is showing moved.
        return await _projectService.CreateProjectDetachedAsync(
            parentDirectory, projectName, firstBookName ?? string.Empty);
    }

    IReadOnlyList<Sdk.Services.BookInfo> IExtensionProjectService.GetBooks()
        => [.. (_projectService.CurrentProject?.Books ?? [])
            .Select(b => new Sdk.Services.BookInfo { Id = b.Id, Name = b.Name })];

    string? IExtensionProjectService.ActiveBookId => _projectService.ActiveBook?.Id;

    async Task<string> IExtensionProjectService.CreateBookAsync(string name)
    {
        var book = await _projectService.CreateBookAsync(name);
        ProjectStructureChanged?.Invoke();
        return book.Id;
    }

    async Task<bool> IExtensionProjectService.RenameBookAsync(string bookId, string name)
    {
        if (_projectService.CurrentProject?.Books.Any(b => b.Id == bookId) != true) return false;
        await _projectService.RenameBookAsync(bookId, name);
        ProjectStructureChanged?.Invoke();
        return true;
    }

    async Task<bool> IExtensionProjectService.SwitchBookAsync(string bookId)
    {
        if (_projectService.CurrentProject?.Books.Any(b => b.Id == bookId) != true) return false;
        // Switching out from under an unsaved scene loses it: the editor holds
        // text for a book that is no longer the one being written to.
        if (_editing.Current.Dirty) return false;
        await ChangeWorkspaceAsync("project/switchBook", () => _projectService.SwitchBookAsync(bookId));
        ProjectStructureChanged?.Invoke();
        return true;
    }

    IReadOnlyList<Sdk.Services.DraftInfo> IExtensionProjectService.GetDrafts()
        => [.. (_projectService.ActiveBook?.Drafts ?? [])
            .Select(d => new Sdk.Services.DraftInfo { Id = d.Id, Name = d.Name })];

    string? IExtensionProjectService.ActiveDraftId => _projectService.ActiveBook?.ActiveDraftId;

    async Task<string> IExtensionProjectService.CreateDraftAsync(string name, string? cloneFromDraftId)
    {
        Core.Models.BookDraftMetadata? draft = null;
        await ChangeWorkspaceAsync("project/createDraft", async () => draft = await _projectService.CreateDraftAsync(name, cloneFromDraftId));
        ProjectStructureChanged?.Invoke();
        return draft?.Id ?? throw new InvalidOperationException("The workspace transaction did not create the draft.");
    }

    async Task<bool> IExtensionProjectService.RenameDraftAsync(string draftId, string name)
    {
        if (_projectService.ActiveBook?.Drafts.Any(d => d.Id == draftId) != true) return false;
        await _projectService.RenameDraftAsync(draftId, name);
        ProjectStructureChanged?.Invoke();
        return true;
    }

    async Task<bool> IExtensionProjectService.SwitchDraftAsync(string draftId)
    {
        if (_projectService.ActiveBook?.Drafts.Any(d => d.Id == draftId) != true) return false;
        if (_editing.Current.Dirty) return false;
        await ChangeWorkspaceAsync("project/switchDraft", () => _projectService.SwitchDraftAsync(draftId));
        ProjectStructureChanged?.Invoke();
        return true;
    }

    // ── Commands and export hooks (IHostServices) ──────────────────

    private readonly Dictionary<string, (HostCommandInfo Info, Func<string?, Task> Handler)> _commands
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IExportPostProcessor> _exportPostProcessors = [];

    public IReadOnlyList<HostCommandInfo> GetCommands()
        => [.. _commands.Values.Select(c => c.Info).OrderBy(c => c.Id, StringComparer.Ordinal)];

    public async Task<bool> InvokeCommandAsync(string commandId, string? argumentsJson = null)
    {
        if (!_commands.TryGetValue(commandId ?? string.Empty, out var command)) return false;
        await command.Handler(argumentsJson);
        return true;
    }

    public void RegisterCommand(HostCommandInfo command, Func<string?, Task> handler)
    {
        if (command == null || string.IsNullOrWhiteSpace(command.Id) || handler == null) return;
        _commands[command.Id] = (command, handler);
    }

    public void UnregisterCommand(string commandId)
        => _commands.Remove(commandId ?? string.Empty);

    public void RegisterExportPostProcessor(IExportPostProcessor processor)
    {
        if (processor != null && !_exportPostProcessors.Contains(processor))
            _exportPostProcessors.Add(processor);
    }

    public void UnregisterExportPostProcessor(IExportPostProcessor processor)
        => _exportPostProcessors.Remove(processor);

    /// <summary>
    /// Runs every post-export check that applies to a format. Used by the host
    /// after writing an export; a processor that throws is reported as a failed
    /// check rather than taking the export down with it - the file is already
    /// written and is probably fine.
    /// </summary>
    internal async Task<IReadOnlyList<(string Name, ExportCheckResult Result)>> RunExportChecksAsync(
        string outputPath, string formatKey, CancellationToken cancellationToken = default)
    {
        var results = new List<(string, ExportCheckResult)>();
        foreach (var processor in _exportPostProcessors)
        {
            if (processor.Formats.Count > 0
                && !processor.Formats.Contains(formatKey, StringComparer.OrdinalIgnoreCase))
                continue;

            try
            {
                results.Add((processor.DisplayName,
                    await processor.CheckAsync(outputPath, formatKey, cancellationToken)));
            }
            catch (Exception ex)
            {
                results.Add((processor.DisplayName, new ExportCheckResult
                {
                    Ok = false,
                    Problems = [ex.GetType().Name]
                }));
            }
        }
        return results;
    }
}
