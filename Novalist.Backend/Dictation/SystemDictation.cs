using System.Text.Json;

namespace Novalist.Backend.Dictation;

public sealed class SystemDictation(string platform, IAppleSpeechBridge apple, Action openWindowsPanel) : ISystemDictation
{
    public const string ProviderId = "novalist.system";
    public static SystemDictation Create() => new(
        OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsIOS() || OperatingSystem.IsMacOSVersionAtLeast(26) ? "apple" : "unsupported",
        new AppleSpeechBridge(), WindowsVoiceTyping.Open);

    public async Task<SystemDictationStatus> StatusAsync(CancellationToken token)
    {
        if (platform == "windows")
            return new("windows", true, true, true, [new("en", true, true), new("de", true, true)]);
        if (platform != "apple") return new("unsupported", false, false, false, []);
        try
        {
            var result = await apple.RequestAsync(new { operation = "status" }, token);
            return JsonSerializer.Deserialize<SystemDictationStatus>(result,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("No system speech status.");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return new("appleUnavailable", false, false, false, []);
        }
    }

    public async Task PrepareAsync(string language, CancellationToken token)
    {
        ValidateLanguage(language);
        if (platform != "apple") throw new InvalidOperationException("Language support is managed in system settings.");
        await apple.RequestAsync(new { operation = "prepare", language }, token);
    }

    public async Task<string> TranscribeAsync(byte[] audio, string language, CancellationToken token)
    {
        ValidateLanguage(language);
        if (platform != "apple") throw new InvalidOperationException("Use the system voice typing panel.");
        var result = await apple.RequestAsync(new
        {
            operation = "transcribe",
            language,
            audio = Convert.ToBase64String(audio)
        }, token);
        return JsonSerializer.Deserialize<string>(result) ?? string.Empty;
    }

    public void OpenSystemPanel()
    {
        if (platform != "windows") throw new InvalidOperationException("No system voice typing panel.");
        openWindowsPanel();
    }

    private static void ValidateLanguage(string language)
    {
        if (language is not ("en" or "de")) throw new ArgumentException("Unsupported dictation language.");
    }
}
