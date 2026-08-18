using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Naming drafts, ordering them, and saying what each one is for.
///
/// The drafts of a book were creation-ordered and nameable only at the moment
/// they were made. Renaming existed on the service and was called by nothing.
/// </summary>
public class DraftManagementTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FileService _files = new();
    private readonly ProjectService _projects;

    public DraftManagementTests()
    {
        _projects = new ProjectService(_files);
    }

    public void Dispose() => _dir.Dispose();

    private async Task<(string First, string Second)> TwoDraftsAsync()
    {
        await _projects.CreateProjectAsync(_dir.Path, "Drafts", "Book");
        var first = _projects.ActiveBook!.ActiveDraftId;
        var second = await _projects.CreateDraftAsync("Beta cut");
        return (first, second.Id);
    }

    [Fact]
    public async Task SetDraftNotes_KeepsTheNoteAndTrimsIt()
    {
        var (first, _) = await TwoDraftsAsync();

        await _projects.SetDraftNotesAsync(first, "  agent submission ");

        Assert.Equal(
            "agent submission",
            _projects.ActiveBook!.Drafts.Single(d => d.Id == first).Notes);
    }

    [Fact]
    public async Task SetDraftNotes_Blank_ClearsIt()
    {
        var (first, _) = await TwoDraftsAsync();
        await _projects.SetDraftNotesAsync(first, "something");

        await _projects.SetDraftNotesAsync(first, "   ");

        Assert.Null(_projects.ActiveBook!.Drafts.Single(d => d.Id == first).Notes);
    }

    [Fact]
    public async Task SetDraftNotes_UnknownDraft_DoesNothing()
    {
        var (first, _) = await TwoDraftsAsync();

        await _projects.SetDraftNotesAsync("draft-nobody", "ignored");

        Assert.All(_projects.ActiveBook!.Drafts, d => Assert.Null(d.Notes));
        Assert.NotNull(first);
    }

    [Fact]
    public async Task SetDraftNotes_NoBookOpen_DoesNothing()
    {
        await _projects.SetDraftNotesAsync("draft-1", "ignored");

        Assert.Null(_projects.ActiveBook);
    }

    [Fact]
    public async Task ReorderDrafts_PutsThemInTheOrderAsked_AndSurvivesReopening()
    {
        var (first, second) = await TwoDraftsAsync();

        await _projects.ReorderDraftsAsync([second, first]);
        var root = _projects.ProjectRoot!;
        var reopened = new ProjectService(_files);
        await reopened.LoadProjectAsync(root);

        Assert.Equal([second, first], reopened.ActiveBook!.Drafts.Select(d => d.Id));
    }

    [Fact]
    public async Task ReorderDrafts_ListMissingOne_KeepsItAtTheEnd()
    {
        var (first, second) = await TwoDraftsAsync();

        await _projects.ReorderDraftsAsync([second]);

        Assert.Equal([second, first], _projects.ActiveBook!.Drafts.Select(d => d.Id));
    }

    [Fact]
    public async Task ReorderDrafts_ListNamingOneTwice_StillListsItOnce()
    {
        var (first, second) = await TwoDraftsAsync();

        await _projects.ReorderDraftsAsync([second, second, first]);

        Assert.Equal([second, first], _projects.ActiveBook!.Drafts.Select(d => d.Id));
    }

    [Fact]
    public async Task ReorderDrafts_UnknownId_IsIgnored()
    {
        var (first, second) = await TwoDraftsAsync();

        await _projects.ReorderDraftsAsync(["draft-nobody", second, first]);

        Assert.Equal([second, first], _projects.ActiveBook!.Drafts.Select(d => d.Id));
    }

    [Fact]
    public async Task ReorderDrafts_NoBookOpen_DoesNothing()
    {
        await _projects.ReorderDraftsAsync(["draft-1"]);

        Assert.Null(_projects.ActiveBook);
    }

    [Fact]
    public async Task FlushAndReload_RoundTripTheActiveDraftThroughItsFolder()
    {
        await _projects.CreateProjectAsync(_dir.Path, "Drafts", "Book");
        var chapter = await _projects.CreateChapterAsync("One");
        await _projects.CreateSceneAsync(chapter.Guid, "Arrival");

        await _projects.FlushActiveDraftAsync();
        // Anything only in memory is gone after this; what comes back is what
        // the folder held.
        _projects.ActiveBook!.Chapters.Clear();
        await _projects.ReloadActiveDraftAsync();

        var back = Assert.Single(_projects.GetChaptersOrdered());
        Assert.Equal("One", back.Title);
        Assert.Single(_projects.GetScenesForChapter(back.Guid));
    }

    [Fact]
    public async Task FlushAndReload_NoBookOpen_DoNothing()
    {
        await _projects.FlushActiveDraftAsync();
        await _projects.ReloadActiveDraftAsync();

        Assert.Null(_projects.ActiveBook);
    }
}
