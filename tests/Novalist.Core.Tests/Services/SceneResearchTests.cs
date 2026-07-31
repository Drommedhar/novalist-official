using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Covers <see cref="SceneResearch"/> - which research the open scene is about.
/// Deterministic on purpose: a suggestion the writer has to double-check costs
/// more than no suggestion at all.
/// </summary>
public sealed class SceneResearchTests
{
    private static ResearchItem Item(
        string id, string title, string[]? refs = null, string[]? tags = null, int order = 0)
        => new()
        {
            Id = id,
            Title = title,
            EntityRefs = [.. refs ?? []],
            Tags = [.. tags ?? []],
            Order = order,
        };

    [Fact]
    public void AnItemLinkedToSomebodyInTheSceneIsSuggested_NamingThem()
    {
        var items = new[]
        {
            Item("r1", "Field surgery in 1755", refs: ["char-amy"]),
            Item("r2", "Shipping lanes", refs: ["char-nobody"]),
        };

        var got = SceneResearch.Suggest(
            items, ["char-amy"], [], new Dictionary<string, string> { ["char-amy"] = "Amy" });

        var only = Assert.Single(got);
        Assert.Equal("r1", only.Item.Id);
        // The reason is the point: a list of titles with no explanation has to
        // be opened one by one to find out why it is there.
        Assert.Equal("Amy", only.Reason);
    }

    [Fact]
    public void ATagTheSceneSharesIsEnough_AndTheEntityMatchOutranksIt()
    {
        var items = new[]
        {
            Item("tagged", "Winter clothing", tags: ["winter"]),
            Item("linked", "Amy's childhood", refs: ["char-amy"]),
        };

        var got = SceneResearch.Suggest(
            items, ["char-amy"], ["winter"], new Dictionary<string, string> { ["char-amy"] = "Amy" });

        Assert.Equal(2, got.Count);
        // The writer linked the note to the character deliberately; a tag may be
        // shared by forty notes.
        Assert.Equal("linked", got[0].Item.Id);
        Assert.Equal("tagged", got[1].Item.Id);
        Assert.Equal("winter", got[1].Reason);
    }

    [Fact]
    public void MatchingBothWaysScoresAboveMatchingOne()
    {
        var items = new[]
        {
            Item("one", "Only linked", refs: ["char-amy"]),
            Item("both", "Linked and tagged", refs: ["char-amy"], tags: ["winter"]),
        };

        var got = SceneResearch.Suggest(items, ["char-amy"], ["winter"]);

        Assert.Equal("both", got[0].Item.Id);
        Assert.True(got[0].Score > got[1].Score);
    }

    [Fact]
    public void TheInboxTagNeverMatches()
    {
        // Everything quick-captured carries it, so a scene tagged "inbox" would
        // otherwise suggest the entire unfiled pile.
        var items = new[] { Item("r1", "Unfiled thought", tags: [ResearchItem.InboxTag]) };

        Assert.Empty(SceneResearch.Suggest(items, [], [ResearchItem.InboxTag]));
    }

    [Fact]
    public void IdsAndTagsMatchWithoutRegardToCaseOrSurroundingSpace()
    {
        var items = new[] { Item("r1", "Bridges", refs: ["CHAR-AMY"], tags: [" Winter "]) };

        Assert.Single(SceneResearch.Suggest(items, ["char-amy"], []));
        Assert.Single(SceneResearch.Suggest(items, [], ["WINTER"]));
    }

    [Fact]
    public void NothingToMatchOn_OrNothingMatching_IsEmptyRatherThanEverything()
    {
        var items = new[] { Item("r1", "Bridges", refs: ["char-amy"], tags: ["winter"]) };

        // A scene with no cast and no tags must not fall through to "show all".
        Assert.Empty(SceneResearch.Suggest(items, [], []));
        Assert.Empty(SceneResearch.Suggest(items, ["char-someone-else"], ["summer"]));
        Assert.Empty(SceneResearch.Suggest(null, ["char-amy"], ["winter"]));
        Assert.Empty(SceneResearch.Suggest(items, null, null));
    }

    [Fact]
    public void BlankIdsAndTagsAreNotMatchableValues()
    {
        // An item with an empty tag would otherwise match every scene that has
        // an empty one too, which is most of them.
        var items = new[] { Item("r1", "Bridges", refs: [""], tags: ["  "]) };

        Assert.Empty(SceneResearch.Suggest(items, ["", " "], ["", "   "]));
    }

    [Fact]
    public void TheListIsCappedAndStableAcrossCalls()
    {
        var items = Enumerable.Range(0, 20)
            .Select(i => Item($"r{i}", $"Note {i:D2}", refs: ["char-amy"], order: 20 - i))
            .ToList();

        var first = SceneResearch.Suggest(items, ["char-amy"], []);
        Assert.Equal(SceneResearch.MaxSuggestions, first.Count);

        // Same input, same order - a panel that reshuffles itself is one nobody
        // learns the shape of.
        var again = SceneResearch.Suggest(items.AsEnumerable().Reverse(), ["char-amy"], []);
        Assert.Equal(first.Select(s => s.Item.Id), again.Select(s => s.Item.Id));
        // The writer's own ordering decides among equal scores.
        Assert.Equal("r19", first[0].Item.Id);
    }

    [Fact]
    public void AnUnknownEntityIdFallsBackToTheTagRatherThanShowingAGuid()
    {
        var items = new[] { Item("r1", "Bridges", refs: ["char-amy"], tags: ["winter"]) };

        var only = Assert.Single(SceneResearch.Suggest(items, ["char-amy"], ["winter"], names: null));
        Assert.Equal("winter", only.Reason);

        // And with neither a name nor a tag there is simply no reason to give.
        var noTag = new[] { Item("r2", "Bridges", refs: ["char-amy"]) };
        Assert.Equal(string.Empty, Assert.Single(SceneResearch.Suggest(noTag, ["char-amy"], [])).Reason);
    }

    [Fact]
    public void ANameThatIsBlankIsNoBetterThanNoName()
    {
        var items = new[] { Item("r1", "Bridges", refs: ["char-amy"], tags: ["winter"]) };

        var only = Assert.Single(SceneResearch.Suggest(
            items, ["char-amy"], ["winter"], new Dictionary<string, string> { ["char-amy"] = "  " }));
        Assert.Equal("winter", only.Reason);
    }

    [Fact]
    public void ANullItemInTheListIsSkippedRatherThanThrowing()
    {
        var items = new ResearchItem?[] { null, Item("r1", "Bridges", refs: ["char-amy"]) };

        Assert.Single(SceneResearch.Suggest(items!, ["char-amy"], []));
    }
}
