using System.Buffers.Binary;
using System.Text;

namespace Novalist.Core.Utilities;

/// <summary>The shape of a piece of audio, without the audio.</summary>
/// <param name="SampleRate">Samples per second per channel.</param>
/// <param name="Channels">1 for mono, 2 for stereo.</param>
/// <param name="BitsPerSample">8, 16, 24 or 32.</param>
public readonly record struct WaveFormat(int SampleRate, int Channels, int BitsPerSample)
{
    /// <summary>Bytes one sample of every channel takes.</summary>
    public int BlockAlign => Channels * (BitsPerSample / 8);

    /// <summary>How long a given number of sample bytes lasts, in milliseconds.</summary>
    public double DurationMs(long bytes)
        => BlockAlign == 0 || SampleRate == 0 ? 0 : bytes * 1000d / (BlockAlign * (long)SampleRate);
}

/// <summary>One clip, unwrapped: its format and its samples.</summary>
public sealed record WaveClip(WaveFormat Format, byte[] Samples)
{
    public double DurationMs => Format.DurationMs(Samples.LongLength);
}

/// <summary>
/// Reads, joins and writes WAV.
///
/// A speech engine hands back one clip per line. A chapter is one file. Between
/// those two facts sits the whole of this class: pull the samples out of each
/// clip, check they are the same shape, lay them end to end with a breath in
/// between, and wrap the result in a new header.
///
/// Deliberately not an audio library. It handles uncompressed PCM at whatever
/// rate and width the engine produced, and refuses anything else rather than
/// guessing - a chapter silently assembled from clips of two different sample
/// rates plays as a chapter that speeds up halfway through, which is worse than
/// an error naming the clip that did not fit.
/// </summary>
public static class WaveAudio
{
    /// <summary>Bytes of a RIFF header before the first chunk.</summary>
    private const int RiffHeader = 12;

    /// <summary>Smallest chunk header - four bytes of id, four of length.</summary>
    private const int ChunkHeader = 8;

    /// <summary>Uncompressed PCM, the only encoding this reads.</summary>
    private const int FormatPcm = 1;

    /// <summary>
    /// IEEE float samples. Read so a clip from an engine that emits float can be
    /// reported honestly as unusable rather than parsed as PCM and played as noise.
    /// </summary>
    private const int FormatFloat = 3;

