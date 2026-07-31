using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// The deterministic continuity gates. Every case here is either a
/// contradiction the writer wants found, or a false alarm that would make them
/// stop reading the report - and the second kind matters as much as the first.
/// </summary>
public class ContinuityGatesTests
{
    private static GateScene Scene(
        int index, string[]? cast = null, string? date = null, string? mode = null)
        => new("c1", $"s{index}", index, cast ?? [], date, mode);

    [Fact]
    public void ACleanBookReportsNothing()
    {
        var findings = ContinuityGates.Run(
            [Scene(0, ["e1"], "2020-01-01"), Scene(1, ["e1"], "2020-01-02")],
            [new GateEntity("e1", "Mara", null)]);

        Assert.Empty(findings);
    }

    [Fact]
    public void SomebodyAppearingAfterTheyAreGoneIsFound()
    {
        // A character standing in a scene two chapters after their funeral was
        // nobody's job to notice.
        var findings = ContinuityGates.Run(
            [Scene(0, ["e1"]), Scene(1, ["e1"]), Scene(2, ["e1"])],
            [new GateEntity("e1", "Mara", GoneFromReadingIndex: 1)]);

        var found = Assert.Single(findings);
        Assert.Equal(ContinuityGates.GoneThenPresent, found.RuleId);
        Assert.Equal("Mara", found.Subject);
        Assert.Equal("s2", found.SceneId);
    }

    [Fact]
    public void TheSceneSomebodyLeavesInIsNotAContradiction()
    {
        // They are in the scene where they die. Reporting that would be
        // reporting the writer's own sentence back at them.
        var findings = ContinuityGates.Run(
            [Scene(0, ["e1"]), Scene(1, ["e1"])],
            [new GateEntity("e1", "Mara", GoneFromReadingIndex: 1)]);

        Assert.Empty(findings);
    }

    [Fact]
    public void ACastMemberTheCodexNoLongerHasIsFound()
    {
        var findings = ContinuityGates.Run(
            [Scene(0, ["deleted-id"])],
            [new GateEntity("e1", "Mara", null)]);

        var found = Assert.Single(findings);
        Assert.Equal(ContinuityGates.UnknownCast, found.RuleId);
        // The id, because there is no entry left to take a name from.
        Assert.Equal("deleted-id", found.Detail);
    }

    [Fact]
    public void TimeRunningBackwardsIsFound()
    {
        var findings = ContinuityGates.Run(
            [Scene(0, date: "2020-05-01"), Scene(1, date: "2020-04-01")],
            []);

        var found = Assert.Single(findings);
        Assert.Equal(ContinuityGates.TimeRunsBackwards, found.RuleId);
        Assert.Equal("2020-05-01 -> 2020-04-01", found.Detail);
    }

    [Fact]
    public void AFlashbackIsNotTimeRunningBackwards()
    {
        // That is exactly what narrative mode is for.
        var findings = ContinuityGates.Run(
            [Scene(0, date: "2020-05-01"), Scene(1, date: "1999-04-01", mode: "Flashback")],
            []);

        Assert.Empty(findings);
    }

    [Fact]
    public void AFlashbackDoesNotDragTheClockBackForEverythingAfterIt()
    {
        // The false alarm that would make the report useless: one flashback
        // turning every later scene into a finding.
        var findings = ContinuityGates.Run(
            [
                Scene(0, date: "2020-05-01"),
                Scene(1, date: "1999-04-01", mode: "Flashback"),
                Scene(2, date: "2020-05-02"),
                Scene(3, date: "2020-05-03")
            ],
            []);

        Assert.Empty(findings);
    }

    [Fact]
    public void OneJumpBackwardsIsOneFindingNotOnePerSceneAfterIt()
    {
        // Found on a real book: holding the baseline at the highest date seen
        // turned a single out-of-order scene into a wall of findings, one for
        // every scene that followed. A report that cries wolf is one the writer
        // stops reading, and then it may as well not run.
        var findings = ContinuityGates.Run(
            [
                Scene(0, date: "2020-12-16"),
                Scene(1, date: "2020-10-25"),
                Scene(2, date: "2020-10-26"),
                Scene(3, date: "2020-10-27"),
                Scene(4, date: "2020-10-28")
            ],
            []);

        var found = Assert.Single(findings);
        Assert.Equal("s1", found.SceneId);
        Assert.Equal("2020-12-16 -> 2020-10-25", found.Detail);
    }

