using Android.Media;

namespace Novalist.Mobile.Services;

public sealed record MicrophoneSnapshot(string[] Clips, bool Ended, double Level);

public sealed class SystemMicrophone
{
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly object _bufferGate = new();
    private readonly Queue<string> _clips = new();
    private CancellationTokenSource? _captureCancellation;
    private Task? _capture;
    private Exception? _failure;
    private double _level;

    public async Task StartAsync(CancellationToken token)
    {
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(token);
        startup.CancelAfter(TimeSpan.FromSeconds(25));
        await _operations.WaitAsync(startup.Token);
        try
        {
            if (_capture != null) throw new InvalidOperationException("The microphone is already recording.");
            var permission = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.Microphone>)
                .WaitAsync(startup.Token);
            if (permission != PermissionStatus.Granted) throw new UnauthorizedAccessException("Microphone access denied.");
            startup.Token.ThrowIfCancellationRequested();
            var minimum = AudioRecord.GetMinBufferSize(16000, ChannelIn.Mono, Encoding.Pcm16bit);
            if (minimum <= 0) throw new PlatformNotSupportedException("16 kHz microphone capture is unavailable.");
            var recorder = new AudioRecord(AudioSource.VoiceRecognition, 16000, ChannelIn.Mono, Encoding.Pcm16bit, Math.Max(minimum, 6400));
            if (recorder.State != State.Initialized)
            {
                recorder.Dispose();
                throw new InvalidOperationException("The microphone could not be initialized.");
            }
            try { recorder.StartRecording(); }
            catch { recorder.Dispose(); throw; }
            lock (_bufferGate) { _clips.Clear(); _failure = null; _level = 0; }
            _captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            _capture = Task.Run(() => CaptureAsync(recorder, _captureCancellation.Token));
        }
        finally { _operations.Release(); }
    }

    private async Task CaptureAsync(AudioRecord recorder, CancellationToken token)
    {
        var chunks = new PcmSpeechChunks(clip =>
        {
            if (_clips.Count >= 16) throw new IOException("Microphone clip queue is full. Stop recording and retry.");
            _clips.Enqueue(Convert.ToBase64String(clip));
        });
        try
        {
            var samples = new short[1600];
            while (!token.IsCancellationRequested)
            {
                var count = recorder.Read(samples, 0, samples.Length, (int)AudioRecordReadOptions.NonBlocking);
                if (count < 0) throw new IOException($"Microphone capture failed ({count}).");
                if (count == 0) { await Task.Delay(20, token); continue; }
                lock (_bufferGate)
                {
                    chunks.Push(samples[..count]);
                    _level = chunks.Level;
                    if (_clips.Count >= 15) throw new IOException("Microphone reader fell behind. Recording stopped with pending clips retained.");
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { lock (_bufferGate) _failure = error; }
        finally
        {
            try { recorder.Stop(); }
            catch (Java.Lang.IllegalStateException error) { lock (_bufferGate) _failure ??= error; }
            finally { recorder.Dispose(); }
            lock (_bufferGate)
            {
                try { chunks.Flush(); }
                catch (IOException error) { _failure ??= error; }
                _level = 0;
            }
        }
    }

    public Task<MicrophoneSnapshot> ReadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_bufferGate)
        {
            if (_failure != null) throw new IOException("Microphone capture stopped unexpectedly.", _failure);
            return Task.FromResult(Drain(_capture?.IsCompleted != false));
        }
    }

    public async Task<MicrophoneSnapshot> StopAsync()
    {
        await _operations.WaitAsync();
        try
        {
            _captureCancellation?.Cancel();
            if (_capture != null) await _capture;
            _capture = null;
            _captureCancellation?.Dispose();
            _captureCancellation = null;
            lock (_bufferGate) return Drain(true);
        }
        finally { _operations.Release(); }
    }

    private MicrophoneSnapshot Drain(bool ended)
    {
        var clips = _clips.ToArray();
        _clips.Clear();
        return new(clips, ended, _level);
    }

    public async Task StopOnDisposeAsync() => await StopAsync();
}
