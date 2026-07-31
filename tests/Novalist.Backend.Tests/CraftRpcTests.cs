using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// The craft reference over the wire. Static content, so no project is needed -
/// which is itself the point: it is there before a book is.
/// </summary>
public sealed class CraftRpcTests
{
    private readonly CraftRpc _rpc = new();

    [Fact]
    public void APromptComesBackAndTheSameNumberGivesTheSameOne()
    {
        var first = _rpc.Prompt(5);
        var again = _rpc.Prompt(5);

        Assert.NotNull(first);
        Assert.False(string.IsNullOrWhiteSpace(first!.Text));
        Assert.Equal(first.Id, again!.Id);
    }

    [Fact]
    public void APromptCanBeHeldToOneKind()
    {
        var stuck = _rpc.Prompt(0, "stuck");

        Assert.NotNull(stuck);
        Assert.Equal("stuck", stuck!.Kind);
    }

    [Fact]
    public void AKindNothingUsesGivesNothing()
    {
        Assert.Null(_rpc.Prompt(0, "no-such-kind"));
    }

    [Fact]
    public void TheKindsAreListedSoTheInterfaceNeedsNoSecondList()
    {
        var kinds = _rpc.PromptKinds();

        Assert.Contains("scene", kinds);
        Assert.Contains("stuck", kinds);
        // Each named once, or the picker shows duplicates.
        Assert.Equal(kinds.Length, kinds.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ALookupFindsByNameAndBySomethingTheEntrySays()
    {
        Assert.Contains(_rpc.Lookup("fear"), e => e.Key == "fear");
        Assert.NotEmpty(_rpc.Lookup("pulse"));
        Assert.Empty(_rpc.Lookup("zzzznothing"));
    }

    [Fact]
    public void AnEmptyLookupBrowsesEverything()
    {
        // The list is short enough to browse, and browsing is how a writer
        // finds the entry they did not know to want.
        Assert.NotEmpty(_rpc.Lookup());
        Assert.Equal(_rpc.Lookup().Length, _rpc.Lookup("").Length);
    }

    [Fact]
    public void EveryLookupEntryCarriesItsSignals()
    {
        Assert.All(_rpc.Lookup(), e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Name));
            Assert.NotEmpty(e.Signals);
        });
    }

    [Fact]
    public void ArticlesListWithoutTheirBodies()
    {
        // A list of eight full articles is a payload nobody asked for.
        var listed = _rpc.Articles();

        Assert.NotEmpty(listed);
        Assert.All(listed, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Topic));
            Assert.False(string.IsNullOrWhiteSpace(a.Title));
        });
    }

    [Fact]
    public void OneArticleReadsBackWholeAndAnUnknownIdDoesNot()
    {
        var first = _rpc.Articles()[0];

        var read = _rpc.Article(first.Id);
        Assert.NotNull(read);
        Assert.Equal(first.Title, read!.Title);
        Assert.True(read.Body.Trim().Length > 400);

        Assert.Null(_rpc.Article("no-such-article"));
    }
}
