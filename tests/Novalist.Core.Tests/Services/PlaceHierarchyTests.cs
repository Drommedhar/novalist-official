using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Where a place is allowed to sit in the tree.
///
/// The hierarchy was rendered from a plain parent string and reparenting meant
/// typing into an autocomplete field, so nothing ever checked the answer: a
/// place could be made its own ancestor and the branch would silently vanish,
/// because a cycle has no root and the renderer refuses to recurse forever.
/// </summary>
public class PlaceHierarchyTests
{
    /// <summary>Continent → Kingdom → City → Inn, plus a loose island.</summary>
    private static List<LocationData> World() =>
    [
        new() { Name = "Aeloria", IsWorld = true },
        new() { Name = "Northreach", Parent = "Aeloria" },
        new() { Name = "Karn", Parent = "Northreach" },
        new() { Name = "The Rookery", Parent = "Karn" },
        new() { Name = "Sill", Parent = string.Empty }
    ];

    private static LocationData Find(List<LocationData> places, string name)
        => places.Single(p => p.Name == name);

    [Fact]
    public void APlaceCanGoUnderAnotherPlace()
    {
        var places = World();

        Assert.True(PlaceHierarchy.CanReparent(places, Find(places, "Sill"), "Karn"));
    }

    [Fact]
    public void AnEmptyParentIsAlwaysAllowed()
    {
        var places = World();

        // That is how a place is lifted back to the top of the tree.
        Assert.True(PlaceHierarchy.CanReparent(places, Find(places, "Karn"), string.Empty));
        Assert.True(PlaceHierarchy.CanReparent(places, Find(places, "Karn"), null));
        Assert.True(PlaceHierarchy.CanReparent(places, Find(places, "Karn"), "   "));
    }

    [Fact]
    public void APlaceCannotContainItself()
    {
        var places = World();

        Assert.False(PlaceHierarchy.CanReparent(places, Find(places, "Karn"), "Karn"));
        // Whatever the case: the tree matches names case-insensitively.
        Assert.False(PlaceHierarchy.CanReparent(places, Find(places, "Karn"), "karn"));
    }

    [Fact]
    public void APlaceCannotGoInsideItsOwnChild()
    {
        var places = World();

        Assert.False(PlaceHierarchy.CanReparent(places, Find(places, "Northreach"), "Karn"));
    }

    [Fact]
    public void APlaceCannotGoInsideItsOwnGrandchild()
    {
        var places = World();

        // The whole branch would vanish: a cycle has no root to render from.
        Assert.False(PlaceHierarchy.CanReparent(places, Find(places, "Northreach"), "The Rookery"));
    }

    [Fact]
    public void AWorldNeverGoesInsideAnything()
    {
        var places = World();

        // There is nothing above a world, which is what makes it one.
        Assert.False(PlaceHierarchy.CanReparent(places, Find(places, "Aeloria"), "Northreach"));
        Assert.True(PlaceHierarchy.CanReparent(places, Find(places, "Aeloria"), string.Empty));
    }

    [Fact]
    public void AParentNothingAnswersToIsAllowed()
    {
        var places = World();

        // The tree already draws such a place at the top, and refusing would
        // block naming a parent before creating it.
        Assert.True(PlaceHierarchy.CanReparent(places, Find(places, "Sill"), "Somewhere Else"));
    }

    [Fact]
    public void ACycleAlreadyInTheFileDoesNotHangTheWalk()
    {
        // Written by an older version, or by hand.
        List<LocationData> broken =
        [
            new() { Name = "A", Parent = "B" },
            new() { Name = "B", Parent = "A" },
            new() { Name = "C" }
        ];

        Assert.True(PlaceHierarchy.CanReparent(broken, Find(broken, "C"), "A"));
    }

    [Fact]
    public void DescendantWalksTheWholeChain()
    {
        var places = World();

        Assert.True(PlaceHierarchy.IsDescendant(places, Find(places, "The Rookery"), Find(places, "Aeloria")));
        Assert.False(PlaceHierarchy.IsDescendant(places, Find(places, "Sill"), Find(places, "Aeloria")));
    }

    [Fact]
    public void AChainThatRunsOffTheEndStops()
    {
        // A parent naming something that was deleted.
        List<LocationData> orphaned =
        [
            new() { Name = "Karn", Parent = "A kingdom that is gone" },
            new() { Name = "Aeloria", IsWorld = true }
        ];

        Assert.False(PlaceHierarchy.IsDescendant(
            orphaned, Find(orphaned, "Karn"), Find(orphaned, "Aeloria")));
    }
}
