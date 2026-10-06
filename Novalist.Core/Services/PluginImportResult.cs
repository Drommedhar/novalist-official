using System.Text.Json;
using System.Text.RegularExpressions;
using Markdig;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>
/// Lightweight wrapper around a JsonElement for reading plugin settings.
/// </summary>
public sealed class PluginSettingsData
{
    private readonly JsonElement _root;

    public PluginSettingsData(JsonElement root) => _root = root;

    public string GetStringOrDefault(string key, string defaultValue = "")
    {
        if (_root.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString() ?? defaultValue;
        return defaultValue;
    }

    public JsonElement? GetObjectOrDefault(string key)
    {
        if (_root.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.Object)
            return prop;
        return null;
    }

    public JsonElement? GetArrayOrDefault(string key)
    {
        if (_root.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.Array)
            return prop;
        return null;
    }
}

public class PluginDetectionResult
{
    public string VaultRoot { get; set; } = string.Empty;
    public bool HasPluginData { get; set; }
    public List<PluginProjectInfo> Projects { get; } = [];
}

public class PluginProjectInfo
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

public class PluginImportResult
{
    public string ProjectPath { get; set; } = string.Empty;
    public Dictionary<string, List<string>> RelationshipPairs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? AutoReplacementLanguage { get; set; }
    public List<AutoReplacementPair> AutoReplacements { get; set; } = [];
}
