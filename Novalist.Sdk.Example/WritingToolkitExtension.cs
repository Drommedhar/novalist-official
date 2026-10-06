using System.Text;
using Novalist.Sdk;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;
using Novalist.Sdk.Models.Narration;
using Novalist.Sdk.Services;

namespace Novalist.Sdk.Example;

/// <summary>
/// Example extension demonstrating all hook interfaces.
/// Provides: Pomodoro timer, word frequency analysis, writing prompts,
/// custom themes, and AI/editor/export hooks.
/// </summary>
public sealed partial class WritingToolkitExtension :
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
    IVoiceEngineContributor,
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

    // aislop-ignore-next-line ai-slop/csharp-null-forgiving -- The extension host calls Initialize before exposing contributions that use host services.
    private IHostServices _host = null!;
    // aislop-ignore-next-line ai-slop/csharp-null-forgiving -- Initialize assigns localization before any contribution callback is exposed.
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
                // aislop-ignore-next-line ai-slop/csharp-console-leftover -- Literal output demonstrates chapter callback delivery to extension developers.
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
                // aislop-ignore-next-line ai-slop/csharp-console-leftover -- Literal output demonstrates scene callback delivery to extension developers.
                System.Diagnostics.Debug.WriteLine("[ExtCtxMenu] Example extension: Scene OnClick fired");
                _host.ActivateContentView("ext.wordfreq");
            }
        }
    ];

    // ── IGrammarCheckContributor ────────────────────────────────────

    public string GrammarCheckName => "Writing Toolkit Style Check";

    public bool IsGrammarCheckEnabled => true;

    // ── IArticleGeneratorContributor ────────────────────────────────

    public string ArticleGeneratorName => "Writing Toolkit Article Generator";

    public bool IsArticleGeneratorEnabled => true;

    // ── IVoiceEngineContributor ─────────────────────────────────────

    // Delegated whole, because a voice engine is a large enough thing to be its
    // own class - and because an extension author reading this should see the
    // seam, not a hundred lines of tone generation mixed into everything else.
    private readonly ExampleVoiceEngine _voice = new();

    public string EngineId => _voice.EngineId;
    public string EngineName => _voice.EngineName;
    public VoiceEngineFeatures Features => _voice.Features;

    // ── IEntityExtractionContributor ────────────────

    public string EntityExtractorName => "Writing Toolkit Entity Extractor";

    public bool IsEntityExtractorEnabled => true;

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
}
