using System.Text;
using Avalonia.Controls;
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
    ISidebarContributor,
    IEditorExtension,
    IAiHook,
    ISettingsContributor,
    IExportFormatContributor,
    IThemeContributor,
    IStatusBarContributor,
    IContextMenuContributor,
    IContentViewContributor
{
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
    }

    public void Shutdown()
    {
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
            Icon = "📊",
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
            Icon = "🎲",
            IconPath = "M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16zM3.27 6.96 12 12.01l8.73-5.05M12 22.08V12",
            Tooltip = _loc.T("ribbon.prompt.tooltip"),
            Size = "Large",
            OnClick = () =>
            {
                var prompt = _prompts.GetRandomPrompt();
                _prompts.AddToHistory(prompt);
                _host.ShowNotification($"🎲 {prompt}");
            }
        },
        new RibbonItem
        {
            Tab = "Extensions",
            Group = _loc.T("group.writingToolkit"),
            Label = _loc.T("ribbon.pomodoro.label"),
            Icon = "⏱",
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
                    _host.ShowNotification($"⏱ {_loc.T("notifications.pomodoroStopped")}");
                }
                else
                {
                    _pomodoro.Start();
                    _host.ShowNotification($"⏱ {_loc.T("notifications.pomodoroStarted", _pomodoro.DurationMinutes)}");
                }
            }
        }
    ];

    // ── ISidebarContributor ─────────────────────────────────────────

    public IReadOnlyList<SidebarPanel> GetSidebarPanels() =>
    [
        new SidebarPanel
        {
            Id = "writingToolkit.prompts",
            Label = _loc.T("sidebar.writingPrompts.label"),
            Icon = "🎲",
            IconPath = "M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16zM3.27 6.96 12 12.01l8.73-5.05M12 22.08V12",
            Side = "Right",
            Tooltip = _loc.T("sidebar.writingPrompts.tooltip"),
            CreateView = () => new Views.WritingPromptsView
            {
                DataContext = new ViewModels.WritingPromptsViewModel(_prompts, _loc)
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

    // ── ISettingsContributor ────────────────────────────────────────

    public IReadOnlyList<SettingsPage> GetSettingsPages() =>
    [
        new SettingsPage
        {
            Category = _loc.T("settings.category"),
            Icon = "🧰",
            IconPath = "M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z",
            CreateView = () => new Views.ToolkitSettingsView
            {
                DataContext = new ViewModels.ToolkitSettingsViewModel(_pomodoro, _prompts, _host, _loc)
            },
            OnSave = () =>
            {
                // Settings would be persisted via IHostServices.GetExtensionSettingsPath
            }
        }
    ];

    // ── IExportFormatContributor ────────────────────────────────────

    public IReadOnlyList<ExportFormatDescriptor> GetExportFormats() =>
    [
        new ExportFormatDescriptor
        {
            FormatKey = "plaintext_clean",
            DisplayName = _loc.T("export.plainTextClean"),
            FileExtension = ".txt",
            Icon = "📝",
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
        new ThemeOverride
        {
            Name = "Sepia",
            ResourcePath = "Themes/SepiaTheme.axaml"
        },
        new ThemeOverride
        {
            Name = "Dark Ocean",
            ResourcePath = "Themes/DarkOceanTheme.axaml"
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
                ? $"⏱ {_pomodoro.RemainingMinutes}:{_pomodoro.RemainingSeconds:D2}"
                : "⏱ --:--",
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
            Icon = "📊",
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
            Icon = "📊",
            Context = "Scene",
            OnClick = _ =>
            {
                System.Diagnostics.Debug.WriteLine("[ExtCtxMenu] Example extension: Scene OnClick fired");
                _host.ActivateContentView("ext.wordfreq");
            }
        }
    ];

    // ── IContentViewContributor ─────────────────────────────────────

    public IReadOnlyList<ContentViewDescriptor> GetContentViews() =>
    [
        new ContentViewDescriptor
        {
            ViewKey = "ext.wordfreq",
            DisplayName = _loc.T("contentView.wordFrequency"),
            Icon = "📊",
            IconPath = "M18 20V10M12 20V4M6 20v-4",
            CreateView = () => new Views.WordFrequencyView
            {
                DataContext = new ViewModels.WordFrequencyViewModel(_wordFrequency, _host, _loc)
            },
            OnActivated = () => { },
            OnDeactivated = () => { }
        }
    ];
}
