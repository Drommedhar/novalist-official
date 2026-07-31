using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Dates that follow other dates. Every case here is a way a chronology could
/// quietly end up saying the wrong thing.
/// </summary>
public class TimelineDependenciesTests
{
    private static TimelineManualEvent Event(
        string id, string date, string end = "", string? dependsOn = null,
        int offset = 0, string? from = null, bool locked = false)
        => new()
        {
            Id = id,
            Title = id,
            Date = date,
            EndDate = end,
            DependsOnEventId = dependsOn,
            DependsOnOffsetDays = offset,
            DependsOnFrom = from,
            DateLocked = locked
        };

    [Fact]
    public void AnEventWithNoAnchorIsLeftWhereItIs()
    {
        var alone = Event("a", "2020-01-01");

        var result = TimelineDependencies.Resolve([alone]);

        Assert.Empty(result.Moved);
        Assert.Empty(result.Cycles);
        Assert.Equal("2020-01-01", alone.Date);
    }

    [Fact]
    public void ADependentFollowsItsAnchorByTheOffset()
    {
        var siege = Event("siege", "2020-01-01");
        var funeral = Event("funeral", "1999-01-01", dependsOn: "siege", offset: 7);

        var result = TimelineDependencies.Resolve([siege, funeral]);

        Assert.Equal("2020-01-08", funeral.Date);
        Assert.Contains(result.Moved, m => m.EventId == "funeral" && m.Date == "2020-01-08");
    }

    [Fact]
    public void ANegativeOffsetPutsTheEventBeforeItsAnchor()
    {
        var battle = Event("battle", "2020-06-10");
        var muster = Event("muster", "", dependsOn: "battle", offset: -14);

        TimelineDependencies.Resolve([battle, muster]);

        Assert.Equal("2020-05-27", muster.Date);
    }

    [Fact]
    public void AnOffsetCanBeCountedFromTheAnchorsEnd()
    {
        // "The week after the siege" means its end when the siege lasts a month.
        var siege = Event("siege", "2020-01-01", end: "2020-02-01");
        var feast = Event("feast", "", dependsOn: "siege", offset: 7,
            from: TimelineDependencies.FromEnd);

        TimelineDependencies.Resolve([siege, feast]);

        Assert.Equal("2020-02-08", feast.Date);
    }

    [Fact]
    public void AnAnchorWithNoEndFallsBackToItsStart()
    {
        var point = Event("point", "2020-01-01");
        var after = Event("after", "", dependsOn: "point", offset: 3,
            from: TimelineDependencies.FromEnd);

        TimelineDependencies.Resolve([point, after]);

        Assert.Equal("2020-01-04", after.Date);
    }

    [Fact]
    public void AMovedSpanKeepsItsOwnLength()
    {
        // A three-week siege that moves is still three weeks long; flattening
        // it to a point would lose something the writer wrote.
        var war = Event("war", "2020-01-01");
        var siege = Event("siege", "2019-01-01", end: "2019-01-22", dependsOn: "war", offset: 10);

        TimelineDependencies.Resolve([war, siege]);

        Assert.Equal("2020-01-11", siege.Date);
        Assert.Equal("2020-02-01", siege.EndDate);
    }

    [Fact]
    public void AChainMovesAllTheWayDown()
    {
        var a = Event("a", "2020-01-01");
        var b = Event("b", "", dependsOn: "a", offset: 5);
        var c = Event("c", "", dependsOn: "b", offset: 5);

        // Deliberately out of order: the engine settles the anchor first, not
        // whatever happens to come first in the list.
        TimelineDependencies.Resolve([c, b, a]);

        Assert.Equal("2020-01-06", b.Date);
        Assert.Equal("2020-01-11", c.Date);
    }

