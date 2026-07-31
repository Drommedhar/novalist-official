using System.Text;
using Novalist.Sdk;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;
using Novalist.Sdk.Services;

namespace Novalist.Sdk.Example;

/// <summary>
/// Example extension demonstrating all hook interfaces.
/// Provides: Pomodoro timer, word frequency analysis, writing prompts,
/// custom themes, and AI/editor/export hooks.
/// </summary>
public sealed class WritingToolkitExtension :
    IExtension,
    IRibbonContributor,
    IEditorExtension,
    IAiHook,
    ISettingsContributor,
    IExportFormatContributor,
    IThemeContributor,
    IStatusBarContributor,
    IContextMenuContributor,
    IEntityTypeContributor,
    IGrammarCheckContributor,
    IArticleGeneratorContributor,
    IEntityExtractionContributor,
    IHotkeyContributor,
    IWizardContributor,
    IPropertyTypeContributor,
    IInlineActionContributor,
    ISettingsSchemaContributor
{
    private bool _autoStartBreaks;
    private string _promptCategory = "any";
    private string _promptKeyword = string.Empty;
    // Autocomplete suggestions filled by the "Suggest keywords" action button —
    // demonstrates SettingsFieldType.Action + SettingsField.Suggestions.
    private List<string> _keywordSuggestions = [];

    private IHostServices _host = null!;
    private IExtensionLocalization _loc = null!;
    private readonly PomodoroService _pomodoro = new();
    private readonly WordFrequencyService _wordFrequency = new();
    private readonly WritingPromptService _prompts = new();

    // ── IExtension ──────────────────────────────────────────────────

    public string Id => "com.novalist.writingtoolkit";
    public string DisplayName => "Writing Toolkit";
    public string Description => "Word frequency analysis, writing prompts, Pomodoro timer, and custom themes.";
    public string Version => "1.0.0";
    public string Author => "Novalist Team";

    public void Initialize(IHostServices host)
    {
        _host = host;
        _loc = host.GetLocalization(Id);
        host.ProjectLoaded += info => _wordFrequency.Clear();
        host.SceneSaved += scene => _wordFrequency.MarkDirty();
        // Inline actions register imperatively (they are not collected from a
        // return-value hook like the other contributions).
        host.RegisterInlineActionContributor(this);

        // A command, which is the surface a script drives and the command
        // palette lists. The same thing the status-bar item does when clicked,
        // reachable without the mouse and without knowing where it lives.
        host.RegisterCommand(
            new HostCommandInfo
            {
                Id = PomodoroCommandId,
                Title = _loc.T("command.pomodoro.title"),
                Description = _loc.T("command.pomodoro.description"),
                // Optional, so the palette can still run it bare. A schema is
                // documentation of what a script may pass, not a demand.
                ArgumentsSchema =
                    """
                    {"type":"object","properties":{"minutes":{"type":"integer"}}}
                    """,
            },
            argumentsJson =>
            {
                if (_pomodoro.IsRunning) _pomodoro.Stop();
                else _pomodoro.Start(ReadMinutes(argumentsJson));
                return Task.CompletedTask;
            });

        // One that genuinely cannot run without being told what to count, which
        // is why the palette leaves it to a script.
        host.RegisterCommand(
            new HostCommandInfo
            {
                Id = CountWordCommandId,
                Title = _loc.T("command.countWord.title"),
                Description = _loc.T("command.countWord.description"),
                ArgumentsSchema =
                    """
                    {"type":"object","required":["word"],
                     "properties":{"word":{"type":"string"}}}
                    """,
            },
            _ => Task.CompletedTask);
    }

    /// <summary>The pomodoro toggle, as a command.</summary>
    public const string PomodoroCommandId = "ext.writingtoolkit.pomodoro.toggle";

    /// <summary>A command that needs an argument, so the palette leaves it out.</summary>
    public const string CountWordCommandId = "ext.writingtoolkit.countword";

    /// <summary>The requested length, or the extension's own default.</summary>
    private static int? ReadMinutes(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson)) return null;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(argumentsJson);
            return document.RootElement.TryGetProperty("minutes", out var value)
                && value.TryGetInt32(out var minutes)
                ? minutes
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    public void Shutdown()
    {
        _host.UnregisterCommand(PomodoroCommandId);
        _host.UnregisterCommand(CountWordCommandId);
        _pomodoro.Stop();
    }

    // ── IRibbonContributor ──────────────────────────────────────────

    public IReadOnlyList<RibbonItem> GetRibbonItems() =>
    [
        new RibbonItem
        {
            Tab = "Extensions",
            Group = _loc.T("group.writingToolkit"),
            Label = _loc.T("ribbon.wordFreq.label"),
            IconPath = "M18 20V10M12 20V4M6 20v-4",
            Tooltip = _loc.T("ribbon.wordFreq.tooltip"),
            Size = "Large",
            OnClick = () => _host.ActivateContentView("ext.wordfreq")
        },
        new RibbonItem
        {
            Tab = "Extensions",
            Group = _loc.T("group.writingToolkit"),
            Label = _loc.T("ribbon.prompt.label"),
            IconPath = "M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16zM3.27 6.96 12 12.01l8.73-5.05M12 22.08V12",
            Tooltip = _loc.T("ribbon.prompt.tooltip"),
            Size = "Large",
            OnClick = () =>
            {
                var prompt = _prompts.GetRandomPrompt();
                _prompts.AddToHistory(prompt);
                _host.ShowNotification(prompt);
            }
        },
        new RibbonItem
        {
            Tab = "Extensions",
            Group = _loc.T("group.writingToolkit"),
            Label = _loc.T("ribbon.pomodoro.label"),
            IconPath = "M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20zM12 6v6l4 2M2 12h2M20 12h2M12 2v2",
            Tooltip = _loc.T("ribbon.pomodoro.tooltip"),
            Size = "Large",
            IsToggle = true,
            IsActive = () => _pomodoro.IsRunning,
            OnClick = () =>
            {
                if (_pomodoro.IsRunning)
                {
                    _pomodoro.Stop();
                    _host.ShowNotification(_loc.T("notifications.pomodoroStopped"));
                }
                else
                {
                    _pomodoro.Start();
                    _host.ShowNotification(_loc.T("notifications.pomodoroStarted", _pomodoro.DurationMinutes));
                }
            }
        }
    ];

    // ── IEditorExtension ────────────────────────────────────────────

    public string Name => "WritingToolkitEditor";
    public int Priority => 200;

    public void OnDocumentOpened(EditorDocumentContext context)
    {
        // Could highlight overused words, track editing time, etc.
        _wordFrequency.MarkDirty();
    }

    public void OnDocumentClosing(EditorDocumentContext context)
    {
        // Clean up any editor-specific state
    }

    // ── IAiHook ─────────────────────────────────────────────────────

    public string? OnBuildSystemPrompt(AiPromptContext context)
    {
        return "The user is using the Writing Toolkit extension which includes a Pomodoro timer and word frequency analysis. " +
               "If the user asks about productivity or writing stats, mention these tools are available.";
    }

    public string OnResponseChunk(string chunk) => chunk; // pass through

    // ── ISettingsContributor (page metadata; the form comes from the schema) ─

    public IReadOnlyList<SettingsPage> GetSettingsPages() =>
    [
        new SettingsPage
        {
            Category = _loc.T("settings.category"),
            IconPath = "M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"
        }
    ];

    // ── IWizardContributor ──────────────────────────────────────────

    public IReadOnlyList<Novalist.Sdk.Models.Wizards.WizardDefinition> GetWizards() =>
    [
        new Novalist.Sdk.Models.Wizards.WizardDefinition
        {
            Id = "com.novalist.writingtoolkit.pomodoro",
            DisplayName = _loc.T("wizard.pomodoro.title"),
            Description = _loc.T("wizard.pomodoro.description"),
            Scope = Novalist.Sdk.Models.Wizards.WizardScope.Reference,
            Steps =
            {
                new Novalist.Sdk.Models.Wizards.NumberStep
                {
                    Id = "duration",
                    Title = _loc.T("wizard.pomodoro.duration"),
                    Min = 5, Max = 90, DefaultValue = 25, Unit = "min",
                    Skippable = false,
                },
                new Novalist.Sdk.Models.Wizards.ChoiceStep
                {
                    Id = "autostart",
                    Title = _loc.T("wizard.pomodoro.autostart"),
                    Choices =
                    {
                        new Novalist.Sdk.Models.Wizards.WizardChoice { Value = "true", Label = _loc.T("wizard.pomodoro.yes") },
                        new Novalist.Sdk.Models.Wizards.WizardChoice { Value = "false", Label = _loc.T("wizard.pomodoro.no") },
                    },
                },
            },
        },
    ];

    // ── IExportFormatContributor ────────────────────────────────────

    public IReadOnlyList<ExportFormatDescriptor> GetExportFormats() =>
    [
        new ExportFormatDescriptor
        {
            FormatKey = "plaintext_clean",
            DisplayName = _loc.T("export.plainTextClean"),
            FileExtension = ".txt",
            IconPath = "M17 3a2.83 2.83 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5zM15 5l4 4",
            Export = async context =>
            {
                var sb = new StringBuilder();
                var chapters = _host.ProjectService.GetChaptersOrdered();
                foreach (var chapter in chapters)
                {
                    sb.AppendLine($"# {chapter.Title}");
                    sb.AppendLine();
                    var scenes = _host.ProjectService.GetScenesForChapter(chapter.Guid);
                    foreach (var scene in scenes)
                    {
                        var content = await _host.ProjectService.ReadSceneContentAsync(chapter.Guid, scene.Id);
                        sb.AppendLine(content);
                        sb.AppendLine();
                    }
                }
                await _host.FileService.WriteTextAsync(context.OutputPath, sb.ToString());
            }
        }
    ];

    // ── IThemeContributor ───────────────────────────────────────────

    public IReadOnlyList<ThemeOverride> GetThemeOverrides() =>
    [
        // A token map: the usual form. Every --nl-* left out keeps its default,
        // so a theme can restate the whole palette or just a corner of it.
        new ThemeOverride
        {
            Name = "Sepia",
            AccentColor = "#8a6d3b",
            Tokens = new Dictionary<string, string>
            {
                ["--nl-base"] = "244 236 216",
                ["--nl-surface-window"] = "#f4ecd8",
                ["--nl-surface-sidebar"] = "#ece0c8",
                ["--nl-surface-toolbar"] = "#ece0c8",
                ["--nl-surface-inspector"] = "#ece0c8",
                ["--nl-surface-editor"] = "#faf4e6",
                ["--nl-surface-card"] = "#ece0c8",
                ["--nl-surface-input"] = "#faf4e6",
                ["--nl-surface-overlay"] = "rgb(59 47 47 / 0.4)",
                ["--nl-surface-hover"] = "rgb(59 47 47 / 0.06)",
                ["--nl-surface-selected"] = "rgb(138 109 59 / 0.2)",
                ["--nl-text"] = "#3b2f2f",
                ["--nl-text-dim"] = "#6b5b4b",
                ["--nl-text-subtle"] = "#8a7a68",
                ["--nl-accent-hover"] = "#a8854a",
                ["--nl-accent-ink"] = "#faf4e6",
                ["--nl-focus-ring"] = "rgb(138 109 59 / 0.6)",
                ["--nl-border"] = "#d8c8a8",
                ["--nl-border-subtle"] = "#e4d8bd",
                ["--nl-border-firm"] = "#c4b08a",
                ["--nl-scrollbar-thumb"] = "rgb(59 47 47 / 0.2)",
                ["--nl-scrollbar-thumb-hover"] = "rgb(59 47 47 / 0.32)",
                ["--nl-scrollbar-thumb-active"] = "rgb(138 109 59 / 0.6)"
            }
        },
        // A stylesheet: for themes that need rules a token map cannot hold. The
        // path is relative to the extension folder.
        new ThemeOverride
        {
            Name = "Dark Ocean",
            AccentColor = "#1b6ca8",
            ResourcePath = "Themes/dark-ocean.css"
        }
    ];

    // ── IStatusBarContributor ───────────────────────────────────────

    public IReadOnlyList<StatusBarItem> GetStatusBarItems() =>
    [
        new StatusBarItem
        {
            Id = "writingToolkit.pomodoro",
            Alignment = "Right",
            Order = 50,
            GetText = () => _pomodoro.IsRunning
                ? $"{_loc.T("statusBar.pomodoroPrefix")} {_pomodoro.RemainingMinutes}:{_pomodoro.RemainingSeconds:D2}"
                : $"{_loc.T("statusBar.pomodoroPrefix")} --:--",
            GetTooltip = () => _pomodoro.IsRunning
                ? _loc.T("statusBar.pomodoroRunning", _pomodoro.SessionCount)
                : _loc.T("statusBar.pomodoroIdle"),
            OnClick = () =>
            {
                if (_pomodoro.IsRunning) _pomodoro.Stop();
                else _pomodoro.Start();
            },
            OnRefresh = () => { /* timer updates automatically */ }
        }
    ];

    // ── IContextMenuContributor ─────────────────────────────────────

    public IReadOnlyList<ContextMenuItem> GetContextMenuItems() =>
    [
        new ContextMenuItem
        {
            Label = _loc.T("contextMenu.analyzeWordFrequency"),
            Icon = string.Empty,
            Context = "Chapter",
            OnClick = _ =>
            {
                System.Diagnostics.Debug.WriteLine("[ExtCtxMenu] Example extension: Chapter OnClick fired");
                _host.ActivateContentView("ext.wordfreq");
            }
        },
        new ContextMenuItem
        {
            Label = _loc.T("contextMenu.analyzeWordFrequency"),
            Icon = string.Empty,
            Context = "Scene",
            // Only meaningful with a concrete scene in context (e.g. the editor's
            // current scene); hidden when there is none.
            IsVisible = ctx => ctx != null,
            OnClick = _ =>
            {
                System.Diagnostics.Debug.WriteLine("[ExtCtxMenu] Example extension: Scene OnClick fired");
                _host.ActivateContentView("ext.wordfreq");
            }
        }
    ];

    // ── IEntityTypeContributor ──────────────────────────────────────

    public IReadOnlyList<EntityTypeDescriptor> GetEntityTypes() =>
    [
        new EntityTypeDescriptor
        {
            TypeKey = "ext.writingtoolkit.faction",
            DisplayName = _loc.T("entityType.faction"),
            DisplayNamePlural = _loc.T("entityType.factions"),
            FolderName = "Factions",
            DefaultFields =
            [
                new EntityFieldDescriptor { Key = "leader", DisplayName = _loc.T("entityType.faction.leader"), TypeKey = "EntityRef", EnumOptions = ["Character"] },
                new EntityFieldDescriptor { Key = "type", DisplayName = _loc.T("entityType.faction.type"), TypeKey = "Enum", EnumOptions = ["Government", "Military", "Religious", "Criminal", "Guild", "Rebellion", "Other"] },
                new EntityFieldDescriptor { Key = "motto", DisplayName = _loc.T("entityType.faction.motto"), TypeKey = "String" },
                new EntityFieldDescriptor { Key = "founded", DisplayName = _loc.T("entityType.faction.founded"), TypeKey = "Date" },
                new EntityFieldDescriptor { Key = "memberCount", DisplayName = _loc.T("entityType.faction.memberCount"), TypeKey = "Int" }
            ],
            Features = new EntityTypeFeatures
            {
                IncludeImages = true,
                IncludeRelationships = true,
                IncludeSections = true
            }
        }
    ];

    // ── IGrammarCheckContributor ────────────────────────────────────

    public string GrammarCheckName => "Writing Toolkit Style Check";

    public bool IsGrammarCheckEnabled => true;

    public Task<GrammarCheckResult> CheckAsync(string plainText, string language, CancellationToken cancellationToken = default)
    {
        // Example: flags the cliché "very unique". A real contributor would do
        // more; this keeps the sample dependency-free and deterministic.
        var issues = new List<GrammarIssue>();
        var idx = plainText.IndexOf("very unique", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            issues.Add(new GrammarIssue
            {
                Offset = idx,
                Length = "very unique".Length,
                Message = _loc.T("grammar.veryUnique"),
                Type = GrammarIssueType.Style,
                Replacements = ["unique"]
            });
        }
        return Task.FromResult(new GrammarCheckResult { Issues = issues });
    }

    // ── IArticleGeneratorContributor ────────────────────────────────

    public string ArticleGeneratorName => "Writing Toolkit Article Generator";

    public bool IsArticleGeneratorEnabled => true;

    public Task<ArticleGenerationResult> GenerateAsync(
        ArticleGenerationRequest request, CancellationToken cancellationToken = default)
    {
        // Deterministic stand-in for a real model. An entity named "GenFail"
        // exercises the error path; everything else returns a one-line summary.
        if (string.Equals(request.EntityName, "GenFail", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new ArticleGenerationResult { Error = "no model configured" });
        return Task.FromResult(new ArticleGenerationResult
        {
            Summary = $"{request.EntityName} is a notable {request.TypeKey} in this story."
        });
    }

    // ── IEntityExtractionContributor ────────────────

    public string EntityExtractorName => "Writing Toolkit Entity Extractor";

    public bool IsEntityExtractorEnabled => true;

    public Task<EntityExtractionResult> ExtractAsync(
        EntityExtractionRequest request, CancellationToken cancellationToken = default)
    {
        // Deterministic stand-in for a real model: prose containing "ExtractFail"
        // exercises the error path. Otherwise every capitalised word the project
        // does not already know is proposed as a character — crude, but enough to
        // drive the host's review flow end to end.
        if (request.Context.Contains("ExtractFail", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new EntityExtractionResult { Error = "no model configured" });

        var known = new HashSet<string>(request.KnownNames, StringComparer.OrdinalIgnoreCase);
        var proposals = new List<EntityProposal>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in SplitWords(request.Context))
        {
            if (word.Length < 2 || !char.IsUpper(word[0])) continue;
            if (known.Contains(word) || !seen.Add(word)) continue;
            proposals.Add(new EntityProposal
            {
                TypeKey = "character",
                Name = word,
                Detail = "Mentioned in this scene."
            });
        }
        return Task.FromResult(new EntityExtractionResult { Proposals = proposals });
    }

    /// <summary>Letter runs only, so no escaped separator literals are needed.</summary>
    private static IEnumerable<string> SplitWords(string text)
    {
        var buffer = new System.Text.StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetter(ch)) { buffer.Append(ch); continue; }
            if (buffer.Length > 0) { yield return buffer.ToString(); buffer.Clear(); }
        }
        if (buffer.Length > 0) yield return buffer.ToString();
    }

    // ── IHotkeyContributor ──────────────────────────────────────────

    public IReadOnlyList<HotkeyDescriptor> GetHotkeyBindings() =>
    [
        new HotkeyDescriptor
        {
            ActionId = "ext.writingtoolkit.wordfreq",
            DisplayName = _loc.T("hotkey.wordFreq"),
            Category = _loc.T("group.writingToolkit"),
            DefaultGesture = "Ctrl+Shift+W",
            OnExecute = () => _host.ActivateContentView("ext.wordfreq")
        }
    ];

    // ── IPropertyTypeContributor ────────────────────────────────────

    public IReadOnlyList<PropertyTypeDescriptor> GetPropertyTypes() =>
    [
        new PropertyTypeDescriptor
        {
            TypeKey = "ext.writingtoolkit.wordcount",
            DisplayName = _loc.T("propertyType.wordCount"),
            DefaultValue = "0"
        }
    ];

    // ── IInlineActionContributor ────────────────────────────────────

    public IReadOnlyList<InlineActionDescriptor> GetInlineActions() =>
    [
        new InlineActionDescriptor
        {
            Id = "ext.writingtoolkit.uppercase",
            Label = _loc.T("inline.uppercase"),
            Group = _loc.T("group.writingToolkit"),
            Priority = 10
        },
        new InlineActionDescriptor
        {
            Id = "ext.writingtoolkit.wordcount",
            Label = _loc.T("inline.wordCount"),
            Group = _loc.T("group.writingToolkit"),
            Priority = 20
        }
    ];

    public Task<InlineActionResult> ExecuteAsync(string actionId, InlineActionRequest request, CancellationToken cancellationToken)
    {
        var text = request.SelectedText ?? string.Empty;
        return actionId switch
        {
            "ext.writingtoolkit.uppercase" => Task.FromResult(new InlineActionResult
            {
                Text = text.ToUpperInvariant(),
                Disposition = InlineActionDisposition.ReplaceSelection
            }),
            "ext.writingtoolkit.wordcount" => Task.FromResult(new InlineActionResult
            {
                Text = _loc.T("inline.wordCountResult",
                    text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length),
                Disposition = InlineActionDisposition.InsertAfterSelection
            }),
            _ => Task.FromResult(new InlineActionResult { Error = _loc.T("inline.unknownAction") })
        };
    }

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
