using Novalist.Core.Services;
using Novalist.Sdk.Models.Narration;
using Xunit;
// Both projects call a piece of a reading a NarrationSegment. The one that
// matters here is the engine's: one instruction to a speech model.
using NarrationSegment = Novalist.Sdk.Models.Narration.NarrationSegment;
using VoiceDirection = Novalist.Sdk.Models.Narration.VoiceDirection;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// What decides whether a line has to be spoken again.
///
/// The reading is kept between listens now, so this key is the whole of the
/// question "is the audio on disk still the right audio". Too loose and a
/// writer hears the line they just corrected in its old form; too tight and the
/// reuse that makes a second listen instant never happens at all.
/// </summary>
public class NarrationRecipeTests
{
    private static NarrationSegment Line(
        string text = "You are late,",
        string voiceId = "hers",
        double angry = 0.9,
        bool dialogue = true,
        byte[]? like = null,
        Dictionary<string, double>? vector = null)
        => new()
        {
            Key = "d:1",
            Text = text,
            VoiceId = voiceId,
            IsDialogue = dialogue,
            Direction = new VoiceDirection
            {
                Key = "angry",
                Vector = vector ?? new Dictionary<string, double> { ["angry"] = angry },
                Source = "Verb",
                ReferenceAudio = like
            }
        };

    private static string For(
        NarrationSegment segment,
        string engine = "eng",
        string language = "en",
        double rate = 1.0,
        byte[]? voice = null,
        string referenceText = "Reference words.")
        => NarrationRecipe.For(
            segment, engine, language, rate, voice ?? [1, 2, 3], referenceText);

    [Fact]
    public void TheSameLineAskedForTwiceIsTheSameKey()
        => Assert.Equal(For(Line()), For(Line()));

    [Fact]
    public void ChangingTheWordsChangesIt()
        => Assert.NotEqual(For(Line()), For(Line(text: "You are early,")));

    [Fact]
    public void ChangingTheDirectionChangesIt()
        => Assert.NotEqual(For(Line()), For(Line(angry: 0.3)));

    [Fact]
    public void ChangingWhoSpeaksItChangesIt()
        => Assert.NotEqual(For(Line()), For(Line(voiceId: "his")));

    [Fact]
    public void RedesigningTheVoiceChangesIt()
    {
        // The id stays - that is what makes it the same character - so the
        // voice goes in as its audio. Keyed on the id alone, every line would
        // have gone on being served in the voice just replaced.
        Assert.NotEqual(For(Line(), voice: [1, 2, 3]), For(Line(), voice: [4, 5, 6]));
    }

    [Fact]
    public void CorrectingTheReferenceTranscriptChangesIt()
        => Assert.NotEqual(
            For(Line(), referenceText: "These were the words."),
            For(Line(), referenceText: "These are the words."));

    [Fact]
    public void ChangingTheSpeedChangesIt()
        => Assert.NotEqual(For(Line()), For(Line(), rate: 1.5));

    [Fact]
    public void ChangingTheEngineChangesIt()
    {
        // Two engines given the same instructions do not produce the same
        // sound, so one engine's work is not the other one's answer.
        Assert.NotEqual(For(Line()), For(Line(), engine: "other"));
    }

    [Fact]
    public void ChangingTheLanguageChangesIt()
        => Assert.NotEqual(For(Line()), For(Line(), language: "de"));

    [Fact]
    public void PointingAtADifferentClipChangesIt()
        => Assert.NotEqual(For(Line(like: [1])), For(Line(like: [2])));

    [Fact]
    public void NarrationAndDialogueAreNotTheSameLine()
        => Assert.NotEqual(For(Line()), For(Line(dialogue: false)));

    [Fact]
    public void TheOrderTheSlidersWereMovedInDoesNotChangeIt()
    {
        // A dictionary has no order, and the same delivery must not hash two
        // ways depending on which dimension was set first.
        var one = Line(vector: new Dictionary<string, double> { ["angry"] = 0.9, ["sad"] = 0.2 });
        var two = Line(vector: new Dictionary<string, double> { ["sad"] = 0.2, ["angry"] = 0.9 });

        Assert.Equal(For(one), For(two));
    }

    [Fact]
    public void ItIsAFileNameAndNothingOfTheStory()
    {
        // Clip files are named for this, and a cache folder must not be
        // readable as somebody's manuscript.
        var said = For(Line(text: "Get out, she said, and meant it."));

        Assert.DoesNotContain(said, c => Path.GetInvalidFileNameChars().Contains(c));
        Assert.DoesNotContain("Get", said);
        Assert.Equal(32, said.Length);
    }

    [Fact]
    public void AVoiceThatCarriesNoAudioIsStillAKey()
    {
        // An engine that speaks without a reference clip.
        Assert.NotEmpty(NarrationRecipe.For(Line(), "eng", "en", 1.0, null));
        Assert.NotEqual(
            NarrationRecipe.For(Line(), "eng", "en", 1.0, null),
            NarrationRecipe.For(Line(), "eng", "en", 1.0, [1, 2, 3]));
    }
}
