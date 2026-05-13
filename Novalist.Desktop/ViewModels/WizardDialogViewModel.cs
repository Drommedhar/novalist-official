using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Sdk.Models.Wizards;
using Novalist.Desktop.Services;

namespace Novalist.Desktop.ViewModels;

public partial class WizardDialogViewModel : ObservableObject
{
    private readonly WizardRunner _runner;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _stepCounter = string.Empty;

    [ObservableProperty]
    private string _stepTitle = string.Empty;

    [ObservableProperty]
    private string _stepHelp = string.Empty;

    [ObservableProperty]
    private bool _isFirstStep;

    [ObservableProperty]
    private bool _isLastStep;

    [ObservableProperty]
    private bool _isCurrentStepSkippable = true;

    /// <summary>True when the wizard has exactly one visible step. The last-step
    /// button then says "Finish" instead of "Review" because there is nothing
    /// meaningful to review.</summary>
    [ObservableProperty]
    private bool _isOnlyStep;

    [ObservableProperty]
    private string _validationError = string.Empty;

    [ObservableProperty]
    private bool _isLoadingDynamicChoices;

    [ObservableProperty]
    private bool _isValidating;

    [ObservableProperty]
    private string _asyncStatusText = string.Empty;

    public bool HasValidationError => !string.IsNullOrEmpty(ValidationError);
    public bool IsAsyncRunning => IsValidating || IsLoadingDynamicChoices;

