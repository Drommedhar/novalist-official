using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using Novalist.Sdk.Models.Wizards;
using Novalist.Core.Services;

namespace Novalist.Desktop.Services;

/// <summary>
/// Drives a <see cref="WizardDefinition"/>: maintains the current step, the
/// answers map, evaluates <see cref="WizardCondition"/>s for branching, and
/// persists state to disk so the user can resume after closing.
/// </summary>
public sealed class WizardRunner : INotifyPropertyChanged
{
    private readonly IFileService _fileService;
    private readonly Func<string?>? _stateDirProvider;

    public WizardDefinition Definition { get; private set; } = new();
    public WizardResult Result { get; private set; } = new();

    private List<WizardStep> _visibleSteps = [];
    public IReadOnlyList<WizardStep> VisibleSteps => _visibleSteps;

    private int _currentIndex;
    public int CurrentIndex
    {
        get => _currentIndex;
        private set { _currentIndex = value; Raise(); Raise(nameof(CurrentStep)); Raise(nameof(IsLastStep)); Raise(nameof(IsFirstStep)); Raise(nameof(StepCounterDisplay)); }
    }

    public WizardStep? CurrentStep
        => _visibleSteps.Count == 0 || _currentIndex < 0 || _currentIndex >= _visibleSteps.Count
            ? null
            : _visibleSteps[_currentIndex];

    public bool IsFirstStep => _currentIndex == 0;
    public bool IsLastStep => _visibleSteps.Count > 0 && _currentIndex == _visibleSteps.Count - 1;
    public string StepCounterDisplay => _visibleSteps.Count == 0
        ? string.Empty
        : $"{_currentIndex + 1} / {_visibleSteps.Count}";

    public event Action? Completed;
    public event Action? Cancelled;
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Optional override of the state directory (per-scope) — when null
    /// the runner does not persist state.</summary>
    public WizardRunner(IFileService fileService, Func<string?>? stateDirProvider = null)
    {
        _fileService = fileService;
        _stateDirProvider = stateDirProvider;
    }

    public async Task StartAsync(WizardDefinition definition, WizardResult? resumeFrom = null)
    {
        Definition = definition;
        Result = resumeFrom ?? new WizardResult { DefinitionId = definition.Id };
        Result.DefinitionId = definition.Id;
        RecomputeVisibleSteps();
        CurrentIndex = Math.Clamp(Result.CurrentStepIndex, 0, Math.Max(0, _visibleSteps.Count - 1));
        await Task.CompletedTask;
    }

    public void SetAnswer(string stepId, WizardAnswer answer)
    {
        if (answer.IsEmpty)
            Result.Answers.Remove(stepId);
        else
            Result.Answers[stepId] = answer;

        RecomputeVisibleSteps();
        _ = PersistAsync();
    }

    public Task NextAsync()
    {
        if (_currentIndex >= _visibleSteps.Count - 1)
        {
            return FinishAsync();
        }
        CurrentIndex++;
        Result.CurrentStepIndex = _currentIndex;
        return PersistAsync();
    }

    public Task JumpToAsync(int index)
    {
        if (index < 0 || index >= _visibleSteps.Count) return Task.CompletedTask;
        CurrentIndex = index;
        Result.CurrentStepIndex = _currentIndex;
        return PersistAsync();
    }

    public Task JumpToStepAsync(string stepId)
    {
        for (int i = 0; i < _visibleSteps.Count; i++)
        {
            if (string.Equals(_visibleSteps[i].Id, stepId, StringComparison.OrdinalIgnoreCase))
                return JumpToAsync(i);
        }
        return Task.CompletedTask;
    }

    public Task BackAsync()
    {
        if (_currentIndex <= 0) return Task.CompletedTask;
        CurrentIndex--;
        Result.CurrentStepIndex = _currentIndex;
        return PersistAsync();
    }

    public Task SkipAsync()
    {
        var step = CurrentStep;
        if (step != null) Result.Answers.Remove(step.Id);
        return NextAsync();
    }

    public async Task FinishAsync()
    {
        Result.Completed = true;
        await PersistAsync(deleteOnFinish: true);
        Completed?.Invoke();
    }

    public async Task CancelAsync()
    {
        await PersistAsync(deleteOnFinish: true);
        Cancelled?.Invoke();
    }

    private void RecomputeVisibleSteps()
    {
        // Evaluate in document order so a step's VisibleWhen can refer to an
        // earlier step's answer, and a step whose gate parent is hidden is
        // also hidden (cascading visibility).
        var visibleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<WizardStep>();
        foreach (var step in Definition.Steps)
        {
            if (step.VisibleWhen == null)
            {
                list.Add(step);
                visibleIds.Add(step.Id);
                continue;
            }
            var refId = step.VisibleWhen.StepId;
            if (!visibleIds.Contains(refId)) continue;          // gate parent hidden → hide child
            if (!EvaluateCondition(step.VisibleWhen, Result)) continue;
            list.Add(step);
            visibleIds.Add(step.Id);
        }
        _visibleSteps = list;
        Raise(nameof(VisibleSteps));
        Raise(nameof(StepCounterDisplay));
        Raise(nameof(IsLastStep));
        Raise(nameof(IsFirstStep));
        Raise(nameof(CurrentStep));
        if (_currentIndex >= _visibleSteps.Count)
            CurrentIndex = Math.Max(0, _visibleSteps.Count - 1);
    }

    private static bool EvaluateCondition(WizardCondition? cond, WizardResult result)
    {
        if (cond == null) return true;
        var stepAnswer = result.Answers.TryGetValue(cond.StepId, out var v) ? v : null;
        var actual = stepAnswer?.Text ?? string.Empty;
        return cond.Operator switch
        {
            "equals" => string.Equals(actual, cond.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase),
            "notEquals" => !string.Equals(actual, cond.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase),
            "contains" => actual.Contains(cond.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase),
            "present" => stepAnswer != null && !stepAnswer.IsEmpty,
            _ => true,
        };
    }

    private string? GetStateFilePath()
    {
        var dir = _stateDirProvider?.Invoke();
        if (string.IsNullOrEmpty(dir)) return null;
        return _fileService.CombinePath(dir, $"wizard-state-{Definition.Id}.json");
    }

    private async Task PersistAsync(bool deleteOnFinish = false)
    {
        var path = GetStateFilePath();
        if (path == null) return;
        try
        {
            if (deleteOnFinish)
            {
                if (await _fileService.ExistsAsync(path))
                    await _fileService.DeleteFileAsync(path);
                return;
            }
            var dir = _fileService.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                await _fileService.CreateDirectoryAsync(dir);
            var json = JsonSerializer.Serialize(Result);
            await _fileService.WriteTextAsync(path, json);
        }
        catch { /* best-effort; never block flow on persistence errors */ }
    }

    public static async Task<WizardResult?> TryLoadStateAsync(IFileService fileService, string stateDir, string definitionId)
    {
        try
        {
            var path = fileService.CombinePath(stateDir, $"wizard-state-{definitionId}.json");
            if (!await fileService.ExistsAsync(path)) return null;
            var raw = await fileService.ReadTextAsync(path);
            return JsonSerializer.Deserialize<WizardResult>(raw);
        }
        catch { return null; }
    }

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
