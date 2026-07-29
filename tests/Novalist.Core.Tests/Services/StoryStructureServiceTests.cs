using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Binding a story structure to the manuscript.
///
/// Applying a template used to append timeline events that by design never
/// touched a chapter or a scene. What these assert is the relationship that was
/// missing: which beats are holes, and whether the midpoint lands in the middle.
/// </summary>
public class StoryStructureServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProjectService _projects = new(new FileService());
    private readonly StoryStructureService _sut;

    public StoryStructureServiceTests()
    {
        _sut = new StoryStructureService(_projects);
    }

    public void Dispose() => _dir.Dispose();

    /// <summary>A book of scenes with the given word counts, in one chapter.</summary>
    private async Task<(string Chapter, List<SceneData> Scenes)> BookAsync(params int[] wordCounts)
    {
        await _projects.CreateProjectAsync(_dir.Path, "P", "Book");
        var chapter = await _projects.CreateChapterAsync("One");
        var scenes = new List<SceneData>();
        for (var i = 0; i < wordCounts.Length; i++)
        {
            var scene = await _projects.CreateSceneAsync(chapter.Guid, $"Scene {i + 1}");
            scene.WordCount = wordCounts[i];
            scenes.Add(scene);
        }
        return (chapter.Guid, scenes);
    }

    // ── Choosing a structure ──

    [Fact]
    public async Task ABookStartsWithNoStructure()
    {
        await BookAsync(100);

        Assert.Null(_sut.ActiveTemplate());
        Assert.Empty(_sut.Beats());
    }

    [Fact]
    public async Task ChoosingAStructureListsAllOfItsBeats()
    {
        await BookAsync(100);

        await _sut.SetTemplateAsync("three-act");

        Assert.Equal(8, _sut.Beats().Count);
        Assert.Contains(_sut.Beats(), b => b.Title == "Midpoint");
    }

    [Fact]
    public async Task AnUnknownStructureClearsItRatherThanDangling()
    {
        await BookAsync(100);
        await _sut.SetTemplateAsync("three-act");

        await _sut.SetTemplateAsync("no-such-structure");

        Assert.Null(_sut.ActiveTemplate());
    }

    [Fact]
    public async Task TheChoiceSurvivesAReload()
    {
        await BookAsync(100);
        await _sut.SetTemplateAsync("save-the-cat");
        var root = _projects.ProjectRoot!;

        var reopened = new ProjectService(new FileService());
        await reopened.LoadProjectAsync(root);

        Assert.Equal("save-the-cat", new StoryStructureService(reopened).ActiveTemplate()!.Id);
    }

    [Fact]
    public async Task ChoosingWithNoBookOpenDoesNothing()
    {
        await _sut.SetTemplateAsync("three-act");

        Assert.Null(_sut.ActiveTemplate());
    }

    // ── Binding scenes ──

    [Fact]
    public async Task ABeatWithNoSceneIsAHole()
    {
        await BookAsync(100);
        await _sut.SetTemplateAsync("three-act");

        var beat = _sut.Beats().First();

        Assert.False(beat.IsFilled);
        // Not zero: a hole and a beat at the very start are different things.
        Assert.Equal(-1, beat.ActualPercent);
        Assert.Equal(0, beat.DriftPercent);
    }

    [Fact]
    public async Task BindingASceneFillsTheBeat()
    {
        var (chapter, scenes) = await BookAsync(100, 100);
        await _sut.SetTemplateAsync("three-act");
        var key = _sut.Beats().First().Key;

        await _sut.SetSceneBeatAsync(chapter, scenes[0].Id, key);

        var beat = _sut.Beats().First();
        Assert.True(beat.IsFilled);
        Assert.Equal(scenes[0].Id, beat.SceneId);
        Assert.Equal("Scene 1", beat.SceneTitle);
    }

    [Fact]
    public async Task ABeatCanOnlyBeClaimedOnce()
    {
        // Two scenes cannot both be the midpoint.
        var (chapter, scenes) = await BookAsync(100, 100);
        await _sut.SetTemplateAsync("three-act");
        var key = _sut.Beats().First().Key;
        await _sut.SetSceneBeatAsync(chapter, scenes[0].Id, key);

        await _sut.SetSceneBeatAsync(chapter, scenes[1].Id, key);

        Assert.Equal(scenes[1].Id, _sut.Beats().First().SceneId);
        Assert.Null(scenes[0].BeatKey);
    }

    [Fact]
    public async Task BindingCanBeCleared()
    {
        var (chapter, scenes) = await BookAsync(100);
        await _sut.SetTemplateAsync("three-act");
        var key = _sut.Beats().First().Key;
        await _sut.SetSceneBeatAsync(chapter, scenes[0].Id, key);

        await _sut.SetSceneBeatAsync(chapter, scenes[0].Id, null);

        Assert.False(_sut.Beats().First().IsFilled);
    }

    [Fact]
    public async Task ABeatKeyThatNamesNothingClearsRatherThanDangles()
    {
        var (chapter, scenes) = await BookAsync(100);
        await _sut.SetTemplateAsync("three-act");

        await _sut.SetSceneBeatAsync(chapter, scenes[0].Id, "not-a-beat");

        Assert.Null(scenes[0].BeatKey);
    }

    [Fact]
    public async Task BindingASceneThatIsGoneDoesNothing()
    {
        var (chapter, _) = await BookAsync(100);
        await _sut.SetTemplateAsync("three-act");

        await _sut.SetSceneBeatAsync(chapter, "no-such-scene", _sut.Beats().First().Key);

        Assert.All(_sut.Beats(), b => Assert.False(b.IsFilled));
    }

    // ── Where a beat lands ──

    [Fact]
    public async Task PositionIsMeasuredInWordsRatherThanSceneCount()
    {
        // "The midpoint" means halfway through the reading, not halfway down
        // the scene list - a book of three long scenes and twenty short ones
        // does not turn over at scene eleven.
        var (chapter, scenes) = await BookAsync(900, 50, 50);
        await _sut.SetTemplateAsync("three-act");
        var key = _sut.Beats().First(b => b.Title == "Midpoint").Key;

        await _sut.SetSceneBeatAsync(chapter, scenes[1].Id, key);

        // Scene 2 starts after 900 of 1000 words.
        Assert.Equal(90, _sut.Beats().First(b => b.Title == "Midpoint").ActualPercent);
    }

    [Fact]
    public async Task DriftIsTheDistanceFromWhereTheStructureSaysTheBeatBelongs()
    {
        var (chapter, scenes) = await BookAsync(900, 100);
        await _sut.SetTemplateAsync("three-act");
        var midpoint = _sut.Beats().First(b => b.Title == "Midpoint");

        await _sut.SetSceneBeatAsync(chapter, scenes[1].Id, midpoint.Key);

        var after = _sut.Beats().First(b => b.Title == "Midpoint");
        Assert.Equal(50, after.TargetPercent);
        Assert.Equal(90, after.ActualPercent);
        Assert.Equal(40, after.DriftPercent);
    }

    [Fact]
    public async Task ABeatEarlierThanExpectedDriftsNegative()
    {
        var (chapter, scenes) = await BookAsync(100, 900);
        await _sut.SetTemplateAsync("three-act");
        var midpoint = _sut.Beats().First(b => b.Title == "Midpoint");

        await _sut.SetSceneBeatAsync(chapter, scenes[0].Id, midpoint.Key);

        Assert.True(_sut.Beats().First(b => b.Title == "Midpoint").DriftPercent < 0);
    }

    [Fact]
    public async Task ABookWithNoWordsYetReportsZeroRatherThanDividingByNothing()
    {
        var (chapter, scenes) = await BookAsync(0, 0);
        await _sut.SetTemplateAsync("three-act");
        var key = _sut.Beats().First().Key;

        await _sut.SetSceneBeatAsync(chapter, scenes[0].Id, key);

        Assert.Equal(0, _sut.Beats().First().ActualPercent);
    }

    // ── Filling the gaps ──

    [Fact]
    public async Task FillGapsCreatesAPlaceholderForEveryUnfilledBeat()
    {
        var (chapter, _) = await BookAsync(100);
        await _sut.SetTemplateAsync("three-act");

        var created = await _sut.FillGapsAsync();

        Assert.Equal(8, created);
        Assert.All(_sut.Beats(), b => Assert.True(b.IsFilled));
        Assert.Equal(9, _projects.GetScenesForChapter(chapter).Count);
    }

    [Fact]
    public async Task APlaceholderCarriesTheBeatsDescriptionAsItsSynopsis()
    {
        await BookAsync(100);
        await _sut.SetTemplateAsync("three-act");

        await _sut.FillGapsAsync();

        var midpoint = _sut.Beats().First(b => b.Title == "Midpoint");
        var scene = _projects.GetScenesForChapter(
            _projects.GetChaptersOrdered().Single().Guid).First(s => s.Id == midpoint.SceneId);
        Assert.Equal(midpoint.Description, scene.Synopsis);
    }

    [Fact]
    public async Task FillGapsLeavesBeatsThatAlreadyHaveASceneAlone()
    {
        var (chapter, scenes) = await BookAsync(100);
        await _sut.SetTemplateAsync("three-act");
        var key = _sut.Beats().First().Key;
        await _sut.SetSceneBeatAsync(chapter, scenes[0].Id, key);

        Assert.Equal(7, await _sut.FillGapsAsync());
        Assert.Equal(scenes[0].Id, _sut.Beats().First().SceneId);
    }

    [Fact]
    public async Task FillGapsWithNoChapterToPutThemInDoesNothing()
    {
        await _projects.CreateProjectAsync(_dir.Path, "P", "Book");
        await _sut.SetTemplateAsync("three-act");

        Assert.Equal(0, await _sut.FillGapsAsync());
    }

    // ── Beat keys ──

    [Fact]
    public void ABeatWithoutAnExplicitKeyGetsASlugOfItsTitle()
    {
        Assert.Equal(
            "opening-image",
            StoryStructureBeatKeys.For(new StoryStructureBeat { Title = "Opening Image" }));
    }

    [Fact]
    public void AnExplicitKeyWins()
    {
        Assert.Equal(
            "mine",
            StoryStructureBeatKeys.For(new StoryStructureBeat { Key = " mine ", Title = "Whatever" }));
    }

    [Fact]
    public void PunctuationCollapsesRatherThanRepeating()
    {
        Assert.Equal(
            "fun-and-games",
            StoryStructureBeatKeys.For(new StoryStructureBeat { Title = "Fun -- and Games!" }));
    }

    [Fact]
    public void EveryBundledBeatHasAUniqueKeyWithinItsTemplate()
    {
        // A duplicate would make two beats fight over the same scene binding.
        foreach (var template in StoryStructureTemplates.All)
        {
            var keys = template.Beats.Select(StoryStructureBeatKeys.For).ToList();
            Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        }
    }
}
