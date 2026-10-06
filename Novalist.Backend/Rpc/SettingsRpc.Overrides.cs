using System.Reflection;
using System.Text.Json;
using Novalist.Backend.Extensions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class SettingsRpc
{
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

    /// <summary>
    /// Replaces the writer's own replacement rules, for their defaults or for
    /// the open book.
    ///
    /// Its own method rather than a key in the settings patch: the patch is a
    /// reflection hop over scalars, and a rule list needs checking before it is
    /// stored - a pattern that will not compile has to be refused with a reason
    /// while the writer is still looking at the row they typed it into.
    /// </summary>
    [JsonRpcMethod("settings/setAutoReplacements")]
    public async Task<JsonElement> SetAutoReplacementsAsync(
        string scope, AutoReplacementPair[] rules)
    {
        var rejected = rules
            .Select((rule, index) => (index, reason: AutoReplacementRules.Validate(rule)))
            .FirstOrDefault(r => r.reason is not null);
        if (rejected.reason is not null)
            throw new InvalidOperationException(
                $"Rule {rejected.index} cannot be stored: {rejected.reason}");

        if (scope == "project")
        {
            if (!_workspace.Projects.IsProjectLoaded)
                throw new InvalidOperationException("No project open.");
            _workspace.Projects.ProjectSettings.Overrides.AutoReplacements = [.. rules];
            await _workspace.Projects.SaveProjectSettingsAsync();
        }
        else
        {
            var settings = _workspace.Settings.Settings;
            settings.AutoReplacements = [.. rules];
            // An empty list is a decision from here on, not a fresh install
            // waiting to be seeded.
            settings.AutoReplacementsSeeded = true;
            await _workspace.Settings.SaveAsync();
        }
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
}
