using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed class SharedProjectReloadTests
{
    [Fact]
    public async Task ReloadReceivesAnotherWritersChangesWithoutFollowingTheirBookOrDraft()
    {
        using var directory = new TempDir();
        var local = new ProjectService(new FileService());
        await local.CreateProjectAsync(directory.Path, "Shared", "First book");
        var originalBook = local.ActiveBook ?? throw new InvalidOperationException("Missing book.");
        var originalDraft = originalBook.ActiveDraftId;
        var chapter = await local.CreateChapterAsync("Opening");
        var scene = await local.CreateSceneAsync(chapter.Guid, "Arrival");
        var otherDraft = await local.CreateDraftAsync("Another draft");
        var root = local.ProjectRoot ?? throw new InvalidOperationException("Missing project.");
        var remote = new ProjectService(new FileService());
        await remote.LoadProjectAsync(root);
        await remote.RenameChapterAsync(chapter.Guid, "Revised opening");
        remote.ProjectSettings.Overrides.EditorFontSize = 24;
        await remote.SaveProjectSettingsAsync();
        await remote.SwitchDraftAsync(otherDraft.Id);
        await remote.CreateChapterAsync("Only in another draft");
        var otherBook = await remote.CreateBookAsync("Other book");
        await remote.SwitchBookAsync(otherBook.Id);

        await local.ReloadSharedProjectAsync();

        Assert.Equal(originalBook.Id, local.ActiveBook?.Id);
        Assert.Equal(originalDraft, local.ActiveBook?.ActiveDraftId);
        Assert.Equal("Revised opening", Assert.Single(local.GetChaptersOrdered()).Title);
        Assert.Equal(scene.Id, Assert.Single(local.GetScenesForChapter(chapter.Guid)).Id);
        Assert.Equal(24, local.ProjectSettings.Overrides.EditorFontSize);
        Assert.Contains(local.CurrentProject?.Books ?? [], book => book.Id == otherBook.Id);
    }

    [Fact]
    public async Task ReceivingRemoteWordsSetsABaselineWithoutClaimingThemAsLocalProgress()
    {
        using var directory = new TempDir();
        var files = new FileService();
        var project = new ProjectService(files);
        await project.CreateProjectAsync(directory.Path, "Shared", "Book");
        var history = new WordHistoryService(files, project);
        await history.RecordBaselineAsync("book", "scene", 100);
        await history.RecordSaveAsync("book", "scene", 120);
        var entries = history.ReadRange(DateOnly.MinValue, DateOnly.MaxValue);
        var entry = Assert.Single(entries);
        Assert.Equal(120, entry.Words);
        Assert.Equal(20, entry.Delta);
    }
}
