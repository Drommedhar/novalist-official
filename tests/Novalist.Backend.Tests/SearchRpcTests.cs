using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class SearchRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly SearchRpc _rpc;
    private readonly string _chapterGuid;
    private readonly string _sceneId;

    public SearchRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-find-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "FindNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        var chapter = _workspace.Projects.CreateChapterAsync("C").GetAwaiter().GetResult();
        var scene = _workspace.Projects.CreateSceneAsync(chapter.Guid, "S").GetAwaiter().GetResult();
        _chapterGuid = chapter.Guid;
        _sceneId = scene.Id;
        _workspace.WriteSceneAsync(chapter.Guid, scene.Id,
            "<p>The wolf howled. The Wolf waited.</p>",
            "The wolf howled. The Wolf waited.").GetAwaiter().GetResult();
        _rpc = new SearchRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task Find_RespectsCaseAndScope()
    {
        var insensitive = await _rpc.FindAsync(
            "wolf", false, false, false, "ActiveBook", null, null, CancellationToken.None);
        Assert.Equal(2, insensitive.Length);
        Assert.Equal("wolf", insensitive[0].MatchedText);

        var sensitive = await _rpc.FindAsync(
            "Wolf", true, false, false, "ActiveBook", null, null, CancellationToken.None);
        Assert.Single(sensitive);

        var scoped = await _rpc.FindAsync(
            "wolf", false, false, false, "CurrentScene", _chapterGuid, _sceneId, CancellationToken.None);
        Assert.Equal(2, scoped.Length);
    }

    [Fact]
    public async Task ReplaceAll_WritesThroughToDisk()
    {
        var count = await _rpc.ReplaceAllAsync(
            "wolf", "bear", false, false, false, "ActiveBook", null, null, CancellationToken.None);
        Assert.Equal(2, count);

        var (chapter, scene) = _workspace.ResolveScene(_chapterGuid, _sceneId);
        var html = await _workspace.Projects.ReadSceneContentAsync(chapter, scene);
        Assert.Contains("bear howled", html);
        Assert.DoesNotContain("wolf", html, StringComparison.OrdinalIgnoreCase);
    }
}
