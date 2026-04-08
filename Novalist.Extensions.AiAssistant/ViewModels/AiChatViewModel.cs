using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Sdk.Models;
using Novalist.Sdk.Services;

namespace Novalist.Extensions.AiAssistant.ViewModels;

public partial class AiChatViewModel : ObservableObject
{
    private readonly IHostServices _host;
    private readonly AiAssistantExtension _extension;
    private readonly IExtensionLocalization _loc;

    [ObservableProperty]
    private string _userInput = string.Empty;

    [ObservableProperty]
    private bool _isGenerating;

    [ObservableProperty]
    private string _streamingResponse = string.Empty;

    [ObservableProperty]
    private string _streamingThinking = string.Empty;

    [ObservableProperty]
    private bool _hasThinking;

    [ObservableProperty]
    private bool _isThinkingExpanded;

    public ObservableCollection<AiChatMessageItem> Messages { get; } = [];

    private readonly List<AiChatMessage> _conversationHistory = [];
    private CancellationTokenSource? _cts;

    public AiChatViewModel(IHostServices host, AiAssistantExtension extension)
    {
        _host = host;
        _extension = extension;
        _loc = host.GetLocalization(extension.Id);
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = UserInput.Trim();
        if (string.IsNullOrEmpty(text)) return;

        UserInput = string.Empty;
        Messages.Add(new AiChatMessageItem("user", text));

        // Build system prompt with entity context on first message
        if (_conversationHistory.Count == 0)
        {
            var systemPrompt = await BuildSystemPromptAsync();
            if (!string.IsNullOrEmpty(systemPrompt))
                _conversationHistory.Add(new AiChatMessage { Role = "system", Content = systemPrompt });
        }

        _conversationHistory.Add(new AiChatMessage { Role = "user", Content = text });

        IsGenerating = true;
        StreamingResponse = string.Empty;
        StreamingThinking = string.Empty;
        HasThinking = false;
        IsThinkingExpanded = false;
        _cts = new CancellationTokenSource();

        try
        {
            var aiHooks = _host.GetAiHooks();
            var result = await _extension.AiService.GenerateChatAsync(
                _conversationHistory,
                chunk =>
                {
                    foreach (var hook in aiHooks)
                    {
                        try { chunk = hook.OnResponseChunk(chunk); } catch { /* swallow */ }
                    }
                    _host.PostToUI(() => StreamingResponse += chunk);
                },
                null,
                chunk => _host.PostToUI(() =>
                {
                    StreamingThinking += chunk;
                    HasThinking = true;
                }),
                _cts.Token);

            _conversationHistory.Add(new AiChatMessage { Role = "assistant", Content = result.Response });

            Messages.Add(new AiChatMessageItem("assistant", result.Response, result.Thinking));
            StreamingResponse = string.Empty;
            StreamingThinking = string.Empty;
            HasThinking = false;
        }
        catch (OperationCanceledException)
        {
            if (!string.IsNullOrEmpty(StreamingResponse))
            {
                Messages.Add(new AiChatMessageItem("assistant", StreamingResponse + " [cancelled]", StreamingThinking));
                _conversationHistory.Add(new AiChatMessage { Role = "assistant", Content = StreamingResponse });
            }
            StreamingResponse = string.Empty;
            StreamingThinking = string.Empty;
        }
        catch (Exception ex)
        {
            Messages.Add(new AiChatMessageItem("error", ex.Message));
        }
        finally
        {
            IsGenerating = false;
            _cts = null;
        }
    }

    private bool CanSend() => !IsGenerating && !string.IsNullOrWhiteSpace(UserInput);

