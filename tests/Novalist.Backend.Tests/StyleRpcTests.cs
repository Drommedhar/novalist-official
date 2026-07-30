using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Craft reports over a scene, a chapter, and the whole book.</summary>
public sealed class StyleRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly StyleRpc _rpc;
    private readonly string _chapterGuid;
    private readonly string _sceneId;

    public StyleRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-style-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "StyleNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();

        var chapter = _workspace.Projects.CreateChapterAsync("One").GetAwaiter().GetResult();
        var scene = _workspace.Projects.CreateSceneAsync(chapter.Guid, "S").GetAwaiter().GetResult();
        _chapterGuid = chapter.Guid;
        _sceneId = scene.Id;
        _workspace.WriteSceneAsync(chapter.Guid, scene.Id,
            "<p>She walked slowly. She saw the door. The door was opened by the guard.</p>",
            "She walked slowly. She saw the door. The door was opened by the guard.")
            .GetAwaiter().GetResult();

        _rpc = new StyleRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static StyleFindingDto Find(StyleReportDto r, string key) =>
        r.Findings.Single(f => f.Key == key);

    [Fact]
    public async Task Scene_ReportsAdverbsFilterWordsAndPassive()
    {
        var report = await _rpc.SceneAsync(_chapterGuid, _sceneId);

        Assert.True(report.WordCount > 0);
        Assert.Equal(3, report.SentenceCount);
        Assert.Equal(1, Find(report, "adverbs").Count);
        Assert.Equal(1, Find(report, "filterWords").Count);
        Assert.Equal(1, Find(report, "passiveVoice").Count);
    }

    [Fact]
    public async Task Scene_StripsMarkupBeforeCounting()
    {
        // "p" and "strong" must not be counted as words.
        await _workspace.WriteSceneAsync(_chapterGuid, _sceneId,
            "<p><strong>One</strong> two.</p>", "One two.");

        var report = await _rpc.SceneAsync(_chapterGuid, _sceneId);

        Assert.Equal(2, report.WordCount);
    }

    [Fact]
    public async Task Book_CoversEveryChapterWhenNoFilterGiven()
    {
        var second = await _workspace.Projects.CreateChapterAsync("Two");
        var scene = await _workspace.Projects.CreateSceneAsync(second.Guid, "S2");
        await _workspace.WriteSceneAsync(second.Guid, scene.Id,
            "<p>He moved quickly and quietly.</p>", "He moved quickly and quietly.");

        var report = await _rpc.BookAsync();

        // One adverb from chapter one, two from chapter two.
        Assert.Equal(3, Find(report, "adverbs").Count);
    }

    [Fact]
    public async Task Book_WithChapterFilter_CoversOnlyThatChapter()
    {
        var second = await _workspace.Projects.CreateChapterAsync("Two");
        var scene = await _workspace.Projects.CreateSceneAsync(second.Guid, "S2");
        await _workspace.WriteSceneAsync(second.Guid, scene.Id,
            "<p>He moved quickly and quietly.</p>", "He moved quickly and quietly.");

        var report = await _rpc.BookAsync(second.Guid);

        Assert.Equal(2, Find(report, "adverbs").Count);
    }

    [Fact]
    public async Task Book_LanguageFollowsTheWritingLanguage()
    {
        _workspace.Settings.Settings.AutoReplacementLanguage = "de-low";

        var report = await _rpc.BookAsync();

        Assert.Equal("de", report.Language);
        // German has no adverb suffix list, so that report is unsupported
        // rather than silently zero.
        Assert.False(Find(report, "adverbs").Supported);
    }

    [Fact]
    public async Task Book_ExamplesCarryContext()
    {
        var report = await _rpc.BookAsync();
        var adverbs = Find(report, "adverbs");

        Assert.NotEmpty(adverbs.Examples);
        Assert.False(string.IsNullOrWhiteSpace(adverbs.Examples[0].Context));
    }

    // ── The writer's own flagged words ──

    [Fact]
    public async Task WatchWords_RoundTripAndReachTheReport()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        await _workspace.WriteSceneAsync(chapter.Guid, scene.Id,
            "<p>Suddenly it closed. Suddenly it opened.</p>",
            "Suddenly it closed. Suddenly it opened.");

        var saved = await _rpc.SetWatchWordsAsync(["  suddenly  ", "SUDDENLY", "  "]);

        // Blanks dropped, repeats counted once - a repeat would count the same
        // word twice in the report.
        Assert.Equal(["suddenly"], saved);
        Assert.Equal(["suddenly"], await _rpc.GetWatchWordsAsync());

        var report = await _rpc.SceneAsync(chapter.Guid, scene.Id);
        var finding = Assert.Single(report.Findings, f => f.Key == "watchWords");
        Assert.Equal(2, finding.Count);
    }

    [Fact]
    public async Task WatchWords_WithNoListTheReportHasNoSuchRow()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        await _workspace.WriteSceneAsync(chapter.Guid, scene.Id, "<p>Suddenly.</p>", "Suddenly.");

        Assert.DoesNotContain(
            (await _rpc.SceneAsync(chapter.Guid, scene.Id)).Findings,
            f => f.Key == "watchWords");
    }

    [Fact]
    public void SentenceReadability_GradesEachSentenceAtItsOwnOffset()
    {
        const string text = "The cat sat on the mat. Notwithstanding the aforementioned "
            + "considerations, the extraordinarily convoluted bureaucratic procedures "
            + "necessitated substantial reconsideration of every prior determination.";

        var graded = _rpc.SentenceReadabilityAsync(text);

        Assert.Equal(2, graded.Length);
        Assert.Equal("The cat sat on the mat.", text.Substring(graded[0].Offset, graded[0].Length));
        Assert.NotEqual(graded[0].Level, graded[1].Level);
    }

    [Fact]
    public void SentenceReadability_NoText_IsEmpty()
        => Assert.Empty(_rpc.SentenceReadabilityAsync(null));

    // ─── Point of view ───────────────────────────────────────────────

    /// <summary>Writes the scene and marks whose head it is in.</summary>
    private async Task SetSceneAsync(string prose, string? pov)
    {
        await _workspace.WriteSceneAsync(_chapterGuid, _sceneId, $"<p>{prose}</p>", prose);
        var (_, scene) = _workspace.ResolveScene(_chapterGuid, _sceneId);
        scene.AnalysisOverrides = pov == null
            ? null
            : new Novalist.Core.Models.SceneAnalysisOverrides { Pov = pov };
        await _workspace.Projects.SaveScenesAsync();
    }

    private async Task AddCastAsync(params string[] names)
    {
        var entities = new Novalist.Core.Services.EntityService(_workspace.Projects);
        foreach (var name in names)
            await entities.SaveCharacterAsync(new Novalist.Core.Models.CharacterData { Name = name });
    }

    [Fact]
    public async Task PovCheck_FindsSomebodyElsesHead()
    {
        await AddCastAsync("Mira", "Tomas");
        await SetSceneAsync("She crossed the yard. Tomas knew she would not come back.", "Mira");

        var report = await _rpc.PovCheckAsync(_chapterGuid, _sceneId);

        Assert.True(report.Checked);
        Assert.Equal("Mira", report.Pov);
        Assert.Equal("Tomas", Assert.Single(report.Slips).Name);
    }

    [Fact]
    public async Task PovCheck_ThePovCharacterThinkingIsFine()
    {
        await AddCastAsync("Mira", "Tomas");
        await SetSceneAsync("Mira knew she would not come back.", "Mira");

        Assert.Empty((await _rpc.PovCheckAsync(_chapterGuid, _sceneId)).Slips);
    }

    [Fact]
    public async Task PovCheck_ASceneWithNoPovSaysSoRatherThanReportingClean()
    {
        await AddCastAsync("Mira", "Tomas");
        await SetSceneAsync("Tomas knew everything.", null);

        var report = await _rpc.PovCheckAsync(_chapterGuid, _sceneId);

        // A zero from a check that never ran reads as a clean scene.
        Assert.False(report.Checked);
        Assert.Equal("noPov", report.SkippedBecause);
    }

    [Fact]
    public async Task PovCheck_AnAliasCountsAsTheName()
    {
        var entities = new Novalist.Core.Services.EntityService(_workspace.Projects);
        await entities.SaveCharacterAsync(new Novalist.Core.Models.CharacterData { Name = "Mira" });
        await entities.SaveCharacterAsync(new Novalist.Core.Models.CharacterData
        {
            Name = "Tomas",
            Aliases = ["the steward"]
        });
        await SetSceneAsync("She crossed the yard. The steward knew she would not return.", "Mira");

        // A character named by their role is still that character, and the
        // slip reads exactly the same to a reader.
        Assert.Single((await _rpc.PovCheckAsync(_chapterGuid, _sceneId)).Slips);
    }
}
