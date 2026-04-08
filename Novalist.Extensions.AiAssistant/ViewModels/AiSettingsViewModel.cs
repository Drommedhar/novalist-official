using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Sdk.Models;
using Novalist.Sdk.Services;

namespace Novalist.Extensions.AiAssistant.ViewModels;

public partial class AiSettingsViewModel : ObservableObject
{
    private readonly AiAssistantExtension _extension;
    private readonly IExtensionLocalization _loc;
    private AiSettings Settings => _extension.Settings;
    private bool _isLoading;

    [ObservableProperty] private bool _aiEnabled;
    [ObservableProperty] private string _aiProvider = "lmstudio";
    [ObservableProperty] private string _aiBaseUrl = string.Empty;
    [ObservableProperty] private string _aiModel = string.Empty;
    [ObservableProperty] private string _aiApiToken = string.Empty;
    [ObservableProperty] private string _aiCopilotPath = "copilot";
    [ObservableProperty] private string _aiCopilotModel = string.Empty;
    [ObservableProperty] private double _aiTemperature;
    [ObservableProperty] private int _aiContextLength;
    [ObservableProperty] private double _aiTopP;
    [ObservableProperty] private double _aiMinP;
    [ObservableProperty] private double _aiFrequencyPenalty;
    [ObservableProperty] private int _aiRepeatLastN;
    [ObservableProperty] private bool _aiCheckReferences;
    [ObservableProperty] private bool _aiCheckInconsistencies;
    [ObservableProperty] private bool _aiCheckSuggestions;
    [ObservableProperty] private bool _aiCheckSceneStats;
    [ObservableProperty] private bool _aiDisableRegexReferences;
    [ObservableProperty] private string _aiResponseLanguage = string.Empty;
    [ObservableProperty] private string _aiSystemPrompt = string.Empty;
    [ObservableProperty] private ObservableCollection<AiModelListItem> _availableAiModels = [];
    [ObservableProperty] private bool _isLoadingModels;
    [ObservableProperty] private string _aiServerStatus = string.Empty;

    public bool IsLmStudioProvider => AiProvider == "lmstudio";
    public bool IsCopilotProvider => AiProvider == "copilot";

    public List<AiProviderItem> AvailableAiProviders { get; } =
    [
        new("lmstudio", "LM Studio"),
        new("copilot", "GitHub Copilot CLI"),
    ];

    public IExtensionLocalization Loc => _loc;

    public AiSettingsViewModel(AiAssistantExtension extension, IExtensionLocalization loc)
    {
        _extension = extension;
        _loc = loc;
        LoadSettings();
    }

    private void LoadSettings()
    {
        _isLoading = true;
        var ai = Settings;
        AiEnabled = ai.Enabled;
        AiProvider = ai.Provider;
        AiBaseUrl = ai.LmStudioBaseUrl;
        AiModel = ai.LmStudioModel;
        AiApiToken = ai.LmStudioApiToken;
        AiCopilotPath = ai.CopilotPath;
        AiCopilotModel = ai.CopilotModel;
        AiTemperature = ai.Temperature;
        AiContextLength = ai.ContextLength;
        AiTopP = ai.TopP;
        AiMinP = ai.MinP;
        AiFrequencyPenalty = ai.FrequencyPenalty;
        AiRepeatLastN = ai.RepeatLastN;
        AiCheckReferences = ai.CheckReferences;
        AiCheckInconsistencies = ai.CheckInconsistencies;
        AiCheckSuggestions = ai.CheckSuggestions;
        AiCheckSceneStats = ai.CheckSceneStats;
        AiDisableRegexReferences = ai.DisableRegexReferences;
        AiResponseLanguage = ai.ResponseLanguage;
        AiSystemPrompt = ai.SystemPrompt;
        _isLoading = false;
    }

    private void SaveAndNotify()
    {
        if (_isLoading) return;
        _extension.SaveSettings();
    }