    partial void OnUserInputChanged(string value) => SendCommand.NotifyCanExecuteChanged();
    partial void OnIsGeneratingChanged(bool value) => SendCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        _extension.AiService.Cancel();
    }

    [RelayCommand]
    private async Task ClearChatAsync()
    {
        Messages.Clear();
        _conversationHistory.Clear();
        StreamingResponse = string.Empty;
        StreamingThinking = string.Empty;
        HasThinking = false;
        await _extension.AiService.ResetChatSessionAsync();
    }

    private async Task<string> BuildSystemPromptAsync()
    {
        var settings = _extension.Settings;
        var template = !string.IsNullOrWhiteSpace(settings.SystemPrompt)
            ? settings.SystemPrompt
            : AiSettings.DefaultSystemPrompt;

        var langName = _host.CurrentLanguageDisplayName;
        var prompt = template.Replace("{{LANGUAGE}}", langName);

        // Collect entity summaries
        var entities = await CollectEntitySummariesAsync();
        if (entities.Count > 0)
        {
            var entityBlock = new StringBuilder("\n\nKnown project entities:\n");
            foreach (var e in entities)
                entityBlock.AppendLine($"- [{e.Type}] {e.Name}: {e.Details}");
            prompt += entityBlock.ToString();
        }

        // Extension AI hooks
        var aiHooks = _host.GetAiHooks();
        if (aiHooks.Count > 0)
        {
            var characters = entities.Where(e => e.Type == "character").Select(e => e.Name).ToList();
            var locations = entities.Where(e => e.Type == "location").Select(e => e.Name).ToList();
            var aiContext = new AiPromptContext
            {
                CharacterNames = characters,
                LocationNames = locations,
                Language = _host.CurrentLanguage
            };

            foreach (var hook in aiHooks)
            {
                try
                {
                    var addition = hook.OnBuildSystemPrompt(aiContext);
                    if (!string.IsNullOrWhiteSpace(addition))
                        prompt += "\n\n" + addition;
                }
                catch { /* swallow — never let an extension break AI */ }
            }
        }

        return prompt;
    }

    internal async Task<List<EntitySummary>> CollectEntitySummariesAsync()
    {
        var summaries = new List<EntitySummary>();
        try
        {
            var characters = await _host.EntityService.LoadCharactersAsync();
            foreach (var c in characters)
            {
                var details = new StringBuilder();
                if (!string.IsNullOrEmpty(c.Role)) details.Append($"Role: {c.Role}. ");
                summaries.Add(new EntitySummary { Name = c.DisplayName, Type = "character", Details = details.ToString().TrimEnd() });
            }

            var locations = await _host.EntityService.LoadLocationsAsync();
            foreach (var l in locations)
            {
                var details = new StringBuilder();
                if (!string.IsNullOrEmpty(l.Type)) details.Append($"Type: {l.Type}. ");
                summaries.Add(new EntitySummary { Name = l.Name, Type = "location", Details = details.ToString().TrimEnd() });
            }

            var items = await _host.EntityService.LoadItemsAsync();
            foreach (var it in items)
            {
                var details = new StringBuilder();
                if (!string.IsNullOrEmpty(it.Type)) details.Append($"Type: {it.Type}. ");
                summaries.Add(new EntitySummary { Name = it.Name, Type = "item", Details = details.ToString().TrimEnd() });
            }

            var lore = await _host.EntityService.LoadLoreAsync();
            foreach (var lr in lore)
            {
                var details = new StringBuilder();
                if (!string.IsNullOrEmpty(lr.Category)) details.Append($"Category: {lr.Category}. ");
                summaries.Add(new EntitySummary { Name = lr.Name, Type = "lore", Details = details.ToString().TrimEnd() });
            }
        }
        catch
        {
            // Best-effort entity collection
        }
        return summaries;
    }
}

public sealed class AiChatMessageItem
{
    public AiChatMessageItem(string role, string content, string? thinking = null)
    {
        Role = role;
        Content = content;
        Thinking = thinking ?? string.Empty;
        HasThinking = !string.IsNullOrWhiteSpace(thinking);
        IsUser = string.Equals(role, "user", StringComparison.Ordinal);
        IsAssistant = string.Equals(role, "assistant", StringComparison.Ordinal);
        IsError = string.Equals(role, "error", StringComparison.Ordinal);
    }

    public string Role { get; }
    public string Content { get; }
    public string Thinking { get; }
    public bool HasThinking { get; }
    public bool IsUser { get; }
    public bool IsAssistant { get; }
    public bool IsError { get; }
}
