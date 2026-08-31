namespace Novalist.Backend.Extensions;

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
