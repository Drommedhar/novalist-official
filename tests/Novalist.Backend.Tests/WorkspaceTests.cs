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
}
