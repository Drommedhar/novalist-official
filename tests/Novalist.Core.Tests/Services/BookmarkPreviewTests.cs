using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// The few lines a bookmark shows of what it points at.
///
/// A bookmark that only navigates makes a writer go and look to remember why
/// they kept it, and for a list of thirty that is thirty trips.
/// </summary>
public class BookmarkPreviewTests
{
    private const string Scene =
        "The bell rang once over the harbour. Mira counted the boats and found one missing. "
        + "She had known since Tuesday and said nothing, which was the part she would think "
        + "about later, long after the tide had turned and the rest of it stopped mattering.";

    [Fact]
    public void WithNoAnchorThePreviewIsTheOpening()
    {
        var preview = BookmarkPreview.Extract(Scene, null);

        Assert.StartsWith("The bell rang once", preview);
    }

    [Fact]
    public void AnAnchorBringsItsOwnPassageIntoView()
    {
        var preview = BookmarkPreview.Extract(Scene, "known since Tuesday");

        Assert.Contains("known since Tuesday", preview);
    }

    [Fact]
    public void TheSentenceLeadingIntoTheAnchorIsKept()
    {
        var preview = BookmarkPreview.Extract(Scene, "known since Tuesday");

        // Starting exactly at the anchor would open mid-clause, and a preview
        // that starts mid-clause is harder to place than one that starts early.
        Assert.StartsWith("…", preview);
        Assert.Contains("found one missing", preview);
    }

    [Fact]
    public void AnAnchorTheProseNoLongerHasFallsBackToTheOpening()
    {
        var preview = BookmarkPreview.Extract(Scene, "a line that was rewritten away");

        // The scene is still worth recognising, and an empty preview reads as a
        // broken bookmark rather than as an edited one.
        Assert.StartsWith("The bell rang once", preview);
    }

    [Fact]
    public void TheAnchorIsFoundWhateverTheCase()
        => Assert.Contains("Mira counted", BookmarkPreview.Extract(Scene, "MIRA COUNTED"));

    [Fact]
    public void ALongSceneIsCutAndSaysSo()
    {
        var preview = BookmarkPreview.Extract(Scene, null);

        Assert.True(preview.Length <= BookmarkPreview.Length + 2);
        Assert.EndsWith("…", preview);
    }

    [Fact]
    public void AShortSceneIsShownWholeWithNoEllipsis()
    {
        var preview = BookmarkPreview.Extract("She left.", null);

        Assert.Equal("She left.", preview);
    }

    [Fact]
    public void ParagraphBreaksBecomeSingleSpaces()
    {
        var preview = BookmarkPreview.Extract("One.\n\n\nTwo.\t\tThree.", null);

        // Three lines tall in a list of thirty is a wall; one line each is what
        // makes the list scannable.
        Assert.Equal("One. Two. Three.", preview);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public void NothingToShowIsAnEmptyPreview(string? text)
        => Assert.Equal(string.Empty, BookmarkPreview.Extract(text, null));

    [Fact]
    public void AnAnchorOfNothingIsNoAnchor()
        => Assert.StartsWith("The bell rang once", BookmarkPreview.Extract(Scene, "   "));
}
