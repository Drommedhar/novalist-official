using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Sdk.Services;

namespace Novalist.Sdk.Example.ViewModels;

public partial class WritingPromptsViewModel : ObservableObject
{
    private readonly WritingPromptService _service;

    public IExtensionLocalization Loc { get; }

    [ObservableProperty]
    private string _currentPrompt = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _history = [];

    public WritingPromptsViewModel(WritingPromptService service, IExtensionLocalization loc)
    {
        _service = service;
        Loc = loc;
        _currentPrompt = loc.T("writingPromptsView.defaultPrompt");
        foreach (var h in service.History)
            History.Add(h);

        _service.PromptAdded += OnExternalPromptAdded;
    }

    private void OnExternalPromptAdded(string prompt)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // Avoid duplicates if GeneratePrompt already added it
            if (History.Count > 0 && History[0] == prompt)
                return;

            CurrentPrompt = prompt;
            History.Insert(0, prompt);
            if (History.Count > 20)
                History.RemoveAt(History.Count - 1);
        });
    }

    [RelayCommand]
    private void GeneratePrompt()
    {
        var prompt = _service.GetRandomPrompt();
        CurrentPrompt = prompt;
        _service.AddToHistory(prompt);
        History.Insert(0, prompt);
        if (History.Count > 20)
            History.RemoveAt(History.Count - 1);
    }
}
