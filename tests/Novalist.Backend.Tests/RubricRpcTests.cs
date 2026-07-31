using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>The named scene rubric, over the wire.</summary>
public sealed class RubricRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly RubricRpc _rpc;
    private readonly string _chapterGuid;
    private readonly string _sceneId;

    public RubricRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-rub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "RubNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();

        var chapter = _workspace.Projects.CreateChapterAsync("One").GetAwaiter().GetResult();
        var scene = _workspace.Projects.CreateSceneAsync(chapter.Guid, "Arrival")
            .GetAwaiter().GetResult();
        _chapterGuid = chapter.Guid;
        _sceneId = scene.Id;
        _rpc = new RubricRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void TheElementsCarryTheirQuestionAndTheirAdvice()
    {
        var elements = _rpc.Elements();

        Assert.NotEmpty(elements);
        Assert.All(elements, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Question));
            Assert.False(string.IsNullOrWhiteSpace(e.Advice));
        });
    }

    [Fact]
    public async Task AScoreIsStoredOnTheSceneAndReadBack()
    {
        await _rpc.SetScoreAsync(_chapterGuid, _sceneId, "goal", 4);

        var scene = _rpc.Scene(_chapterGuid, _sceneId);
        Assert.Equal(4, Assert.Single(scene.Scores).Score);
        Assert.Equal(1, scene.Answered);
        Assert.Equal(0, scene.Weak);

        // On disk, not only in the reply.
        var stored = _workspace.Projects.GetScenesForChapter(_chapterGuid).First(s => s.Id == _sceneId);
        Assert.Equal("4", stored.Properties!["rubric:goal"]);
    }

    [Fact]
    public async Task ZeroClearsAnAnswerRatherThanScoringIt()
    {
        await _rpc.SetScoreAsync(_chapterGuid, _sceneId, "goal", 4);

        var scene = await _rpc.SetScoreAsync(_chapterGuid, _sceneId, "goal", 0);

        Assert.Empty(scene.Scores);
        Assert.Equal(0, scene.Answered);
    }

    [Fact]
    public async Task AScoreOnSomethingThatIsNotThereChangesNothing()
    {
        Assert.Empty((await _rpc.SetScoreAsync(_chapterGuid, "no-such-scene", "goal", 4)).Scores);
        Assert.Empty((await _rpc.SetScoreAsync(_chapterGuid, _sceneId, "no-such-element", 4)).Scores);
    }

    [Fact]
    public async Task TheWeakestListNamesTheScenesWorthOpening()
    {
        var second = await _workspace.Projects.CreateSceneAsync(_chapterGuid, "Later");
        await _rpc.SetScoreAsync(_chapterGuid, _sceneId, "goal", 1);
        await _rpc.SetScoreAsync(_chapterGuid, _sceneId, "stakes", 2);
        await _rpc.SetScoreAsync(_chapterGuid, second.Id, "goal", 2);

        var weakest = _rpc.Weakest();

        Assert.Equal(2, weakest.Length);
        // Most weak answers first: that is the scene to open.
        Assert.Equal(_sceneId, weakest[0].SceneId);
        Assert.Equal(2, weakest[0].Weak);
        Assert.Equal("One", weakest[0].ChapterTitle);
        Assert.Equal("Arrival", weakest[0].SceneTitle);
    }

    [Fact]
    public async Task ASceneNobodyHasReadAgainstTheRubricIsNotCalledWeak()
    {
        // It is unread, not weak, and listing it would bury the ones that were
        // actually judged.
        await _rpc.SetScoreAsync(_chapterGuid, _sceneId, "goal", 5);

        Assert.Empty(_rpc.Weakest());
    }

    [Fact]
    public async Task AnArchivedSceneIsNotListed()
    {
        var archived = await _workspace.Projects.CreateSceneAsync(_chapterGuid, "Archived");
        await _rpc.SetScoreAsync(_chapterGuid, archived.Id, "goal", 1);
        archived.ArchivedAt = DateTime.UtcNow;
        await _workspace.Projects.SaveScenesAsync();

        Assert.DoesNotContain(_rpc.Weakest(), w => w.SceneId == archived.Id);
    }

    [Fact]
    public async Task TheListCanBeHeldToALength()
    {
        await _rpc.SetScoreAsync(_chapterGuid, _sceneId, "goal", 1);
        var second = await _workspace.Projects.CreateSceneAsync(_chapterGuid, "Later");
        await _rpc.SetScoreAsync(_chapterGuid, second.Id, "goal", 1);

        Assert.Single(_rpc.Weakest(1));
        // A nonsense limit still returns something rather than nothing.
        Assert.Single(_rpc.Weakest(0));
    }
}
