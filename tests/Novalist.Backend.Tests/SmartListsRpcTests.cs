using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class SmartListsRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly SmartListsRpc _rpc;

    public SmartListsRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-sl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "SmartNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new SmartListsRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static SmartListRuleDto Rule(string field, string op, string? value = null)
        => new(field, op, value);

    [Fact]
    public async Task SaveEvaluateUpdateDelete_FullFlow()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S1");

        var lists = await _rpc.SaveAsync(
            null, "Outline scenes", "All", [Rule("chapterStatus", "Is", "Outline")]);
        var list = lists.Single();
        Assert.Equal("Outline scenes", list.Name);
        Assert.Equal("All", list.Match);
        Assert.Equal("chapterStatus", list.Rules.Single().Field);

        var matches = await _rpc.EvaluateAsync(list.Id);
        Assert.Equal("S1", matches.Single().SceneTitle);

        var updated = await _rpc.SaveAsync(
            list.Id, "Final scenes", "All", [Rule("chapterStatus", "Is", "Final")]);
        Assert.Equal("Final scenes", updated.Single().Name);
        Assert.Empty(await _rpc.EvaluateAsync(list.Id));

        Assert.Empty(await _rpc.DeleteAsync(list.Id));
    }

    [Fact]
    public async Task Save_DropsBlankFields_AndFallsBackOnNonsense()
    {
        var saved = await _rpc.SaveAsync(null, "Odd", "sideways", [
            Rule("  ", "Is", "x"),
            Rule("title", "Rhubarb", " a ")
        ]);

        var list = saved.Single();
        // An unparseable match or comparison falls back rather than throwing:
        // the renderer is not the only possible caller.
        Assert.Equal("All", list.Match);
        Assert.Equal("Contains", list.Rules.Single().Op);
        Assert.Equal("a", list.Rules.Single().Value);
    }

    [Fact]
    public async Task Save_WithNoRules_KeepsTheWholeBook()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S1");
        await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S2");

        var list = (await _rpc.SaveAsync(null, "Everything", "All", null!)).Single();

        Assert.Empty(list.Rules);
        Assert.Equal(2, (await _rpc.EvaluateAsync(list.Id)).Length);
    }

    [Fact]
    public async Task AnyMatch_FindsScenesOnEitherSide()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        await _workspace.Projects.CreateSceneAsync(chapter.Guid, "The vault");
        await _workspace.Projects.CreateSceneAsync(chapter.Guid, "The rooftop");
        await _workspace.Projects.CreateSceneAsync(chapter.Guid, "The kitchen");

        var list = (await _rpc.SaveAsync(null, "Either", "Any", [
            Rule("title", "Contains", "vault"),
            Rule("title", "Contains", "rooftop")
        ])).Single();

        Assert.Equal(
            ["The vault", "The rooftop"],
            (await _rpc.EvaluateAsync(list.Id)).Select(m => m.SceneTitle));
    }

    [Fact]
    public async Task Fields_OfferThisBooksOwnVocabulary()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        chapter.Act = "Act One";
        _workspace.Projects.ActiveBook!.Plotlines.Add(
            new Novalist.Core.Models.PlotlineData { Id = "p1", Name = "The heist" });
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        scene.AnalysisOverrides = new Novalist.Core.Models.SceneAnalysisOverrides
        {
            Tags = ["action", "quiet"]
        };
        scene.Cast = ["halden", "mira"];
        scene.FocusEntityId = "mira";
        await _workspace.Projects.SaveScenesAsync();
        await new ManuscriptPropertyRpc(_workspace).SetDefinitionsAsync([
            new ManuscriptPropertyDto("tension", "Tension", "Int", [], "Scene", false)
        ]);

        var fields = _rpc.Fields();

        // The tags and acts actually used in this book, plus the writer's own
        // fields - none of which the renderer could know by itself.
        Assert.Equal(["action", "quiet"], fields.Single(f => f.Field == "tag").Options);
        Assert.Equal(["Act One"], fields.Single(f => f.Field == "act").Options);
        Assert.Equal(["p1"], fields.Single(f => f.Field == "plotline").Options);
        // Everyone any scene says is present, deduped and in order - the focus
        // is one of them rather than a separate list.
        Assert.Equal(["halden", "mira"], fields.Single(f => f.Field == "cast").Options);
        Assert.Equal(["halden", "mira"], fields.Single(f => f.Field == "focus").Options);
        Assert.Contains("firstDraft", fields.Single(f => f.Field == "stage").Options);
        var mine = fields.Single(f => f.Field == "prop:tension");
        Assert.Equal("Tension", mine.Label);
        Assert.Equal("number", mine.Kind);
    }

    [Fact]
    public async Task Fields_TypeTheWritersOwnFieldsByWhatTheyHold()
    {
        await new ManuscriptPropertyRpc(_workspace).SetDefinitionsAsync([
            new ManuscriptPropertyDto("mood", "Mood", "Enum", ["Calm", "Tense"], "Scene", false),
            new ManuscriptPropertyDto("checked", "Checked", "Bool", [], "Scene", false),
            new ManuscriptPropertyDto("note", "Note", "String", [], "Scene", false),
            new ManuscriptPropertyDto("pov", "POV", "String", [], "Chapter", false)
        ]);

        var fields = _rpc.Fields();

        Assert.Equal("choice", fields.Single(f => f.Field == "prop:mood").Kind);
        Assert.Equal(["Calm", "Tense"], fields.Single(f => f.Field == "prop:mood").Options);
        Assert.Equal(["true"], fields.Single(f => f.Field == "prop:checked").Options);
        Assert.Equal("text", fields.Single(f => f.Field == "prop:note").Kind);
        // Chapter fields are not offered: a saved list picks scenes.
        Assert.DoesNotContain(fields, f => f.Field == "prop:pov");
    }

    [Fact]
    public void Fields_WithNoProjectOpen_StillOffersTheBuiltIns()
    {
        var bare = new Workspace(Path.Combine(_root, "settings2"));

        var fields = new SmartListsRpc(bare).Fields();

        Assert.Contains(fields, f => f.Field == "title");
        Assert.Empty(fields.Single(f => f.Field == "tag").Options);
    }

    [Fact]
    public async Task ARuleCanAskAboutTheWritersOwnField()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var tense = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Tense");
        await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Calm");
        var props = new ManuscriptPropertyRpc(_workspace);
        await props.SetDefinitionsAsync([
            new ManuscriptPropertyDto("tension", "Tension", "Int", [], "Scene", false)
        ]);
        await props.SetSceneValueAsync(tense.Id, "tension", "9");

        var list = (await _rpc.SaveAsync(null, "Tense scenes", "All", [
            Rule("prop:tension", "GreaterThan", "5")
        ])).Single();

        Assert.Equal("Tense", (await _rpc.EvaluateAsync(list.Id)).Single().SceneTitle);
    }

    [Fact]
    public async Task AListSavedBeforeRulesExisted_StillEvaluates()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S1");
        // Written the way an older build stored it: four fields, no rules.
        _workspace.Projects.CurrentProject!.SmartLists.Add(new Novalist.Core.Models.SmartList
        {
            Id = "legacy",
            Name = "Outline scenes",
            ChapterStatus = "Outline"
        });
        await _workspace.Projects.SaveProjectAsync();

        var listed = _rpc.List().Single();

        Assert.Equal("chapterStatus", listed.Rules.Single().Field);
        Assert.Equal("Is", listed.Rules.Single().Op);
        Assert.Single(await _rpc.EvaluateAsync("legacy"));
    }

    [Fact]
    public async Task Evaluate_UnknownList_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.EvaluateAsync("missing"));
    }

    [Fact]
    public async Task Fields_OfferTheSceneDiagnosticAndParkedScenes()
    {
        var fields = _rpc.Fields().Select(f => f.Field).ToArray();

        Assert.Contains("goal", fields);
        Assert.Contains("outcome", fields);
        Assert.Contains("inactive", fields);

        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var answered = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Answered");
        var open = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Open");
        answered.Outcome = "She burns it";
        await _workspace.Projects.SaveScenesAsync();

        // The list worth saving: every scene nothing has come of yet.
        var lists = await _rpc.SaveAsync(null, "No outcome", "All", [Rule("outcome", "IsNotSet")]);
        var matches = await _rpc.EvaluateAsync(lists.Single().Id);

        Assert.Equal("Open", matches.Single().SceneTitle);
        Assert.NotEqual(open.Id, answered.Id);
    }

}
