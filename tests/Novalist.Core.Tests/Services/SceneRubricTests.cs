using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// The named rubric and its advice. The cases that matter are the ones where a
/// scoreboard would say something untrue about a scene.
/// </summary>
public class SceneRubricTests
{
    [Fact]
    public void EveryElementIsNamedGroupedAndCarriesAdvice()
    {
        // A number with no next move is a judgement, not teaching. An element
        // without advice is exactly that.
        Assert.NotEmpty(SceneRubric.Elements);
        Assert.All(SceneRubric.Elements, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Key));
            Assert.False(string.IsNullOrWhiteSpace(e.Name));
            Assert.False(string.IsNullOrWhiteSpace(e.Question));
            Assert.False(string.IsNullOrWhiteSpace(e.Advice));
            Assert.Contains(e.Group, new[]
            {
                RubricGroups.Character, RubricGroups.Plot, RubricGroups.Setting
            });
        });
    }

    [Fact]
    public void EveryKeyIsItsOwn()
    {
        // Two elements sharing a key would overwrite each other's score.
        Assert.Equal(
            SceneRubric.Elements.Count,
            SceneRubric.Elements.Select(e => e.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AllThreeGroupsAreRepresented()
    {
        var groups = SceneRubric.Elements.Select(e => e.Group).Distinct().ToList();

        Assert.Contains(RubricGroups.Character, groups);
        Assert.Contains(RubricGroups.Plot, groups);
        Assert.Contains(RubricGroups.Setting, groups);
    }

    [Fact]
    public void AScoreRoundTripsThroughTheScenesOwnProperties()
    {
        var properties = new Dictionary<string, string>();

        SceneRubric.Write(properties, "goal", 4);

        var read = Assert.Single(SceneRubric.Read(properties));
        Assert.Equal("goal", read.ElementKey);
        Assert.Equal(4, read.Score);
    }

    [Fact]
    public void ARubricKeyCannotOverwriteAFieldTheWriterInvented()
    {
        // A scene's property bag also holds the writer's own fields.
        var properties = new Dictionary<string, string> { ["goal"] = "Reach the bridge." };

        SceneRubric.Write(properties, "goal", 4);

        Assert.Equal("Reach the bridge.", properties["goal"]);
        Assert.Single(SceneRubric.Read(properties));
    }

    [Fact]
    public void NotAskedRemovesTheAnswerRatherThanStoringAZero()
    {
        // Otherwise a scene somebody has been through is indistinguishable from
        // one nobody has touched.
        var properties = new Dictionary<string, string>();
        SceneRubric.Write(properties, "goal", 4);

        SceneRubric.Write(properties, "goal", SceneRubric.NotAsked);

        Assert.Empty(SceneRubric.Read(properties));
        Assert.Empty(properties);
    }

    [Fact]
    public void AScoreOutsideTheScaleIsRefused()
    {
        var properties = new Dictionary<string, string>();

        SceneRubric.Write(properties, "goal", 9);
        SceneRubric.Write(properties, "goal", -1);

        Assert.Empty(properties);
    }

    [Fact]
    public void AnElementTheRubricDoesNotHaveIsRefused()
    {
        var properties = new Dictionary<string, string>();

        SceneRubric.Write(properties, "no-such-element", 4);

        Assert.Empty(properties);
    }

    [Fact]
    public void AStaleKeyFromAnOlderRubricIsNotShown()
    {
        // It would appear as a nameless row with no question and no advice.
        var properties = new Dictionary<string, string> { ["rubric:retired"] = "4" };

        Assert.Empty(SceneRubric.Read(properties));
    }

    [Fact]
    public void AStoredValueThatIsNotANumberIsSkipped()
    {
        var properties = new Dictionary<string, string> { ["rubric:goal"] = "very good" };

        Assert.Empty(SceneRubric.Read(properties));
    }

    [Fact]
    public void ASceneNobodyHasReadHasNoAverageRatherThanZero()
    {
        // A zero would sort it beside the worst scenes in the book.
        var summary = SceneRubric.Summarise("c1", "s1", null);

        Assert.Equal(0, summary.Answered);
        Assert.Equal(0, summary.Weak);
        Assert.Equal(0, summary.Average);
    }

    [Fact]
    public void ASummaryCountsWhatWasAnsweredAndWhatIsWeak()
    {
        var properties = new Dictionary<string, string>();
        SceneRubric.Write(properties, "goal", 5);
        SceneRubric.Write(properties, "obstacle", 2);
        SceneRubric.Write(properties, "stakes", 1);

        var summary = SceneRubric.Summarise("c1", "s1", properties);

        Assert.Equal(3, summary.Answered);
        // Weak is at or below two: the ones worth the writer's attention.
        Assert.Equal(2, summary.Weak);
        Assert.Equal(8d / 3, summary.Average, 5);
    }

    [Fact]
    public void NotAskedIsNotCountedAgainstAScene()
    {
        // A chase scene is not failing at interiority, it is not trying, and a
        // rubric that cannot say so scores every action scene as broken.
        var properties = new Dictionary<string, string>();
        SceneRubric.Write(properties, "goal", 5);
        SceneRubric.Write(properties, "interiority", SceneRubric.NotAsked);

        var summary = SceneRubric.Summarise("c1", "s1", properties);

        Assert.Equal(1, summary.Answered);
        Assert.Equal(0, summary.Weak);
        Assert.Equal(5, summary.Average);
    }

    [Fact]
    public void AnElementCanBeLookedUpByKey()
    {
        Assert.Equal("goal", SceneRubric.Find("goal")!.Key);
        Assert.Null(SceneRubric.Find("no-such-element"));
    }
}
