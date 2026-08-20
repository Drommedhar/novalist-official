using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk.Models.Narration;
using Xunit;
// Both assemblies have a NarrationSegment: Core's is the reading, the SDK's is
// what an engine is sent. This file is about the mapping between them, so it
// names Core's plainly and the SDK's never.
using NarrationSegment = Novalist.Core.Services.NarrationSegment;
using VoiceDirection = Novalist.Core.Services.VoiceDirection;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Covers how much an engine is told.
///
/// The three emotion flags are three and not one because engines take direction
/// in incompatible ways, and the wrong choice is not a degraded reading but a
/// wrong one - an engine that reads affect off the script itself being told what
/// to feel will override what it heard in the words.
/// </summary>
public class NarrationRenderTests
{
    private static NarrationSegment Segment(
        string key,
        string text,
        string? speakerId,
        string emotion = "angry",
        string? evidence = "snapped",
        NarrationSegmentKind kind = NarrationSegmentKind.Dialogue)
        => new(
            0, kind, key, key, text, speakerId, DialogueConfidence.High, [],
            new VoiceDirection(
                emotion,
                EmotionDirector.Vector(emotion, null),
                DirectionSource.Verb,
                evidence),
            0, text.Length);

    private static VoiceCastSheet Cast() => new()
    {
        NarratorVoiceId = "narrator-voice",
        Voices = { ["mira"] = "mira-voice" }
    };

    private static Dictionary<string, byte[]> Audio() => new(StringComparer.Ordinal)
    {
        ["narrator-voice"] = [1],
        ["mira-voice"] = [2]
    };

    [Fact]
    public void Build_CarriesTheWordsAndTheVoiceEachIsReadIn()
    {
        var request = NarrationRender.Build(
            [
                Segment("n:1", "She waited.", null, kind: NarrationSegmentKind.Narration),
                Segment("d:1", "You are late,", "mira")
            ],
            Cast(),
            Audio(),
            VoiceEngineFeatures.EmotionVector,
            "en",
            1.5);

        Assert.Equal(["n:1", "d:1"], request.Segments.Select(s => s.Key));
        Assert.Equal(["narrator-voice", "mira-voice"], request.Segments.Select(s => s.VoiceId));
        Assert.Equal([false, true], request.Segments.Select(s => s.IsDialogue));
        Assert.Equal("en", request.Language);
        Assert.Equal(1.5, request.Rate);
        Assert.Equal(Audio(), request.Voices);
    }

    [Fact]
    public void Build_LeavesOutASegmentWhoseVoiceThisMachineDoesNotHave()
    {
        // A cast assembled elsewhere. Left out rather than sent as something the
        // engine will refuse, so the gap is visible to the caller.
        var request = NarrationRender.Build(
            [Segment("d:1", "You are late,", "mira")],
            Cast(),
            new Dictionary<string, byte[]>(StringComparer.Ordinal),
            VoiceEngineFeatures.EmotionVector,
            "en");

        Assert.Empty(request.Segments);
    }

    [Fact]
    public void Build_LeavesOutASegmentWithNothingCastAndOneWithNoWords()
    {
        var nothingCast = new VoiceCastSheet();

        Assert.Empty(NarrationRender
            .Build([Segment("d:1", "A line.", "mira")], nothingCast, Audio(),
                VoiceEngineFeatures.EmotionVector, "en").Segments);
        Assert.Empty(NarrationRender
            .Build([Segment("n:1", "   ", null)], Cast(), Audio(),
                VoiceEngineFeatures.EmotionVector, "en").Segments);
    }

    [Fact]
    public void Direct_AnEngineThatTakesNumbersGetsThem()
    {
        var direction = NarrationRender.Direct(
            new VoiceDirection("angry", EmotionDirector.Vector("angry", null), DirectionSource.Verb, "snapped"),
            VoiceEngineFeatures.EmotionVector);

        Assert.Equal("angry", direction.Key);
        Assert.NotEmpty(direction.Vector);
        Assert.Equal(string.Empty, direction.Instruction);
        Assert.Equal(nameof(DirectionSource.Verb), direction.Source);
    }

    [Fact]
    public void Direct_AnEngineThatTakesWordsGetsTheWritersOwnVerb()
    {
        // "snapped", "whispered" and "hissed" all map to one key and are three
        // different performances, so the verb is worth more than the key alone.
        var direction = NarrationRender.Direct(
            new VoiceDirection("angry", EmotionDirector.Vector("angry", null), DirectionSource.Verb, "snapped"),
            VoiceEngineFeatures.EmotionInstruction);

        Assert.Equal("Read this angry, as though snapped.", direction.Instruction);
        Assert.Empty(direction.Vector);
    }

    [Fact]
    public void Direct_WithNoVerbTheInstructionNamesTheEmotionAlone()
    {
        var direction = NarrationRender.Direct(
            new VoiceDirection("tense", EmotionDirector.Vector("tense", null), DirectionSource.Scene),
            VoiceEngineFeatures.EmotionInstruction);

        Assert.Equal("Read this tense.", direction.Instruction);
    }

