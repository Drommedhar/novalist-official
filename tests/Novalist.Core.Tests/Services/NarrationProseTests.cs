using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Covers marking a scene's own HTML up with the reading: that every segment is
/// addressable in the prose, that nothing of the writer's text is lost or
/// reordered, and - the one that matters - that a marker never wraps a tag.
/// </summary>
[Collection(LexiconStaticsCollection.Name)]
public class NarrationProseTests
{
    private static readonly CharacterData Mira = new() { Id = "mira", Name = "Mira" };

    private static IReadOnlyList<NarrationSegment> Segments(string html)
    {
        var lexicon = SceneAnalysisLexicon.For("en");
        return NarrationScript.Build(
            html,
            DialogueAttributor.BuildCandidates([Mira], wordBoundaries: true),
            DialogueAttributor.BuildLanguage(lexicon),
            EmotionDirector.BuildLanguage(lexicon),
            null, null, null, null);
    }

    private static string Annotate(string html) => NarrationProse.Annotate(html, Segments(html));

    /// <summary>Every marker's contents, in document order.</summary>
    private static IReadOnlyList<string> Markers(string annotated)
    {
        var found = new List<string>();
        var at = 0;
        while (true)
        {
            var open = annotated.IndexOf("<span data-nl-seg=", at, StringComparison.Ordinal);
            if (open < 0) break;
            var contentStart = annotated.IndexOf('>', open) + 1;
            var close = annotated.IndexOf("</span>", contentStart, StringComparison.Ordinal);
            found.Add(annotated[contentStart..close]);
            at = contentStart;
        }
        return found;
    }

    [Fact]
    public void Annotate_MarksTheQuoteAndTheTagSeparately()
    {
        var annotated = Annotate("<p>\"Get out,\" she said, not turning round.</p>");

        Assert.Equal(
            ["\"Get out,\"", " she said, not turning round."],
            Markers(annotated));
        Assert.Contains("data-nl-kind=\"dialogue\"", annotated);
        Assert.Contains("data-nl-kind=\"narration\"", annotated);
    }

    [Fact]
    public void Annotate_KeysEachMarkerToItsSegment()
    {
        const string html = "<p>\"Get out,\" she said.</p>";
        var segments = Segments(html);
        var annotated = NarrationProse.Annotate(html, segments);

        foreach (var segment in segments)
            Assert.Contains($"data-nl-seg=\"{segment.Key}\"", annotated);
    }

    [Fact]
    public void Annotate_NeverPutsATagInsideAMarker()
    {
        // The whole safety property. A marker holding "</p><p>" would be
        // mis-nested markup, and the scene would come apart on screen.
        var annotated = Annotate(
            "<p>The tide turned.</p><p>It turned again. \"Late,\" said Mira.</p>");

        Assert.All(Markers(annotated), marker => Assert.DoesNotContain("<", marker));
    }

    [Fact]
    public void Annotate_ASegmentCrossingAParagraphGetsAMarkerEachSide()
    {
        // One narration run, two paragraphs, one key.
        const string html = "<p>The tide turned.</p><p>It turned again.</p>";
        var segments = Segments(html);
        var annotated = NarrationProse.Annotate(html, segments);

        var key = Assert.Single(segments).Key;
        var markers = Markers(annotated);
        Assert.Equal(["The tide turned.", "It turned again."], markers);
        Assert.Equal(2, Occurrences(annotated, $"data-nl-seg=\"{key}\""));
    }

    [Fact]
    public void Annotate_ASegmentStraddlingEmphasisIsCutAtIt()
    {
        var annotated = Annotate("<p>She was <em>late</em> again.</p>");

        Assert.Equal(["She was ", "late", " again."], Markers(annotated));
        // The emphasis survives, outside the markers rather than crossed by them.
        Assert.Contains("<em>", annotated);
        Assert.Contains("</em>", annotated);
    }

    [Fact]
    public void Annotate_KeepsEveryWordOfTheProse()
    {
        // The invariant worth more than any of the shape assertions: marking the
        // reading up must not change what the scene says.
        const string html =
            "<p>The tide turned &amp; the cold got in. \"You are <em>late</em>,\" Mira snapped.</p>" +
            "<p>She did not turn round.</p>";

        var annotated = NarrationProse.Annotate(html, Segments(html));

        Assert.Equal(
            DialogueScanner.ProjectScene(html).Text,
            DialogueScanner.ProjectScene(annotated).Text);
    }

