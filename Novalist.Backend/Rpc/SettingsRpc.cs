using System.Reflection;
using System.Text.Json;
using Novalist.Backend.Extensions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Global and per-project settings with the effective merge view.</summary>
public sealed partial class SettingsRpc
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly Workspace _workspace;

    public SettingsRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    /// <summary>
    /// The writing languages a project can be set to, in the order they are
    /// offered.
    ///
    /// Served rather than duplicated in the renderer, because it was duplicated
    /// and the two copies drifted: the backend's idea of the list carried
    /// Chinese and Japanese and the picker's did not, so a book could not be
    /// declared as written in either - and everything that reads the writing
    /// language, up to and including which language the book is read aloud in,
    /// was therefore told it was English.
    /// </summary>
    [JsonRpcMethod("settings/writingLanguages")]
    public string[] WritingLanguages() => [.. AutoReplacementDefaults.AvailableLanguages];

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
            // Which sections the open project has pinned. The Settings view
            // derives its per-section project-override switch from these, so the
            // switch reflects what is actually stored rather than what was last
            // clicked in this session.
            overriddenSections = hasProject ? BuildOverriddenSections() : null,
            effective = BuildEffective(),
            project = hasProject ? BuildProjectMeta() : null
        };
        return JsonSerializer.SerializeToElement(payload, JsonOptions);
    }

    /// <summary>
    /// The patch key that carries the writing language.
    ///
    /// Named because picking a language has to do more than store the name:
    /// the pairs are what the editor actually types against, and the language
    /// only ever seeded them - and only while the list was empty. A writer who
    /// chose German after their first launch got a preview promising low-9
    /// quotes and English ones in the prose ever after.
    /// </summary>
    private const string LanguageKey = "autoReplacementLanguage";

    [JsonRpcMethod("settings/updateGlobal")]
    public async Task<JsonElement> UpdateGlobalAsync(Dictionary<string, JsonElement> patch)
    {
        var beforeLanguage = _workspace.Settings.Effective.Language;
        var settings = _workspace.Settings.Settings;
        Apply(settings, patch);
        if (patch.ContainsKey(LanguageKey))
            settings.AutoReplacements = AutoReplacementDefaults.GetPreset(settings.AutoReplacementLanguage);
        await _workspace.Settings.SaveAsync();
        if (patch.ContainsKey("diagnosticLoggingEnabled"))
        {
            Log.SetDirectory(LogsDirectory);
            Log.EnableFileLogging(settings.DiagnosticLoggingEnabled);
            Log.Info($"Diagnostic logging {(settings.DiagnosticLoggingEnabled ? "enabled" : "disabled")} by user.");
        }
        RaiseLanguageIfChanged(beforeLanguage);
        return await GetAsync();
    }

    /// <summary>Fires the extension LanguageChanged event when the effective UI
    /// language changed as a result of the last settings write.</summary>
    private void RaiseLanguageIfChanged(string beforeLanguage)
    {
        var afterLanguage = _workspace.Settings.Effective.Language;
        if (!string.Equals(beforeLanguage, afterLanguage, StringComparison.Ordinal))
            _workspace.RaiseLanguageChanged(afterLanguage);
    }

    /// <summary>Sets a user override for a single hotkey action's gesture.</summary>
    [JsonRpcMethod("settings/setHotkeyBinding")]
    public async Task<JsonElement> SetHotkeyBindingAsync(string actionId, string gesture)
    {
        _workspace.Settings.Settings.HotkeyBindings[actionId] = gesture;
        await _workspace.Settings.SaveAsync();
        return await GetAsync();
    }

    /// <summary>Removes the override for a single hotkey action (reverts to default).</summary>
    [JsonRpcMethod("settings/resetHotkeyBinding")]
    public async Task<JsonElement> ResetHotkeyBindingAsync(string actionId)
    {
        _workspace.Settings.Settings.HotkeyBindings.Remove(actionId);
        await _workspace.Settings.SaveAsync();
        return await GetAsync();
    }

    /// <summary>Clears every hotkey override (reverts all actions to defaults).</summary>
    [JsonRpcMethod("settings/resetAllHotkeys")]
    public async Task<JsonElement> ResetAllHotkeysAsync()
    {
        _workspace.Settings.Settings.HotkeyBindings.Clear();
        await _workspace.Settings.SaveAsync();
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
            ["editorLineHeight"] = effective.EditorLineHeight,
            ["readabilityHighlighting"] = effective.ReadabilityHighlighting,
            ["readAloudRate"] = effective.ReadAloudRate,
            ["readAloudVoiceUri"] = effective.ReadAloudVoiceUri,
            ["editorLetterSpacing"] = effective.EditorLetterSpacing,
            ["editorParagraphSpacing"] = effective.EditorParagraphSpacing,
            ["editorFirstLineIndent"] = effective.EditorFirstLineIndent,
            ["composeDimming"] = effective.ComposeDimming,
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
            ["autoReplacementEnabled"] = effective.AutoReplacementEnabled,
            ["reviewerName"] = effective.ReviewerName,
            ["dialogueCorrectionEnabled"] = effective.DialogueCorrectionEnabled,
            ["grammarCheckEnabled"] = effective.GrammarCheckEnabled,
            ["grammarCheckProvider"] = effective.GrammarCheckProvider,
            ["grammarCheckLanguage"] = GrammarCheckService.ResolveLanguageCode(
                effective.AutoReplacementLanguage, effective.SpellCheckLanguages),
            ["spellCheckEnabled"] = effective.SpellCheckEnabled,
            ["spellCheckLanguages"] = effective.SpellCheckLanguages,
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
                // Plain string lists (spell-check languages). Richer lists have
                // their own RPCs; this covers the settings that are just tags.
                _ when type == typeof(List<string>) => ReadStringList(value, key),
                _ => Unsupported(key)
            };
            property.SetValue(target, converted);
        }
    }

    private static List<string> ReadStringList(JsonElement value, string key)
    {
        if (value.ValueKind != JsonValueKind.Array) Unsupported(key);
        return [.. value.EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => item.Length > 0)];
    }

    private static object Unsupported(string key) =>
        throw new InvalidOperationException($"Unsupported settings value for '{key}'.");
}

/// <summary>Diagnostic-log location reported to the renderer's Diagnostics section.</summary>
public sealed record LogInfoDto(string Directory, string? CurrentLog);
