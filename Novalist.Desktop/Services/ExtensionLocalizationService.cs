using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Novalist.Desktop.Utilities;
using Novalist.Sdk.Services;

namespace Novalist.Desktop.Services;

/// <summary>
/// Per-extension localization service that loads JSON locale files from
/// the extension's <c>Locales/</c> folder, flattens nested keys with dot notation,
/// and provides English fallback.
/// </summary>
public sealed class ExtensionLocalizationService : IExtensionLocalization
{
    private readonly string _localesDir;
    private Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private Dictionary<string, string> _fallback = new(StringComparer.Ordinal);

    public event PropertyChangedEventHandler? PropertyChanged;

    public ExtensionLocalizationService(string localesDir, string language)
    {
        _localesDir = localesDir;
        _fallback = LoadFile(Path.Combine(localesDir, "en.json"));
        LoadLanguage(language);
    }

    public string this[string key] => Resolve(key);

    public string T(string key) => Resolve(key);

    public string T(string key, params object[] args)
    {
        var template = Resolve(key);
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
    /// Reload strings for the given language and notify all bindings.
    /// </summary>
    internal void Reload(string language)
    {
        LoadLanguage(language);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    private void LoadLanguage(string language)
    {
        if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            _strings = _fallback;
            return;
        }

        var path = Path.Combine(_localesDir, $"{language}.json");
        _strings = LoadFile(path);
    }

    private string Resolve(string key)
    {
        if (_strings.TryGetValue(key, out var value))
            return value;
        if (_fallback.TryGetValue(key, out var fallback))
            return fallback;
        Log.Debug($"[ExtLoc] MISSING key: '{key}'");
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
                foreach (var prop in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                    FlattenJson(prop.Value, key, result);
                }
                break;
            case JsonValueKind.String:
                result[prefix] = element.GetString() ?? string.Empty;
                break;
            default:
                result[prefix] = element.ToString();
                break;
        }
    }
}
