using System.Text;
using Novalist.Sdk;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;
using Novalist.Sdk.Models.Narration;
using Novalist.Sdk.Services;

namespace Novalist.Sdk.Example;

public sealed partial class WritingToolkitExtension
{
    // ── ISettingsContributor (page metadata; the form comes from the schema) ─

    public IReadOnlyList<SettingsPage> GetSettingsPages() =>
    [
        new SettingsPage
        {
            Category = _loc.T("settings.category"),
            IconPath = "M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"
        }
    ];

    // ── ISettingsSchemaContributor (declarative advanced settings) ───

    public SettingsSchema GetSettingsSchema() => new()
    {
        Title = _loc.T("settingsSchema.title"),
        Fields =
        [
            new SettingsField
            {
                Key = "duration",
                Label = _loc.T("settingsSchema.duration"),
                Type = SettingsFieldType.Number,
                Value = _pomodoro.DurationMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Min = 5,
                Max = 90
            },
            new SettingsField
            {
                Key = "autoStartBreaks",
                Label = _loc.T("settingsSchema.autoStart"),
                Type = SettingsFieldType.Bool,
                Value = _autoStartBreaks ? "true" : "false"
            },
            new SettingsField
            {
                Key = "promptCategory",
                Label = _loc.T("settingsSchema.promptCategory"),
                Type = SettingsFieldType.Select,
                Value = _promptCategory,
                Options = ["any", "character", "setting", "conflict"],
                // Demonstrates conditional visibility: the host shows this field
                // only while the "autoStartBreaks" field above is enabled.
                VisibleWhenKey = "autoStartBreaks",
                VisibleWhenValues = ["true"]
            },
            new SettingsField
            {
                Key = "promptKeyword",
                Label = _loc.T("settingsSchema.promptKeyword"),
                Type = SettingsFieldType.Text,
                Value = _promptKeyword,
                // Stays free-text, but offers the action-populated list as a datalist.
                Suggestions = _keywordSuggestions
            },
            new SettingsField
            {
                Key = "suggestKeywords",
                Label = _loc.T("settingsSchema.suggestKeywords"),
                Type = SettingsFieldType.Action
            }
        ]
    };

    public Task<SettingsSchema?> ExecuteSchemaActionAsync(
        string actionKey, IReadOnlyDictionary<string, string> values)
    {
        if (actionKey != "suggestKeywords") return Task.FromResult<SettingsSchema?>(null);
        // A real extension might fetch these from a service; here we just supply a
        // fixed set to show how an action refreshes a field's suggestions.
        _keywordSuggestions = ["conflict", "mystery", "betrayal", "reunion"];
        return Task.FromResult<SettingsSchema?>(GetSettingsSchema());
    }

    public Task ApplySettingsAsync(IReadOnlyDictionary<string, string> values)
    {
        if (values.TryGetValue("duration", out var d)
            && int.TryParse(d, System.Globalization.CultureInfo.InvariantCulture, out var mins))
        {
            _pomodoro.DurationMinutes = Math.Clamp(mins, 5, 90);
        }
        if (values.TryGetValue("autoStartBreaks", out var a))
        {
            _autoStartBreaks = string.Equals(a, "true", StringComparison.OrdinalIgnoreCase);
        }
        if (values.TryGetValue("promptCategory", out var c) && !string.IsNullOrWhiteSpace(c))
        {
            _promptCategory = c;
        }
        if (values.TryGetValue("promptKeyword", out var kw))
        {
            _promptKeyword = kw;
        }
        return _host.WriteHostDataAsync("writingtoolkit", System.Text.Json.JsonSerializer.Serialize(new
        {
            duration = _pomodoro.DurationMinutes,
            autoStartBreaks = _autoStartBreaks,
            promptCategory = _promptCategory,
            promptKeyword = _promptKeyword
        }));
    }
}
