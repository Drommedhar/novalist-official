using Android.OS;
using Android.Speech;

namespace Novalist.Mobile.Services;

internal sealed class SpeechSupportCallback : Java.Lang.Object, IRecognitionSupportCallback
{
    public TaskCompletionSource<RecognitionSupport> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void OnSupportResult(RecognitionSupport support) => Completion.TrySetResult(support);
    public void OnError(int error) => Completion.TrySetException(new InvalidOperationException($"Offline speech support check failed ({error})."));
}

internal sealed class SpeechModelDownloadListener : Java.Lang.Object, IModelDownloadListener
{
    public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void OnSuccess() => Completion.TrySetResult(true);
    public void OnScheduled() => Completion.TrySetResult(false);
    public void OnError(SpeechRecognizerError error) => Completion.TrySetException(new InvalidOperationException($"Offline language download failed ({error})."));
    public void OnProgress(int completedPercent) { }
}

internal sealed class SpeechResultsListener : Java.Lang.Object, IRecognitionListener
{
    private readonly List<string> _segments = [];
    public TaskCompletionSource<string> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void OnResults(Bundle? results) => Completion.TrySetResult(Text(results));
    public void OnSegmentResults(Bundle? results) => _segments.Add(Text(results));
    public void OnEndOfSegmentedSession() => Completion.TrySetResult(string.Join(" ", _segments.Where(s => s.Length > 0)));
    public void OnError(SpeechRecognizerError error)
    {
        if (error is SpeechRecognizerError.NoMatch or SpeechRecognizerError.SpeechTimeout) Completion.TrySetResult("");
        else Completion.TrySetException(new InvalidOperationException($"Offline speech recognition failed ({error})."));
    }
    private static string Text(Bundle? results) => results?.GetStringArrayList(SpeechRecognizer.ResultsRecognition)?.FirstOrDefault() ?? "";
    public void OnBeginningOfSpeech() { }
    public void OnBufferReceived(byte[]? buffer) { }
    public void OnEndOfSpeech() { }
    public void OnEvent(int eventType, Bundle? parameters) { }
    public void OnPartialResults(Bundle? partialResults) { }
    public void OnReadyForSpeech(Bundle? parameters) { }
    public void OnRmsChanged(float rmsdB) { }
}
