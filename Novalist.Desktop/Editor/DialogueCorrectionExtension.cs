using System.Collections.Generic;
using System.Linq;

namespace Novalist.Desktop.Editor;

/// <summary>
/// Provides dialogue punctuation auto-correction configuration.
/// The actual correction logic runs in JavaScript inside the WebView editor.
/// This extension holds the language-specific rules and serializes them for JS.
/// </summary>
public sealed class DialogueCorrectionExtension : IEditorExtension
{
    private string _language = "en";
    private bool _enabled;

    public string Name => "DialogueCorrection";
    public int Priority => 55;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public string Language
    {
        get => _language;
        set => _language = value ?? "en";
    }

    /// <summary>
    /// Serializes the dialogue correction configuration as JSON for the JS editor.
    /// </summary>
    public string SerializeConfigJson()
    {
        if (!_enabled) return "{\"enabled\":false}";

        var config = GetLanguageConfig(_language);
        return config;
    }

    private static string GetLanguageConfig(string language)
    {
        // Determine rule family from the auto-replacement language key
        var ruleFamily = language switch
        {
            "de-low" or "de-guillemet" => "de",
            "en" => "en",
            _ => "en" // Default to English rules for unsupported languages
        };

        var openQuotes = language switch
        {
            "de-low" => "\u201E",       // „
            "de-guillemet" => "\u00BB", // »
            "en" => "\u201C",           // "
            _ => "\u201C"
        };

        var closeQuotes = language switch
        {
            "de-low" => "\u201C",       // "
            "de-guillemet" => "\u00AB", // «
            "en" => "\u201D",           // "
            _ => "\u201D"
        };

        var sb = new System.Text.StringBuilder(512);
        sb.Append("{\"enabled\":true");
        sb.Append(",\"ruleFamily\":\"").Append(ruleFamily).Append('"');
        sb.Append(",\"openQuote\":\"").Append(openQuotes).Append('"');
        sb.Append(",\"closeQuote\":\"").Append(closeQuotes).Append('"');

        // Speech verbs per language
        sb.Append(",\"speechVerbs\":");
        if (ruleFamily == "de")
        {
            sb.Append("[\"sagte\",\"fragte\",\"rief\",\"schrie\",\"fl\\u00FCsterte\"," +
                       "\"erwiderte\",\"antwortete\",\"murmelte\",\"brummte\",\"zischte\"," +
                       "\"seufzte\",\"st\\u00F6hnte\",\"meinte\",\"entgegnete\",\"sprach\"," +
                       "\"erkl\\u00E4rte\",\"bemerkte\",\"bat\",\"flehte\",\"knurrte\"," +
                       "\"hauchte\",\"jammerte\",\"klagte\",\"stotterte\",\"stammelte\"," +
                       "\"schluchzte\",\"keuchte\",\"wimmerte\",\"dr\\u00E4ngte\",\"forderte\"," +
                       "\"befahl\",\"warnte\",\"mahnte\",\"tröstete\",\"beruhigte\"]");
        }
        else
        {
            sb.Append("[\"said\",\"asked\",\"whispered\",\"shouted\",\"cried\"," +
                       "\"replied\",\"answered\",\"murmured\",\"exclaimed\",\"muttered\"," +
                       "\"yelled\",\"screamed\",\"called\",\"remarked\",\"responded\"," +
                       "\"explained\",\"stated\",\"declared\",\"added\",\"continued\"," +
                       "\"insisted\",\"suggested\",\"wondered\",\"demanded\",\"pleaded\"," +
                       "\"begged\",\"stammered\",\"stuttered\",\"sobbed\",\"groaned\"," +
                       "\"sighed\",\"breathed\",\"hissed\",\"snapped\",\"barked\"," +
                       "\"growled\",\"urged\",\"warned\",\"cautioned\",\"consoled\"]");
        }
        sb.Append('}');
        return sb.ToString();
    }

    public void OnDocumentOpened(EditorDocumentContext context) { }
    public void OnDocumentClosing(EditorDocumentContext context) { }
}
