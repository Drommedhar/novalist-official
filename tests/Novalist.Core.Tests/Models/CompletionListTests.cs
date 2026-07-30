using Novalist.Core.Models;
using Xunit;

namespace Novalist.Core.Tests.Models;

/// <summary>
/// The book's own completion list.
///
/// The @-mention picker completes Codex names in scene prose and nothing else,
/// which leaves out everything a secondary world is full of and the Codex is
/// not - a settled spelling, a rank, a coined verb. Those get retyped slightly
/// differently, and the inconsistency turns up in copy-edit.
/// </summary>
public class CompletionListTests
{
    private static CompletionList With(params string[] words)
        => new() { Words = [.. words] };

    [Fact]
    public void APrefixOffersTheWordsThatContinueIt()
    {
        var list = With("Aerthorn", "Aerily", "Kaeryn");

        Assert.Equal(["Aerthorn", "Aerily"], list.Suggest("Aer"));
    }

    [Fact]
    public void MatchingIgnoresCaseButTheStoredSpellingIsWhatComesOut()
    {
        var list = With("Aerthorn");

        // The whole point is that the word comes out the way it was decided.
        Assert.Equal(["Aerthorn"], list.Suggest("aer"));
    }

    [Fact]
    public void AWordIsOfferedOnlyWhereItContinuesWhatWasTyped()
    {
        var list = With("Kaeryn");

        // Substring matching would offer "Kaeryn" for "aer", which is noise.
        Assert.Empty(list.Suggest("aer"));
    }

    [Fact]
    public void AWordIdenticalToWhatIsTypedCompletesNothing()
        => Assert.Empty(With("Aerthorn").Suggest("Aerthorn"));

    [Theory]
    [InlineData("Ae")]
    [InlineData("A")]
    [InlineData("")]
    [InlineData(null)]
    public void NothingIsOfferedBelowTheTrigger(string? typed)
    {
        // Two characters match half the list, and the popup becomes something
        // to dismiss rather than something to use.
        Assert.Empty(With("Aerthorn").Suggest(typed));
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(2, 3)]
    [InlineData(-5, 3)]
    [InlineData(5, 5)]
    [InlineData(40, 10)]
    public void TheTriggerIsClampedRatherThanRefused(int asked, int expected)
        => Assert.Equal(expected, new CompletionList { Trigger = asked }.EffectiveTrigger);

    [Fact]
    public void ALongerTriggerHoldsTheOfferBack()
    {
        var list = new CompletionList { Words = ["Aerthorn"], Trigger = 5 };

        Assert.Empty(list.Suggest("Aer"));
        Assert.Equal(["Aerthorn"], list.Suggest("Aerth"));
    }

    [Fact]
    public void OnlyAHandfulIsOffered()
    {
        var many = new CompletionList();
        for (var i = 0; i < 40; i++) many.Words.Add($"Aerthorn{i}");

        // A longer list is a menu to read rather than a completion to accept.
        Assert.Equal(CompletionList.MaxSuggestions, many.Suggest("Aer").Count);
    }

    [Fact]
    public void ABlankEntryIsNeverOffered()
        => Assert.Equal(["Aerthorn"], With("   ", "Aerthorn").Suggest("Aer"));

    [Fact]
    public void CleaningDropsBlanksAndFoldsDuplicatesKeepingOrder()
    {
        var cleaned = CompletionList.Clean(["  Aerthorn ", "", "kaeryn", "AERTHORN", "   ", "Sill"]);

        // A list somebody grouped by hand is a list they can find things in, so
        // the order they put it in is the order it keeps.
        Assert.Equal(["Aerthorn", "kaeryn", "Sill"], cleaned);
    }

    [Fact]
    public void CleaningNothingIsAnEmptyList()
        => Assert.Empty(CompletionList.Clean(null));

    [Fact]
    public void ABookStartsWithNothingToComplete()
    {
        var book = new BookData();

        Assert.Empty(book.Completions.Words);
        Assert.Equal(CompletionList.MinimumTrigger, book.Completions.EffectiveTrigger);
    }
}
