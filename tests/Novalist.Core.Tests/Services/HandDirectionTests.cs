using Novalist.Core.Services;
using Novalist.Sdk.Models.Narration;
using Xunit;
using NarrationSegment = Novalist.Core.Services.NarrationSegment;
using VoiceDirection = Novalist.Core.Services.VoiceDirection;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Covers the writer overruling the machine: sliders pushed by hand, a standing
/// register on a character, and a clip pointed at instead of described.
///
/// The thread running through all three is that what the writer set is what
/// gets performed. A number quietly rescaled, a register silently dropped, or a
/// reference sent to an engine that cannot take one are all failures that can
/// only be caught by ear.
/// </summary>
[Collection(LexiconStaticsCollection.Name)]
public class HandDirectionTests
{
    private static DirectionLanguage NoVerbs()
        => EmotionDirector.BuildLanguage(null);

    // ─── sliders pushed by hand ─────────────────────────────────────

    [Fact]
    public void AHandPushedVector_IsWhatGetsPerformed()
    {
        var direction = EmotionDirector.Resolve(
            "v:happy=0.8,surprised=0.3", null, null, null, null, NoVerbs());

        Assert.Equal(DirectionSource.Writer, direction.Source);
        Assert.Equal(0.8, direction.Vector["happy"], 3);
        Assert.Equal(0.3, direction.Vector["surprised"], 3);
    }

    [Fact]
    public void AHandPushedVector_IsNotRescaledByTheScenesIntensity()
    {
        // The screen shows what the writer set. Scaling it behind them would be
        // the screen and the ear disagreeing.
        var direction = EmotionDirector.Resolve(
            "v:angry=0.9", null, null, "peaceful", -10, NoVerbs());

        Assert.Equal(0.9, direction.Vector["angry"], 3);
    }

    [Fact]
    public void AHandPushedVector_IsNotReducedBecauseTheLineIsNarration()
    {
        var direction = EmotionDirector.Resolve(
            "v:sad=0.9", null, null, null, null, NoVerbs(),
            EmotionDirector.NarrationMagnitude);

        Assert.Equal(0.9, direction.Vector["sad"], 3);
    }

    [Fact]
    public void AHandPushedVectorWithNoName_IsCalledSomethingTheLexiconDoesNotOwn()
    {
        // Labelling it with the nearest of the sixteen names would be putting
        // back the word the writer had just refused.
        var direction = EmotionDirector.Resolve(
            "v:happy=0.5", null, null, null, null, NoVerbs());

        Assert.Equal(EmotionDirector.CustomKey, direction.Key);
    }

    [Fact]
    public void AHandPushedVectorKeepsItsNameWhenItWasGivenOne()
    {
        var direction = EmotionDirector.Resolve(
            "angry|v:angry=0.4", null, null, null, null, NoVerbs());

        Assert.Equal("angry", direction.Key);
        Assert.Equal(0.4, direction.Vector["angry"], 3);
    }

    [Fact]
    public void AHandPushedVectorAskingForEverything_IsHeldUnderTheCeiling()
    {
        var direction = EmotionDirector.Resolve(
            "v:happy=1,angry=1,sad=1", null, null, null, null, NoVerbs());

        Assert.True(direction.Vector.Values.Sum() <= EmotionDirector.MaxVectorSum + 0.001);
    }

    [Fact]
    public void AVectorHeldUnderTheCeiling_KeepsItsProportions()
    {
        // Truncating dimension by dimension turns two parts grief to one part
        // fear into equal parts, and the line stops being desperate.
        var held = EmotionDirector.Held(
            new Dictionary<string, double> { ["sad"] = 1.0, ["afraid"] = 0.5 });

        Assert.Equal(2, held["sad"] / held["afraid"], 2);
    }

    // ─── a standing register ────────────────────────────────────────

    [Fact]
    public void AStandingRegister_IsAddedToTheLinesOwnDirection()
    {
        var line = new Dictionary<string, double> { ["angry"] = 0.6 };

        var performed = EmotionDirector.WithRegister(
            line, new Dictionary<string, double> { ["calm"] = 0.2 });

        Assert.Equal(0.6, performed["angry"], 3);
        Assert.Equal(0.2, performed["calm"], 3);
    }

    [Fact]
    public void ARegisterAddsToADimensionTheLineAlreadyHas()
    {
        var performed = EmotionDirector.WithRegister(
            new Dictionary<string, double> { ["calm"] = 0.4 },
            new Dictionary<string, double> { ["calm"] = 0.3 });

        Assert.Equal(0.7, performed["calm"], 3);
    }

    [Fact]
    public void ARegisterCanTakeEmotionAway_ForSomebodyFlatterThanTheProseSays()
    {
        var performed = EmotionDirector.WithRegister(
            new Dictionary<string, double> { ["happy"] = 0.6 },
            new Dictionary<string, double> { ["happy"] = -0.4 });

        Assert.Equal(0.2, performed["happy"], 3);
    }

    [Fact]
    public void ARegisterThatWouldTakeADimensionBelowNothing_SimplyRemovesIt()
    {
        var performed = EmotionDirector.WithRegister(
            new Dictionary<string, double> { ["happy"] = 0.2 },
            new Dictionary<string, double> { ["happy"] = -0.9 });

        Assert.DoesNotContain("happy", performed.Keys);
    }

    [Fact]
    public void AFuriousLineFromAFlatCharacter_IsStillFurious()
    {
        var performed = EmotionDirector.WithRegister(
            new Dictionary<string, double> { ["angry"] = 0.9 },
            new Dictionary<string, double> { ["calm"] = 0.3 });

        Assert.True(performed["angry"] > performed["calm"]);
    }