    [Fact]
    public void Annotate_HandlesAnEntityAsOneCharacter()
    {
        var annotated = Annotate("<p>Salt &amp; rope.</p>");

        var marker = Assert.Single(Markers(annotated));
        Assert.Equal("Salt &amp; rope.", marker);
    }

    [Fact]
    public void Annotate_TwoSegmentsThatMeetDoNotNest()
    {
        // A quote with the tag hard against it: the close has to be written
        // before the next open at the same index.
        var annotated = Annotate("<p>\"Go.\"she said.</p>");

        Assert.Equal(["\"Go.\"", "she said."], Markers(annotated));
        Assert.DoesNotContain("<span data-nl-seg=\"n:", annotated[..annotated.IndexOf("</span>", StringComparison.Ordinal)]);
    }

    [Fact]
    public void Annotate_NothingToMarkLeavesTheSceneExactlyAsItWas()
    {
        const string html = "<p>The tide turned.</p>";

        Assert.Equal(html, NarrationProse.Annotate(html, []));
        Assert.Equal(string.Empty, NarrationProse.Annotate(null, Segments(html)));
        Assert.Equal(string.Empty, NarrationProse.Annotate("", Segments(html)));
    }

    [Fact]
    public void Annotate_MarkupWithNoTextIsLeftAlone()
    {
        const string html = "<p><br></p>";

        Assert.Equal(html, NarrationProse.Annotate(html, Segments("<p>Something else.</p>")));
    }

    [Fact]
    public void Annotate_ASegmentPastTheEndOfTheSceneIsIgnored()
    {
        // Only reachable if a caller pairs a script with another scene's HTML,
        // which is a bug - but it must not throw and must not corrupt the prose.
        const string html = "<p>Short.</p>";
        var stray = Segments("<p>A much longer scene than the other one.</p>");

        var annotated = NarrationProse.Annotate(html, stray);

        Assert.Equal(
            DialogueScanner.ProjectScene(html).Text,
            DialogueScanner.ProjectScene(annotated).Text);
    }

    [Fact]
    public void Annotate_EscapesAKeyThatCouldEndItsOwnAttribute()
    {
        var segment = new NarrationSegment(
            0, NarrationSegmentKind.Narration, "a\"<&b", "Salt.", null,
            DialogueConfidence.None, [], new VoiceDirection("neutral", new Dictionary<string, double>(),
                DirectionSource.None), 0, 5);

        var annotated = NarrationProse.Annotate("<p>Salt.</p>", [segment]);

        Assert.Contains("data-nl-seg=\"a&quot;&lt;&amp;b\"", annotated);
    }

    [Fact]
    public void Annotate_ASegmentWithNoTextOfItsOwnMarksNothing()
    {
        // A zero-width range addresses no character, so there is nothing to put
        // a marker round - and the scene comes back exactly as it went in.
        const string html = "<p>Salt.</p>";
        var empty = new NarrationSegment(
            0, NarrationSegmentKind.Narration, "k", "", null,
            DialogueConfidence.None, [], Plain(), 2, 2);

        Assert.Equal(html, NarrationProse.Annotate(html, [empty]));
    }

    [Fact]
    public void Annotate_ASegmentPastTheEndOfTheSceneMarksNothing()
    {
        // Clamped to nothing rather than throwing: only reachable when a caller
        // pairs a script with another scene's HTML, which is a bug, but not one
        // that should take the prose with it.
        const string html = "<p>Salt.</p>";
        var beyond = new NarrationSegment(
            0, NarrationSegmentKind.Narration, "k", "", null,
            DialogueConfidence.None, [], Plain(), 900, 950);

        Assert.Equal(html, NarrationProse.Annotate(html, [beyond]));
    }

    [Fact]
    public void ProjectScene_MarkupWithNothingInItProjectsNothing()
    {
        foreach (var html in new[] { null, "" })
        {
            var projection = DialogueScanner.ProjectScene(html);
            Assert.Equal(string.Empty, projection.Text);
            Assert.Empty(projection.Start);
            Assert.Empty(projection.End);
        }
    }

    /// <summary>A direction with nothing said about it, for the segments these
    /// tests build by hand.</summary>
    private static VoiceDirection Plain()
        => new("neutral", new Dictionary<string, double>(), DirectionSource.None);

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        var at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }
        return count;
    }
}
