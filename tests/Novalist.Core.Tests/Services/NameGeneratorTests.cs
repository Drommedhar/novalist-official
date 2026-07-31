using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Invented names, offline and repeatable. Naming is the highest-frequency
/// thing that stops a draft and Novalist had no help for it at all.
/// </summary>
public class NameGeneratorTests
{
    [Fact]
    public void TheSameSeedGivesTheSameNames()
    {
        var first = NameGenerator.Generate("soft", 8, 50, seed: 1234);
        var again = NameGenerator.Generate("soft", 8, 50, seed: 1234);

        // A generator that cannot be asked twice loses the name somebody liked
        // and did not write down.
        Assert.Equal(first, again);
    }

    [Fact]
    public void ADifferentSeedGivesDifferentNames()
    {
        var first = NameGenerator.Generate("soft", 8, 50, seed: 1);
        var other = NameGenerator.Generate("soft", 8, 50, seed: 2);

        Assert.NotEqual(first, other);
    }

    [Fact]
    public void EveryNameIsUsableAsATyped()
    {
        var names = NameGenerator.Generate("hard", 25, 80, seed: 7);

        Assert.All(names, n =>
        {
            Assert.False(string.IsNullOrWhiteSpace(n));
            Assert.Equal(char.ToUpperInvariant(n[0]), n[0]);
            Assert.DoesNotContain(" ", n);
        });
    }

    [Fact]
    public void NamesDoNotRepeatWithinOneRun()
    {
        var names = NameGenerator.Generate("coastal", 30, 90, seed: 11);

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ObscurityWidensWhatTheSetReachesFor()
    {
        // At zero only the head of each list is in play, so a large sample runs
        // out of distinct names far sooner than the same sample does at full.
        var narrow = NameGenerator.Generate("soft", 100, 0, seed: 3);
        var wide = NameGenerator.Generate("soft", 100, 100, seed: 3);

        Assert.True(wide.Count > narrow.Count);
    }

    [Fact]
    public void EvenAtZeroASetDoesNotProduceOneNameOverAndOver()
    {
        // The window has a floor of two, or the commonest onset, nucleus and
        // coda would be the only name the set could make.
        Assert.True(NameGenerator.Generate("old", 20, 0, seed: 5).Count > 1);
    }

    [Theory]
    [InlineData("Th")]
    [InlineData("m")]
    public void AStartingFilterIsHonoured(string prefix)
    {
        var names = NameGenerator.Generate("hard", 5, 100, seed: 9, startsWith: prefix);

        Assert.All(names, n =>
            Assert.StartsWith(prefix, n, StringComparison.CurrentCultureIgnoreCase));
    }

    [Fact]
    public void AFilterNothingMatchesReturnsWhatItCanRatherThanHanging()
    {
        // Bounded attempts: four names is better than not returning.
        var names = NameGenerator.Generate("soft", 10, 100, seed: 2, startsWith: "zzq");

        Assert.Empty(names);
    }

    [Fact]
    public void AnUnknownSetStillProducesNames()
    {
        // A picker out of step with the shipped list should not fail.
        Assert.NotEmpty(NameGenerator.Generate("no-such-set", 3, 50, seed: 1));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(500, 100)]
    public void TheCountIsClamped(int asked, int expected)
        => Assert.Equal(expected, NameGenerator.Generate("soft", asked, 100, seed: 4).Count);

    [Theory]
    [InlineData(-10)]
    [InlineData(250)]
    public void ObscurityOutsideTheRangeIsClampedRatherThanThrowing(int obscurity)
        => Assert.NotEmpty(NameGenerator.Generate("soft", 3, obscurity, seed: 6));

    [Fact]
    public void EverySetShipsUsable()
    {
        Assert.All(NameGenerator.Sets, set =>
        {
            Assert.NotEmpty(NameGenerator.Generate(set.Key, 5, 60, seed: 42));
            // An empty coda is what keeps a set from sounding uniformly clipped.
            Assert.Contains(string.Empty, set.Codas);
        });
    }
}
