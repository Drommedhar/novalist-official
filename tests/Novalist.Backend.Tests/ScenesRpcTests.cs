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
}
