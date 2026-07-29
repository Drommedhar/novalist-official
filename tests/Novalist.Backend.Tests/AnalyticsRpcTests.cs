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
}
