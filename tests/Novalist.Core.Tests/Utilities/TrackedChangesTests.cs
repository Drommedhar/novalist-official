using Novalist.Core.Utilities;
using Xunit;

namespace Novalist.Core.Tests.Utilities;

/// <summary>
/// Suggested edits stored in the prose itself.
///
/// Keeping them in the HTML means they travel wherever the scene travels. The
/// cost is that everything reading prose has to know what they mean, which is
/// what these guard.
/// </summary>
public class TrackedChangesTests
{
    private const string Ins =
        "<ins data-nl-change=\"a1\" data-nl-author=\"Mira\" data-nl-at=\"2026-03-14\">twice</ins>";
    private const string Del =
        "<del data-nl-change=\"b2\" data-nl-author=\"Mira\" data-nl-at=\"2026-03-14\">once</del>";

    private static string Sentence => $"<p>The bell rang {Del}{Ins}.</p>";

    // ── Reading ──

    [Fact]
    public void ProseWithNoSuggestionsHasNone()
    {
        Assert.False(TrackedChanges.HasChanges("<p>The bell rang once.</p>"));
        Assert.False(TrackedChanges.HasChanges(null));
        Assert.Empty(TrackedChanges.Pending("<p>Nothing here.</p>"));
        Assert.Equal(0, TrackedChanges.Count(null));
    }

    [Fact]
    public void EachSuggestionIsReadBackWithWhoAndWhen()
    {
        var pending = TrackedChanges.Pending(Sentence);

        Assert.Equal(2, pending.Count);
        Assert.Equal(ChangeKind.Deletion, pending[0].Kind);
        Assert.Equal("once", pending[0].Text);
        Assert.Equal("Mira", pending[0].Author);
        Assert.Equal("2026-03-14", pending[0].At);
        Assert.Equal(ChangeKind.Insertion, pending[1].Kind);
        Assert.Equal("twice", pending[1].Text);
    }

    [Fact]
    public void APlainDelIsStrikethrough_NotASuggestedCut()
    {
        // A writer struck those words on purpose and wants them printed
        // struck. Reading it as a suggested cut would drop them from every
        // export - the same shape of bug as losing the prose outright.
        const string struck = "<p>She kept <del>most of</del> it.</p>";

        Assert.False(TrackedChanges.HasChanges(struck));
        Assert.Empty(TrackedChanges.Pending(struck));
        Assert.Equal(struck, TrackedChanges.Final(struck));
        Assert.Contains("most of", TextDiff.StripHtml(struck));
    }

    [Fact]
    public void APlainInsIsUnderlining_NotASuggestedAddition()
        => Assert.False(TrackedChanges.HasChanges("<p>a <ins>b</ins> c</p>"));

    [Fact]
    public void AMarkedTagIsASuggestionEvenAmongPlainOnes()
    {
        var mixed = $"<p>plain <del>struck</del> and {Ins}</p>";

        var change = Assert.Single(TrackedChanges.Pending(mixed));
        Assert.Equal("a1", change.Id);
        Assert.Contains("struck", TrackedChanges.Final(mixed));
    }

    [Fact]
    public void MarkupInsideASuggestionIsNotPartOfItsText()
        => Assert.Equal("bold word", Assert.Single(
            TrackedChanges.Pending("<ins data-nl-change=\"x\"><b>bold</b> word</ins>")).Text);

    // ── Resolving ──

    [Fact]
    public void TheFinalTextReadsAsIfEverySuggestionWereTaken()
        => Assert.Equal("<p>The bell rang twice.</p>", TrackedChanges.Final(Sentence));

    [Fact]
    public void TheOriginalTextReadsAsItDidBeforeAnybodySuggestedAnything()
        => Assert.Equal("<p>The bell rang once.</p>", TrackedChanges.Original(Sentence));

    [Fact]
    public void AcceptingOneLeavesTheRestPending()
    {
        var after = TrackedChanges.Accept(Sentence, "a1");

        Assert.DoesNotContain("<ins", after);
        Assert.Contains("<del", after);
        Assert.Contains("twice", after);
        Assert.Equal(ChangeKind.Deletion, Assert.Single(TrackedChanges.Pending(after)).Kind);
    }

    [Fact]
    public void RejectingAnInsertionTakesItsWordsOut()
    {
        var after = TrackedChanges.Reject(Sentence, "a1");

        Assert.DoesNotContain("twice", after);
        Assert.Contains("once", after);
    }

    [Fact]
    public void AcceptingADeletionTakesItsWordsOut()
        => Assert.DoesNotContain("once", TrackedChanges.Accept(Sentence, "b2"));