    [Fact]
    public void Direct_AnEngineThatTakesBothGetsBoth()
    {
        var direction = NarrationRender.Direct(
            new VoiceDirection("angry", EmotionDirector.Vector("angry", null), DirectionSource.Writer, "snapped"),
            VoiceEngineFeatures.EmotionVector | VoiceEngineFeatures.EmotionInstruction);

        Assert.NotEmpty(direction.Vector);
        Assert.NotEmpty(direction.Instruction);
    }

    [Fact]
    public void Direct_AnEngineThatReadsTheScriptItselfIsToldNothing()
    {
        // It has already read the line. Telling it what to feel would override
        // what it heard in the words.
        var direction = NarrationRender.Direct(
            new VoiceDirection("angry", EmotionDirector.Vector("angry", null), DirectionSource.Verb, "snapped"),
            VoiceEngineFeatures.EmotionInferred | VoiceEngineFeatures.EmotionVector);

        Assert.Empty(direction.Vector);
        Assert.Equal(string.Empty, direction.Instruction);
        // The key still travels, so an engine that logs it can say what the host
        // thought without being steered by it.
        Assert.Equal("angry", direction.Key);
    }

    [Fact]
    public void Direct_AnEngineThatCannotBeDirectedReadsFlat()
    {
        var direction = NarrationRender.Direct(
            new VoiceDirection("angry", EmotionDirector.Vector("angry", null), DirectionSource.Verb, "snapped"),
            VoiceEngineFeatures.RunsOnCpu);

        Assert.Empty(direction.Vector);
        Assert.Equal(string.Empty, direction.Instruction);
    }

    [Fact]
    public void Direct_ADirectionWithNoKeyIsReadPlainlyRatherThanAsNothing()
    {
        var direction = NarrationRender.Direct(
            new VoiceDirection("  ", new Dictionary<string, double>(), DirectionSource.None),
            VoiceEngineFeatures.EmotionInstruction);

        Assert.Equal("Read this neutral.", direction.Instruction);
    }

    [Fact]
    public void VoicesNeeded_NamesEachVoiceOnce()
    {
        var needed = NarrationRender.VoicesNeeded(
            [
                Segment("n:1", "She waited.", null),
                Segment("d:1", "A line.", "mira"),
                Segment("d:2", "Another.", "mira"),
                Segment("d:3", "Nobody's.", "uncast")
            ],
            Cast());

        // Every voice the cast names, each once. Not only the ones this window
        // mentions: once a character can sound different in chapter twenty,
        // which voices a window needs depends on where the window is, and a
        // caller that worked it out from the speakers alone would read the
        // wrong ones and leave those lines unspoken.
        Assert.Equal(["mira-voice", "narrator-voice"], needed);
    }

    [Fact]
    public void VoicesNeeded_IncludesAVoiceOnlyAnOverrideNames()
    {
        var sheet = Cast();
        sheet.Overrides.Add(new VoiceOverride
        {
            CharacterId = "mira",
            Chapter = "ch-20",
            VoiceId = "mira-at-sixty"
        });

        Assert.Contains("mira-at-sixty", NarrationRender.VoicesNeeded([], sheet));
    }

    [Fact]
    public void VoicesNeeded_NothingCastNeedsNothing()
        => Assert.Empty(NarrationRender.VoicesNeeded(
            [Segment("d:1", "A line.", "mira")], new VoiceCastSheet()));

    [Fact]
    public void NarratorBrief_IsAnAcousticVoiceDesignInstruction()
    {
        var book = new BookData
        {
            NarrativePerson = "third limited",
            Tense = "past",
            Premise = new StoryPremise { Logline = "A harbourmaster hides a wreck." }
        };

        var brief = NarrationRender.NarratorBrief(book);

        Assert.Contains("audiobook narrator", brief);
        Assert.Contains("mid-range pitch", brief);
        Assert.Contains("natural timbre", brief);
        Assert.DoesNotContain("third limited", brief);
        Assert.DoesNotContain("harbourmaster", brief);
    }

    [Fact]
    public void NarratorBrief_NeverIncludesPlotOrMood()
    {
        // "Tense" is one of the sixteen emotion keys, so filtering the finished
        // sentence deleted the label and left the value dangling after a colon:
        // "Narration: third limited. : past." The test that should have caught
        // it asserted only that "past" was somewhere in the string.
        var lexicon = SceneAnalysisLexicon.For("en");
        var book = new BookData
        {
            NarrativePerson = "third limited",
            Tense = "past",
            Premise = new StoryPremise { Logline = "A furious harbourmaster loses her boat." }
        };

        var brief = NarrationRender.NarratorBrief(book, lexicon);

        Assert.Contains("Neutral baseline", brief);
        Assert.DoesNotContain("furious", brief, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("harbourmaster", brief, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NarratorBrief_HasANeutralDefaultForAnyBook()
    {
        Assert.NotEmpty(NarrationRender.NarratorBrief(new BookData()));
        Assert.Equal(string.Empty, NarrationRender.NarratorBrief(null));
        Assert.DoesNotContain(
            "present",
            NarrationRender.NarratorBrief(new BookData { Tense = " present " }),
            StringComparison.OrdinalIgnoreCase);
    }
}
