using Novalist.Mobile.Services;
using Novalist.Core.Utilities;
using Xunit;

namespace Novalist.Core.Tests;

public sealed class PcmSpeechChunksTests
{
    [Fact]
    public void SilenceDoesNotProduceAClip()
    {
        var clips = new List<byte[]>();
        var chunks = new PcmSpeechChunks(clips.Add);
        for (var i = 0; i < 300; i++) chunks.Push(new short[1600]);
        chunks.Flush();
        Assert.Empty(clips);
        Assert.Equal(0, chunks.Level);
    }

    [Fact]
    public void StopRetainsTheLastPhraseInAWavClipOnce()
    {
        var clips = new List<byte[]>();
        var chunks = new PcmSpeechChunks(clips.Add);
        chunks.Push(Enumerable.Repeat((short)1000, 3200).ToArray());
        chunks.Flush();
        chunks.Flush();
        var clip = WaveAudio.Read(Assert.Single(clips));
        Assert.NotNull(clip);
        Assert.Equal(new WaveFormat(16000, 1, 16), clip.Format);
        Assert.Equal(6400, clip.Samples.Length);
        Assert.True(chunks.Level > 0);
    }

    [Fact]
    public void PausesSeparatePhrasesAndLongSpeechIsBounded()
    {
        var clips = new List<byte[]>();
        var chunks = new PcmSpeechChunks(clips.Add);
        for (var i = 0; i < 7; i++) chunks.Push(Enumerable.Repeat((short)1000, 1600).ToArray());
        for (var i = 0; i < 8; i++) chunks.Push(new short[1600]);
        Assert.Single(clips);
        for (var i = 0; i < 250; i++) chunks.Push(Enumerable.Repeat((short)1000, 1600).ToArray());
        Assert.Equal(2, clips.Count);
        Assert.Equal(25000, WaveAudio.Read(clips[1])!.DurationMs);
    }
}