    /// <summary>
    /// Pulls the samples out of a WAV file.
    /// </summary>
    /// <returns>The clip, or null when the bytes are not a PCM WAV this can join.</returns>
    public static WaveClip? Read(byte[]? wav)
    {
        if (wav == null || wav.Length < RiffHeader + ChunkHeader)
            return null;
        if (Encoding.ASCII.GetString(wav, 0, 4) != "RIFF")
            return null;
        if (Encoding.ASCII.GetString(wav, 8, 4) != "WAVE")
            return null;

        WaveFormat? format = null;
        var at = RiffHeader;
        while (at + ChunkHeader <= wav.Length)
        {
            var id = Encoding.ASCII.GetString(wav, at, 4);
            var length = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(at + 4, 4));
            if (length < 0)
                return null;
            var body = at + ChunkHeader;
            // A truncated final chunk is taken for what is there. Engines that
            // write the header before the audio and are killed part way leave
            // exactly this, and the part that arrived is still speech.
            // Never negative: the loop only entered with eight bytes left, so
            // `body` is at most the end of the buffer.
            var available = Math.Min(length, wav.Length - body);

            if (id == "fmt " && available >= 16)
            {
                var encoding = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(body, 2));
                if (encoding != FormatPcm && encoding != FormatFloat)
                    return null;
                var channels = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(body + 2, 2));
                var rate = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(body + 4, 4));
                var bits = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(body + 14, 2));
                if (encoding == FormatFloat || channels <= 0 || rate <= 0 || bits <= 0 || bits % 8 != 0)
                    return null;
                format = new WaveFormat(rate, channels, bits);
            }
            else if (id == "data")
            {
                if (format is not { } found)
                    return null;
                var samples = new byte[available];
                Array.Copy(wav, body, samples, 0, available);
                // Half a sample frame is not a sample frame.
                var whole = samples.Length - samples.Length % Math.Max(1, found.BlockAlign);
                if (whole != samples.Length)
                    Array.Resize(ref samples, whole);
                return new WaveClip(found, samples);
            }

            // Chunks are word-aligned; an odd length is followed by a pad byte.
            at = body + available + (available % 2);
        }

        return null;
    }

    /// <summary>Wraps samples in a WAV header.</summary>
    public static byte[] Write(WaveFormat format, byte[] samples)
    {
        var wav = new byte[44 + samples.Length];
        var span = wav.AsSpan();

        Encoding.ASCII.GetBytes("RIFF").CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + samples.Length);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(span[8..]);

        Encoding.ASCII.GetBytes("fmt ").CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], FormatPcm);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], (short)format.Channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], format.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], format.SampleRate * format.BlockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], (short)format.BlockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], (short)format.BitsPerSample);

        Encoding.ASCII.GetBytes("data").CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], samples.Length);
        samples.CopyTo(span[44..]);
        return wav;
    }

    /// <summary>
    /// Fades a clip in and out at its own edges, in place.
    ///
    /// An engine that renders each line in isolation starts and ends on
    /// whatever sample it happened to stop at, and a chapter assembled from
    /// forty thousand of those clicks at every join - the discontinuity is a
    /// step in the waveform, and a step is a click. A few milliseconds of ramp
    /// at each end removes it without being audible as a fade.
    ///
    /// Only for engines that do not carry prosody across a request. One that
    /// does has already made the joins continuous, and fading them would be
    /// undoing the thing that makes it worth having.
    /// </summary>
    public static byte[] Fade(byte[] samples, WaveFormat format, int milliseconds)
    {
        // Only 16-bit, which is what every engine here returns. Another width
        // is left alone rather than corrupted by being read as this one.
        if (format.BitsPerSample != 16 || format.Channels <= 0 || milliseconds <= 0)
            return samples;

        var frames = samples.Length / format.BlockAlign;
        var ramp = (int)Math.Round(format.SampleRate * (milliseconds / 1000d));
        // Never more than half the clip: a ramp longer than the audio would
        // fade the line out before it had finished being said.
        ramp = Math.Min(ramp, frames / 2);
        if (ramp <= 0)
            return samples;

        var faded = (byte[])samples.Clone();
        for (var i = 0; i < ramp; i++)
        {
            var gain = (i + 1) / (double)ramp;
            Scale(faded, format, i, gain);
            Scale(faded, format, frames - 1 - i, gain);
        }
        return faded;
    }

    private static void Scale(byte[] samples, WaveFormat format, int frame, double gain)
    {
        for (var channel = 0; channel < format.Channels; channel++)
        {
            var at = (frame * format.Channels + channel) * 2;
            var value = BinaryPrimitives.ReadInt16LittleEndian(samples.AsSpan(at, 2));
            BinaryPrimitives.WriteInt16LittleEndian(
                samples.AsSpan(at, 2), (short)Math.Round(value * gain));
        }
    }

    /// <summary>
    /// Silence of a given length in a given format.
    ///
    /// Zero is silence for every width this reads except 8-bit, where the
    /// samples are unsigned and the middle of the range is 128. Filling 8-bit
    /// silence with zeroes writes a full-scale offset - a click at both ends of
    /// every pause.
    /// </summary>
    public static byte[] Silence(WaveFormat format, int milliseconds)
    {
        if (milliseconds <= 0 || format.BlockAlign == 0)
            return [];
        var frames = (long)Math.Round(format.SampleRate * (milliseconds / 1000d));
        var bytes = new byte[frames * format.BlockAlign];
        if (format.BitsPerSample == 8)
            Array.Fill(bytes, (byte)128);
        return bytes;
    }
}
