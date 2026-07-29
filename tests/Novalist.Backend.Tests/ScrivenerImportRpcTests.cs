using NSubstitute;
using Novalist.Backend;
using Novalist.Backend.Extensions;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk.Services;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Importing a Scrivener project end to end, and the structural authoring API
/// that lets a third-party importer do the same thing for a format Novalist
/// does not read itself.
/// </summary>
public sealed class ScrivenerImportRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly ManuscriptImportRpc _rpc;

    public ScrivenerImportRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-scriv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "ScrivNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new ManuscriptImportRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>A two-scene Scrivener 3 project with a research folder that
    /// should be reported rather than imported.</summary>
    private string BuildProject()
    {
        var project = Path.Combine(_root, "Imported.scriv");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "Imported.scrivx"), """
            <?xml version="1.0"?><ScrivenerProject><Binder>
              <BinderItem UUID="F1" Type="Folder"><Title>Chapter One</Title><Children>
                <BinderItem UUID="D1" Type="Text"><Title>Arrival</Title></BinderItem>
                <BinderItem UUID="D2" Type="Text"><Title>The Inn</Title></BinderItem>
              </Children></BinderItem>
              <BinderItem UUID="R1" Type="Folder"><Title>Research</Title></BinderItem>
            </Binder></ScrivenerProject>
            """);

        Doc(project, "D1", "She arrived at dusk.", "She arrives.");
        Doc(project, "D2", "The inn was full.");
        return project;
    }

    private static void Doc(string project, string uuid, string text, string? synopsis = null)
    {
        var folder = Path.Combine(project, "Files", "Data", uuid);
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "content.rtf"),
            "{\\rtf1\\ansi{\\fonttbl{\\f0 Times;}}\\f0 " + text + "\\par}");
        if (synopsis != null)
            File.WriteAllText(Path.Combine(folder, "synopsis.txt"), synopsis);
    }

    [Fact]
    public void TheScrivExtensionIsOfferedAlongsideTheFileFormats()
    {
        Assert.Contains(".scriv", _rpc.Formats());
        Assert.Contains(".docx", _rpc.Formats());
    }

    [Fact]
    public void PreviewReadsTheBinderWithoutWritingAnything()
    {
        var plan = _rpc.Preview(BuildProject());

        Assert.Equal("scrivener3", plan.Format);
        Assert.Equal(1, plan.ChapterCount);
        Assert.Equal(2, plan.SceneCount);
        Assert.Equal(["Arrival", "The Inn"], plan.Chapters.Single().Scenes.Select(s => s.Title));
        // Nothing created yet.
        Assert.Empty(_workspace.Projects.GetChaptersOrdered());
    }

    [Fact]
    public void PreviewNamesWhatWillNotComeAcross()
    {
        // A silent import that drops a research folder is worse than one that
        // says so before it starts.
        Assert.Contains("Research", _rpc.Preview(BuildProject()).Losses);
    }

    [Fact]
    public async Task RunCreatesTheChaptersAndScenes()
    {
        var result = await _rpc.RunAsync(BuildProject());

        Assert.Equal(1, result.Chapters);
        Assert.Equal(2, result.Scenes);

        var chapter = _workspace.Projects.GetChaptersOrdered().Single();
        Assert.Equal("Chapter One", chapter.Title);
        Assert.Equal(
            ["Arrival", "The Inn"],
            _workspace.Projects.GetScenesForChapter(chapter.Guid).Select(s => s.Title));
    }

    [Fact]
    public async Task TheProseLandsInTheSceneFiles()
    {
        await _rpc.RunAsync(BuildProject());

        var chapter = _workspace.Projects.GetChaptersOrdered().Single();
        var scene = _workspace.Projects.GetScenesForChapter(chapter.Guid).First();
        var html = await _workspace.Projects.ReadSceneContentAsync(chapter, scene);

        Assert.Contains("She arrived at dusk.", html);
        // Read through the paragraph markup the editor speaks, not as raw text.
        Assert.StartsWith("<p>", html);
    }

    [Fact]
    public async Task ASynopsisCardBecomesTheScenesSynopsis()
    {
        await _rpc.RunAsync(BuildProject());

        var chapter = _workspace.Projects.GetChaptersOrdered().Single();
        var scene = _workspace.Projects.GetScenesForChapter(chapter.Guid).First();

        Assert.Equal("She arrives.", scene.Synopsis);
    }

    [Fact]
    public async Task AnUnreadableProjectCreatesNothing()
    {
        var empty = Path.Combine(_root, "Empty.scriv");
        Directory.CreateDirectory(empty);

        var result = await _rpc.RunAsync(empty);

        Assert.Equal(0, result.Chapters);
        Assert.Empty(_workspace.Projects.GetChaptersOrdered());
    }

    [Fact]
    public async Task ImportingTwiceAppendsRatherThanReplaces()
    {
        var project = BuildProject();
        await _rpc.RunAsync(project);

        await _rpc.RunAsync(project);

        Assert.Equal(2, _workspace.Projects.GetChaptersOrdered().Count);
    }

    // ── The structural authoring API ──

    private IExtensionProjectService ExtensionHost()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Settings.Returns(new AppSettings());
        return new HostServices(
            _workspace.FileService,
            _workspace.Projects,
            new EntityService(_workspace.Projects),
            settings);
    }

    [Fact]
    public async Task AnExtensionCanBuildAChapterAndSceneAndWriteToIt()
    {
        // This is what a third-party .fdx or .scriv reader needs; before it
        // existed, every importer had to be written into core.
        var host = ExtensionHost();

        var chapterGuid = await host.CreateChapterAsync("From an extension");
        var sceneId = await host.CreateSceneAsync(chapterGuid, "Scene one");
        await host.WriteSceneContentAsync(chapterGuid, sceneId, "<p>Written by an extension.</p>");

        Assert.Equal(
            "<p>Written by an extension.</p>",
            await host.ReadSceneContentAsync(chapterGuid, sceneId));
    }

    [Fact]
    public async Task AnExtensionWriteUpdatesTheWordCountTheBinderShows()
    {
        var host = ExtensionHost();
        var chapterGuid = await host.CreateChapterAsync("One");
        var sceneId = await host.CreateSceneAsync(chapterGuid, "Scene");

        await host.WriteSceneContentAsync(chapterGuid, sceneId, "<p>one two three four</p>");

        Assert.Equal(
            4,
            _workspace.Projects.GetScenesForChapter(chapterGuid).Single(s => s.Id == sceneId).WordCount);
    }

    [Fact]
    public async Task ASceneUnderAChapterThatDoesNotExistIsRefused()
    {
        // An orphan scene would be unreachable in the binder.
        Assert.Empty(await ExtensionHost().CreateSceneAsync("no-such-chapter", "Scene"));
    }

    [Fact]
    public async Task WritingToASceneThatDoesNotExistDoesNothing()
    {
        var host = ExtensionHost();
        var chapterGuid = await host.CreateChapterAsync("One");

        await host.WriteSceneContentAsync(chapterGuid, "no-such-scene", "<p>x</p>");

        Assert.Empty(_workspace.Projects.GetScenesForChapter(chapterGuid));
    }
}
