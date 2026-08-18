using Novalist.Core.Utilities;
using Xunit;

namespace Novalist.Core.Tests.Utilities;

/// <summary>
/// Covers the only audio handling Novalist does itself: taking a speech
/// engine's clips apart and laying them end to end as a chapter.
///
/// The failures worth catching here are the quiet ones. A clip parsed with the
/// wrong sample rate does not throw - it plays at the wrong pitch. Silence
/// written as zeroes into an 8-bit chapter does not throw either; it clicks.
/// </summary>
public class WaveAudioTests
{
    private static byte[] Wav(int rate = 24000, int channels = 1, int bits = 16, int frames = 240)
        => WaveAudio.Write(new WaveFormat(rate, channels, bits), new byte[frames * channels * (bits / 8)]);

    [Fact]
    public void WhatIsWritten_ReadsBackAsWhatWentIn()
    {
        var samples = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var read = WaveAudio.Read(WaveAudio.Write(new WaveFormat(22050, 2, 16), samples));

        Assert.NotNull(read);
        Assert.Equal(new WaveFormat(22050, 2, 16), read!.Format);
        Assert.Equal(samples, read.Samples);
    }

    [Fact]
    public void ADurationIsCountedFromTheSamples_NotClaimedByTheHeader()
    {
        // One second of mono 16-bit at 24 kHz is 48,000 bytes.
        var read = WaveAudio.Read(Wav(frames: 24000));

        Assert.NotNull(read);
        Assert.Equal(1000, read!.DurationMs, 3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new byte[0])]
    public void NothingIsNotAClip(byte[]? bytes) => Assert.Null(WaveAudio.Read(bytes));

    [Fact]
    public void SomethingThatIsNotRiff_IsRefused()
        => Assert.Null(WaveAudio.Read(new byte[64]));

    [Fact]
    public void RiffThatIsNotWave_IsRefused()
    {
        var wav = Wav();
        wav[8] = (byte)'A';

        Assert.Null(WaveAudio.Read(wav));
    }

    [Fact]
    public void AClipWithNoDataChunk_IsRefused()
    {
        // Header and format, and then it stops - which is what a sidecar killed
        // between opening the file and writing the audio leaves behind.
        var wav = Wav(frames: 0);
        Array.Resize(ref wav, 36);

        Assert.Null(WaveAudio.Read(wav));
    }

    [Fact]
    public void ADataChunkBeforeTheFormat_IsRefused()
    {
        // Not a shape any encoder writes, and reading it would mean guessing the
        // sample rate - which is the one thing that must never be guessed.
        var wav = Wav();
        wav[12] = (byte)'d';
        wav[13] = (byte)'a';
        wav[14] = (byte)'t';
        wav[15] = (byte)'a';

        Assert.Null(WaveAudio.Read(wav));
    }

    [Fact]
    public void FloatSamples_AreRefusedRatherThanReadAsIntegers()
    {
        // The bytes of a float clip parse perfectly as PCM and play as noise.
        var wav = Wav();
        wav[20] = 3;

        Assert.Null(WaveAudio.Read(wav));
    }

    [Theory]
    [InlineData(20, 0)]     // no channels
    [InlineData(24, 0)]     // no sample rate
    [InlineData(34, 0)]     // no bit depth
    [InlineData(34, 12)]    // a bit depth that is not whole bytes
    public void AFormatThatCannotBeTrue_IsRefused(int offset, int value)
    {
        var wav = Wav();
        wav[offset] = (byte)value;
        wav[offset + 1] = 0;

        Assert.Null(WaveAudio.Read(wav));
    }

    [Fact]
    public void AChunkClaimingANegativeLength_IsRefused()
    {
        var wav = Wav();
        wav[19] = 0xFF;

        Assert.Null(WaveAudio.Read(wav));
    }

    [Fact]
    public void AClipCutOffMidWrite_KeepsWhatArrived()
    {
        // The header says one second; the file holds half. What arrived is
        // speech and is worth keeping.
        var wav = Wav(frames: 24000);
        Array.Resize(ref wav, 44 + 24000);

        var read = WaveAudio.Read(wav);

        Assert.NotNull(read);
        Assert.Equal(24000, read!.Samples.Length);
    }

    [Fact]
    public void HalfASampleFrame_IsNotASampleFrame()
    {
        var wav = Wav(channels: 2, frames: 10);
        Array.Resize(ref wav, wav.Length - 3);

        var read = WaveAudio.Read(wav);

        Assert.NotNull(read);
        Assert.Equal(0, read!.Samples.Length % 4);
    }