    partial void OnAiEnabledChanged(bool value) { Settings.Enabled = value; SaveAndNotify(); }
    partial void OnAiProviderChanged(string value) { Settings.Provider = value; OnPropertyChanged(nameof(IsLmStudioProvider)); OnPropertyChanged(nameof(IsCopilotProvider)); SaveAndNotify(); }
    partial void OnAiBaseUrlChanged(string value) { Settings.LmStudioBaseUrl = value; SaveAndNotify(); }
    partial void OnAiModelChanged(string value) { Settings.LmStudioModel = value; SaveAndNotify(); }
    partial void OnAiApiTokenChanged(string value) { Settings.LmStudioApiToken = value; SaveAndNotify(); }
    partial void OnAiCopilotPathChanged(string value) { Settings.CopilotPath = value; SaveAndNotify(); }
    partial void OnAiCopilotModelChanged(string value) { Settings.CopilotModel = value; SaveAndNotify(); }
    partial void OnAiTemperatureChanged(double value) { Settings.Temperature = Math.Clamp(value, 0, 2); SaveAndNotify(); }
    partial void OnAiContextLengthChanged(int value) { Settings.ContextLength = Math.Max(0, value); SaveAndNotify(); }
    partial void OnAiTopPChanged(double value) { Settings.TopP = Math.Clamp(value, 0, 1); SaveAndNotify(); }
    partial void OnAiMinPChanged(double value) { Settings.MinP = Math.Clamp(value, 0, 1); SaveAndNotify(); }
    partial void OnAiFrequencyPenaltyChanged(double value) { Settings.FrequencyPenalty = Math.Clamp(value, 0, 2); SaveAndNotify(); }
    partial void OnAiRepeatLastNChanged(int value) { Settings.RepeatLastN = Math.Max(0, value); SaveAndNotify(); }
    partial void OnAiCheckReferencesChanged(bool value) { Settings.CheckReferences = value; SaveAndNotify(); }
    partial void OnAiCheckInconsistenciesChanged(bool value) { Settings.CheckInconsistencies = value; SaveAndNotify(); }
    partial void OnAiCheckSuggestionsChanged(bool value) { Settings.CheckSuggestions = value; SaveAndNotify(); }
    partial void OnAiCheckSceneStatsChanged(bool value) { Settings.CheckSceneStats = value; SaveAndNotify(); }
    partial void OnAiDisableRegexReferencesChanged(bool value) { Settings.DisableRegexReferences = value; SaveAndNotify(); }
    partial void OnAiResponseLanguageChanged(string value) { Settings.ResponseLanguage = value; SaveAndNotify(); }
    partial void OnAiSystemPromptChanged(string value) { Settings.SystemPrompt = value; SaveAndNotify(); }

    [RelayCommand]
    private async Task TestAiConnectionAsync()
    {
        AiServerStatus = _loc.T("settings.aiTesting");
        try
        {
            _extension.ConfigureAiService();
            var running = await _extension.AiService.IsServerRunningAsync();
            AiServerStatus = running ? _loc.T("settings.aiConnected") : _loc.T("settings.aiNotReachable");
        }
        catch
        {
            AiServerStatus = _loc.T("settings.aiNotReachable");
        }
    }

    [RelayCommand]
    private async Task RefreshAiModelsAsync()
    {
        IsLoadingModels = true;
        try
        {
            _extension.ConfigureAiService();
            var models = await _extension.AiService.ListModelsAsync();
            AvailableAiModels = new ObservableCollection<AiModelListItem>(
                models.Select(m => new AiModelListItem(m.Key, m.DisplayName, m.SizeBytes)));
        }
        catch
        {
            AvailableAiModels = [];
        }
        finally
        {
            IsLoadingModels = false;
        }
    }

    [RelayCommand]
    private void ResetAiSystemPrompt()
    {
        AiSystemPrompt = string.Empty;
    }

    [RelayCommand]
    private void SelectAiModel(string? modelKey)
    {
        if (string.IsNullOrEmpty(modelKey)) return;
        if (IsCopilotProvider)
            AiCopilotModel = modelKey;
        else
            AiModel = modelKey;
    }

    [RelayCommand]
    private void SetAiProvider(string? provider)
    {
        if (!string.IsNullOrEmpty(provider))
        {
            AiProvider = provider;
            AvailableAiModels = [];
            AiServerStatus = string.Empty;
        }
    }
}

public sealed class AiModelListItem(string key, string displayName, long sizeBytes)
{
    public string Key { get; } = key;
    public string DisplayName { get; } = displayName;
    public long SizeBytes { get; } = sizeBytes;
    public string SizeDisplay => SizeBytes > 0 ? $"{SizeBytes / (1024.0 * 1024 * 1024):F1} GB" : "";

    public override string ToString() => string.IsNullOrEmpty(SizeDisplay) ? DisplayName : $"{DisplayName} ({SizeDisplay})";
}

public sealed class AiProviderItem(string key, string displayName)
{
    public string Key { get; } = key;
    public string DisplayName { get; } = displayName;

    public override string ToString() => DisplayName;
}