    [Fact]
    public void ARegisterOfNothing_ChangesNothing()
    {
        var line = new Dictionary<string, double> { ["angry"] = 0.6 };

        Assert.Same(line, EmotionDirector.WithRegister(line, null));
        Assert.Same(line, EmotionDirector.WithRegister(line, new Dictionary<string, double>()));
    }

    [Fact]
    public void ARegisterNamingADimensionNoEngineTakes_IsIgnored()
    {
        var performed = EmotionDirector.WithRegister(
            new Dictionary<string, double> { ["angry"] = 0.6 },
            new Dictionary<string, double> { ["smug"] = 0.4, ["calm"] = 0 });

        Assert.Equal(["angry"], performed.Keys);
    }

    [Fact]
    public void TheCastSheetKnowsWhoseRegisterIsWhose()
    {
        var sheet = new VoiceCastSheet
        {
            NarratorRegister = new Dictionary<string, double> { ["calm"] = 0.2 },
            Registers = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal)
            {
                ["mira"] = new() { ["angry"] = 0.1 }
            }
        };

        Assert.Equal(0.2, sheet.RegisterFor(null)!["calm"], 3);
        Assert.Equal(0.2, sheet.RegisterFor(string.Empty)!["calm"], 3);
        Assert.Equal(0.1, sheet.RegisterFor("mira")!["angry"], 3);
        Assert.Null(sheet.RegisterFor("aldric"));
    }

    // ─── like that line ─────────────────────────────────────────────

    [Fact]
    public void AClipTheWriterPointedAt_ReachesAnEngineThatTakesOne()
    {
        var direction = new VoiceDirection(
            "angry", new Dictionary<string, double>(), DirectionSource.Writer, null, "a1.wav");

        var sent = NarrationRender.Direct(
            direction,
            VoiceEngineFeatures.EmotionVector | VoiceEngineFeatures.EmotionReference,
            null,
            [1, 2, 3]);

        Assert.Equal([1, 2, 3], sent.ReferenceAudio);
    }

    [Fact]
    public void AClip_IsNotSentToAnEngineThatCannotTakeOne()
    {
        var direction = new VoiceDirection(
            "angry", new Dictionary<string, double>(), DirectionSource.Writer, null, "a1.wav");

        var sent = NarrationRender.Direct(
            direction, VoiceEngineFeatures.EmotionVector, null, [1, 2, 3]);

        Assert.Null(sent.ReferenceAudio);
    }

    [Fact]
    public void AClipReachesEvenAnEngineThatIsToldNothingElse()
    {
        // It is the most precise thing the writer can say, so it goes wherever
        // it can be taken - including to an engine that reads affect off the
        // script and is otherwise sent no direction at all.
        var direction = new VoiceDirection(
            "angry", new Dictionary<string, double>(), DirectionSource.Writer, null, "a1.wav");

        var sent = NarrationRender.Direct(
            direction,
            VoiceEngineFeatures.EmotionInferred | VoiceEngineFeatures.EmotionReference,
            null,
            [9]);

        Assert.Equal([9], sent.ReferenceAudio);
    }

    [Fact]
    public void ARunSaysWhichClipsItNeeds_SoOnlyThoseAreReadOffDisk()
    {
        var segments = new[]
        {
            Segment("one", "a1.wav"),
            Segment("two", "a1.wav"),
            Segment("three", null),
            Segment("four", "b2.wav")
        };

        Assert.Equal(["a1.wav", "b2.wav"], NarrationRender.ClipsNeeded(segments));
    }

    [Fact]
    public void AClipThatIsNoLongerInTheCache_LeavesTheLineOnItsVector()
    {
        // A reference that has expired must not stop a render; the line is
        // performed on its numbers instead.
        var segments = new[] { Segment("one", "gone.wav") };
        var sheet = new VoiceCastSheet { NarratorVoiceId = "narrator" };
        var voices = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["narrator"] = [1] };

        var request = NarrationRender.Build(
            segments, sheet, voices,
            VoiceEngineFeatures.EmotionVector | VoiceEngineFeatures.EmotionReference,
            "en", 1.0, new Dictionary<string, byte[]>(StringComparer.Ordinal));

        Assert.Null(Assert.Single(request.Segments).Direction.ReferenceAudio);
    }

    [Fact]
    public void ASpeakersStandingRegister_ReachesTheEngineWithTheirLine()
    {
        var segments = new[]
        {
            new NarrationSegment(
                0, NarrationSegmentKind.Dialogue, "k", "k", "Get out.", "mira",
                DialogueConfidence.Manual, [],
                new VoiceDirection(
                    "angry", new Dictionary<string, double> { ["angry"] = 0.6 },
                    DirectionSource.Writer),
                0, 8)
        };
        var sheet = new VoiceCastSheet
        {
            NarratorVoiceId = "narrator",
            Voices = new Dictionary<string, string>(StringComparer.Ordinal) { ["mira"] = "mira-voice" },
            Registers = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal)
            {
                ["mira"] = new() { ["calm"] = 0.2 }
            }
        };
        var voices = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["mira-voice"] = [1] };

        var request = NarrationRender.Build(
            segments, sheet, voices, VoiceEngineFeatures.EmotionVector, "en");

        var sent = Assert.Single(request.Segments).Direction;
        Assert.Equal(0.6, sent.Vector["angry"], 3);
        Assert.Equal(0.2, sent.Vector["calm"], 3);
    }

    private static NarrationSegment Segment(string key, string? clip)
        => new(
            0, NarrationSegmentKind.Narration, key, key, "Some prose.", null,
            DialogueConfidence.None, [],
            new VoiceDirection(
                "neutral", new Dictionary<string, double> { ["calm"] = 0.6 },
                DirectionSource.None, null, clip),
            0, 11);
}
