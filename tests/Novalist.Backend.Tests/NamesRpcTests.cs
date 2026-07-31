using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Invented names over the wire. No project is needed: the generator reads
/// nothing from disk, which is what lets it answer instantly while somebody is
/// mid-sentence.
/// </summary>
public sealed class NamesRpcTests
{
    private readonly NamesRpc _rpc = new();

    [Fact]
    public void TheShippedSetsAreListedForThePicker()
    {
        var sets = _rpc.Sets();

        // The renderer asks for these rather than keeping a copy, so a list that
        // drifted would offer a set that does not exist.
        Assert.NotEmpty(sets);
        Assert.Contains("soft", sets);
    }

    [Fact]
    public void TheSameSeedGivesTheSameNamesBack()
    {
        var first = _rpc.Generate("soft", 6, 50, 99);
        var again = _rpc.Generate("soft", 6, 50, 99);

        Assert.Equal(6, first.Length);
        Assert.Equal(first, again);
    }

    [Fact]
    public void AStartingFilterReachesTheGenerator()
    {
        var names = _rpc.Generate("hard", 4, 100, 3, startsWith: "Th");

        Assert.All(names, n =>
            Assert.StartsWith("Th", n, System.StringComparison.CurrentCultureIgnoreCase));
    }
}
