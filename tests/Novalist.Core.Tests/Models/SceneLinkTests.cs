using Novalist.Core.Models;
using Xunit;

namespace Novalist.Core.Tests.Models;

/// <summary>
/// What a scene points at.
///
/// Scenes had no link model, so a scene that answers another scene could only
/// say so as prose in its own notes - which nothing could follow, and which
/// the scene at the other end never knew about.
/// </summary>
public class SceneLinkTests
{
    [Theory]
    [InlineData("scene")]
    [InlineData("research")]
    [InlineData("entity")]
    [InlineData("Scene")]
    public void TheKindsALinkMayName(string kind)
        => Assert.True(LinkKinds.IsKnown(kind));

    [Theory]
    [InlineData("postcard")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElseIsRefusedRatherThanStored(string? kind)
    {
        // A stored kind nothing can resolve is a link that will never open,
        // and the writer finds that out at the moment they click it.
        Assert.False(LinkKinds.IsKnown(kind));
    }

    [Fact]
    public void EveryKindIsListedForAPicker()
        => Assert.Equal(["scene", "research", "entity"], LinkKinds.All);

    [Fact]
    public void TwoLinksMadeAtOnceAreTellableApart()
    {
        var first = new SceneLink { TargetId = "a" };
        var second = new SceneLink { TargetId = "b" };

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void ALinkPointsAtASceneUnlessItSaysOtherwise()
    {
        var link = new SceneLink();

        Assert.Equal(LinkKinds.Scene, link.Kind);
        Assert.Equal(string.Empty, link.TargetId);
        // A bare link is still worth having; demanding a reason is how a link
        // does not get made.
        Assert.Equal(string.Empty, link.Note);
    }

    [Fact]
    public void ALinkCanCarryWhyItIsThere()
    {
        var link = new SceneLink
        {
            Kind = LinkKinds.Research,
            TargetId = "note-1",
            Note = "pays off the promise made here"
        };

        Assert.Equal("pays off the promise made here", link.Note);
        Assert.Equal("research", link.Kind);
    }

    [Fact]
    public void ASceneStartsPointingAtNothing()
        => Assert.Null(new SceneData().Links);
}