    [Fact]
    public void EachSeparateJumpBackwardsIsItsOwnFinding()
    {
        var findings = ContinuityGates.Run(
            [
                Scene(0, date: "2020-05-10"),
                Scene(1, date: "2020-05-01"),
                Scene(2, date: "2020-05-20"),
                Scene(3, date: "2020-05-02")
            ],
            []);

        Assert.Equal(2, findings.Count);
        Assert.Equal(["s1", "s3"], findings.Select(f => f.SceneId));
    }

    [Fact]
    public void ScenesWithNoDateAreSkippedRatherThanGuessedAt()
    {
        var findings = ContinuityGates.Run(
            [Scene(0, date: "2020-05-01"), Scene(1), Scene(2, date: "2020-05-02")],
            []);

        Assert.Empty(findings);
    }

    [Fact]
    public void ADateNothingCanReadIsNotAFinding()
    {
        // An in-world calendar string is not a contradiction.
        var findings = ContinuityGates.Run(
            [Scene(0, date: "Third of Sowing"), Scene(1, date: "First of Reaping")],
            []);

        Assert.Empty(findings);
    }

    [Fact]
    public void TwoScenesOnTheSameDayAreFine()
    {
        var findings = ContinuityGates.Run(
            [Scene(0, date: "2020-05-01"), Scene(1, date: "2020-05-01")],
            []);

        Assert.Empty(findings);
    }

    [Fact]
    public void ScenesAreCheckedInReadingOrderNotListOrder()
    {
        var findings = ContinuityGates.Run(
            [Scene(2, date: "2020-04-01"), Scene(0, date: "2020-05-01"), Scene(1, date: "2020-06-01")],
            []);

        var found = Assert.Single(findings);
        Assert.Equal("s2", found.SceneId);
    }

    [Fact]
    public void ARuleTheWriterTurnedOffStaysQuiet()
    {
        // A gate that keeps reporting something they have decided is fine is a
        // gate they stop reading.
        var scenes = new[] { Scene(0, ["gone-id"], "2020-05-01"), Scene(1, date: "2020-04-01") };

        Assert.Empty(ContinuityGates.Run(scenes, [],
            new HashSet<string>(ContinuityGates.AllRules, StringComparer.Ordinal)));

        // And each one on its own.
        Assert.DoesNotContain(
            ContinuityGates.Run(scenes, [], new HashSet<string> { ContinuityGates.UnknownCast }),
            f => f.RuleId == ContinuityGates.UnknownCast);
        Assert.DoesNotContain(
            ContinuityGates.Run(scenes, [], new HashSet<string> { ContinuityGates.TimeRunsBackwards }),
            f => f.RuleId == ContinuityGates.TimeRunsBackwards);
    }

    [Fact]
    public void EveryRuleIsListedSoASettingsScreenNeedsNoSecondList()
    {
        Assert.Contains(ContinuityGates.GoneThenPresent, ContinuityGates.AllRules);
        Assert.Contains(ContinuityGates.UnknownCast, ContinuityGates.AllRules);
        Assert.Contains(ContinuityGates.TimeRunsBackwards, ContinuityGates.AllRules);
    }

    // ── Where an entry leaves the story ──

    [Fact]
    public void GoneFromTakesTheEarliestMarkedOverride()
    {
        var overrides = new[]
        {
            new EntityStateOverride { Chapter = "c1", Scene = "s5", Gone = true },
            new EntityStateOverride { Chapter = "c1", Scene = "s2", Gone = true },
            new EntityStateOverride { Chapter = "c1", Scene = "s1" }
        };

        var index = ContinuityGates.GoneFrom(overrides,
            (_, scene) => scene switch { "s1" => 1, "s2" => 2, "s5" => 5, _ => null });

        Assert.Equal(2, index);
    }

    [Fact]
    public void GoneFromIsNullWhenNothingSaysTheyLeft()
    {
        var overrides = new[] { new EntityStateOverride { Chapter = "c1", Scene = "s1" } };

        Assert.Null(ContinuityGates.GoneFrom(overrides, (_, _) => 1));
    }

    [Fact]
    public void AMarkerOnAPlaceTheBookNoLongerHasIsIgnored()
    {
        // A scene that was deleted must not put the entry out of the story from
        // nowhere in particular.
        var overrides = new[] { new EntityStateOverride { Chapter = "c1", Scene = "gone", Gone = true } };

        Assert.Null(ContinuityGates.GoneFrom(overrides, (_, _) => null));
    }

    [Fact]
    public void AnOverrideSayingOnlyGoneIsNotTreatedAsEmpty()
    {
        // Pruning it would throw away the whole marker.
        Assert.True(new EntityStateOverride { Chapter = "c1", Gone = true }.HasValues);
        Assert.False(new EntityStateOverride { Chapter = "c1" }.HasValues);
    }
}
