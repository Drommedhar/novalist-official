using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Scene stages: the writer's own revision states, replacing a chapter-level
/// status that could not describe a chapter with scenes at four different
/// stages inside it.
/// </summary>
public class SceneStageServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProjectService _projects = new(new FileService());
    private readonly SceneStageService _sut;

    public SceneStageServiceTests()
    {
        _sut = new SceneStageService(_projects);
    }

    public void Dispose() => _dir.Dispose();

    private async Task<(string Chapter, SceneData A, SceneData B)> BookAsync()
    {
        await _projects.CreateProjectAsync(_dir.Path, "P", "Book");
        var chapter = await _projects.CreateChapterAsync("One");
        var a = await _projects.CreateSceneAsync(chapter.Guid, "A");
        var b = await _projects.CreateSceneAsync(chapter.Guid, "B");
        return (chapter.Guid, a, b);
    }

    // ── The stage list ──

    [Fact]
    public async Task ABookWithNoStagesConfiguredGetsTheDefaults()
    {
        await BookAsync();

        var stages = _sut.Stages();

        Assert.Equal(
            ["outline", "firstDraft", "revised", "edited", "final"],
            stages.Select(s => s.Key));
    }

    [Fact]
    public async Task OutlineDoesNotCountAsWrittenByDefault()
    {
        // At that stage the words are usually notes to self, and counting them
        // inflates every number the writer judges progress by.
        await BookAsync();

        Assert.False(_sut.Stages().Single(s => s.Key == "outline").CountsAsWritten);
        Assert.True(_sut.Stages().Single(s => s.Key == "final").CountsAsWritten);
    }

    [Fact]
    public async Task TheWritersOwnStagesReplaceTheDefaults()
    {
        await BookAsync();

        var saved = await _sut.SetStagesAsync([
            new SceneStage { Key = "beta", Label = "Needs a beta read", Color = "#ff0000" }
        ]);

        Assert.Equal(["beta"], saved.Select(s => s.Key));
    }

    [Fact]
    public async Task StagesSurviveAReload()
    {
        await BookAsync();
        await _sut.SetStagesAsync([new SceneStage { Key = "beta", Label = "Beta" }]);
        var root = _projects.ProjectRoot!;

        var reopened = new ProjectService(new FileService());
        await reopened.LoadProjectAsync(root);

        Assert.Equal(["beta"], new SceneStageService(reopened).Stages().Select(s => s.Key));
    }

    [Theory]
    [InlineData("", "Label")]
    [InlineData("key", "")]
    [InlineData("   ", "Label")]
    public async Task AStageMissingAKeyOrALabelIsDropped(string key, string label)
    {
        await BookAsync();

        var saved = await _sut.SetStagesAsync([
            new SceneStage { Key = key, Label = label },
            new SceneStage { Key = "good", Label = "Good" }
        ]);

        Assert.Equal(["good"], saved.Select(s => s.Key));
    }

    [Fact]
    public async Task ADuplicateKeyIsDroppedRatherThanShadowingTheFirst()
    {
        // Two stages sharing a key would make a scene's stage ambiguous.
        await BookAsync();

        var saved = await _sut.SetStagesAsync([
            new SceneStage { Key = "draft", Label = "First" },
            new SceneStage { Key = "DRAFT", Label = "Second" }
        ]);

        Assert.Equal(["First"], saved.Select(s => s.Label));
    }

    [Fact]
    public async Task AStageWithNoColourGetsADefaultOne()
    {
        await BookAsync();

        var saved = await _sut.SetStagesAsync([new SceneStage { Key = "k", Label = "L", Color = " " }]);

        Assert.False(string.IsNullOrWhiteSpace(saved.Single().Color));
    }

    [Fact]
    public async Task ClearingEveryStageFallsBackToTheDefaults()
    {
        // A writer who deletes the lot should not be left with no way to stage
        // anything at all.
        await BookAsync();

        var saved = await _sut.SetStagesAsync([]);

        Assert.Equal(5, saved.Count);
    }

    [Fact]
    public async Task SettingStagesWithNoBookOpenThrows()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SetStagesAsync([new SceneStage { Key = "k", Label = "L" }]));
    }

    // ── A scene's stage ──

    [Fact]
    public async Task ASceneStartsWithNoStage()
    {
        var (chapter, a, _) = await BookAsync();

        Assert.Null(_projects.GetScenesForChapter(chapter).First(s => s.Id == a.Id).Stage);
    }

    [Fact]
    public async Task SettingASceneStageRoundTrips()
    {
        var (chapter, a, _) = await BookAsync();

        await _sut.SetSceneStageAsync(chapter, a.Id, "revised");

        Assert.Equal("revised", _projects.GetScenesForChapter(chapter).First(s => s.Id == a.Id).Stage);
    }

    [Fact]
    public async Task AKeyThatNamesNoStageClearsItRatherThanDangling()
    {
        var (chapter, a, _) = await BookAsync();
        await _sut.SetSceneStageAsync(chapter, a.Id, "revised");

        await _sut.SetSceneStageAsync(chapter, a.Id, "no-such-stage");

        Assert.Null(_projects.GetScenesForChapter(chapter).First(s => s.Id == a.Id).Stage);
    }

    [Fact]
    public async Task SettingTheStageOfASceneThatIsGoneDoesNothing()
    {
        var (chapter, _, _) = await BookAsync();

        await _sut.SetSceneStageAsync(chapter, "no-such-scene", "revised");

        Assert.All(_projects.GetScenesForChapter(chapter), s => Assert.Null(s.Stage));
    }

    // ── The breakdown ──

    [Fact]
    public async Task TheBreakdownCountsScenesAndWordsPerStage()
    {
        var (chapter, a, b) = await BookAsync();
        a.WordCount = 100;
        b.WordCount = 250;
        await _sut.SetSceneStageAsync(chapter, a.Id, "final");
        await _sut.SetSceneStageAsync(chapter, b.Id, "final");

        var final = _sut.Breakdown().Single(t => t.Key == "final");

        Assert.Equal(2, final.SceneCount);
        Assert.Equal(350, final.WordCount);
    }

    [Fact]
    public async Task EveryStageAppearsEvenWithNothingAtIt()
    {
        // A stage with a zero beside it is information; a stage missing from
        // the list looks like it was deleted.
        await BookAsync();

        Assert.Contains(_sut.Breakdown(), t => t.Key == "revised" && t.SceneCount == 0);
    }

    [Fact]
    public async Task ScenesWithNoStageAreReportedSeparately()
    {
        var (chapter, a, _) = await BookAsync();
        a.WordCount = 40;
        await _sut.SetSceneStageAsync(chapter, a.Id, "final");

        var untriaged = _sut.Breakdown().Single(t => t.Key.Length == 0);

        Assert.Equal(1, untriaged.SceneCount);
    }

    [Fact]
    public async Task NoUntriagedRowWhenEverySceneHasAStage()
    {
        var (chapter, a, b) = await BookAsync();
        await _sut.SetSceneStageAsync(chapter, a.Id, "final");
        await _sut.SetSceneStageAsync(chapter, b.Id, "final");

        Assert.DoesNotContain(_sut.Breakdown(), t => t.Key.Length == 0);
    }

    [Fact]
    public async Task TheBreakdownKeepsTheWritersStageOrder()
    {
        await BookAsync();
        await _sut.SetStagesAsync([
            new SceneStage { Key = "z", Label = "Last thing" },
            new SceneStage { Key = "a", Label = "First thing" }
        ]);

        // The untriaged row trails the writer's stages rather than sorting in
        // among them.
        Assert.Equal(["z", "a", ""], _sut.Breakdown().Select(t => t.Key));
    }

    // ── Written words ──

    [Fact]
    public async Task WordsAtAStageThatDoesNotCountAreLeftOut()
    {
        var (chapter, a, b) = await BookAsync();
        a.WordCount = 500;
        b.WordCount = 300;
        await _sut.SetSceneStageAsync(chapter, a.Id, "outline");
        await _sut.SetSceneStageAsync(chapter, b.Id, "final");

        Assert.Equal(300, _sut.WrittenWords());
    }

    [Fact]
    public async Task AnUntriagedSceneCountsAsWritten()
    {
        // Otherwise every project's totals would drop the moment stages arrived,
        // which is a scary number to see for no reason the writer caused.
        var (chapter, a, _) = await BookAsync();
        a.WordCount = 500;
        await _sut.SetSceneStageAsync(chapter, a.Id, null);

        Assert.Equal(500, _sut.WrittenWords());
    }
}
