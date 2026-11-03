using Android.Speech.Tts;
using Novalist.Backend.Speech;
using NativeSpeech = Android.Speech.Tts.TextToSpeech;

namespace Novalist.Mobile.Services;

public sealed class AndroidReadAloud : IAsyncSystemVoices, IDisposable
{
    private readonly Initialization _initialization = new();
    private readonly Progress _progress;
    private readonly NativeSpeech _engine;
    private TaskCompletionSource<bool>? _utterance;
    private string? _utteranceId;
    private bool _disposed;

    public AndroidReadAloud()
    {
        _progress = new Progress(Complete);
        _engine = new NativeSpeech(Platform.AppContext, _initialization);
    }

    private async Task<bool> ReadyAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var ready = await _initialization.Ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
        ObjectDisposedException.ThrowIf(_disposed, this);
        return ready;
    }

    private IEnumerable<Voice> OfflineVoices() => (_engine.Voices ?? new HashSet<Voice>())
        .Where(voice => !voice.IsNetworkConnectionRequired
            && voice.Features?.Contains(NativeSpeech.Engine.KeyFeatureNotInstalled ?? "notInstalled") != true);

    public Task<IReadOnlyList<SystemVoice>> ListAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!await ReadyAsync()) return (IReadOnlyList<SystemVoice>)Array.Empty<SystemVoice>();
        return OfflineVoices().Select(voice => new SystemVoice(voice.Name ?? "", voice.Name ?? "",
            voice.Locale?.ToLanguageTag() ?? "")).ToArray();
    });

    public Task<bool> SpeakAsync(string text, string? voiceId, double rate) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!await ReadyAsync()) return false;
        var voice = OfflineVoices().FirstOrDefault(candidate => candidate.Name == voiceId);
        if (voice == null || string.IsNullOrWhiteSpace(text)) return false;
        StopOnMainThread();
        if (_engine.SetVoice(voice) != OperationResult.Success) return false;
        if (_engine.SetSpeechRate((float)Math.Clamp(rate, 0.25, 4)) != OperationResult.Success) return false;
        _engine.SetOnUtteranceProgressListener(_progress);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _utterance = completion;
        _utteranceId = Guid.NewGuid().ToString("N");
        try
        {
            if (_engine.Speak(text, QueueMode.Flush, null, _utteranceId) != OperationResult.Success) return false;
            return await completion.Task.WaitAsync(TimeSpan.FromMinutes(5));
        }
        finally
        {
            if (ReferenceEquals(_utterance, completion)) StopOnMainThread();
        }
    });

    public void Stop() => MainThread.BeginInvokeOnMainThread(StopOnMainThread);

    private void Complete(string? id, bool success) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (id == _utteranceId) _utterance?.TrySetResult(success);
    });

    private void StopOnMainThread()
    {
        _utterance?.TrySetResult(false);
        _utterance = null;
        _utteranceId = null;
        if (!_disposed) _engine.Stop();
    }

    public void Dispose() => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (_disposed) return;
        StopOnMainThread();
        _disposed = true;
        _engine.Shutdown();
        _engine.Dispose();
        _progress.Dispose();
        _initialization.Dispose();
    });

    private sealed class Initialization : Java.Lang.Object, NativeSpeech.IOnInitListener
    {
        public TaskCompletionSource<bool> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void OnInit(OperationResult status) => Ready.TrySetResult(status == OperationResult.Success);
    }

    private sealed class Progress(Action<string?, bool> complete) : UtteranceProgressListener
    {
        public override void OnStart(string? utteranceId) { }
        public override void OnDone(string? utteranceId) => complete(utteranceId, true);
        [Obsolete("Required by the Android listener contract; newer engines use the overload with an error code.")]
        public override void OnError(string? utteranceId) => complete(utteranceId, false);
        public override void OnError(string? utteranceId, TextToSpeechError errorCode) => complete(utteranceId, false);
        public override void OnStop(string? utteranceId, bool interrupted) => complete(utteranceId, false);
    }
}