    [Fact]
    public void ALockedEventStaysPutAndItsDependentsStillFollowIt()
    {
        // Otherwise the lock is a decoration.
        var anchor = Event("anchor", "2020-01-01");
        var pinned = Event("pinned", "2021-06-01", dependsOn: "anchor", offset: 5, locked: true);
        var after = Event("after", "", dependsOn: "pinned", offset: 2);

        var result = TimelineDependencies.Resolve([anchor, pinned, after]);

        Assert.Equal("2021-06-01", pinned.Date);
        Assert.DoesNotContain(result.Moved, m => m.EventId == "pinned");
        Assert.Equal("2021-06-03", after.Date);
    }

    [Fact]
    public void TwoEventsWaitingOnEachOtherAreReportedAndLeftAlone()
    {
        // There is no right answer, and guessing one corrupts a chronology
        // without saying so.
        var a = Event("a", "2020-01-01", dependsOn: "b", offset: 1);
        var b = Event("b", "2020-02-01", dependsOn: "a", offset: 1);

        var result = TimelineDependencies.Resolve([a, b]);

        Assert.NotEmpty(result.Cycles);
        Assert.Equal("2020-01-01", a.Date);
        Assert.Equal("2020-02-01", b.Date);
    }

    [Fact]
    public void AnEventWaitingOnItselfIsACycleToo()
    {
        var self = Event("self", "2020-01-01", dependsOn: "self", offset: 1);

        var result = TimelineDependencies.Resolve([self]);

        Assert.Contains("self", result.Cycles);
        Assert.Equal("2020-01-01", self.Date);
    }

    [Fact]
    public void AnAnchorThatIsNotThereChangesNothing()
    {
        // A deleted anchor must not blank the date of everything that hung off
        // it - the writer's date is better than no date.
        var orphan = Event("orphan", "2020-01-01", dependsOn: "gone", offset: 5);

        var result = TimelineDependencies.Resolve([orphan]);

        Assert.Empty(result.Moved);
        Assert.Equal("2020-01-01", orphan.Date);
    }

    [Fact]
    public void AnAnchorWithADateNothingCanReadIsLeftAlone()
    {
        // An in-world calendar string is not a bug to be corrected.
        var anchor = Event("anchor", "Third of Sowing, 1043");
        var after = Event("after", "2020-01-01", dependsOn: "anchor", offset: 5);

        var result = TimelineDependencies.Resolve([anchor, after]);

        Assert.Empty(result.Moved);
        Assert.Equal("2020-01-01", after.Date);
    }

    [Fact]
    public void AnEventAlreadyWhereItBelongsIsNotReportedAsMoved()
    {
        var anchor = Event("anchor", "2020-01-01");
        var after = Event("after", "2020-01-06", dependsOn: "anchor", offset: 5);

        var result = TimelineDependencies.Resolve([anchor, after]);

        Assert.Empty(result.Moved);
    }

    [Fact]
    public void TheOtherDateFormatsTheTimelineReadsAreUnderstood()
    {
        Assert.Equal(new DateTime(2020, 3, 1), TimelineDependencies.Parse("2020-03"));
        Assert.Equal(new DateTime(2020, 1, 1), TimelineDependencies.Parse("2020"));
        Assert.Equal(new DateTime(2020, 3, 4), TimelineDependencies.Parse("4.3.2020"));
        Assert.Equal(new DateTime(2020, 3, 4), TimelineDependencies.Parse("March 4, 2020"));
        Assert.Null(TimelineDependencies.Parse("   "));
        Assert.Null(TimelineDependencies.Parse(null));
    }

    [Fact]
    public void AnEndBeforeItsStartIsNotTreatedAsALength()
    {
        // Nonsense in, nothing invented: the end is left as written rather than
        // recomputed to something that looks deliberate.
        var anchor = Event("anchor", "2020-01-01");
        var backwards = Event("backwards", "2019-05-10", end: "2019-05-01",
            dependsOn: "anchor", offset: 2);

        TimelineDependencies.Resolve([anchor, backwards]);

        Assert.Equal("2020-01-03", backwards.Date);
        Assert.Equal("2019-05-01", backwards.EndDate);
    }
}
