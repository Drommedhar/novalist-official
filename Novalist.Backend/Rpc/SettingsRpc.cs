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

    [JsonRpcMethod("settings/updateProject")]
    public async Task<JsonElement> UpdateProjectAsync(Dictionary<string, JsonElement> patch)
    {
        if (!_workspace.Projects.IsProjectLoaded)
        {
            throw new InvalidOperationException("No project open.");
        }
        var beforeLanguage = _workspace.Settings.Effective.Language;
        var overrides = _workspace.Projects.ProjectSettings.Overrides;
        Apply(overrides, patch);
        // Clearing the language override clears the pairs with it, so the
        // project goes back to inheriting both rather than keeping one book's
        // quotes against everyone else's language.
        if (patch.ContainsKey(LanguageKey))
            overrides.AutoReplacements = overrides.AutoReplacementLanguage is null
                ? null
                : AutoReplacementDefaults.GetPreset(overrides.AutoReplacementLanguage);
        await _workspace.Projects.SaveProjectSettingsAsync();
        RaiseLanguageIfChanged(beforeLanguage);
        return await GetAsync();
    }

    [JsonRpcMethod("settings/clearSection")]
    public async Task<JsonElement> ClearSectionAsync(string section)
    {
        if (!_workspace.Projects.IsProjectLoaded)
        {
            throw new InvalidOperationException("No project open.");
        }
        // Dropping an Appearance override reverts the effective UI language to
        // the global one, so extensions have to hear about it just as they do
        // for a direct settings write.
        var beforeLanguage = _workspace.Settings.Effective.Language;
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
        RaiseLanguageIfChanged(beforeLanguage);
        return await GetAsync();
    }

    /// <summary>
    /// Pins a settings section to the open project by copying the values in
    /// effect right now into the project's overrides — what turning the
    /// section's project-override switch on does. The inverse of
    /// <see cref="ClearSectionAsync"/>, and idempotent: pinning an already
    /// pinned section rewrites the same values.
    /// </summary>
    [JsonRpcMethod("settings/pinSection")]
    public async Task<JsonElement> PinSectionAsync(string section)
    {
        if (!_workspace.Projects.IsProjectLoaded)
        {
            throw new InvalidOperationException("No project open.");
        }
        var overrides = _workspace.Projects.ProjectSettings.Overrides;
        var effective = _workspace.Settings.Effective;
        switch (section)
        {
            case "appearance":
                overrides.PinAppearance(effective);
                break;
            case "editor":
                overrides.PinEditor(effective);
                break;
            case "writing":
                overrides.PinWriting(effective);
                break;
            default:
                throw new InvalidOperationException($"Unknown settings section '{section}'.");
        }
        await _workspace.Projects.SaveProjectSettingsAsync();
        return await GetAsync();
    }

    /// <summary>
    /// Updates per-project metadata that lives outside <see cref="SettingsOverrides"/>:
    /// author, filesystem-watch toggle, and the writing-goal deadline. Only the keys
    /// present in the patch are changed.
    /// </summary>
    [JsonRpcMethod("settings/updateProjectMeta")]
    public async Task<JsonElement> UpdateProjectMetaAsync(Dictionary<string, JsonElement> patch)
    {
        if (!_workspace.Projects.IsProjectLoaded)
        {
            throw new InvalidOperationException("No project open.");
        }
        var settings = _workspace.Projects.ProjectSettings;
        foreach (var (key, value) in patch)
        {
            switch (key)
            {
                case "author":
                    settings.Author = value.ValueKind == JsonValueKind.Null
                        ? string.Empty
                        : value.GetString() ?? string.Empty;
                    break;
                case "watchFilesystem":
                    settings.WatchFilesystem = value.GetBoolean();
                    break;
                case "deadline":
                    var deadline = value.ValueKind == JsonValueKind.Null ? null : value.GetString();
                    settings.WordCountGoals.Deadline =
                        string.IsNullOrWhiteSpace(deadline) ? null : deadline;
                    break;
                // Both goals were readable here but only writable from the
                // Dashboard, which is not where a writer looks for a setting.
                case "dailyGoal":
                    settings.WordCountGoals.DailyGoal = Math.Max(0, value.GetInt32());
                    break;
                case "projectGoal":
                    settings.WordCountGoals.ProjectGoal = Math.Max(0, value.GetInt32());
                    break;
                // A week is the budget somebody who writes three heavy days can
                // actually keep; a daily goal marks them down four days in seven
                // for being exactly on schedule. 0 turns the horizon off.
                case "weeklyGoal":
                    settings.WordCountGoals.WeeklyGoal = Math.Max(0, value.GetInt32());
                    break;
                case "monthlyGoal":
                    settings.WordCountGoals.MonthlyGoal = Math.Max(0, value.GetInt32());
                    break;
                // Zero would divide the page estimate by nothing, so a cleared
                // field goes back to the trade-paperback default rather than
                // breaking the count.
                case "wordsPerPage":
                    var perPage = value.GetInt32();
                    settings.WordsPerPage = perPage > 0
                        ? perPage
                        : Core.Services.PageEstimate.DefaultWordsPerPage;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown project meta key '{key}'.");
            }
        }
        await _workspace.Projects.SaveProjectSettingsAsync();
        return await GetAsync();
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

    /// <summary>Reports the diagnostic-log directory and newest log file (if any).</summary>
    [JsonRpcMethod("settings/logInfo")]
    public LogInfoDto LogInfo() => ResolveLogInfo(LogsDirectory);

    /// <summary>Deletes every <c>*.log</c> file in the diagnostic-log directory.</summary>
    [JsonRpcMethod("settings/clearLogs")]
    public int ClearLogs() => ClearLogFiles(LogsDirectory);

    /// <summary>Test seam: overrides the diagnostic-log directory (null = OS default).</summary>
    internal static string? LogsDirectoryOverride { get; set; }

    /// <summary>Diagnostic-log directory, matching the desktop shell's convention.</summary>
    internal static string LogsDirectory => LogsDirectoryOverride
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Novalist",
            "logs");

    internal static LogInfoDto ResolveLogInfo(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return new LogInfoDto(directory, null);
        }
        var newest = new DirectoryInfo(directory)
            .EnumerateFiles("*.log")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
        return new LogInfoDto(directory, newest?.FullName);
    }

    internal static int ClearLogFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*.log"))
        {
            File.Delete(file);
            count++;
        }
        return count;
    }

    private Dictionary<string, object?> BuildProjectMeta()
    {
        var settings = _workspace.Projects.ProjectSettings;
        return new Dictionary<string, object?>
        {
            ["author"] = settings.Author,
            ["watchFilesystem"] = settings.WatchFilesystem,
            ["deadline"] = settings.WordCountGoals.Deadline,
            ["dailyGoal"] = settings.WordCountGoals.DailyGoal,
            ["weeklyGoal"] = settings.WordCountGoals.WeeklyGoal,
            ["monthlyGoal"] = settings.WordCountGoals.MonthlyGoal,
            ["wordsPerPage"] = settings.WordsPerPage,
            ["projectGoal"] = settings.WordCountGoals.ProjectGoal
        };
    }

    /// <summary>Per-section "this project overrides the global value" flags,
    /// keyed by the same section names <see cref="ClearSectionAsync"/> and
    /// <see cref="PinSectionAsync"/> take.</summary>
    private Dictionary<string, bool> BuildOverriddenSections()
    {
        var overrides = _workspace.Projects.ProjectSettings.Overrides;
        return new Dictionary<string, bool>
        {
            ["appearance"] = overrides.HasAppearanceOverride,
            ["editor"] = overrides.HasEditorOverride,
            ["writing"] = overrides.HasWritingOverride
        };
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
