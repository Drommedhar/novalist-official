using Novalist.Backend;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class WorkspaceTests : IDisposable
{
    private readonly string _root;

    public WorkspaceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-backend-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private Workspace CreateWorkspace() => new(Path.Combine(_root, "settings"));

    private async Task<Workspace> CreateOpenProjectAsync()
    {
        var workspace = CreateWorkspace();
        await workspace.Projects.CreateProjectAsync(_root, "TestNovel", "Book One");
        await workspace.OpenProjectAsync(workspace.Projects.ProjectRoot!);
        return workspace;
    }

    [Fact]
    public void BuildState_NoProject_ReportsUnloaded()
    {
        var state = CreateWorkspace().BuildState();
        Assert.False(state.IsLoaded);
        Assert.Empty(state.Chapters)
;    }

    [Fact]
    public async Task OpenProject_BuildsBinderState_AndRecordsRecent()
    {
        var workspace = await CreateOpenProjectAsync();
        var state = workspace.BuildState();

        Assert.True(state.IsLoaded);
        Assert.Equal("TestNovel", state.ProjectName);
        Assert.NotNull(state.ActiveBookId)
;        Assert.Single(state.Books);

        var recents = await workspace.GetRecentProjectsAsync();
        Assert.Contains(recents, r => r.Name == "TestNovel");
    }

    [Fact]
    public async Task SceneRoundTrip_PersistsContentAndWordCount()
    {
        var workspace = await CreateOpenProjectAsync();
        var chapter = await workspace.Projects.CreateChapterAsync("Chapter One");
        var scene = await workspace.Projects.CreateSceneAsync(chapter.Guid, "Opening");

        var html = "<p>Hello brave new world</p>";
        var count = await workspace.WriteSceneAsync(chapter.Guid, scene.Id, html, "Hello brave new world");
        Assert.Equal(4, count);

        var (ch, sc) = workspace.ResolveScene(chapter.Guid, scene.Id);
        var readBack = await workspace.Projects.ReadSceneContentAsync(ch, sc);
        Assert.Contains("Hello brave new world", readBack);

        var state = workspace.BuildState();
        var sceneDto = state.Chapters.Single(c => c.Guid == chapter.Guid).Scenes.Single();
        Assert.Equal(4, sceneDto.WordCount);
    }

    [Fact]
    public async Task WriteScene_EmptyPlainText_CountsFromHtml()
    {
        var workspace = await CreateOpenProjectAsync();
        var chapter = await workspace.Projects.CreateChapterAsync("C");
        var scene = await workspace.Projects.CreateSceneAsync(chapter.Guid, "S");

        var count = await workspace.WriteSceneAsync(chapter.Guid, scene.Id, "<p>one two&nbsp;three</p>", "");
        Assert.Equal(3, count);
    }

    [Fact]
    public void ResolveScene_Throws_ForMissingProjectChapterOrScene()
    {
        var empty = CreateWorkspace();
        Assert.Throws<InvalidOperationException>(() => empty.ResolveScene("x", "y"));
    }

    [Fact]
    public async Task ResolveScene_Throws_ForUnknownChapter_AndUnknownScene()
    {
        var workspace = await CreateOpenProjectAsync();
        Assert.Throws<InvalidOperationException>(() => workspace.ResolveScene("missing", "y"));

        var chapter = await workspace.Projects.CreateChapterAsync("C");
        Assert.Throws<InvalidOperationException>(() => workspace.ResolveScene(chapter.Guid, "missing"));
    }

    [Fact]
    public async Task BuildState_ChapterWithoutManifestEntry_HasNoScenes()
    {
        var workspace = await CreateOpenProjectAsync();
        var chapter = await workspace.Projects.CreateChapterAsync("Orphan");
        workspace.Projects.ScenesManifest!.Chapters.Remove(chapter.Guid);

        var state = workspace.BuildState();

        Assert.Empty(state.Chapters.Single(c => c.Guid == chapter.Guid).Scenes);
    }

    [Fact]
    public async Task Snapshots_TakeListLoadRestoreDelete_FullFlow()
    {
        var workspace = await CreateOpenProjectAsync();
        var chapter = await workspace.Projects.CreateChapterAsync("C");
        var scene = await workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        await workspace.WriteSceneAsync(chapter.Guid, scene.Id, "<p>version one</p>", "version one");
        var rpc = new Novalist.Backend.Rpc.SnapshotsRpc(workspace);

        var afterTake = await rpc.TakeAsync(chapter.Guid, scene.Id, "before rewrite");
        Assert.Single(afterTake, s => s.Label == "before rewrite");

        await workspace.WriteSceneAsync(chapter.Guid, scene.Id, "<p>version two</p>", "version two");
        var content = await rpc.LoadAsync(chapter.Guid, scene.Id, afterTake[0].Id);
        Assert.Contains("version one", content);

        Assert.True(await rpc.RestoreAsync(chapter.Guid, scene.Id, afterTake[0].Id));
        var restored = await workspace.Projects.ReadSceneContentAsync(
            workspace.ResolveChapter(chapter.Guid),
            workspace.ResolveScene(chapter.Guid, scene.Id).scene);
        Assert.Contains("version one", restored);

        var afterDelete = await rpc.DeleteAsync(chapter.Guid, scene.Id, afterTake[0].Id);
        Assert.DoesNotContain(afterDelete, s => s.Id == afterTake[0].Id);
        Assert.Null(await rpc.LoadAsync(chapter.Guid, scene.Id, "missing"));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("it's a two-part word", 4)]
    [InlineData("Hello, world", 2)]
    public void CountWords_MatchesEditorRegex(string text, int expected)
    {
        Assert.Equal(expected, Workspace.CountWords(text));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("plain text", "plain text")]
    [InlineData("<p>a &amp; b</p>", "a & b")]
    public void StripHtml_HandlesEmptyPlainAndMarkup(string input, string expected)
    {
        Assert.Equal(expected, Workspace.StripHtml(input));
    }

    [Fact]
    public async Task Recents_CarryCoverDataUri_AfterCoverSet()
    {
        var workspace = await CreateOpenProjectAsync();
        var source = Path.Combine(_root, "cover.png");
        await File.WriteAllBytesAsync(source, [0x89, 0x50, 0x4E, 0x47, 1, 2, 3]);
        await new Rpc.DashboardRpc(workspace).SetCoverAsync(source);

        var recents = await workspace.GetRecentProjectsAsync();
        var entry = recents.Single(r => r.Path == workspace.Projects.ProjectRoot);
        Assert.NotNull(entry.Cover);
        Assert.StartsWith("data:image/png;base64,", entry.Cover);
    }

    [Fact]
    public async Task Recents_DropAProjectWhoseFolderTheWriterDeleted()
    {
        var workspace = await CreateOpenProjectAsync();
        var root = workspace.Projects.ProjectRoot!;
        Assert.Contains(await workspace.GetRecentProjectsAsync(), r => r.Path == root);

        // Deleted outside Novalist, which is how projects actually go away.
        workspace.CloseProject();
        Directory.Delete(root, recursive: true);

        Assert.Empty(await workspace.GetRecentProjectsAsync());
    }

    [Fact]
    public async Task Recents_ForgetADeletedProjectRatherThanRecheckingItForever()
    {
        var workspace = await CreateOpenProjectAsync();
        var root = workspace.Projects.ProjectRoot!;
        workspace.CloseProject();
        Directory.Delete(root, recursive: true);

        await workspace.GetRecentProjectsAsync();

        // Gone from the stored settings too, so a fresh launch never offers it -
        // and so a folder later recreated at the same path does not come back as
        // a project the writer never reopened.
        await workspace.Settings.LoadAsync();
        Assert.DoesNotContain(workspace.Settings.Settings.RecentProjects, r => r.Path == root);

        var reopened = new Workspace(Path.Combine(_root, "settings"));
        Assert.Empty(await reopened.GetRecentProjectsAsync());
    }

    [Fact]
    public async Task Recents_KeepTheOnesThatAreStillThere()
    {
        var workspace = CreateWorkspace();
        await workspace.Projects.CreateProjectAsync(_root, "Kept", "Book One");
        await workspace.OpenProjectAsync(workspace.Projects.ProjectRoot!);
        var kept = workspace.Projects.ProjectRoot!;

        await workspace.Projects.CreateProjectAsync(_root, "Deleted", "Book One");
        await workspace.OpenProjectAsync(workspace.Projects.ProjectRoot!);
        var deleted = workspace.Projects.ProjectRoot!;

        workspace.CloseProject();
        Directory.Delete(deleted, recursive: true);

        var recents = await workspace.GetRecentProjectsAsync();

        Assert.Equal([kept], recents.Select(r => r.Path).ToArray());
    }

    [Fact]
    public async Task Recents_DropAFolderThatIsNoLongerAProject()
    {
        var workspace = await CreateOpenProjectAsync();
        var root = workspace.Projects.ProjectRoot!;
        workspace.CloseProject();

        // The folder survives; the thing that made it a project does not.
        Directory.Delete(Path.Combine(root, ".novalist"), recursive: true);

        Assert.Empty(await workspace.GetRecentProjectsAsync());
    }

    [Fact]
    public async Task Recents_NoCover_YieldsNullDataUri()
    {
        var workspace = await CreateOpenProjectAsync();
        var recents = await workspace.GetRecentProjectsAsync();
        Assert.NotEmpty(recents);
        Assert.All(recents, r => Assert.Null(r.Cover));
    }

    [Fact]
    public async Task LoadCoverDataUri_HandlesEmptyMissingAndReal()
    {
        Assert.Null(await Workspace.LoadCoverDataUriAsync(null));
        Assert.Null(await Workspace.LoadCoverDataUriAsync(""));
        Assert.Null(await Workspace.LoadCoverDataUriAsync(Path.Combine(_root, "absent.png")));

        var file = Path.Combine(_root, "real.jpg");
        var bytes = new byte[] { 1, 2, 3, 4 };
        await File.WriteAllBytesAsync(file, bytes);
        var uri = await Workspace.LoadCoverDataUriAsync(file);
        Assert.NotNull(uri);
        Assert.StartsWith("data:image/jpeg;base64,", uri);
        Assert.EndsWith(Convert.ToBase64String(bytes), uri);
    }

    [Fact]
    public async Task LoadCoverDataUri_UnreadableCover_YieldsNullNotThrow()
    {
        // A cover that exists but cannot be read (locked here; in the wild a
        // sandbox-denied or dataless iCloud file for a recent project we don't
        // hold access to) must degrade to null rather than throw and take down
        // the whole recents list. Hold an exclusive lock so the read fails.
        var file = Path.Combine(_root, "locked-cover.png");
        await File.WriteAllBytesAsync(file, [1, 2, 3, 4]);
        using var exclusive = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.True(File.Exists(file));
        Assert.Null(await Workspace.LoadCoverDataUriAsync(file));
    }

    [Theory]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".JPEG", "image/jpeg")]
    [InlineData("png", "image/png")]
    [InlineData(".gif", "image/gif")]
    [InlineData(".webp", "image/webp")]
    [InlineData(".bmp", "image/bmp")]
    [InlineData(".xyz", "application/octet-stream")]
    public void MimeForExtension_MapsKnownAndDefaults(string extension, string expected)
    {
        Assert.Equal(expected, Workspace.MimeForExtension(extension));
    }

    [Fact]
    public void ActiveCoverAbsolutePath_NullWhenNoProject()
    {
        Assert.Null(CreateWorkspace().ActiveCoverAbsolutePath());
    }

    [Fact]
    public async Task ActiveCoverAbsolutePath_NullWhenNoCover_RootedWhenSet()
    {
        var workspace = await CreateOpenProjectAsync();
        Assert.Null(workspace.ActiveCoverAbsolutePath());

        workspace.Projects.ActiveBook!.CoverImage = "Images/x.png";
        var abs = workspace.ActiveCoverAbsolutePath();
        Assert.NotNull(abs);
        Assert.EndsWith("x.png", abs);
        Assert.True(Path.IsPathRooted(abs));
    }

    [Fact]
    public async Task RefreshRecentCover_NoOp_WhenNoProjectOrNotInRecents()
    {
        // No project open -> ProjectRoot null -> silent no-op.
        await CreateWorkspace().RefreshRecentCoverAsync();

        // Project open but absent from the recents list -> silent no-op.
        var workspace = await CreateOpenProjectAsync();
        workspace.Settings.Settings.RecentProjects.Clear();
        await workspace.RefreshRecentCoverAsync();
        Assert.Empty(workspace.Settings.Settings.RecentProjects);
    }

    // ── Putting a chapter in the middle ──

    [Fact]
    public async Task CreateChapter_InsertsAtAPositionAndMovesTheRestDown()
    {
        var workspace = await CreateOpenProjectAsync();
        var rpc = new Rpc.ProjectRpc(workspace);
        await rpc.CreateChapterAsync("One");
        await rpc.CreateChapterAsync("Two");
        await rpc.CreateChapterAsync("Three");

        // Before this, putting a chapter mid-book meant appending it and
        // dragging it up past everything after it - a dozen drags on a long
        // book, each one a save.
        var state = await rpc.CreateChapterAsync("New Two", insertAtOrder: 2);

        Assert.Equal(
            ["One", "New Two", "Two", "Three"],
            state.Chapters.OrderBy(c => c.Order).Select(c => c.Title));
        Assert.Equal([1, 2, 3, 4], state.Chapters.OrderBy(c => c.Order).Select(c => c.Order));
    }

    [Fact]
    public async Task CreateChapter_WithNoPositionStillAppends()
    {
        var workspace = await CreateOpenProjectAsync();
        var rpc = new Rpc.ProjectRpc(workspace);
        await rpc.CreateChapterAsync("One");

        var state = await rpc.CreateChapterAsync("Two");

        Assert.Equal(["One", "Two"], state.Chapters.OrderBy(c => c.Order).Select(c => c.Title));
    }

    [Fact]
    public async Task CreateChapter_APositionPastTheEndAppendsRatherThanLeavingAHole()
    {
        var workspace = await CreateOpenProjectAsync();
        var rpc = new Rpc.ProjectRpc(workspace);
        await rpc.CreateChapterAsync("One");

        var state = await rpc.CreateChapterAsync("Far", insertAtOrder: 99);

        Assert.Equal([1, 2], state.Chapters.OrderBy(c => c.Order).Select(c => c.Order));
        Assert.Equal("Far", state.Chapters.OrderBy(c => c.Order).Last().Title);
        // And below one is the front rather than a negative slot.
        var front = await rpc.CreateChapterAsync("First", insertAtOrder: -5);
        Assert.Equal("First", front.Chapters.OrderBy(c => c.Order).First().Title);
    }

    [Fact]
    public async Task ChapterDescription_IsTheWritersNoteAndNotTheSubtitle()
    {
        var workspace = await CreateOpenProjectAsync();
        var rpc = new Rpc.ProjectRpc(workspace);
        var created = await rpc.CreateChapterAsync("One");
        var guid = created.Chapters.Single().Guid;

        var state = await rpc.SetChapterDescriptionAsync(guid, "  Where she finds out.  ");
        Assert.Equal("Where she finds out.", state.Chapters.Single().Description);
        // The subtitle - what a reader sees - is untouched by it.
        Assert.Null(state.Chapters.Single().Subtitle);

        Assert.Null((await rpc.SetChapterDescriptionAsync(guid, "  ")).Chapters.Single().Description);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => rpc.SetChapterDescriptionAsync("no-such-guid", "x"));
    }

}
