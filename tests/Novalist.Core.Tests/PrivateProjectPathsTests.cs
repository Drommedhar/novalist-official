using Novalist.Mobile.Services;
using Xunit;

namespace Novalist.Core.Tests;

public sealed class PrivateProjectPathsTests
{
    private static string Root => Path.Combine(Path.GetTempPath(), "novalist-private-project-test");

    [Fact]
    public void ResolvesProjectInsidePrivateRoot()
    {
        var path = Path.Combine(Root, "book");
        Assert.Equal(path, new PrivateProjectPaths(Root).Resolve(path));
    }

    [Fact]
    public void RejectsSiblingWithMatchingPrefix()
        => Assert.Null(new PrivateProjectPaths(Root).Resolve(Root + "-outside"));

    [Fact]
    public void RejectsParentTraversal()
        => Assert.Null(new PrivateProjectPaths(Root).Resolve(Path.Combine(Root, "..", "outside")));

    [Fact]
    public void AllowsSelectingRootForNewProjects()
        => Assert.Equal(Root, new PrivateProjectPaths(Root).Resolve(Root));

    [Theory]
    [InlineData("")]
    [InlineData("relative/project")]
    [InlineData("content://provider/tree/root")]
    public void RejectsUnresolvedLocations(string path)
        => Assert.Null(new PrivateProjectPaths(Root).Resolve(path));
}