    [Fact]
    public void RejectingADeletionPutsTheWordsBackAsProse()
    {
        var after = TrackedChanges.Reject(Sentence, "b2");

        Assert.Contains("once", after);
        Assert.DoesNotContain("<del", after);
        Assert.Equal(ChangeKind.Insertion, Assert.Single(TrackedChanges.Pending(after)).Kind);
    }

    [Fact]
    public void AnIdThatIsNotThereChangesNothing()
        => Assert.Equal(Sentence, TrackedChanges.Accept(Sentence, "no-such-change"));

    [Fact]
    public void AcceptAllAndRejectAllAreTheTwoWholeReadings()
    {
        Assert.Equal(TrackedChanges.Final(Sentence), TrackedChanges.AcceptAll(Sentence));
        Assert.Equal(TrackedChanges.Original(Sentence), TrackedChanges.RejectAll(Sentence));
        Assert.False(TrackedChanges.HasChanges(TrackedChanges.AcceptAll(Sentence)));
        Assert.False(TrackedChanges.HasChanges(TrackedChanges.RejectAll(Sentence)));
    }

    [Fact]
    public void ResolvingProseWithNothingInItIsSafe()
    {
        Assert.Equal(string.Empty, TrackedChanges.Final(null));
        Assert.Equal(string.Empty, TrackedChanges.Original(""));
        Assert.Equal("<p>Plain.</p>", TrackedChanges.Final("<p>Plain.</p>"));
    }

    [Fact]
    public void MarkupInsideAnAcceptedInsertionSurvives()
        => Assert.Equal("<p><b>bold</b></p>", TrackedChanges.Final(
            "<p><ins data-nl-change=\"x\"><b>bold</b></ins></p>"));

    [Fact]
    public void AClosingTagWithSpaceIsStillAClosingTag()
        // Written by hand or by another tool; refusing it would silently leave
        // a suggestion unresolved in an exported book.
        => Assert.Equal("<p>b</p>", TrackedChanges.Final("<p><ins data-nl-change=\"x\">b</ins ></p>"));

    // ── Building ──

    [Fact]
    public void AnInsertionRoundTripsThroughItsOwnMarkup()
    {
        var html = TrackedChanges.Insertion("x1", "twice", "Mira", "2026-03-14T10:00:00Z");

        var change = Assert.Single(TrackedChanges.Pending(html));
        Assert.Equal("x1", change.Id);
        Assert.Equal(ChangeKind.Insertion, change.Kind);
        Assert.Equal("twice", change.Text);
        Assert.Equal("Mira", change.Author);
    }

    [Fact]
    public void ADeletionRoundTripsThroughItsOwnMarkup()
    {
        var html = TrackedChanges.Deletion("x2", "once", "Mira", "2026-03-14T10:00:00Z");

        var change = Assert.Single(TrackedChanges.Pending(html));
        Assert.Equal(ChangeKind.Deletion, change.Kind);
        Assert.Equal("once", change.Text);
    }

    [Fact]
    public void AnAuthorNameCannotBreakOutOfItsAttribute()
    {
        // A name with a quote in it would otherwise rewrite the document.
        var html = TrackedChanges.Insertion("x", "word", "a\" onload=\"alert(1)", "now");

        Assert.DoesNotContain("onload=\"alert", html);
        Assert.Equal("a\" onload=\"alert(1)", Assert.Single(TrackedChanges.Pending(html)).Author);
    }

    [Fact]
    public void EveryCharacterThatWouldBreakTheMarkupIsEscaped()
    {
        // An ampersand, an angle bracket and a quote each end the attribute or
        // the tag if they travel through as written.
        var html = TrackedChanges.Insertion("x", "word", "Ada & <Co> \"Ltd\"", "now");

        Assert.Contains("&amp;", html);
        Assert.Contains("&lt;Co&gt;", html);
        Assert.Contains("&quot;", html);
        Assert.Equal("Ada & <Co> \"Ltd\"", Assert.Single(TrackedChanges.Pending(html)).Author);
    }

    // ── What everything downstream sees ──

    [Fact]
    public void StrippingProseToTextTakesTheSuggestionsIntoAccount()
    {
        // A word count that measured sentences somebody asked to cut, or a
        // search that found them, would both be wrong.
        Assert.Equal("The bell rang twice.", TextDiff.StripHtml(Sentence).Trim());
    }

    [Fact]
    public void ProseWithNoSuggestionsStripsExactlyAsItAlwaysDid()
        => Assert.Equal("The bell rang once.",
            TextDiff.StripHtml("<p>The bell rang once.</p>").Trim());
}
