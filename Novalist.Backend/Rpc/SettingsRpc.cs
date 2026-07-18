using System.Reflection;
using System.Text.Json;
using Novalist.Core.Models;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Global and per-project settings with the effective merge view.</summary>
public sealed class SettingsRpc
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly Workspace _workspace;

    public SettingsRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    [JsonRpcMethod("settings/get")]
    public async Task<JsonElement> GetAsync()
    {
        await _workspace.Settings.LoadAsync();
        var hasProject = _workspace.Projects.IsProjectLoaded;
        var payload = new
        {
            hasProject,
            global = _workspace.Settings.Settings,
            overrides = hasProject ? _workspace.Projects.ProjectSettings.Overrides : null,
            effective = BuildEffective()
        };
        return JsonSerializer.SerializeToElement(payload, JsonOptions);
    }

    [JsonRpcMethod("settings/updateGlobal")]
    public async Task<JsonElement> UpdateGlobalAsync(Dictionary<string, JsonElement> patch)
    {
        Apply(_workspace.Settings.Settings, patch);
        await _workspace.Settings.SaveAsync();
        return await GetAsync();
    }

    [JsonRpcMethod("settings/updateProject")]
    public async Task<JsonElement> UpdateProjectAsync(Dictionary<string, JsonElement> patch)
    {
        if (!_workspace.Projects.IsProjectLoaded)
        {
            throw new InvalidOperationException("No project open.");
        }
        Apply(_workspace.Projects.ProjectSettings.Overrides, patch);
        await _workspace.Projects.SaveProjectSettingsAsync();
        return await GetAsync();
    }

    [JsonRpcMethod("settings/clearSection")]
    public async Task<JsonElement> ClearSectionAsync(string section)
    {
        if (!_workspace.Projects.IsProjectLoaded)
        {
            throw new InvalidOperationException("No project open.");
        }
        var overrides = _workspace.Projects.ProjectSettings.Overrides;
        switch (section)
        {
            case "appearance":
                overrides.ClearAppearance();
                break;
            case "editor":
                overrides.ClearEditor();
                break;
            case "writing":
                overrides.ClearWriting();
                break;
            default:
                throw new InvalidOperationException($"Unknown settings section '{section}'.");
        }
        await _workspace.Projects.SaveProjectSettingsAsync();
        return await GetAsync();
    }

    private Dictionary<string, object?> BuildEffective()
    {
        var effective = _workspace.Settings.Effective;
        return new Dictionary<string, object?>
        {
            ["language"] = effective.Language,
            ["theme"] = effective.Theme,
            ["accentColor"] = effective.AccentColor,
            ["editorFontFamily"] = effective.EditorFontFamily,
            ["editorFontSize"] = effective.EditorFontSize,
            ["typewriterScrollEnabled"] = effective.TypewriterScrollEnabled,
            ["typewriterScrollAnchor"] = effective.TypewriterScrollAnchor,
            ["pageViewEnabled"] = effective.PageViewEnabled,
            ["enableBookParagraphSpacing"] = effective.EnableBookParagraphSpacing,
            ["enableBookWidth"] = effective.EnableBookWidth,
            ["bookPageFormat"] = effective.BookPageFormat,
            ["bookTextBlockWidth"] = effective.BookTextBlockWidth,
            ["bookFontFamily"] = effective.BookFontFamily,
            ["bookFontSize"] = effective.BookFontSize,
            ["autoReplacementLanguage"] = effective.AutoReplacementLanguage,
            ["dialogueCorrectionEnabled"] = effective.DialogueCorrectionEnabled,
            ["grammarCheckEnabled"] = effective.GrammarCheckEnabled,
            ["grammarCheckApiUrl"] = effective.GrammarCheckApiUrl,
            ["grammarCheckApiKey"] = effective.GrammarCheckApiKey,
            ["grammarCheckUsername"] = effective.GrammarCheckUsername,
            ["grammarCheckPickyMode"] = effective.GrammarCheckPickyMode,
            ["grammarCheckMotherTongue"] = effective.GrammarCheckMotherTongue
        };
    }

    internal static void Apply(object target, Dictionary<string, JsonElement> patch)
    {
        foreach (var (key, value) in patch)
        {
            var property = target.GetType().GetProperty(
                char.ToUpperInvariant(key[0]) + key[1..],
                BindingFlags.Public | BindingFlags.Instance);
            if (property?.CanWrite != true) continue;
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            object? converted = value.ValueKind switch
            {
                JsonValueKind.Null => null,
                _ when type == typeof(string) => value.GetString(),
                _ when type == typeof(bool) => value.GetBoolean(),
                _ when type == typeof(double) => value.GetDouble(),
                _ => Unsupported(key)
            };
            property.SetValue(target, converted);
        }
    }

    private static object Unsupported(string key) =>
        throw new InvalidOperationException($"Unsupported settings value for '{key}'.");
}
