using System.Runtime.CompilerServices;
using System.Text;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models.Narration;

namespace Novalist.Sdk.Example;

/// <summary>
/// A voice engine that speaks no words.
///
/// It designs a "voice" by hashing the brief into a short tone, and renders a
/// segment as a tone of a length proportional to its text - deterministic,
/// instant, and with no model, no download and no GPU. That is the point: the
/// whole path from a Codex entry through a designed voice to a rendered clip can
/// be exercised end to end on any machine, including a CI runner, exactly as the
/// example's article generator stands in for a language model.
///
/// It also demonstrates the shape a real engine has to have: the two stages kept
/// apart, the emotion taken as a parameter rather than spliced into the text, and
/// the whole run handed over in one call.
/// </summary>
public sealed class ExampleVoiceEngine : IVoiceEngineContributor
{
    /// <summary>Sample rate of the tones this produces. A real number, so a host
    /// that writes the clip to disk writes something playable.</summary>
    public const int Rate = 16000;

    /// <summary>A brief containing this word fails to design, so the error path
    /// can be exercised without breaking anything else - the same trick the
    /// example's article generator uses with "GenFail".</summary>
    public const string FailWord = "designfail";

    private readonly Dictionary<string, byte[]> _voices = new(StringComparer.Ordinal);
    private bool _prepared;

    public string EngineId => "com.example.toolkit.voice";

    public string EngineName => "Writing Toolkit Example Voice";

    /// <summary>Everything except cloning: this engine has no recording to clone
    /// from, and says so rather than accepting a call it cannot honour.</summary>
    public VoiceEngineFeatures Features =>
        VoiceEngineFeatures.DesignFromDescription
        | VoiceEngineFeatures.EmotionVector
        | VoiceEngineFeatures.EmotionInstruction
        // Declared so the whole reference-clip path - the writer pointing at a
        // line and saying "like that" - has an engine to exercise it without a
        // model on the machine.
        | VoiceEngineFeatures.EmotionReference
        | VoiceEngineFeatures.Streaming
        | VoiceEngineFeatures.RunsOnCpu;

    public Task<VoiceEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new VoiceEngineStatus
        {
            IsReady = _prepared,
            Detail = _prepared ? "tone generator, ready" : "tone generator, not prepared",
            DownloadBytes = _prepared ? null : 0
        });

    /// <summary>Instant, because there is nothing to fetch - but it still reports
    /// progress, so a host wiring up a progress bar has something to wire it to.</summary>
    public Task PrepareAsync(
        IProgress<VoiceEnginePrepare>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new VoiceEnginePrepare { Step = "loading-model", Fraction = 0.5 });
        _prepared = true;
        progress?.Report(new VoiceEnginePrepare { Step = "ready", Fraction = 1 });
        return Task.CompletedTask;
    }

    public Task<VoiceDesignResult> DesignVoiceAsync(
        VoiceBrief brief, CancellationToken cancellationToken = default)
    {
        if (brief.Description.Contains(FailWord, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("the brief asked this to fail");

        // The "voice" is a pitch, derived from the brief so the same description
        // gives the same voice - which a real designer does not guarantee, and is
        // exactly why the host stores the audio rather than the prompt.
        var pitch = 90 + Math.Abs(Hash(brief.Description + brief.DisplayName)) % 220;
        var audio = Tone(pitch, milliseconds: 400);
        _voices[brief.VoiceId] = audio;

        return Task.FromResult(new VoiceDesignResult
        {
            VoiceId = brief.VoiceId,
            ReferenceAudio = audio,
            AudioFormat = "wav",
            SampleRate = Rate,
            ResolvedDescription = brief.Description
        });
    }

    public async IAsyncEnumerable<NarrationClip> RenderAsync(
        NarrationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var segment in request.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A segment naming a voice the request did not carry cannot be
            // spoken. Saying so per clip lets the host stop at the last good one
            // rather than read on in the wrong voice.
            if (!request.Voices.TryGetValue(segment.VoiceId, out var reference))
            {
                yield return new NarrationClip { Key = segment.Key, Error = "unknown voice" };
                continue;
            }

            // The emotion moves the pitch. Nothing musical about it - it is here
            // so a test can prove the direction reached the engine at all.
            //
            // Derived from which dimensions carry the weight, not from how much
            // weight there is: "angry 0.9" and "sad 0.9" are different
            // directions, and an engine that heard only their magnitude would
            // read grief and fury identically - which is exactly the failure the
            // vector exists to prevent.
            var pitch = 90 + Math.Abs(Hash(Convert.ToBase64String(reference))) % 220;
            var shift = segment.Direction.Vector
                .Sum(part => (Math.Abs(Hash(part.Key)) % 40) * part.Value);
            var length = Math.Clamp(segment.Text.Length * 8, 80, 4000);

            yield return new NarrationClip
            {
                Key = segment.Key,
                Audio = Tone(pitch + shift, length / Math.Max(0.5, request.Rate)),
                AudioFormat = "wav",
                SampleRate = Rate,
                DurationMs = length / Math.Max(0.5, request.Rate)
            };

            await Task.Yield();
        }
    }

    public Task ForgetVoiceAsync(string voiceId, CancellationToken cancellationToken = default)
    {
        _voices.Remove(voiceId);
        return Task.CompletedTask;
    }

    /// <summary>A sine tone as a 16-bit mono WAV.</summary>
    private static byte[] Tone(double hertz, double milliseconds)
    {
        var samples = (int)(Rate * milliseconds / 1000);
        var body = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            var value = (short)(Math.Sin(2 * Math.PI * hertz * i / Rate) * 8000);
            body[i * 2] = (byte)(value & 0xFF);
            body[(i * 2) + 1] = (byte)((value >> 8) & 0xFF);
        }
        return Wav(body);
    }

    /// <summary>The 44-byte canonical WAV header, so what comes out of here is a
    /// file an operating system will actually play.</summary>
    private static byte[] Wav(byte[] body)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + body.Length);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(Rate);
        writer.Write(Rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(body.Length);
        writer.Write(body);
        writer.Flush();
        return stream.ToArray();
    }

    private static int Hash(string value)
    {
        var hash = 17;
        foreach (var ch in value)
            hash = (hash * 31) + ch;
        return hash;
    }
}
