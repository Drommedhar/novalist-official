using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>The tag vocabulary over the RPC surface, against a real project.</summary>
public sealed class TagsRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly TagsRpc _rpc;

    public TagsRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-tags-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "TagNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new TagsRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private async Task<Novalist.Core.Models.SceneData> TaggedSceneAsync(params string[] tags)
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        scene.AnalysisOverrides = new Novalist.Core.Models.SceneAnalysisOverrides
        {
            Tags = [.. tags]
        };
        await _workspace.Projects.SaveScenesAsync();
        return scene;
    }

    [Fact]
    public async Task ListReportsWhatCarriesEachTag()
    {
        await TaggedSceneAsync("night", "flashback");

        var tags = await _rpc.ListAsync();

        Assert.Equal(2, tags.Length);
        Assert.All(tags, t => Assert.Equal(1, t.Scenes));
        Assert.All(tags, t => Assert.Equal(1, t.Total));
    }

    [Fact]
    public async Task AColourSurvivesAReadBack()
    {
        await TaggedSceneAsync("night");

        var tags = await _rpc.SetColorAsync("night", "#123456");

        Assert.Equal("#123456", tags.Single(t => t.Name == "night").Color);
        Assert.Equal("#123456", (await _rpc.ListAsync()).Single(t => t.Name == "night").Color);
    }

    [Fact]
    public async Task RenamingOntoAnExistingTagMergesThem()
    {
        var scene = await TaggedSceneAsync("night", "flashback");

        var tags = await _rpc.RenameAsync("flashback", "night");

        Assert.Single(tags);
        Assert.Equal(["night"], scene.AnalysisOverrides!.Tags);
    }

    [Fact]
    public async Task DeletingRemovesItFromTheScene()
    {
        var scene = await TaggedSceneAsync("night", "flashback");

        var tags = await _rpc.DeleteAsync("flashback");

        Assert.Single(tags);
        Assert.Equal(["night"], scene.AnalysisOverrides!.Tags);
    }
}
