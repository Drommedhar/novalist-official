using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Whole-book analytics from the RPC the Dashboard calls.</summary>
public sealed class AnalyticsRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly AnalyticsRpc _rpc;

    public AnalyticsRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-stats-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "StatsNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new AnalyticsRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task AnEmptyBookReportsNothing()
    {
        var result = await _rpc.BookAsync();

        Assert.Empty(result.ChapterTitles);
        Assert.Empty(result.Pov);
    }

    [Fact]
    public async Task PovAndPresenceComeThroughForARealProject()
    {
        var entities = new Novalist.Core.Services.EntityService(_workspace.Projects);
        var rose = new Novalist.Core.Models.CharacterData { Name = "Rose" };
        await entities.SaveCharacterAsync(rose);

        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "A");
        scene.AnalysisOverrides = new Novalist.Core.Models.SceneAnalysisOverrides { Pov = "Rose" };
        await _workspace.WriteSceneAsync(
            chapter.Guid, scene.Id,
            $"<p><span class=\"nv-entity-mention\" data-entity-id=\"{rose.Id}\">Rose</span> went in.</p>",
            "Rose went in.");

        var result = await _rpc.BookAsync();

        Assert.Equal(["One"], result.ChapterTitles);
        Assert.Equal("Rose", result.Pov.Single().Key);
        Assert.Equal("Rose", result.Characters.Single().Label);
        Assert.Equal([1], result.Characters.Single().ScenesPerChapter);
    }

    [Fact]
    public async Task AnEntryTheManuscriptNeverMentionsIsReported()
    {
        var entities = new Novalist.Core.Services.EntityService(_workspace.Projects);
        await entities.SaveCharacterAsync(new Novalist.Core.Models.CharacterData { Name = "Forgotten" });
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        await _workspace.Projects.CreateSceneAsync(chapter.Guid, "A");

        Assert.Contains("Forgotten", (await _rpc.BookAsync()).Unused);
    }

    // ── Tension ──
    //
    // Intensity has been computed and hand-overridable per scene for a long
    // time and shown only as one Inspector number, which is not a shape.

    [Fact]
    public async Task Tension_ReportsEverySceneInReadingOrder()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var first = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Calm");
        var second = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Storm");
        first.AnalysisOverrides = new Novalist.Core.Models.SceneAnalysisOverrides
        {
            Intensity = -3,
            Emotion = "weary"
        };
        second.AnalysisOverrides = new Novalist.Core.Models.SceneAnalysisOverrides { Intensity = 9 };
        await _workspace.Projects.SaveScenesAsync();

        var points = _rpc.Tension();

        Assert.Equal(["Calm", "Storm"], points.Select(p => p.SceneTitle));
        Assert.Equal(-3, points[0].Intensity);
        Assert.Equal("weary", points[0].Emotion);
        Assert.Equal(9, points[1].Intensity);
    }

    [Fact]
    public async Task Tension_AnUnratedSceneIsNullRatherThanZero()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Unrated");

        // Nobody has rated it, which is not the same as saying it is flat.
        Assert.Null(Assert.Single(_rpc.Tension()).Intensity);
    }
}
