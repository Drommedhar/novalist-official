using Novalist.Backend.Rpc;
using Novalist.Sdk.Hooks;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// The surface a continue-writing or beat action needs: invocation with nothing
/// selected, the prose before the caret, a typed directive, and an insertion
/// that replaces nothing.
///
/// The load-bearing default is <see cref="InlineActionDescriptor.AllowsEmptySelection"/>
/// being false: an action written before this existed must keep needing a
/// selection rather than appearing at every caret with an empty SelectedText it
/// was never written to handle.
/// </summary>
public class InlineActionCaretTests
{
    // ── The descriptor ──

    [Fact]
    public void AnActionNeedsASelectionUnlessItSaysOtherwise()
    {
        Assert.False(new InlineActionDescriptor { Id = "ai.rewrite" }.AllowsEmptySelection);
    }

    [Fact]
    public void AnActionCanOptIntoTheBareCaret()
    {
        Assert.True(
            new InlineActionDescriptor { Id = "ai.continue", AllowsEmptySelection = true }
                .AllowsEmptySelection);
    }

    // ── The request ──

    [Fact]
    public void ARequestCarriesNothingExtraByDefault()
    {
        var request = new InlineActionRequest();

        Assert.Empty(request.PrecedingText);
        Assert.Empty(request.Directive);
    }

    [Fact]
    public void ARequestCanCarryThePrecedingProseAndADirective()
    {
        var request = new InlineActionRequest
        {
            PrecedingText = "She opened the door.",
            Directive = "she finally admits it"
        };

        Assert.Equal("She opened the door.", request.PrecedingText);
        Assert.Equal("she finally admits it", request.Directive);
    }

    // ── The disposition ──

    [Fact]
    public void ReplaceSelectionIsStillTheDefault()
    {
        Assert.Equal(InlineActionDisposition.ReplaceSelection, new InlineActionResult().Disposition);
    }

    [Theory]
    [InlineData(InlineActionDisposition.ReplaceSelection, "replace")]
    [InlineData(InlineActionDisposition.InsertAfterSelection, "insertAfter")]
    [InlineData(InlineActionDisposition.InsertAtCaret, "insertAtCaret")]
    public void EveryDispositionHasItsOwnNameOnTheWire(
        InlineActionDisposition disposition, string expected)
    {
        // A new disposition silently collapsing to "replace" would overwrite the
        // writer's prose instead of adding to it.
        Assert.Equal(expected, ExtensionContribRpc.DispositionName(disposition));
    }

    // ── The slash keyword ──

    [Theory]
    [InlineData("ai.continue", "", "continue")]
    [InlineData("ai.beat", "", "beat")]
    [InlineData("continue", "", "continue")]
    [InlineData("ai.continue", "carryon", "carryon")]
    [InlineData("ai.continue", "  spaced  ", "spaced")]
    public void TheSlashKeywordFallsBackToTheIdsLastSegment(
        string id, string keyword, string expected)
    {
        var descriptor = new InlineActionDescriptor { Id = id, SlashKeyword = keyword };

        Assert.Equal(expected, ExtensionContribRpc.SlashKeyword(descriptor));
    }

    [Fact]
    public void AnIdEndingInADotFallsBackToTheWholeId()
    {
        // Degenerate, but "" as a slash keyword would match every query.
        Assert.Equal("ai.", ExtensionContribRpc.SlashKeyword(new InlineActionDescriptor { Id = "ai." }));
    }
}
