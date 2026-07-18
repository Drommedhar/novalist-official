namespace Novalist.Backend.Extensions;

/// <summary>Headless replacement for the Desktop file logger: stderr only,
/// keeping the same no-story-content logging contract.</summary>
internal static class Log
{
    public static void Debug(string message) => Console.Error.WriteLine(message);
    public static void Info(string message) => Console.Error.WriteLine(message);
    public static void Warn(string message) => Console.Error.WriteLine(message);
    public static void Error(string message) => Console.Error.WriteLine(message);
}

/// <summary>Headless replacement for the Desktop Loc singleton. The backend
/// tracks the effective language; display names cover the bundled locales.</summary>
internal sealed class Loc
{
    public static Loc Instance { get; } = new();

    public string CurrentLanguage { get; set; } = "en";

    public string GetLanguageDisplayName(string code) => code switch
    {
        "de" => "Deutsch",
        "zh-CN" => "简体中文",
        _ => "English"
    };
}
