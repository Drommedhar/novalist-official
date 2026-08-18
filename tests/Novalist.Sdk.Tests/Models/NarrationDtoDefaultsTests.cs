using Novalist.Sdk.Models.Narration;
using Xunit;

namespace Novalist.Sdk.Tests.Models;

/// <summary>
/// Exercises every voice-engine DTO's field initializers, and asserts the
/// defaults an engine author will rely on: nothing here may default to null,
/// because an engine that has to null-check the host's own request is a seam
/// that will be got wrong.
/// </summary>
public class NarrationDtoDefaultsTests
{
    [Fact]
    public void VoiceBrief_Defaults()
    {
        var brief = new VoiceBrief();

        Assert.Equal(string.Empty, brief.VoiceId);
        Assert.Equal(string.Empty, brief.DisplayName);
        Assert.Equal(string.Empty, brief.Description);
        Assert.Empty(brief.SampleLines);
        Assert.Equal("en", brief.Language);
        Assert.Null(brief.Seed);
    }

    [Fact]
    public void VoiceDesignResult_Defaults()
    {
        var result = new VoiceDesignResult();

        Assert.Equal(string.Empty, result.VoiceId);
        Assert.Empty(result.ReferenceAudio);
        Assert.Equal("wav", result.AudioFormat);
        Assert.Equal(0, result.SampleRate);
        Assert.Equal(string.Empty, result.ResolvedDescription);
    }

    [Fact]
    public void VoiceDirection_DefaultsToNeutralAndUndirected()
    {
        var direction = new VoiceDirection();

        Assert.Equal("neutral", direction.Key);
        Assert.Empty(direction.Vector);
        Assert.Equal(string.Empty, direction.Instruction);
        Assert.Equal("None", direction.Source);
    }

    [Fact]
    public void NarrationSegment_Defaults()
    {
        var segment = new NarrationSegment();

        Assert.Equal(string.Empty, segment.Key);
        Assert.Equal(string.Empty, segment.Text);
        Assert.Equal(string.Empty, segment.VoiceId);
        Assert.False(segment.IsDialogue);
        // Never null: an engine reading the direction off every segment should
        // not have to check first.
        Assert.Equal("neutral", segment.Direction.Key);
    }

    [Fact]
    public void NarrationRequest_Defaults()
    {
        var request = new NarrationRequest();

        Assert.Empty(request.Segments);
        Assert.Empty(request.Voices);
        Assert.Equal("en", request.Language);
        Assert.Equal(1.0, request.Rate);
    }

    [Fact]
    public void NarrationClip_Defaults()
    {
        var clip = new NarrationClip();

        Assert.Equal(string.Empty, clip.Key);
        Assert.Empty(clip.Audio);
        Assert.Equal("wav", clip.AudioFormat);
        Assert.Equal(0, clip.SampleRate);
        Assert.Equal(0, clip.DurationMs);
        Assert.Null(clip.Error);
    }

    [Fact]
    public void VoiceEngineStatus_DefaultsToNotReadyAndNotBroken()
    {
        var status = new VoiceEngineStatus();

        Assert.False(status.IsReady);
        Assert.False(status.IsPreparing);
        Assert.Null(status.Error);
        Assert.Equal(string.Empty, status.Detail);
        Assert.Null(status.DownloadBytes);
    }

    [Fact]
    public void VoiceEnginePrepare_Defaults()
    {
        var prepare = new VoiceEnginePrepare();

        Assert.Equal(string.Empty, prepare.Step);
        Assert.Null(prepare.Fraction);
        Assert.Equal(string.Empty, prepare.Detail);
    }

    [Fact]
    public void VoiceEngineFeatures_CombineAsFlags()
    {
        var features = VoiceEngineFeatures.DesignFromDescription | VoiceEngineFeatures.EmotionVector;

        Assert.True(features.HasFlag(VoiceEngineFeatures.DesignFromDescription));
        Assert.True(features.HasFlag(VoiceEngineFeatures.EmotionVector));
        Assert.False(features.HasFlag(VoiceEngineFeatures.EmotionInferred));
        Assert.Equal(VoiceEngineFeatures.None, VoiceEngineFeatures.None);
    }
}