    [Fact]
    public void ChunksBeforeTheAudio_AreSteppedOver()
    {
        // Engines write LIST/INFO chunks; an odd-length one is followed by a pad
        // byte, and a reader that forgets the pad lands one byte into the next
        // chunk id and finds nothing at all.
        var inner = new byte[] { 1, 2, 3, 4 };
        var full = WaveAudio.Write(new WaveFormat(24000, 1, 16), inner);
        var withList = new List<byte>();
        withList.AddRange(full[..36]);
        withList.AddRange("LIST"u8.ToArray());
        withList.AddRange(BitConverter.GetBytes(3));
        withList.AddRange([9, 9, 9, 0]);
        withList.AddRange(full[36..]);

        var read = WaveAudio.Read([.. withList]);

        Assert.NotNull(read);
        Assert.Equal(inner, read!.Samples);
    }

    // ─── the joins ──────────────────────────────────────────────────

    private static byte[] Tone(int frames, short level = 10000)
    {
        var samples = new byte[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            samples[i * 2] = (byte)(level & 0xFF);
            samples[i * 2 + 1] = (byte)(level >> 8);
        }
        return samples;
    }

    private static short At(byte[] samples, int frame)
        => BitConverter.ToInt16(samples, frame * 2);

    [Fact]
    public void AClipsEdges_AreRampedSoAJoinDoesNotClick()
    {
        var format = new WaveFormat(1000, 1, 16);

        var faded = WaveAudio.Fade(Tone(1000), format, 100);

        // Up from nothing at the start, down to nothing at the end, and the
        // middle untouched.
        Assert.True(At(faded, 0) < 2000);
        Assert.Equal(10000, At(faded, 500));
        Assert.True(At(faded, 999) < 2000);
    }

    [Fact]
    public void TheRampIsMonotonic_RatherThanAStepOfItsOwn()
    {
        var faded = WaveAudio.Fade(Tone(1000), new WaveFormat(1000, 1, 16), 100);

        for (var i = 1; i < 100; i++)
            Assert.True(At(faded, i) >= At(faded, i - 1));
    }

    [Fact]
    public void ARampLongerThanTheClip_DoesNotFadeOutTheWholeLine()
    {
        var faded = WaveAudio.Fade(Tone(100), new WaveFormat(1000, 1, 16), 500);

        Assert.Equal(10000, At(faded, 50));
    }

    [Fact]
    public void FadingDoesNotChangeTheClipItWasGiven()
    {
        // The original is what the cache holds; a fade applied in place would
        // put a faded clip back in it.
        var original = Tone(1000);

        WaveAudio.Fade(original, new WaveFormat(1000, 1, 16), 100);

        Assert.Equal(10000, At(original, 0));
    }

    [Fact]
    public void EveryChannel_IsRamped()
    {
        var faded = WaveAudio.Fade(Tone(2000), new WaveFormat(1000, 2, 16), 100);

        Assert.True(At(faded, 0) < 2000);
        Assert.True(At(faded, 1) < 2000);
    }

    [Theory]
    [InlineData(24, 1, 15)]   // a width this does not know how to scale
    [InlineData(16, 0, 15)]   // no channels
    [InlineData(16, 1, 0)]    // no ramp asked for
    public void AClipItCannotRamp_IsHandedBackUntouched(int bits, int channels, int ms)
    {
        var original = Tone(1000);

        Assert.Same(original, WaveAudio.Fade(original, new WaveFormat(1000, channels, bits), ms));
    }

    [Fact]
    public void AClipTooShortToRamp_IsHandedBackUntouched()
    {
        var original = Tone(1);

        Assert.Same(original, WaveAudio.Fade(original, new WaveFormat(1000, 1, 16), 15));
    }

    [Fact]
    public void SilenceIsAsLongAsItWasAskedFor()
    {
        var silence = WaveAudio.Silence(new WaveFormat(24000, 1, 16), 500);

        Assert.Equal(24000, silence.Length);
    }

    [Fact]
    public void EightBitSilence_IsTheMiddleOfTheRangeRatherThanZero()
    {
        // Eight-bit samples are unsigned. Zeroes are full-scale negative, so a
        // pause written as zeroes clicks at both ends.
        var silence = WaveAudio.Silence(new WaveFormat(8000, 1, 8), 10);

        Assert.All(silence, b => Assert.Equal(128, b));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-40)]
    public void NoPauseIsNoBytes(int milliseconds)
        => Assert.Empty(WaveAudio.Silence(new WaveFormat(24000, 1, 16), milliseconds));

    [Fact]
    public void AFormatWithNoBlockSize_ProducesNoSilenceRatherThanDividingByZero()
        => Assert.Empty(WaveAudio.Silence(new WaveFormat(24000, 0, 16), 500));

    [Fact]
    public void ADurationWithoutARate_IsZeroRatherThanInfinite()
        => Assert.Equal(0, new WaveFormat(0, 1, 16).DurationMs(4800));
}
