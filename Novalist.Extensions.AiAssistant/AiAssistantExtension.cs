using System.Text.Json;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Extensions.AiAssistant.ViewModels;
using Novalist.Extensions.AiAssistant.Views;
using Novalist.Sdk;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;
using Novalist.Sdk.Services;

namespace Novalist.Extensions.AiAssistant;

public sealed class AiAssistantExtension : IExtension, IRibbonContributor, ISidebarContributor, IContentViewContributor, ISettingsContributor
{
    public string Id => "com.novalist.ai";
    public string DisplayName => "AI Assistant";
    public string Description => "AI-powered chat, story analysis, and scene statistics.";
    public string Version => "1.0.0";
    public string Author => "Novalist Team";

    private IHostServices _host = null!;
    private IExtensionLocalization _loc = null!;
    internal AiService AiService { get; } = new();
    internal AiSettings Settings { get; private set; } = new();

    private AiChatViewModel? _chatVm;
    private StoryAnalysisViewModel? _analysisVm;
    private AiSettingsViewModel? _settingsVm;
    private bool _isChatVisible;
    private bool _isAnalysisVisible;

    // Icon paths (Lucide)
    private const string IconMessageSquare = "M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z";
    private const string IconSearch = "M11 17.25a6.25 6.25 0 1 1 0-12.5 6.25 6.25 0 0 1 0 12.5zm0 0L16.65 22.9";

    public void Initialize(IHostServices host)
    {
        _host = host;
        _loc = host.GetLocalization(Id);

        LoadSettings();
        ConfigureAiService();

        host.LanguageChanged += OnLanguageChanged;
    }

    public void Shutdown()
    {
        _host.LanguageChanged -= OnLanguageChanged;
        _chatVm = null;
        _analysisVm = null;
        _settingsVm = null;
    }

    // ── Settings persistence ────────────────────────────────────────

    private void LoadSettings()
    {
        // Read from host settings (AppSettings.Ai) for backwards compatibility
        var json = _host.ReadHostData("ai");
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                Settings = JsonSerializer.Deserialize<AiSettings>(json) ?? new AiSettings();
            }
            catch
            {
                Settings = new AiSettings();
            }
        }
    }

    internal void SaveSettings()
    {
        var json = JsonSerializer.Serialize(Settings);
        _ = _host.WriteHostDataAsync("ai", json);
        ConfigureAiService();
    }

    internal void ConfigureAiService()
    {
        AiService.Configure(Settings);
        var aiLangOverride = Settings.ResponseLanguage;
        AiService.LanguageName = !string.IsNullOrWhiteSpace(aiLangOverride)
            ? aiLangOverride
            : _host.CurrentLanguageDisplayName;
    }

    private void OnLanguageChanged(string lang)
    {
        if (string.IsNullOrWhiteSpace(Settings.ResponseLanguage))
            AiService.LanguageName = _host.CurrentLanguageDisplayName;
    }

    // ── IRibbonContributor ──────────────────────────────────────────

    public IReadOnlyList<RibbonItem> GetRibbonItems()
    {
        return
        [
            new RibbonItem
            {
                Tab = "View",
                Group = _loc.T("ribbon.aiGroup"),
                Label = _loc.T("ribbon.aiChat"),
                IconPath = IconMessageSquare,
                Tooltip = _loc.T("ribbon.aiChatTooltip"),
                IsToggle = true,
                IsActive = () => _isChatVisible,
                OnClick = ToggleAiChat,
                Size = "Large",
            },
            new RibbonItem
            {
                Tab = "View",
                Group = _loc.T("ribbon.aiGroup"),
                Label = _loc.T("ribbon.storyAnalysis"),
                IconPath = IconSearch,
                Tooltip = _loc.T("ribbon.storyAnalysisTooltip"),
                IsToggle = true,
                IsActive = () => _isAnalysisVisible,
                OnClick = ToggleStoryAnalysis,
                Size = "Large",
            }
        ];
    }

    private void ToggleAiChat()
    {
        _isChatVisible = !_isChatVisible;
        _host.ToggleRightSidebar("com.novalist.ai.chat");
    }

    private void ToggleStoryAnalysis()
    {
        _isAnalysisVisible = !_isAnalysisVisible;
        if (_isAnalysisVisible)
            _host.ActivateContentView("com.novalist.ai.analysis");
        else
            _host.ActivateContentView("");
    }

    // ── ISidebarContributor (right sidebar for AI Chat) ─────────────

    public IReadOnlyList<SidebarPanel> GetSidebarPanels()
    {
        return
        [
            new SidebarPanel
            {
                Id = "com.novalist.ai.chat",
                Label = _loc.T("ribbon.aiChat"),
                IconPath = IconMessageSquare,
                Side = "Right",
                Tooltip = _loc.T("ribbon.aiChatTooltip"),
                CreateView = () =>
                {
                    _chatVm ??= new AiChatViewModel(_host, this);
                    return new AiChatView { DataContext = _chatVm };
                }
            }
        ];
    }

    // ── IContentViewContributor (Story Analysis as full content view) ─

    public IReadOnlyList<ContentViewDescriptor> GetContentViews()
    {
        return
        [
            new ContentViewDescriptor
            {
                ViewKey = "com.novalist.ai.analysis",
                DisplayName = _loc.T("ribbon.storyAnalysis"),
                IconPath = IconSearch,
                CreateView = () =>
                {
                    _analysisVm ??= new StoryAnalysisViewModel(_host, this);
                    return new StoryAnalysisView { DataContext = _analysisVm };
                },
                OnActivated = () =>
                {
                    _isAnalysisVisible = true;
                    _analysisVm?.RefreshChapters();
                },
                OnDeactivated = () => _isAnalysisVisible = false,
            }
        ];
    }

    // ── ISettingsContributor ────────────────────────────────────────

    public IReadOnlyList<SettingsPage> GetSettingsPages()
    {
        return
        [
            new SettingsPage
            {
                Category = _loc.T("settings.ai"),
                IconPath = IconMessageSquare,
                CreateView = () =>
                {
                    _settingsVm ??= new AiSettingsViewModel(this, _loc);
                    return new AiSettingsView { DataContext = _settingsVm };
                },
                OnSave = () => SaveSettings(),
            }
        ];
    }
}
