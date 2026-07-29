using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class SmartListServiceTests
{
    private static (SmartListService Sut, IProjectService Project, IEntityService Entity) Build()
    {
        var project = Substitute.For<IProjectService>();
        var entity = Substitute.For<IEntityService>();
        entity.LoadCharactersAsync().Returns(new List<CharacterData>());
        return (new SmartListService(project, entity), project, entity);
    }

    [Fact]
    public void GetAll_NoProject_ReturnsEmpty()
    {
        var (sut, project, _) = Build();
        project.CurrentProject.Returns((ProjectMetadata?)null);
        Assert.Empty(sut.GetAll());
    }

    [Fact]
    public void GetAll_ReturnsProjectSmartLists()
    {
        var (sut, project, _) = Build();
        var meta = new ProjectMetadata { SmartLists = { new SmartList { Id = "l1" } } };
        project.CurrentProject.Returns(meta);
        Assert.Single(sut.GetAll());
    }

    [Fact]
    public async Task SaveAsync_NoProject_DoesNothing()
    {
        var (sut, project, _) = Build();
        project.CurrentProject.Returns((ProjectMetadata?)null);
        await sut.SaveAsync(new SmartList());
        await project.DidNotReceive().SaveProjectAsync();
    }

    [Fact]
    public async Task SaveAsync_AddsNewList()
    {
        var (sut, project, _) = Build();
        var meta = new ProjectMetadata();
        project.CurrentProject.Returns(meta);

        await sut.SaveAsync(new SmartList { Id = "new" });

        Assert.Single(meta.SmartLists);
        await project.Received(1).SaveProjectAsync();
    }

    [Fact]
    public async Task SaveAsync_ReplacesExistingList()
    {
        var (sut, project, _) = Build();
        var meta = new ProjectMetadata { SmartLists = { new SmartList { Id = "x", Name = "old" } } };
        project.CurrentProject.Returns(meta);

        await sut.SaveAsync(new SmartList { Id = "x", Name = "updated" });

        Assert.Single(meta.SmartLists);
        Assert.Equal("updated", meta.SmartLists[0].Name);
    }

    [Fact]
    public async Task DeleteAsync_NoProject_DoesNothing()
    {
        var (sut, project, _) = Build();
        project.CurrentProject.Returns((ProjectMetadata?)null);
        await sut.DeleteAsync("x");
        await project.DidNotReceive().SaveProjectAsync();
    }

    [Fact]
    public async Task DeleteAsync_RemovesMatchingList()
    {
        var (sut, project, _) = Build();
        var meta = new ProjectMetadata { SmartLists = { new SmartList { Id = "x" } } };
        project.CurrentProject.Returns(meta);

        await sut.DeleteAsync("x");

        Assert.Empty(meta.SmartLists);
        await project.Received(1).SaveProjectAsync();
    }

    [Fact]
    public async Task EvaluateAsync_NoFilters_ReturnsAllScenes()
    {
        var (sut, project, _) = Build();
        var ch = new ChapterData { Guid = "c1" };
        var sc = new SceneData { Id = "s1" };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { sc });

        var result = await sut.EvaluateAsync(new SmartList());
        Assert.Single(result);
    }

    [Fact]
    public async Task EvaluateAsync_ChapterStatusFilter_SkipsNonMatching()
    {
        var (sut, project, _) = Build();
        var ch = new ChapterData { Guid = "c1", Status = ChapterStatus.Outline };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { new() { Id = "s1" } });

        var result = await sut.EvaluateAsync(new SmartList { ChapterStatus = "Done" });
        Assert.Empty(result);
    }

    [Fact]
    public async Task EvaluateAsync_TagFilter_MatchesSceneTag()
    {
        var (sut, project, _) = Build();
        var ch = new ChapterData { Guid = "c1" };
        var matching = new SceneData { Id = "s1", AnalysisOverrides = new() { Tags = new() { "action" } } };
        var other = new SceneData { Id = "s2", AnalysisOverrides = new() { Tags = new() { "calm" } } };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { matching, other });

        var result = await sut.EvaluateAsync(new SmartList { Tag = "action" });
        Assert.Single(result);
        Assert.Equal("s1", result[0].Scene.Id);
    }

    [Fact]
    public async Task EvaluateAsync_TagFilter_NoTags_Skips()
    {
        var (sut, project, _) = Build();
        var ch = new ChapterData { Guid = "c1" };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { new() { Id = "s1" } });

        var result = await sut.EvaluateAsync(new SmartList { Tag = "action" });
        Assert.Empty(result);
    }

    [Fact]
    public async Task EvaluateAsync_PlotlineFilter_MatchesSceneMembership()
    {
        var (sut, project, _) = Build();
        var ch = new ChapterData { Guid = "c1" };
        var matching = new SceneData { Id = "s1", PlotlineIds = ["p1", "p2"] };
        var other = new SceneData { Id = "s2", PlotlineIds = ["p2"] };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { matching, other });

        var result = await sut.EvaluateAsync(new SmartList { PlotlineId = "p1" });
        Assert.Single(result);
        Assert.Equal("s1", result[0].Scene.Id);
    }

    [Fact]
    public async Task EvaluateAsync_PlotlineFilter_SceneWithoutPlotlines_Skips()
    {
        var (sut, project, _) = Build();
        var ch = new ChapterData { Guid = "c1" };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { new() { Id = "s1" } });

        var result = await sut.EvaluateAsync(new SmartList { PlotlineId = "p1" });
        Assert.Empty(result);
    }

    [Fact]
    public async Task EvaluateAsync_PovFilter_UsesOverridePov()
    {
        var (sut, project, _) = Build();
        var ch = new ChapterData { Guid = "c1" };
        var sc = new SceneData { Id = "s1", AnalysisOverrides = new() { Pov = "Alice" } };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { sc });

        var result = await sut.EvaluateAsync(new SmartList { PovContains = "ali" });
        Assert.Single(result);
    }

    [Fact]
    public async Task EvaluateAsync_PovFilter_FallsBackToAutoDetect()
    {
        var (sut, project, entity) = Build();
        entity.LoadCharactersAsync().Returns(new List<CharacterData> { new() { Name = "Bob" } });
        var ch = new ChapterData { Guid = "c1" };
        var sc = new SceneData { Id = "s1" };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { sc });
        project.ReadSceneContentAsync(ch, sc).Returns("<p>Bob ran. Bob fell. Bob wept. Bob slept.</p>");

        var result = await sut.EvaluateAsync(new SmartList { PovContains = "Bob" });
        Assert.Single(result);
    }

    [Fact]
    public async Task EvaluateAsync_PovFilter_NoMatch_Skips()
    {
        var (sut, project, _) = Build();
        var ch = new ChapterData { Guid = "c1" };
        var sc = new SceneData { Id = "s1", AnalysisOverrides = new() { Pov = "Alice" } };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { sc });

        var result = await sut.EvaluateAsync(new SmartList { PovContains = "Zelda" });
        Assert.Empty(result);
    }

    // -- Rules --
    //
    // The four pre-rules filters were ANDed and covered four fields, which
    // cannot ask "either of these two POVs" or "which scenes still have no
    // synopsis" - the questions a collection is usually for.

    private static SmartList Rules(SmartListMatch match, params SmartListRule[] rules)
        => new() { Match = match, Rules = [.. rules] };

    private static SmartListRule Rule(string field, SmartListOperator op, string value = "")
        => new() { Field = field, Op = op, Value = value };

    private static (SmartListService Sut, IProjectService Project) OneChapter(params SceneData[] scenes)
    {
        var (sut, project, _) = Build();
        var chapter = new ChapterData { Guid = "c1", Act = "Act One", Status = ChapterStatus.Revised };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { chapter });
        project.GetScenesForChapter("c1").Returns([.. scenes]);
        return (sut, project);
    }

    [Fact]
    public async Task Rules_Any_MatchesOnEitherSide()
    {
        var (sut, _) = OneChapter(
            new SceneData { Id = "s1", Title = "The vault" },
            new SceneData { Id = "s2", Title = "The rooftop" },
            new SceneData { Id = "s3", Title = "The kitchen" });

        var result = await sut.EvaluateAsync(Rules(
            SmartListMatch.Any,
            Rule("title", SmartListOperator.Contains, "vault"),
            Rule("title", SmartListOperator.Contains, "rooftop")));

        Assert.Equal(["s1", "s2"], result.Select(r => r.Scene.Id));
    }

    [Fact]
    public async Task Rules_All_NeedsEveryOne()
    {
        var (sut, _) = OneChapter(
            new SceneData { Id = "s1", Title = "The vault", Stage = "revised" },
            new SceneData { Id = "s2", Title = "The vault", Stage = "outline" });

        var result = await sut.EvaluateAsync(Rules(
            SmartListMatch.All,
            Rule("title", SmartListOperator.Contains, "vault"),
            Rule("stage", SmartListOperator.Is, "revised")));

        Assert.Equal("s1", Assert.Single(result).Scene.Id);
    }

    [Fact]
    public async Task Rules_None_MatchesEverything()
    {
        var (sut, _) = OneChapter(new SceneData { Id = "s1" }, new SceneData { Id = "s2" });

        // An empty rule set is a collection of the whole book, not an empty one.
        Assert.Equal(2, (await sut.EvaluateAsync(Rules(SmartListMatch.All))).Count);
        Assert.Equal(2, (await sut.EvaluateAsync(Rules(SmartListMatch.Any))).Count);
    }

    [Fact]
    public async Task Rules_IsSetAndIsNotSet_FindTheGaps()
    {
        var (sut, _) = OneChapter(
            new SceneData { Id = "s1", Synopsis = "They meet" },
            new SceneData { Id = "s2" },
            new SceneData { Id = "s3", Synopsis = "   " });

        var missing = await sut.EvaluateAsync(Rules(
            SmartListMatch.All, Rule("synopsis", SmartListOperator.IsNotSet)));
        var present = await sut.EvaluateAsync(Rules(
            SmartListMatch.All, Rule("synopsis", SmartListOperator.IsSet)));

        // Whitespace is not a synopsis.
        Assert.Equal(["s2", "s3"], missing.Select(r => r.Scene.Id));
        Assert.Equal("s1", Assert.Single(present).Scene.Id);
    }

    [Fact]
    public async Task Rules_NumericComparisons_NeedNumbersOnBothSides()
    {
        var (sut, _) = OneChapter(
            new SceneData { Id = "s1", WordCount = 300 },
            new SceneData { Id = "s2", WordCount = 3000 });

        var longer = await sut.EvaluateAsync(Rules(
            SmartListMatch.All, Rule("words", SmartListOperator.GreaterThan, "1000")));
        var shorter = await sut.EvaluateAsync(Rules(
            SmartListMatch.All, Rule("words", SmartListOperator.LessThan, "1000")));
        // Comparing against something that is not a number is true of nothing,
        // rather than quietly comparing as text.
        var nonsense = await sut.EvaluateAsync(Rules(
            SmartListMatch.All, Rule("words", SmartListOperator.GreaterThan, "lots")));

        Assert.Equal("s2", Assert.Single(longer).Scene.Id);
        Assert.Equal("s1", Assert.Single(shorter).Scene.Id);
        Assert.Empty(nonsense);
    }

    [Fact]
    public async Task Rules_MembershipFields_AskWhetherOneOfThemMatches()
    {
        var (sut, _) = OneChapter(
            new SceneData { Id = "s1", PlotlineIds = ["p1", "p2"] },
            new SceneData { Id = "s2", AnalysisOverrides = new() { Tags = ["action"] } },
            new SceneData { Id = "s3" });

        Assert.Equal("s1", Assert.Single(await sut.EvaluateAsync(Rules(
            SmartListMatch.All, Rule("plotline", SmartListOperator.Is, "p2")))).Scene.Id);
        Assert.Equal("s2", Assert.Single(await sut.EvaluateAsync(Rules(
            SmartListMatch.All, Rule("tag", SmartListOperator.IsSet)))).Scene.Id);
        Assert.Equal(2, (await sut.EvaluateAsync(Rules(
            SmartListMatch.All, Rule("plotline", SmartListOperator.IsNotSet)))).Count);
    }

    [Fact]
    public async Task Rules_ReachTheWritersOwnSceneFields()
    {
        var (sut, _) = OneChapter(
            new SceneData { Id = "s1", Properties = new() { ["tension"] = "8" } },
            new SceneData { Id = "s2", Properties = new() { ["tension"] = "2" } },
            new SceneData { Id = "s3" });

        var tense = await sut.EvaluateAsync(Rules(
            SmartListMatch.All, Rule("prop:tension", SmartListOperator.GreaterThan, "5")));
        var unset = await sut.EvaluateAsync(Rules(
            SmartListMatch.All, Rule("prop:tension", SmartListOperator.IsNotSet)));
        var unknown = await sut.EvaluateAsync(Rules(
            SmartListMatch.All, Rule("prop:nope", SmartListOperator.IsSet)));

        Assert.Equal("s1", Assert.Single(tense).Scene.Id);
        Assert.Equal("s3", Assert.Single(unset).Scene.Id);
        Assert.Empty(unknown);
    }

    [Fact]
    public async Task Rules_ChapterFieldsAndTheRestOfTheSceneAreReachable()
    {
        var (sut, _) = OneChapter(new SceneData
        {
            Id = "s1",
            Notes = "check the timetable",
            BeatKey = "midpoint",
            WordTarget = 1500
        });

        foreach (var rule in new[]
        {
            Rule("chapterStatus", SmartListOperator.Is, "Revised"),
            Rule("act", SmartListOperator.Contains, "Act"),
            Rule("notes", SmartListOperator.Contains, "timetable"),
            Rule("beat", SmartListOperator.Is, "midpoint"),
            Rule("target", SmartListOperator.Is, "1500")
        })
        {
            Assert.Single(await sut.EvaluateAsync(Rules(SmartListMatch.All, rule)));
        }
    }

    [Fact]
    public async Task Rules_UnknownFieldMatchesNothing()
    {
        var (sut, _) = OneChapter(new SceneData { Id = "s1", Title = "A" });

        Assert.Empty(await sut.EvaluateAsync(Rules(
            SmartListMatch.All, Rule("rhubarb", SmartListOperator.IsSet))));
    }

    [Fact]
    public void EffectiveRules_ConvertsAListSavedBeforeRulesExisted()
    {
        var legacy = new SmartList
        {
            ChapterStatus = "Revised",
            PovContains = "Mira",
            Tag = "action",
            PlotlineId = "p1"
        };

        var rules = legacy.EffectiveRules();

        Assert.Equal(
            ["chapterStatus", "pov", "tag", "plotline"],
            rules.Select(r => r.Field));
        Assert.Equal(SmartListOperator.Contains, rules[1].Op);
        // Saved rules win, so a converted list is never mixed with a real one.
        legacy.Rules.Add(Rule("title", SmartListOperator.Is, "x"));
        Assert.Single(legacy.EffectiveRules());
    }

    // -- Who is in the scene --

    [Fact]
    public async Task ACastRuleFindsEverySceneSomeoneIsIn()
    {
        var (sut, project, _) = Build();
        var chapter = new ChapterData { Guid = "c1" };
        var withMira = new SceneData { Id = "s1", Cast = ["mira", "halden"] };
        var without = new SceneData { Id = "s2", Cast = ["halden"] };
        project.GetChaptersOrdered().Returns([chapter]);
        project.GetScenesForChapter("c1").Returns([withMira, without]);

        var matches = await sut.EvaluateAsync(new SmartList
        {
            Rules = [new SmartListRule { Field = "cast", Op = SmartListOperator.Is, Value = "mira" }]
        });

        Assert.Equal(["s1"], matches.Select(m => m.Scene.Id));
    }

    [Fact]
    public async Task AFocusRuleFindsTheScenesThatAreAboutSomeone()
    {
        var (sut, project, _) = Build();
        var chapter = new ChapterData { Guid = "c1" };
        var about = new SceneData { Id = "s1", Cast = ["mira"], FocusEntityId = "mira" };
        var merelyPresent = new SceneData { Id = "s2", Cast = ["mira"] };
        project.GetChaptersOrdered().Returns([chapter]);
        project.GetScenesForChapter("c1").Returns([about, merelyPresent]);

        var matches = await sut.EvaluateAsync(new SmartList
        {
            Rules = [new SmartListRule { Field = "focus", Op = SmartListOperator.Is, Value = "mira" }]
        });

        Assert.Equal(["s1"], matches.Select(m => m.Scene.Id));
    }

    [Fact]
    public async Task ASceneWithNobodyInItMatchesNoCastRule()
    {
        var (sut, project, _) = Build();
        var chapter = new ChapterData { Guid = "c1" };
        project.GetChaptersOrdered().Returns([chapter]);
        project.GetScenesForChapter("c1").Returns([new SceneData { Id = "s1" }]);

        var matches = await sut.EvaluateAsync(new SmartList
        {
            Rules = [new SmartListRule { Field = "cast", Op = SmartListOperator.IsSet, Value = "" }]
        });

        Assert.Empty(matches);
    }
}
