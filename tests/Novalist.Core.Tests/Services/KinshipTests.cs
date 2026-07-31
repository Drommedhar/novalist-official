using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Great-aunt, second cousin once removed - derived from parentage rather than
/// recorded pair by pair. Novalist could draw the lines and could not say what
/// they meant.
/// </summary>
public class KinshipTests
{
    /// <summary>
    /// A small family, three generations:
    ///
    ///   gran ─┬─ (children)  mum, aunt
    ///   mum   ─── me, brother
    ///   aunt  ─── cousin
    ///   me    ─── kid
    ///   cousin─── cousinkid
    /// </summary>
    private static Dictionary<string, IReadOnlyCollection<string>> Family() => new()
    {
        ["mum"] = ["gran"],
        ["aunt"] = ["gran"],
        ["me"] = ["mum"],
        ["brother"] = ["mum"],
        ["cousin"] = ["aunt"],
        ["kid"] = ["me"],
        ["cousinkid"] = ["cousin"],
        ["grandkid"] = ["kid"]
    };

    private static KinshipResult Of(string from, string to)
        => Kinship.Describe(Family(), from, to);

    [Fact]
    public void SomebodyIsThemselves()
        => Assert.Equal(KinshipKind.Self, Of("me", "me").Kind);

    [Theory]
    [InlineData("mum", "me", 1)]      // parent
    [InlineData("gran", "me", 2)]     // grandparent
    [InlineData("gran", "kid", 3)]    // great-grandparent
    public void TheDirectLineUpwards(string from, string to, int degree)
    {
        var result = Of(from, to);
        Assert.Equal(KinshipKind.Ancestor, result.Kind);
        Assert.Equal(degree, result.Degree);
    }

    [Theory]
    [InlineData("me", "mum", 1)]      // child
    [InlineData("me", "gran", 2)]     // grandchild
    [InlineData("grandkid", "me", 2)]
    public void TheDirectLineDownwards(string from, string to, int degree)
    {
        var result = Of(from, to);
        Assert.Equal(KinshipKind.Descendant, result.Kind);
        Assert.Equal(degree, result.Degree);
    }

    [Fact]
    public void TwoChildrenOfOneParentAreSiblings()
        => Assert.Equal(KinshipKind.Sibling, Of("me", "brother").Kind);

    [Fact]
    public void ASiblingOfAParentIsAnAunt()
    {
        var result = Of("aunt", "me");
        Assert.Equal(KinshipKind.AuntUncle, result.Kind);
        Assert.Equal(1, result.Degree);
    }

    [Fact]
    public void ASiblingOfAGrandparentWouldBeAGreatAunt()
    {
        // Reading the table directly rather than building a fifth generation:
        // one step up on her side, three on theirs.
        var result = Kinship.Classify(up: 1, down: 3);
        Assert.Equal(KinshipKind.AuntUncle, result.Kind);
        Assert.Equal(2, result.Degree);
    }

    [Fact]
    public void TheReverseOfAnAuntIsANiece()
    {
        var result = Of("me", "aunt");
        Assert.Equal(KinshipKind.NieceNephew, result.Kind);
        Assert.Equal(1, result.Degree);
    }

    [Fact]
    public void TheChildrenOfTwoSiblingsAreFirstCousins()
    {
        var result = Of("me", "cousin");
        Assert.Equal(KinshipKind.Cousin, result.Kind);
        Assert.Equal(1, result.Degree);
        Assert.Equal(0, result.Removed);
    }

    [Fact]
    public void ACousinsChildIsOnceRemoved()
    {
        var result = Of("me", "cousinkid");
        Assert.Equal(KinshipKind.Cousin, result.Kind);
        Assert.Equal(1, result.Degree);
        Assert.Equal(1, result.Removed);
    }

    [Theory]
    // The canonical table: the cousin number is how far the nearer one is from
    // the shared ancestor, and the removal is the difference.
    [InlineData(3, 3, 2, 0)]   // second cousins
    [InlineData(4, 4, 3, 0)]   // third cousins
    [InlineData(3, 4, 2, 1)]   // second cousins once removed
    [InlineData(2, 5, 1, 3)]   // first cousins three times removed
    public void TheCousinTable(int up, int down, int degree, int removed)
    {
        var result = Kinship.Classify(up, down);
        Assert.Equal(KinshipKind.Cousin, result.Kind);
        Assert.Equal(degree, result.Degree);
        Assert.Equal(removed, result.Removed);
    }

    [Fact]
    public void TwoFamiliesThatNeverMeetAreUnrelated()
    {
        var parents = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["a"] = ["a-parent"],
            ["b"] = ["b-parent"]
        };

        Assert.Equal(KinshipKind.Unrelated, Kinship.Describe(parents, "a", "b").Kind);
    }

    [Fact]
    public void TheNearerOfTwoSharedAncestorsWins()
    {
        // Half-siblings who are also distant cousins read as siblings, which is
        // the relationship anybody in the room would name.
        var parents = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["a"] = ["dad", "mum-a"],
            ["b"] = ["dad", "mum-b"],
            ["mum-a"] = ["far"],
            ["mum-b"] = ["far"]
        };

        Assert.Equal(KinshipKind.Sibling, Kinship.Describe(parents, "a", "b").Kind);
    }

    [Fact]
    public void ParentageThatLoopsBackOnItselfTerminates()
    {
        // Hand-entered records can say somebody is their own grandparent. The
        // answer does not matter; not hanging the app does.
        var parents = new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["a"] = ["b"],
            ["b"] = ["c"],
            ["c"] = ["a"]
        };

        Assert.NotNull(Kinship.Describe(parents, "a", "b"));
    }

    [Theory]
    [InlineData("", "me")]
    [InlineData("me", "")]
    public void AMissingPersonIsNotRelatedToAnybody(string from, string to)
        => Assert.Equal(KinshipKind.Unrelated, Of(from, to).Kind);

    [Fact]
    public void SomebodyWithNoRecordedParentsIsStillHandled()
        => Assert.Equal(KinshipKind.Unrelated, Of("gran", "stranger").Kind);
}
