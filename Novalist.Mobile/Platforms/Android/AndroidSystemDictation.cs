using Android.Content;
using Android.Media;
using Android.OS;
using Android.Speech;
using Novalist.Backend.Dictation;
using Novalist.Core.Utilities;

namespace Novalist.Mobile.Services;

public sealed class AndroidSystemDictation : ISystemDictation
{
    private static readonly string[] Languages = ["en", "de"];

    public Task<SystemDictationStatus> StatusAsync(CancellationToken token) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!SpeechRecognizer.IsOnDeviceRecognitionAvailable(Platform.AppContext))
            return new SystemDictationStatus("android", false, false, false, []);
        var languages = new List<SystemDictationLanguage>();
        foreach (var language in Languages)
        {
            var status = await WithRecognizerAsync(async recognizer =>
            {
                using var intent = RecognitionIntent(language);
                using var callback = new SpeechSupportCallback();
                recognizer.CheckRecognitionSupport(intent, MainExecutor(), callback);
                using var support = await callback.Completion.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
                var installed = Supports(support.InstalledOnDeviceLanguages, language);
                return new SystemDictationLanguage(language, installed || Supports(support.SupportedOnDeviceLanguages, language), installed);
            });
            languages.Add(status);
        }
        return new SystemDictationStatus("android", languages.Any(l => l.Supported), false, false, languages.ToArray());
    });

    public Task PrepareAsync(string language, CancellationToken token) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        using var intent = RecognitionIntent(language);
        await WithRecognizerAsync(async recognizer =>
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(34))
            {
                using var listener = new SpeechModelDownloadListener();
                recognizer.TriggerModelDownload(intent, MainExecutor(), listener);
                if (await listener.Completion.Task.WaitAsync(token)) return true;
            }
            else recognizer.TriggerModelDownload(intent);
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), token);
                using var callback = new SpeechSupportCallback();
                recognizer.CheckRecognitionSupport(intent, MainExecutor(), callback);
                using var support = await callback.Completion.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
                if (Supports(support.InstalledOnDeviceLanguages, language)) return true;
            }
        });
    });

    public async Task<string> TranscribeAsync(byte[] audio, string language, CancellationToken token)
    {
        var clip = WaveAudio.Read(audio) ?? throw new ArgumentException("A PCM WAV recording is required.");
        if (clip.Format != new WaveFormat(16000, 1, 16))
            throw new ArgumentException("Android dictation requires 16 kHz mono PCM16 audio.");
        var path = Path.Combine(FileSystem.CacheDirectory, "speech-" + Guid.NewGuid().ToString("N") + ".pcm");
        try
        {
            await File.WriteAllBytesAsync(path, clip.Samples, token);
            using var source = ParcelFileDescriptor.Open(new Java.IO.File(path), ParcelFileMode.ReadOnly)
                ?? throw new IOException("The recording could not be opened.");
            return await MainThread.InvokeOnMainThreadAsync(() => WithRecognizerAsync(async recognizer =>
            {
                using var intent = RecognitionIntent(language);
                intent.PutExtra(RecognizerIntent.ExtraAudioSource, source);
                intent.PutExtra(RecognizerIntent.ExtraAudioSourceChannelCount, 1);
                intent.PutExtra(RecognizerIntent.ExtraAudioSourceEncoding, (int)Encoding.Pcm16bit);
                intent.PutExtra(RecognizerIntent.ExtraAudioSourceSamplingRate, 16000);
                intent.PutExtra(RecognizerIntent.ExtraSegmentedSession, RecognizerIntent.ExtraAudioSource);
                using var listener = new SpeechResultsListener();
                recognizer.SetRecognitionListener(listener);
                recognizer.StartListening(intent);
                var text = await listener.Completion.Task.WaitAsync(TimeSpan.FromSeconds(60), token);
                if (Android.Systems.Os.Lseek(source.FileDescriptor, 0, Android.Systems.OsConstants.SeekCur) != clip.Samples.Length)
                    throw new PlatformNotSupportedException("The installed speech service did not consume the supplied recording.");
                return text;
            }));
        }
        finally { File.Delete(path); }
    }

    public void OpenSystemPanel() => throw new PlatformNotSupportedException("Android dictation runs inside the editor.");

    private static Intent RecognitionIntent(string language)
    {
        if (!Languages.Contains(language)) throw new ArgumentException("Unsupported dictation language.");
        var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
        intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
        intent.PutExtra(RecognizerIntent.ExtraLanguage, language == "en" ? "en-US" : "de-DE");
        intent.PutExtra(RecognizerIntent.ExtraPreferOffline, true);
        return intent;
    }

    private static Java.Util.Concurrent.IExecutor MainExecutor() => Platform.AppContext.MainExecutor
        ?? throw new InvalidOperationException("The Android main executor is unavailable.");

    private static bool Supports(IList<string>? languages, string language) => languages?.Any(value =>
        value.Equals(language, StringComparison.OrdinalIgnoreCase) || value.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase)) == true;

    private static async Task<T> WithRecognizerAsync<T>(Func<SpeechRecognizer, Task<T>> action)
    {
        if (!SpeechRecognizer.IsOnDeviceRecognitionAvailable(Platform.AppContext))
            throw new PlatformNotSupportedException("On-device speech recognition is unavailable.");
        using var recognizer = SpeechRecognizer.CreateOnDeviceSpeechRecognizer(Platform.AppContext)
            ?? throw new PlatformNotSupportedException("On-device speech recognition is unavailable.");
        try { return await action(recognizer); }
        finally { recognizer.Destroy(); }
    }
}
