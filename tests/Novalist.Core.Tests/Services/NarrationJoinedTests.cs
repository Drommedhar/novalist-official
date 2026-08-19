using Novalist.Core.Services;
using Xunit;
using NarrationSegment = Novalist.Core.Services.NarrationSegment;
using VoiceDirection = Novalist.Core.Services.VoiceDirection;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Which consecutive sentences one voice says in one breath.
///
/// A sentence at a time is right for the live reading and wrong for a
/// recording: a cloning model starts each call afresh from the reference clip
/// with no memory of the sentence before it, so pitch, pace and energy reset at
/// every full stop and a stitched paragraph sounds like four readings rather
/// than one narrator.
///
/// Everything here is about what must <em>not</em> be joined. Joining too
/// eagerly is the dangerous direction: a run that should have been two calls
/// silently throws one line's direction away, or hands the model more than it
/// will say in one breath and gets back prose cut off mid-word.
/// </summary>
public class NarrationJoinedTests
{
    private static NarrationSegment Line(
        int index,
        string text,
        string? speaker = "mira",
        string emotion = "neutral",
        double weight = 0.6,
        string? like = null,
        NarrationSegmentKind? kind = null)
        => new(
            index,
            kind ?? (speaker == null ? NarrationSegmentKind.Narration : NarrationSegmentKind.Dialogue),
            "k" + index,
            "k" + index,
            text,
            speaker,
            DialogueConfidence.Manual,
            [],
            new VoiceDirection(
                emotion,
                new Dictionary<string, double> { ["calm"] = weight },
                DirectionSource.None,
                ReferenceClip: like),
            0,
            text.Length);

    private static IReadOnlyList<NarrationRender.NarrationJoin> Joined(
        IReadOnlyList<NarrationSegment> lines, int max = 400)
        => NarrationRender.Joined(lines, max);

    [Fact]
    public void OneVoiceSayingThreeSentences_SaysThemInOneBreath()
    {
        var only = Assert.Single(Joined([Line(0, "One."), Line(1, "Two."), Line(2, "Three.")]));

        Assert.Equal("One. Two. Three.", only.Segment.Text);
        // It stands for three lines, and everything counting lines has to know.
        Assert.Equal(3, only.Covers);
    }

    [Fact]
    public void TheRunKeepsTheFirstLinesKeyAndDirection()
    {
        // The first line is the one the recording reaches first, and every other
        // line in the run agreed with it or would not be in it.
        var only = Assert.Single(Joined([Line(0, "One."), Line(1, "Two.")]));

        Assert.Equal("k0", only.Segment.Key);
        Assert.Equal("neutral", only.Segment.Direction.Key);
    }

    [Fact]
    public void TwoSpeakers_AreTwoBreaths()
    {
        var joined = Joined([Line(0, "One."), Line(1, "Two.", speaker: "aldric")]);

        Assert.Equal(2, joined.Count);
        Assert.All(joined, j => Assert.Equal(1, j.Covers));
    }

    [Fact]
    public void NarrationAndDialogue_AreNeverOneBreath()
    {
        // Never across the quote marks: the tag is the narrator's and the line
        // is the character's, which is the whole of how this reading works.
        var joined = Joined([
            Line(0, "\"One,\"", kind: NarrationSegmentKind.Dialogue),
            Line(1, "she said.", kind: NarrationSegmentKind.Narration)
        ]);

        Assert.Equal(2, joined.Count);
    }

    [Fact]
    public void LinesDirectedDifferently_AreTwoBreaths()
    {
        // One call performs one delivery, so merging would throw one away.
        Assert.Equal(2, Joined([Line(0, "One."), Line(1, "Two.", emotion: "angry")]).Count);
    }

    [Fact]
    public void TheSameEmotionSetToADifferentDegree_IsStillADifferentDelivery()
    {
        // The name matches and the numbers do not. Comparing names alone would
        // read a line at half the intensity the writer set.
        Assert.Equal(2, Joined([Line(0, "One."), Line(1, "Two.", weight: 0.9)]).Count);
    }

    [Fact]
    public void ADeliveryWithAnExtraDimensionSetByHand_IsItsOwnBreath()
    {
        // Behind the sixteen names are eight sliders, and a line pushed on two
        // of them is not the same delivery as one pushed on the first alone -
        // however much the first one matches.
        var plain = Line(0, "One.");
        var mixed = Line(1, "Two.") with
        {
            Direction = new VoiceDirection(
                "neutral",
                new Dictionary<string, double> { ["calm"] = 0.6, ["sad"] = 0.2 },
                DirectionSource.Writer)
        };

        Assert.Equal(2, Joined([plain, mixed]).Count);
    }

    [Fact]
    public void ALineToldToSoundLikeAnother_IsItsOwnBreath()
    {
        Assert.Equal(2, Joined([Line(0, "One."), Line(1, "Two.", like: "clip.wav")]).Count);
        // And two pointing at the same clip are not thereby separated.
        Assert.Single(Joined([Line(0, "One.", like: "clip.wav"), Line(1, "Two.", like: "clip.wav")]));
    }

    [Fact]
    public void ARunPastWhatAModelWillSay_IsBrokenBeforeItIsSent()
    {
        // The failure that made splitting necessary: prose past what a model
        // says in one breath comes back cut off mid-word, and nothing says so.
        var joined = Joined([Line(0, "aaaa."), Line(1, "bbbb."), Line(2, "cccc.")], max: 12);

        Assert.Equal(2, joined.Count);
        Assert.All(joined, j => Assert.True(j.Segment.Text.Length <= 12));
        Assert.Equal(3, joined.Sum(j => j.Covers));
    }

    [Fact]
    public void ACeilingOfNothing_ReadsEverySentenceOnItsOwn()
    {
        // What the live reading asks for: the highlight follows the voice, and
        // correcting a line costs a line.
        var joined = Joined([Line(0, "One."), Line(1, "Two.")], max: 0);

        Assert.Equal(2, joined.Count);
        Assert.All(joined, j => Assert.Equal(1, j.Covers));
    }

    [Fact]
    public void NothingToRead_IsNothingToSay()
        => Assert.Empty(Joined([]));

    [Fact]
    public void OneLine_IsHandedOnUntouched()
    {
        // Not rebuilt into a run of one: the object that goes to the engine
        // should be the object the reading already has.
        var line = Line(0, "One.");

        var only = Assert.Single(Joined([line]));

        Assert.Same(line, only.Segment);
    }

    [Fact]
    public void ARunThatEndsAndBeginsAgain_IsTwoRunsRatherThanOne()
    {
        // Mira, Aldric, Mira: the last is a new breath, not a continuation of
        // the first.
        var joined = Joined([
            Line(0, "One."), Line(1, "Two.", speaker: "aldric"), Line(2, "Three.")
        ]);

        Assert.Equal(3, joined.Count);
        Assert.Equal("Three.", joined[2].Segment.Text);
    }
}
