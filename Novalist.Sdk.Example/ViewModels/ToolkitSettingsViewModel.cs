using CommunityToolkit.Mvvm.ComponentModel;
using Novalist.Sdk.Services;

namespace Novalist.Sdk.Example.ViewModels;

public partial class ToolkitSettingsViewModel : ObservableObject
{
    private readonly PomodoroService _pomodoro;
    private readonly WritingPromptService _prompts;
    private readonly IHostServices _host;

    public IExtensionLocalization Loc { get; }

    [ObservableProperty]
    private decimal _pomodoroDuration;

    public ToolkitSettingsViewModel(PomodoroService pomodoro, WritingPromptService prompts, IHostServices host, IExtensionLocalization loc)
    {
        _pomodoro = pomodoro;
        _prompts = prompts;
        _host = host;
        Loc = loc;
        _pomodoroDuration = pomodoro.DurationMinutes;
    }

    partial void OnPomodoroDurationChanged(decimal value)
    {
        var intVal = (int)value;
        if (intVal is >= 1 and <= 120)
            _pomodoro.DurationMinutes = intVal;
    }
}
