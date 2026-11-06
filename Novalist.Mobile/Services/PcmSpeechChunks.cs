using System.Buffers.Binary;
using Novalist.Core.Utilities;

namespace Novalist.Mobile.Services;

public sealed class PcmSpeechChunks(Action<byte[]> emit)
{
    private const int Rate = 16000;
    private readonly Queue<short[]> _frames = new();
    private int _count;
    private int _voiced;
    private int _silence;
    public double Level { get; private set; }

    public void Push(short[] samples)
    {
        if (samples.Length == 0) return;
        var rms = Math.Sqrt(samples.Sum(value => (double)value * value) / samples.Length) / 32768;
        Level = Math.Clamp((20 * Math.Log10(Math.Max(rms, 0.000001)) + 60) / 60, 0, 1);
        _frames.Enqueue(samples);
        _count += samples.Length;
        if (rms > 0.008) { _voiced += samples.Length; _silence = 0; }
        else _silence += samples.Length;
        if (_voiced == 0 && _count > Rate * 0.3) _count -= _frames.Dequeue().Length;
        if (_voiced > 0 && ((_silence >= Rate * 0.8 && _count >= Rate * 1.5) || _count >= Rate * 25)) Flush();
    }

    public void Flush()
    {
        byte[]? samples = null;
        if (_voiced >= Rate * 0.12)
        {
            samples = new byte[_count * 2];
            var offset = 0;
            foreach (var frame in _frames)
                foreach (var value in frame)
                {
                    BinaryPrimitives.WriteInt16LittleEndian(samples.AsSpan(offset, 2), value);
                    offset += 2;
                }
        }
        _frames.Clear();
        _count = _voiced = _silence = 0;
        if (samples != null) emit(WaveAudio.Write(new WaveFormat(Rate, 1, 16), samples));
    }
}
