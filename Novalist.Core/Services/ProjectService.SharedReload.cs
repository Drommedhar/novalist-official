using System.Text.Json;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public partial class ProjectService
{
    public async Task ReloadSharedProjectAsync()
    {
        if (ProjectRoot is not { } root) throw new InvalidOperationException("No project is open.");
        var activeBook = ActiveBook?.Id;
        var drafts = CurrentProject?.Books.ToDictionary(book => book.Id, book => book.ActiveDraftId);
        var metadata = JsonSerializer.Deserialize<ProjectMetadata>(await _fileService.ReadTextAsync(
            _fileService.CombinePath(root, ".novalist", "project.json")), JsonOptions)
            ?? throw new InvalidDataException("Invalid shared project metadata.");
        if (activeBook is not null && metadata.Books.Any(book => book.Id == activeBook)) metadata.ActiveBookId = activeBook;
        foreach (var book in metadata.Books)
            if (drafts?.TryGetValue(book.Id, out var draft) == true && book.Drafts.Any(item => item.Id == draft))
                book.ActiveDraftId = draft;
        CurrentProject = metadata;
        ActiveBook = metadata.GetActiveBook() ?? throw new InvalidDataException("The shared project has no books.");
        await LoadActiveDraftDataAsync();
        await LoadScenesManifestAsync();
        await LoadProjectSettingsAsync();
    }
}
