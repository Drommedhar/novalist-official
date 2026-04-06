using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Novalist.Desktop.Localization;

/// <summary>
/// Singleton localization service.
/// XAML usage: <code>{Binding [key], Source={x:Static loc:Loc.Instance}}</code>
/// Code usage: <code>Loc.T("key")</code> or <code>Loc.T("key", arg0, arg1)</code>
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static Loc Instance { get; } = new();

    private Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private Dictionary<string, string> _fallback = new(StringComparer.Ordinal);
    private string _currentLanguage = "en";
    private string _localesDirectory = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? LanguageChanged;

    private Loc() { }

    /// <summary>
    /// The currently active language code (e.g. "en", "de").
    /// </summary>
    public string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            Console.Error.WriteLine($"[Loc] CurrentLanguage setter: '{_currentLanguage}' -> '{value}'");
            if (string.Equals(_currentLanguage, value, StringComparison.Ordinal))
                return;

            _currentLanguage = value;
            LoadLanguage(value);
            Console.Error.WriteLine($"[Loc] Language switched, strings={_strings.Count}, subscribers={PropertyChanged?.GetInvocationList().Length ?? 0}, langChanged={LanguageChanged?.GetInvocationList().Length ?? 0}");
            // Notify all bindings to refresh
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
            LanguageChanged?.Invoke();
        }
    }

    /// <summary>
    /// Indexer for XAML compiled bindings: <code>{Binding [settings.editor], Source={x:Static loc:Loc.Instance}}</code>
    /// </summary>
    public string this[string key] => Resolve(key);

    /// <summary>
    /// Initialize the localization system. Call once at startup.
    /// </summary>
    public void Initialize(string localesDirectory, string language)
    {
        _localesDirectory = localesDirectory;
        Debug.WriteLine($"[Loc] Initialize: dir={localesDirectory}, lang={language}, exists={Directory.Exists(localesDirectory)}");
        var enPath = Path.Combine(localesDirectory, "en.json");
        Console.Error.WriteLine($"[Loc] Initialize: dir={localesDirectory}, lang={language}, dir_exists={Directory.Exists(localesDirectory)}, en_exists={File.Exists(enPath)}");
        _fallback = LoadFile(enPath);
        Console.Error.WriteLine($"[Loc] Fallback loaded: {_fallback.Count} keys");
        _currentLanguage = language;
        LoadLanguage(language);
        Console.Error.WriteLine($"[Loc] Active strings: {_strings.Count} keys");
    }

    /// <summary>
    /// Get a translated string by key.
    /// </summary>
    public static string T(string key) => Instance.Resolve(key);

    /// <summary>
    /// Get a translated string with format arguments.
    /// </summary>
    public static string T(string key, params object[] args)
    {
        var template = Instance.Resolve(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    /// <summary>
    /// Returns an alphabetically sorted list of available language codes
    /// discovered from JSON files in the locales directory.
    /// Each file named "xx.json" becomes language code "xx".
    /// </summary>
    public List<string> GetAvailableLanguages()
    {
        if (string.IsNullOrWhiteSpace(_localesDirectory) || !Directory.Exists(_localesDirectory))
            return ["en"];

        return Directory.GetFiles(_localesDirectory, "*.json")
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Returns the display name for a language code, read from the "language.name" key
    /// in that language's file. Falls back to the code itself.
    /// </summary>
    public string GetLanguageDisplayName(string code)
    {
        var filePath = Path.Combine(_localesDirectory, $"{code}.json");
        var data = LoadFile(filePath);
        return data.TryGetValue("language.name", out var name) ? name : code;
    }

    private void LoadLanguage(string language)
    {
        if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            _strings = _fallback;
            return;
        }

        var filePath = Path.Combine(_localesDirectory, $"{language}.json");
        _strings = LoadFile(filePath);
    }

    private string Resolve(string key)
    {
        if (_strings.TryGetValue(key, out var value))
            return value;
        if (_fallback.TryGetValue(key, out var fallback))
            return fallback;
        Debug.WriteLine($"[Loc] MISSING key: '{key}'");
        return key;
    }

    private static Dictionary<string, string> LoadFile(string path)
    {
        if (!File.Exists(path))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            FlattenJson(doc.RootElement, string.Empty, result);
            return result;
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static void FlattenJson(JsonElement element, string prefix, Dictionary<string, string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
                    FlattenJson(property.Value, key, result);
                }
                break;
            case JsonValueKind.String:
                result[prefix] = element.GetString() ?? string.Empty;
                break;
            case JsonValueKind.Number:
                result[prefix] = element.GetRawText();
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                result[prefix] = element.GetRawText();
                break;
        }
    }
}
