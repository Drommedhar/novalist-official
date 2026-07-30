using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Covers the analysis-override and story-date surface added to
/// <see cref="ScenesRpc"/> for the Inspector.
/// </summary>
public sealed class ScenesRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly ScenesRpc _rpc;

    public ScenesRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-scenes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "SceneNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new ScenesRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private async Task<(string chapterGuid, string sceneId)> CreateSceneAsync()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        return (chapter.Guid, scene.Id);
    }

    private SceneAnalysisOverrides? Overrides(string chapterGuid, string sceneId)
        => _workspace.ResolveScene(chapterGuid, sceneId).scene.AnalysisOverrides;

    // ----- getMeta story date -----

    [Fact]
    public async Task GetMeta_NoDates_ReturnsEmptyStoryDateAndNullIso()
    {
        var (chapterGuid, sceneId) = await CreateSceneAsync();

        var meta = _rpc.GetMeta(chapterGuid, sceneId);

        Assert.Equal(string.Empty, meta.StoryDate);
        Assert.Null(meta.IsoDate);
    }

    [Fact]
    public async Task GetMeta_SceneDate_ExtractsLeadingIso()
    {
        var (chapterGuid, sceneId) = await CreateSceneAsync();
        _workspace.ResolveScene(chapterGuid, sceneId).scene.Date = "2024-10-22";

        var meta = _rpc.GetMeta(chapterGuid, sceneId);

        Assert.Equal("2024-10-22", meta.StoryDate);
        Assert.Equal("2024-10-22", meta.IsoDate);
    }

    [Fact]
    public async Task GetMeta_SceneDateRange_TakesPrecedence()
    {
        var (chapterGuid, sceneId) = await CreateSceneAsync();
        var (_, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        scene.Date = "2024-01-01";
        scene.DateRange = new StoryDateRange { Start = "2024-10-22", StartTime = "18:00" };

        var meta = _rpc.GetMeta(chapterGuid, sceneId);

        Assert.Equal("2024-10-22 18:00", meta.StoryDate);
        Assert.Equal("2024-10-22", meta.IsoDate);
    }

    [Fact]
    public async Task GetMeta_ChapterDateRange_UsedWhenSceneHasNoDate()
    {
        var (chapterGuid, sceneId) = await CreateSceneAsync();
        _workspace.ResolveChapter(chapterGuid).DateRange = new StoryDateRange { Start = "2030-05-01" };

        var meta = _rpc.GetMeta(chapterGuid, sceneId);

        Assert.Equal("2030-05-01", meta.StoryDate);
        Assert.Equal("2030-05-01", meta.IsoDate);
    }

    [Fact]
    public async Task GetMeta_ChapterDate_UsedAsFinalFallback()
    {
        var (chapterGuid, sceneId) = await CreateSceneAsync();
        _workspace.ResolveChapter(chapterGuid).Date = "spring, third age";

        var meta = _rpc.GetMeta(chapterGuid, sceneId);

        Assert.Equal("spring, third age", meta.StoryDate);
        Assert.Null(meta.IsoDate);
    }

    // ----- setAnalysisOverride -----

    [Fact]
    public async Task SetAnalysisOverride_AllFields_PersistAndClamp()
    {
        var (chapterGuid, sceneId) = await CreateSceneAsync();

        await _rpc.SetAnalysisOverrideAsync(chapterGuid, sceneId,
            new AnalysisOverrideDto("Mira", " tense ", 999, "  duel  ", ["a", "  ", "b "]));

        var o = Overrides(chapterGuid, sceneId);
        Assert.NotNull(o);
        Assert.Equal("Mira", o!.Pov);
        Assert.Equal("tense", o.Emotion);
        Assert.Equal(10, o.Intensity);
        Assert.Equal("duel", o.Conflict);
        Assert.Equal(new[] { "a", "b" }, o.Tags);
    }

    [Fact]
    public async Task SetAnalysisOverride_MergesOntoExisting_LeavingOthersIntact()
    {
        var (chapterGuid, sceneId) = await CreateSceneAsync();
        await _rpc.SetAnalysisOverrideAsync(chapterGuid, sceneId,
            new AnalysisOverrideDto("Mira", null, null, null, null));

        await _rpc.SetAnalysisOverrideAsync(chapterGuid, sceneId,
            new AnalysisOverrideDto(null, null, -20, null, null));

        var o = Overrides(chapterGuid, sceneId);
        Assert.Equal("Mira", o!.Pov);
        Assert.Equal(-10, o.Intensity);
    }

    [Fact]
    public async Task SetAnalysisOverride_EmptyPatchOnCleanScene_LeavesNoOverride()
    {
        var (chapterGuid, sceneId) = await CreateSceneAsync();

        await _rpc.SetAnalysisOverrideAsync(chapterGuid, sceneId,
            new AnalysisOverrideDto(null, null, null, null, null));

        Assert.Null(Overrides(chapterGuid, sceneId));
    }

    // ----- resetAnalysisOverride -----

    [Fact]
    public async Task ResetAnalysisOverride_NoExistingOverride_IsNoOp()
    {
        var (chapterGuid, sceneId) = await CreateSceneAsync();

        await _rpc.ResetAnalysisOverrideAsync(chapterGuid, sceneId, "pov");

        Assert.Null(Overrides(chapterGuid, sceneId));
    }

    [Fact]
    public async Task ResetAnalysisOverride_UnknownField_LeavesOverridesUntouched()
    {
        var (chapterGuid, sceneId) = await CreateSceneAsync();
        await _rpc.SetAnalysisOverrideAsync(chapterGuid, sceneId,
            new AnalysisOverrideDto("Mira", null, null, null, null));

        await _rpc.ResetAnalysisOverrideAsync(chapterGuid, sceneId, "bogus");

        Assert.Equal("Mira", Overrides(chapterGuid, sceneId)!.Pov);
    }

    [Fact]
    public async Task ResetAnalysisOverride_ClearsEachField_AndDropsWhenEmpty()
    {
        var (chapterGuid, sceneId) = await CreateSceneAsync();
        await _rpc.SetAnalysisOverrideAsync(chapterGuid, sceneId,
            new AnalysisOverrideDto("Mira", "tense", 5, "duel", ["a"]));

        await _rpc.ResetAnalysisOverrideAsync(chapterGuid, sceneId, "emotion");
        var afterEmotion = Overrides(chapterGuid, sceneId)!;
        Assert.Null(afterEmotion.Emotion);
        Assert.Equal("Mira", afterEmotion.Pov); // still present -> saved non-null

        await _rpc.ResetAnalysisOverrideAsync(chapterGuid, sceneId, "pov");
        Assert.Null(Overrides(chapterGuid, sceneId)!.Pov);

        await _rpc.ResetAnalysisOverrideAsync(chapterGuid, sceneId, "intensity");
        Assert.Null(Overrides(chapterGuid, sceneId)!.Intensity);

        await _rpc.ResetAnalysisOverrideAsync(chapterGuid, sceneId, "conflict");
        Assert.Null(Overrides(chapterGuid, sceneId)!.Conflict);

        await _rpc.ResetAnalysisOverrideAsync(chapterGuid, sceneId, "tags");
        // Last remaining field cleared -> whole override object dropped.
        Assert.Null(Overrides(chapterGuid, sceneId));
    }

    // ----- explicit cast -----

    [Fact]
    public async Task SetCast_RoundTripsAndDedupes()
    {
        var (chapterGuid, sceneId) = await CreateSceneAsync();

        var meta = await _rpc.SetCastAsync(
            chapterGuid, sceneId, ["char-1", "char-1", "  ", "loc-1"], "char-1");

        Assert.Equal(["char-1", "loc-1"], meta.Cast);
        Assert.Equal("char-1", meta.FocusEntityId);
        Assert.Equal(["char-1", "loc-1"], _rpc.GetMeta(chapterGuid, sceneId).Cast);
    }

    [Fact]
    public async Task SetCast_AFocusOutsideTheCastIsNotKept()
    {
        var (chapterGuid, sceneId) = await CreateSceneAsync();

        // A focus nobody put in the scene is a dangling reference that no
        // surface could resolve.
        var meta = await _rpc.SetCastAsync(chapterGuid, sceneId, ["char-1"], "char-9");

        Assert.Null(meta.FocusEntityId);
    }

    [Fact]
    public async Task SetCast_RemovingTheFocusFromTheCastClearsIt()
    {
        var (chapterGuid, sceneId) = await CreateSceneAsync();
        await _rpc.SetCastAsync(chapterGuid, sceneId, ["char-1", "char-2"], "char-2");

        var meta = await _rpc.SetCastAsync(chapterGuid, sceneId, ["char-1"], "char-2");

        Assert.Equal(["char-1"], meta.Cast);
        Assert.Null(meta.FocusEntityId);
    }

    [Fact]
    public async Task SetCast_AnEmptyCastIsNoCast()
    {
        var (chapterGuid, sceneId) = await CreateSceneAsync();
        await _rpc.SetCastAsync(chapterGuid, sceneId, ["char-1"], "char-1");

        var meta = await _rpc.SetCastAsync(chapterGuid, sceneId, null, null);

        Assert.Empty(meta.Cast);
        Assert.Null(meta.FocusEntityId);
    }

    // ----- how a scene sits in time -----

    [Fact]
    public async Task SetNarrativeMode_RoundTrips()
    {
        var (chapterGuid, sceneId) = await CreateSceneAsync();

        var meta = await _rpc.SetNarrativeModeAsync(chapterGuid, sceneId, " flashback ", null);

        Assert.Equal("flashback", meta.NarrativeMode);
        Assert.Equal("flashback", _rpc.GetMeta(chapterGuid, sceneId).NarrativeMode);
    }

    [Fact]
    public async Task SetNarrativeMode_AStrandOnlySticksToAParallelScene()
    {
        var (chapterGuid, sceneId) = await CreateSceneAsync();

        // A strand on a scene that is not running alongside another would put
        // it in a lane with no thread to be part of.
        Assert.Null((await _rpc.SetNarrativeModeAsync(
            chapterGuid, sceneId, "flashback", "the siege")).Strand);
        Assert.Equal("the siege", (await _rpc.SetNarrativeModeAsync(
            chapterGuid, sceneId, "parallel", " the siege ")).Strand);
        // Changing away from parallel drops the strand with it.
        Assert.Null((await _rpc.SetNarrativeModeAsync(
            chapterGuid, sceneId, "dream", "the siege")).Strand);
    }

    [Fact]
    public async Task SetNarrativeMode_BlankIsNoMode()
    {
        var (chapterGuid, sceneId) = await CreateSceneAsync();
        await _rpc.SetNarrativeModeAsync(chapterGuid, sceneId, "flashback", null);

        var meta = await _rpc.SetNarrativeModeAsync(chapterGuid, sceneId, "   ", null);

        Assert.Null(meta.NarrativeMode);
    }

    // ── What a scene wanted, and whether it is in the book at all ──

    [Fact]
    public async Task GoalAndOutcome_AreAuthoredAndComeBack()
    {
        var (chapterGuid, sceneId) = await CreateSceneAsync();

        var meta = await _rpc.SetGoalOutcomeAsync(
            chapterGuid, sceneId, "  Get the letter back  ", "She burns it instead");

        Assert.Equal("Get the letter back", meta.Goal);
        Assert.Equal("She burns it instead", meta.Outcome);
        Assert.Equal("Get the letter back", _rpc.GetMeta(chapterGuid, sceneId).Goal);
    }

    [Fact]
    public async Task GoalAndOutcome_BlankMeansUnanswered()
    {
        var (chapterGuid, sceneId) = await CreateSceneAsync();
        await _rpc.SetGoalOutcomeAsync(chapterGuid, sceneId, "Something", "Something else");

        // Cleared rather than stored as an empty string: "no outcome yet" is a
        // list a writer saves, and it has to be answerable.
        var meta = await _rpc.SetGoalOutcomeAsync(chapterGuid, sceneId, "   ", null);

        Assert.Null(meta.Goal);
        Assert.Null(meta.Outcome);
    }

    [Fact]
    public async Task InactiveScene_StaysInThePlanAndLeavesTheBook()
    {
        var (chapterGuid, sceneId) = await CreateSceneAsync();

        var meta = await _rpc.SetInactiveAsync(chapterGuid, sceneId, true);
        Assert.True(meta.Inactive);

        // Still in the binder - it is parked, not archived.
        var state = _workspace.BuildState();
        var scene = state.Chapters
            .Single(c => c.Guid == chapterGuid).Scenes
            .Single(s => s.Id == sceneId);
        Assert.True(scene.Inactive);

        Assert.False((await _rpc.SetInactiveAsync(chapterGuid, sceneId, false)).Inactive);
    }


    [Fact]
    public async Task RelativeTime_LetsASceneSayWhenWithoutSayingWhich()
    {
        var (chapterGuid, sceneId) = await CreateSceneAsync();

        var meta = await _rpc.SetRelativeTimeAsync(chapterGuid, sceneId, 2, "hours");
        Assert.Equal(2, meta.RelativeAmount);
        Assert.Equal("Hours", meta.RelativeUnit);

        // An unknown unit is hours: what a writer means most often by "later",
        // and the one that reads least wrong when guessed.
        Assert.Equal(
            "Hours",
            (await _rpc.SetRelativeTimeAsync(chapterGuid, sceneId, 3, "rhubarb")).RelativeUnit);

        // Zero is no statement at all rather than "zero minutes later".
        var cleared = await _rpc.SetRelativeTimeAsync(chapterGuid, sceneId, 0, "days");
        Assert.Equal(0, cleared.RelativeAmount);
        Assert.Equal(string.Empty, cleared.RelativeUnit);
    }

    [Fact]
    public async Task RelativeTime_PutsAnUndatedSceneOnTheTimeline()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var anchored = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Anchored");
        var later = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Later");
        anchored.Date = "1043-03-01";
        await _workspace.Projects.SaveScenesAsync();
        await _rpc.SetRelativeTimeAsync(chapter.Guid, later.Id, 1, "days");

        var timeline = await new TimelineRpc(_workspace).Get();
        var events = timeline.Groups.SelectMany(g => g.Events).ToList();

        // A scene that only said "the next day" used to fall out of the
        // timeline entirely, which is how a whole book ends up looking undated.
        Assert.Contains(events, e => e.Title.EndsWith("Later") && e.DateStr == "1043-03-02");
    }

}