    partial void OnValidationErrorChanged(string value) => OnPropertyChanged(nameof(HasValidationError));
    partial void OnIsValidatingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAsyncRunning));
        OnPropertyChanged(nameof(CanProceed));
    }
    partial void OnIsLoadingDynamicChoicesChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAsyncRunning));
        OnPropertyChanged(nameof(CanProceed));
    }

    public bool ShowReviewButton => IsLastStep && !IsOnlyStep && !IsReviewMode;
    public bool ShowFinishOnLastStep => IsLastStep && IsOnlyStep && !IsReviewMode;
    partial void OnIsOnlyStepChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowReviewButton));
        OnPropertyChanged(nameof(ShowFinishOnLastStep));
    }
    partial void OnIsLastStepChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowReviewButton));
        OnPropertyChanged(nameof(ShowFinishOnLastStep));
    }

    /// <summary>True when the current step has a valid (non-empty) answer or
    /// is marked skippable. Drives the Next/Review/Finish button's enabled
    /// state so required fields cannot be bypassed.</summary>
    public bool CanProceed
        => !IsValidating
           && !IsLoadingDynamicChoices
           && (IsReviewMode
               || IsCurrentStepSkippable
               || CurrentAnswerIsFilled());

    // ── Bindings for each rendered step type ─────────────────────────
    [ObservableProperty] private bool _isTextStep;
    [ObservableProperty] private bool _isMultilineText;
    [ObservableProperty] private string _textAnswer = string.Empty;
    [ObservableProperty] private string _placeholder = string.Empty;

    [ObservableProperty] private bool _isNumberStep;
    [ObservableProperty] private int _numberAnswer;
    [ObservableProperty] private int _numberMin;
    [ObservableProperty] private int _numberMax = int.MaxValue;

    [ObservableProperty] private bool _isChoiceStep;
    [ObservableProperty] private ObservableCollection<ChoiceOption> _choices = [];

    [ObservableProperty] private bool _isDateStep;
    [ObservableProperty] private DateTime? _dateAnswer;

    [ObservableProperty] private bool _isEntityListStep;
    [ObservableProperty] private ObservableCollection<EntityListEntry> _entityListEntries = [];

    // ── Progress sidebar ─────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<StepNavItem> _stepNavItems = [];

    // ── Review screen ────────────────────────────────────────────────
    [ObservableProperty] private bool _isReviewMode;
    [ObservableProperty] private ObservableCollection<ReviewItem> _reviewItems = [];
    public bool ShowStepContent => !IsReviewMode;
    partial void OnIsReviewModeChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowStepContent));
        OnPropertyChanged(nameof(CanProceed));
        OnPropertyChanged(nameof(ShowReviewButton));
        OnPropertyChanged(nameof(ShowFinishOnLastStep));
    }

    public bool IsFinished { get; private set; }
    public WizardResult Result => _runner.Result;
    public WizardDefinition Definition => _runner.Definition;

    private bool CurrentAnswerIsFilled()
    {
        var step = _runner.CurrentStep;
        if (step == null) return true;
        return step switch
        {
            TextStep => !string.IsNullOrWhiteSpace(TextAnswer),
            NumberStep => true, // numeric always has a value
            ChoiceStep => Choices.Any(c => c.IsSelected),
            DateStep => DateAnswer.HasValue,
            EntityListStep => EntityListEntries.Any(e => e.Cells.Any(c => !string.IsNullOrWhiteSpace(c.Value))),
            _ => true,
        };
    }

    partial void OnTextAnswerChanged(string value) { CommitCurrentAnswer(); OnPropertyChanged(nameof(CanProceed)); }
    partial void OnNumberAnswerChanged(int value) { CommitCurrentAnswer(); OnPropertyChanged(nameof(CanProceed)); }
    partial void OnDateAnswerChanged(DateTime? value) { CommitCurrentAnswer(); OnPropertyChanged(nameof(CanProceed)); }
    partial void OnChoicesChanged(ObservableCollection<ChoiceOption> value)
    {
        foreach (var c in value)
            c.PropertyChanged += (_, __) => OnPropertyChanged(nameof(CanProceed));
        OnPropertyChanged(nameof(CanProceed));
    }
    partial void OnEntityListEntriesChanged(ObservableCollection<EntityListEntry> value)
    {
        foreach (var entry in value)
            foreach (var cell in entry.Cells)
                cell.PropertyChanged += (_, __) => OnPropertyChanged(nameof(CanProceed));
        OnPropertyChanged(nameof(CanProceed));
    }
    partial void OnIsCurrentStepSkippableChanged(bool value) => OnPropertyChanged(nameof(CanProceed));

    public event Action? CloseRequested;

    public WizardDialogViewModel(WizardRunner runner)
    {
        _runner = runner;
        _runner.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(WizardRunner.CurrentStep)
                or nameof(WizardRunner.CurrentIndex)
                or nameof(WizardRunner.VisibleSteps))
            {
                LoadCurrentStep();
                RefreshSidebar();
            }
        };
        _runner.Completed += () => { IsFinished = true; CloseRequested?.Invoke(); };
        _runner.Cancelled += () => { IsFinished = false; CloseRequested?.Invoke(); };

        Title = _runner.Definition.DisplayName;
        LoadCurrentStep();
        RefreshSidebar();
        _ = TryPopulateDynamicChoicesAsync();
    }

    private void RefreshSidebar()
    {
        var items = new ObservableCollection<StepNavItem>();
        for (int i = 0; i < _runner.VisibleSteps.Count; i++)
        {
            var step = _runner.VisibleSteps[i];
            var hasAnswer = _runner.Result.Answers.ContainsKey(step.Id);
            items.Add(new StepNavItem
            {
                Index = i,
                Title = step.Title,
                IsCurrent = i == _runner.CurrentIndex && !IsReviewMode,
                IsAnswered = hasAnswer,
            });
        }
        StepNavItems = items;
    }

    private string? _loadedStepId;

    private void LoadCurrentStep()
    {
        if (IsReviewMode) return;

        var step = _runner.CurrentStep;

        // Same step still active — only refresh derived button state, do not
        // rebuild the input collections (would reset selection mid-click).
        if (step != null && string.Equals(step.Id, _loadedStepId, StringComparison.OrdinalIgnoreCase))
        {
            StepCounter = _runner.StepCounterDisplay;
            IsFirstStep = _runner.IsFirstStep;
            IsLastStep = _runner.IsLastStep;
            IsOnlyStep = _runner.VisibleSteps.Count <= 1;
            OnPropertyChanged(nameof(CanProceed));
            return;
        }
        _loadedStepId = step?.Id;

        StepCounter = _runner.StepCounterDisplay;
        IsFirstStep = _runner.IsFirstStep;
        IsLastStep = _runner.IsLastStep;

        IsTextStep = false;
        IsNumberStep = false;
        IsChoiceStep = false;
        IsDateStep = false;
        IsEntityListStep = false;
        Choices = new ObservableCollection<ChoiceOption>();
        EntityListEntries = new ObservableCollection<EntityListEntry>();

        if (step == null)
        {
            StepTitle = string.Empty;
            StepHelp = string.Empty;
            return;
        }

        StepTitle = step.Title;
        StepHelp = step.Help ?? string.Empty;
        IsCurrentStepSkippable = step.Skippable;
        IsOnlyStep = _runner.VisibleSteps.Count <= 1;

        var existing = _runner.Result.Answers.TryGetValue(step.Id, out var v) ? v : null;

        switch (step)
        {
            case TextStep t:
                IsTextStep = true;
                IsMultilineText = t.Multiline;
                Placeholder = t.Placeholder ?? string.Empty;
                TextAnswer = existing?.Text ?? string.Empty;
                break;
            case NumberStep n:
                IsNumberStep = true;
                NumberMin = n.Min ?? int.MinValue;
                NumberMax = n.Max ?? int.MaxValue;
                NumberAnswer = existing?.Number ?? n.DefaultValue;
                break;
            case ChoiceStep c:
                IsChoiceStep = true;
                foreach (var ch in c.Choices)
                    Choices.Add(new ChoiceOption(ch.Value, ch.Label, ch.Description));
                var currentValue = existing?.Text;
                foreach (var opt in Choices)
                    opt.IsSelected = string.Equals(opt.Value, currentValue, StringComparison.OrdinalIgnoreCase);
                break;
            case DateStep:
                IsDateStep = true;
                if (existing?.Text != null && DateTime.TryParse(existing.Text, out var dt))
                    DateAnswer = dt;
                else
                    DateAnswer = null;
                break;
            case EntityListStep el:
                IsEntityListStep = true;
                var subSteps = el.SubSteps?.Count > 0
                    ? el.SubSteps
                    : new List<WizardStep> { new TextStep { Id = "name", Title = "Name" } };
                if (existing?.List != null)
                {
                    foreach (var rec in existing.List)
                    {
                        EntityListEntries.Add(BuildEntry(subSteps, rec));
                    }
                }
                if (EntityListEntries.Count == 0)
                    EntityListEntries.Add(BuildEntry(subSteps, null));
                break;
        }

        // Items inside Choices / EntityListEntries are added AFTER the
        // ObservableProperty setter fired, so the auto-subscriptions on the
        // collection-changed handlers saw an empty collection. Wire them now
        // and force a CanProceed recompute so the Next button enables.
        // Each input change also live-commits so branching steps appear /
        // disappear immediately (and the Next button switches between
        // "Next" / "Review" / "Finish" as the visible-step count changes).
        foreach (var opt in Choices)
            opt.PropertyChanged += (_, __) => { CommitCurrentAnswer(); OnPropertyChanged(nameof(CanProceed)); };
        foreach (var entry in EntityListEntries)
            foreach (var cell in entry.Cells)
                cell.PropertyChanged += (_, __) => { CommitCurrentAnswer(); OnPropertyChanged(nameof(CanProceed)); };
        OnPropertyChanged(nameof(CanProceed));
    }

    private EntityListEntry BuildEntry(IReadOnlyList<WizardStep> subSteps, IReadOnlyDictionary<string, WizardAnswer>? values)
    {
        var entry = new EntityListEntry();
        foreach (var sub in subSteps)
        {
            var cell = new EntityListCell
            {
                Key = sub.Id,
                Label = sub.Title,
                Placeholder = sub is TextStep ts ? (ts.Placeholder ?? sub.Title) : sub.Title,
            };
            if (values != null && values.TryGetValue(sub.Id, out var av))
                cell.Value = av.Text ?? string.Empty;
            entry.Cells.Add(cell);
        }
        return entry;
    }

    [RelayCommand]
    private void AddEntityListEntry()
    {
        var step = _runner.CurrentStep as EntityListStep;
        if (step == null) return;
        var subSteps = step.SubSteps?.Count > 0
            ? step.SubSteps
            : new List<WizardStep> { new TextStep { Id = "name", Title = "Name" } };
        EntityListEntries.Add(BuildEntry(subSteps, null));
    }

    [RelayCommand]
    private void RemoveEntityListEntry(EntityListEntry? entry)
    {
        if (entry == null) return;
        EntityListEntries.Remove(entry);
    }

    private void CommitCurrentAnswer()
    {
        if (IsReviewMode) return;
        var step = _runner.CurrentStep;
        if (step == null) return;

        WizardAnswer answer = new();
        switch (step)
        {
            case TextStep:
                answer.Text = string.IsNullOrWhiteSpace(TextAnswer) ? null : TextAnswer.Trim();
                break;
            case NumberStep:
                answer.Number = NumberAnswer;
                break;
            case ChoiceStep:
                var sel = Choices.FirstOrDefault(c => c.IsSelected);
                answer.Text = sel?.Value;
                break;
            case DateStep:
                answer.Text = DateAnswer?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                break;
            case EntityListStep:
                answer.List = EntityListEntries
                    .Select(e =>
                    {
                        var dict = new Dictionary<string, WizardAnswer>(StringComparer.OrdinalIgnoreCase);
                        foreach (var cell in e.Cells)
                        {
                            if (!string.IsNullOrWhiteSpace(cell.Value))
                                dict[cell.Key] = new WizardAnswer { Text = cell.Value.Trim() };
                        }
                        return dict;
                    })
                    .Where(d => d.Count > 0)
                    .ToList();
                if (answer.List.Count == 0) answer.List = null;
                break;
        }
        _runner.SetAnswer(step.Id, answer);
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        if (IsReviewMode) return;

        CommitCurrentAnswer();

        // Validate before advancing.
        var step = _runner.CurrentStep;
        if (step?.Validator != null)
        {
            ValidationError = string.Empty;
            AsyncStatusText = $"Validating: {step.Title}…";
            IsValidating = true;
            try
            {
                var err = await step.Validator.Invoke(_runner.Result);
                if (!string.IsNullOrEmpty(err))
                {
                    ValidationError = err;
                    return;
                }
            }
            catch (Exception ex)
            {
                ValidationError = ex.Message;
                return;
            }
            finally
            {
                IsValidating = false;
                AsyncStatusText = string.Empty;
            }
        }
        ValidationError = string.Empty;

        if (_runner.IsLastStep)
        {
            if (_runner.VisibleSteps.Count <= 1)
            {
                await _runner.FinishAsync();
                return;
            }
            EnterReviewMode();
            return;
        }
        await _runner.NextAsync();
        await TryPopulateDynamicChoicesAsync();
    }

    [RelayCommand]
    private async Task BackAsync()
    {
        ValidationError = string.Empty;
        if (IsReviewMode)
        {
            IsReviewMode = false;
            await _runner.JumpToAsync(_runner.VisibleSteps.Count - 1);
            return;
        }
        CommitCurrentAnswer();
        await _runner.BackAsync();
        await TryPopulateDynamicChoicesAsync();
    }

    private async Task TryPopulateDynamicChoicesAsync()
    {
        if (_runner.CurrentStep is not ChoiceStep cs || cs.DynamicChoicesProvider == null) return;
        AsyncStatusText = $"Loading options for: {cs.Title}…";
        IsLoadingDynamicChoices = true;
        try
        {
            var list = await cs.DynamicChoicesProvider.Invoke(_runner.Result);
            cs.Choices = list?.ToList() ?? new List<WizardChoice>();
            if (cs.Choices.Count == 0 && cs.AutoSkipIfChoicesEmpty)
            {
                // Skip this step entirely.
                if (_runner.IsLastStep)
                {
                    if (_runner.VisibleSteps.Count <= 1) await _runner.FinishAsync();
                    else EnterReviewMode();
                }
                else await _runner.NextAsync();
                return;
            }
            // Force LoadCurrentStep to rebuild Choices collection for this step.
            _loadedStepId = null;
            LoadCurrentStep();
        }
        catch (Exception ex)
        {
            ValidationError = ex.Message;
        }
        finally
        {
            IsLoadingDynamicChoices = false;
            AsyncStatusText = string.Empty;
        }
    }

    [RelayCommand]
    private async Task SkipAsync()
    {
        if (IsReviewMode) return;
        await _runner.SkipAsync();
    }

    [RelayCommand]
    private async Task FinishAsync()
    {
        if (!IsReviewMode) CommitCurrentAnswer();
        await _runner.FinishAsync();
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        await _runner.CancelAsync();
    }

    [RelayCommand]
    private async Task JumpToStepAsync(StepNavItem? item)
    {
        if (item == null) return;
        // Required + empty current step cannot be left via sidebar either.
        if (!IsReviewMode && !CanProceed) return;
        if (!IsReviewMode) CommitCurrentAnswer();

        // Forward jumps are only allowed when every required step between
        // current and target has been answered. Backward jumps are unrestricted.
        var targetIndex = item.Index;
        var fromIndex = IsReviewMode ? _runner.VisibleSteps.Count : _runner.CurrentIndex;
        if (targetIndex > fromIndex)
        {
            for (int i = fromIndex + 1; i < targetIndex; i++)
            {
                var s = _runner.VisibleSteps[i];
                if (s.Skippable) continue;
                if (!HasFilledAnswer(s)) return;
            }
        }

        IsReviewMode = false;
        await _runner.JumpToAsync(targetIndex);
        await TryPopulateDynamicChoicesAsync();
    }

    private bool HasFilledAnswer(WizardStep step)
    {
        if (!_runner.Result.Answers.TryGetValue(step.Id, out var v)) return false;
        if (v.IsEmpty) return false;
        if (step is ChoiceStep)
            return !string.IsNullOrWhiteSpace(v.Text);
        if (step is DateStep)
            return !string.IsNullOrWhiteSpace(v.Text);
        if (step is TextStep)
            return !string.IsNullOrWhiteSpace(v.Text);
        if (step is EntityListStep)
            return v.List != null && v.List.Count > 0;
        return true;
    }

    [RelayCommand]
    private async Task EditFromReviewAsync(ReviewItem? item)
    {
        if (item == null) return;
        IsReviewMode = false;
        await _runner.JumpToStepAsync(item.StepId);
        await TryPopulateDynamicChoicesAsync();
    }

    private void EnterReviewMode()
    {
        var items = new ObservableCollection<ReviewItem>();
        foreach (var step in _runner.VisibleSteps)
        {
            items.Add(new ReviewItem
            {
                StepId = step.Id,
                StepTitle = step.Title,
                AnswerSummary = SummarizeAnswer(step, _runner.Result),
            });
        }
        ReviewItems = items;
        StepCounter = "Review";
        StepTitle = "Review your answers";
        StepHelp = "Click 'Edit' to jump back. Click 'Finish' to confirm.";
        IsReviewMode = true;
        RefreshSidebar();
    }

    private static string SummarizeAnswer(WizardStep step, WizardResult result)
    {
        if (!result.Answers.TryGetValue(step.Id, out var v)) return "—";
        if (v.IsEmpty) return "—";
        if (!string.IsNullOrEmpty(v.Text)) return v.Text!;
        if (v.Number.HasValue) return v.Number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (v.List is { Count: > 0 } list)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                var dict = list[i];
                var name = dict.TryGetValue("name", out var n) ? n.Text : null;
                var role = dict.TryGetValue("role", out var r) ? r.Text : null;
                if (string.IsNullOrEmpty(name)) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(name);
                if (!string.IsNullOrEmpty(role)) sb.Append(" (").Append(role).Append(')');
            }
            return sb.Length == 0 ? "—" : sb.ToString();
        }
        if (v.Multi is { Count: > 0 } multi) return string.Join(", ", multi);
        return "—";
    }
}

public partial class ChoiceOption : ObservableObject
{
    public string Value { get; }
    public string Label { get; }
    public string? Description { get; }

    [ObservableProperty]
    private bool _isSelected;

    public ChoiceOption(string value, string label, string? description)
    {
        Value = value;
        Label = label;
        Description = description;
    }
}

public partial class EntityListEntry : ObservableObject
{
    public ObservableCollection<EntityListCell> Cells { get; } = new();
}

public partial class EntityListCell : ObservableObject
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Placeholder { get; set; } = string.Empty;

    [ObservableProperty]
    private string _value = string.Empty;
}

public sealed class StepNavItem
{
    public int Index { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
    public bool IsAnswered { get; set; }
}

public sealed class ReviewItem
{
    public string StepId { get; set; } = string.Empty;
    public string StepTitle { get; set; } = string.Empty;
    public string AnswerSummary { get; set; } = string.Empty;
}
