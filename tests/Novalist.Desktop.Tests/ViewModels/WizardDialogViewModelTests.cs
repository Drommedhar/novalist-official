using NSubstitute;
using Novalist.Core.Services;
using Novalist.Desktop.Services;
using Novalist.Desktop.ViewModels;
using Novalist.Sdk.Models.Wizards;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

public class WizardDialogViewModelTests
{
    private static WizardRunner Runner(params WizardStep[] steps)
    {
        var runner = new WizardRunner(Substitute.For<IFileService>()); // null stateDir -> no persist
        runner.StartAsync(new WizardDefinition { Id = "w", DisplayName = "Wiz", Steps = steps.ToList() })
              .GetAwaiter().GetResult();
        return runner;
    }

    private static TextStep Text(string id, bool required = false, bool multiline = false)
        => new() { Id = id, Title = id.ToUpperInvariant(), Skippable = !required, Multiline = multiline, Placeholder = "ph" };

    [Fact]
    public void Ctor_LoadsFirstTextStep_AndSidebar()
    {
        var vm = new WizardDialogViewModel(Runner(Text("a"), Text("b")));
        Assert.Equal("Wiz", vm.Title);
        Assert.True(vm.IsTextStep);
        Assert.True(vm.IsFirstStep);
        Assert.False(vm.IsLastStep);
        Assert.Equal(2, vm.StepNavItems.Count);
        Assert.True(vm.StepNavItems[0].IsCurrent);
        Assert.Equal("ph", vm.Placeholder);
    }

    [Fact]
    public void TextStep_CanProceed_RequiredVsFilled()
    {
        var vm = new WizardDialogViewModel(Runner(Text("a", required: true), Text("b")));
        Assert.False(vm.CanProceed); // required + empty
        vm.TextAnswer = "hi";
        Assert.True(vm.CanProceed);
        Assert.True(vm.HasValidationError == false);
    }

    [Fact]
    public void NumberStep_AlwaysProceeds()
    {
        var vm = new WizardDialogViewModel(Runner(new NumberStep { Id = "n", Title = "N", Skippable = false, Min = 1, Max = 10, DefaultValue = 3 }));
        Assert.True(vm.IsNumberStep);
        Assert.Equal(3, vm.NumberAnswer);
        Assert.Equal(1, vm.NumberMin);
        Assert.Equal(10, vm.NumberMax);
        Assert.True(vm.CanProceed);
        vm.NumberAnswer = 7; // commits
    }

    [Fact]
    public void ChoiceStep_SelectionDrivesProceed()
    {
        var step = new ChoiceStep
        {
            Id = "c", Title = "C", Skippable = false,
            Choices = [new() { Value = "x", Label = "X" }, new() { Value = "y", Label = "Y", Description = "d" }]
        };
        var vm = new WizardDialogViewModel(Runner(step));
        Assert.True(vm.IsChoiceStep);
        Assert.Equal(2, vm.Choices.Count);
        Assert.False(vm.CanProceed);
        vm.Choices[0].IsSelected = true;
        Assert.True(vm.CanProceed);
    }

    [Fact]
    public void DateStep_AnswerDrivesProceed()
    {
        var vm = new WizardDialogViewModel(Runner(new DateStep { Id = "d", Title = "D", Skippable = false }));
        Assert.True(vm.IsDateStep);
        Assert.False(vm.CanProceed);
        vm.DateAnswer = new DateTime(2026, 1, 1);
        Assert.True(vm.CanProceed);
    }

    [Fact]
    public void EntityListStep_AddRemove_AndProceed()
    {
        var step = new EntityListStep { Id = "e", Title = "E", Skippable = false,
            SubSteps = [new TextStep { Id = "name", Title = "Name" }] };
        var vm = new WizardDialogViewModel(Runner(step));
        Assert.True(vm.IsEntityListStep);
        Assert.Single(vm.EntityListEntries); // seeded with one empty entry
        Assert.False(vm.CanProceed);

        vm.EntityListEntries[0].Cells[0].Value = "Bob";
        Assert.True(vm.CanProceed);

        vm.AddEntityListEntryCommand.Execute(null);
        Assert.Equal(2, vm.EntityListEntries.Count);
        vm.RemoveEntityListEntryCommand.Execute(vm.EntityListEntries[1]);
        Assert.Single(vm.EntityListEntries);
        vm.RemoveEntityListEntryCommand.Execute(null); // no-op
    }

    [Fact]
    public void EntityListStep_DefaultSubStep_WhenNoneProvided()
    {
        var vm = new WizardDialogViewModel(Runner(new EntityListStep { Id = "e", Title = "E" }));
        Assert.Single(vm.EntityListEntries);
        Assert.Single(vm.EntityListEntries[0].Cells); // default "name"
        vm.AddEntityListEntryCommand.Execute(null);
        Assert.Equal(2, vm.EntityListEntries.Count);
    }

    [Fact]
    public async Task Next_AdvancesThroughSteps()
    {
        var vm = new WizardDialogViewModel(Runner(Text("a"), Text("b")));
        await vm.NextCommand.ExecuteAsync(null);
        Assert.False(vm.IsFirstStep);
        Assert.True(vm.IsLastStep);
    }

    [Fact]
    public async Task Next_OnLastOfMany_EntersReview()
    {
        var vm = new WizardDialogViewModel(Runner(Text("a"), Text("b")));
        vm.TextAnswer = "x";
        await vm.NextCommand.ExecuteAsync(null); // -> step b
        vm.TextAnswer = "y";
        await vm.NextCommand.ExecuteAsync(null); // last -> review
        Assert.True(vm.IsReviewMode);
        Assert.False(vm.ShowStepContent);
        Assert.False(vm.ShowReviewButton);
        Assert.NotEmpty(vm.ReviewItems);
    }

    [Fact]
    public async Task Next_SingleStep_Finishes()
    {
        var runner = Runner(Text("only"));
        var vm = new WizardDialogViewModel(runner);
        Assert.True(vm.IsOnlyStep);
        Assert.True(vm.ShowFinishOnLastStep);
        bool closed = false; vm.CloseRequested += () => closed = true;
        await vm.NextCommand.ExecuteAsync(null); // single visible -> finish
        Assert.True(vm.IsFinished);
        Assert.True(closed);
    }

    [Fact]
    public async Task Validation_Error_BlocksAdvance_ThenSuccess()
    {
        var step = Text("a");
        bool fail = true;
        step.Validator = _ => Task.FromResult<string?>(fail ? "bad" : null);
        var vm = new WizardDialogViewModel(Runner(step, Text("b")));
        vm.TextAnswer = "v";

        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal("bad", vm.ValidationError);
        Assert.True(vm.HasValidationError);
        Assert.True(vm.IsFirstStep); // did not advance
        Assert.False(vm.IsValidating);

        fail = false;
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(string.Empty, vm.ValidationError);
        Assert.True(vm.IsLastStep);
    }

    [Fact]
    public async Task Validation_Throws_SetsError()
    {
        var step = Text("a");
        step.Validator = _ => throw new InvalidOperationException("boom");
        var vm = new WizardDialogViewModel(Runner(step, Text("b")));
        vm.TextAnswer = "v";
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal("boom", vm.ValidationError);
    }

    [Fact]
    public async Task Back_FromStep_AndFromReview()
    {
        var vm = new WizardDialogViewModel(Runner(Text("a"), Text("b")));
        vm.TextAnswer = "x";
        await vm.NextCommand.ExecuteAsync(null);
        vm.TextAnswer = "y";
        await vm.NextCommand.ExecuteAsync(null); // review
        Assert.True(vm.IsReviewMode);

        await vm.BackCommand.ExecuteAsync(null); // exits review to last step
        Assert.False(vm.IsReviewMode);
        Assert.True(vm.IsLastStep);

        await vm.BackCommand.ExecuteAsync(null); // back to first
        Assert.True(vm.IsFirstStep);
    }

    [Fact]
    public async Task Skip_Cancel_Finish()
    {
        var runner = Runner(Text("a"), Text("b"));
        var vm = new WizardDialogViewModel(runner);
        await vm.SkipCommand.ExecuteAsync(null); // advances past a
        Assert.True(vm.IsLastStep);

        bool cancelledClosed = false;
        vm.CloseRequested += () => cancelledClosed = true;
        await vm.CancelCommand.ExecuteAsync(null);
        Assert.False(vm.IsFinished);
        Assert.True(cancelledClosed);
    }

    [Fact]
    public async Task Finish_FromReview_Completes()
    {
        var runner = Runner(Text("a"), Text("b"));
        var vm = new WizardDialogViewModel(runner);
        vm.TextAnswer = "x"; await vm.NextCommand.ExecuteAsync(null);
        vm.TextAnswer = "y"; await vm.NextCommand.ExecuteAsync(null); // review
        bool done = false; vm.CloseRequested += () => done = true;
        await vm.FinishCommand.ExecuteAsync(null);
        Assert.True(vm.IsFinished);
        Assert.True(done);
    }

    [Fact]
    public async Task EditFromReview_JumpsBack()
    {
        var vm = new WizardDialogViewModel(Runner(Text("a"), Text("b")));
        vm.TextAnswer = "x"; await vm.NextCommand.ExecuteAsync(null);
        vm.TextAnswer = "y"; await vm.NextCommand.ExecuteAsync(null); // review
        await vm.EditFromReviewCommand.ExecuteAsync(vm.ReviewItems[0]);
        Assert.False(vm.IsReviewMode);
        Assert.True(vm.IsFirstStep);
        await vm.EditFromReviewCommand.ExecuteAsync(null); // no-op
    }

    [Fact]
    public async Task JumpToStep_BackwardFree_ForwardGated()
    {
        var vm = new WizardDialogViewModel(Runner(Text("a", required: true), Text("b", required: true), Text("c")));
        await vm.JumpToStepCommand.ExecuteAsync(null); // null no-op

        // current 'a' required + empty -> CanProceed false -> jump blocked
        await vm.JumpToStepCommand.ExecuteAsync(vm.StepNavItems[2]);
        Assert.True(vm.IsFirstStep);

        // fill a, then forward jump to c is gated by b (required, empty) -> blocked
        vm.TextAnswer = "A";
        await vm.JumpToStepCommand.ExecuteAsync(vm.StepNavItems[2]);
        Assert.True(vm.IsFirstStep); // still blocked by b

        // jump forward to b (adjacent) allowed
        await vm.JumpToStepCommand.ExecuteAsync(vm.StepNavItems[1]);
        Assert.False(vm.IsFirstStep);

        // b is required + empty -> the CanProceed gate blocks leaving it (even backward).
        await vm.JumpToStepCommand.ExecuteAsync(vm.StepNavItems[0]);
        Assert.False(vm.IsFirstStep); // still on b

        // fill b, then backward jump to a is allowed
        vm.TextAnswer = "B";
        await vm.JumpToStepCommand.ExecuteAsync(vm.StepNavItems[0]);
        Assert.True(vm.IsFirstStep);
    }

    [Fact]
    public async Task DynamicChoices_PopulatesAndAutoSkipsWhenEmpty()
    {
        var dyn = new ChoiceStep { Id = "c", Title = "C", Skippable = false,
            AutoSkipIfChoicesEmpty = true, DynamicChoicesProvider = _ => Task.FromResult<IReadOnlyList<WizardChoice>>([]) };
        var vm = new WizardDialogViewModel(Runner(Text("a"), dyn, Text("z")));
        vm.TextAnswer = "x";
        await vm.NextCommand.ExecuteAsync(null); // enters dyn -> empty -> auto-skip to z
        // should have advanced past the empty dynamic choice step
        Assert.False(vm.IsChoiceStep);
    }

    [Fact]
    public async Task DynamicChoices_Populated_BuildsChoices()
    {
        var dyn = new ChoiceStep { Id = "c", Title = "C", Skippable = false,
            DynamicChoicesProvider = _ => Task.FromResult<IReadOnlyList<WizardChoice>>([new() { Value = "p", Label = "P" }]) };
        var vm = new WizardDialogViewModel(Runner(Text("a"), dyn));
        vm.TextAnswer = "x";
        await vm.NextCommand.ExecuteAsync(null);
        Assert.True(vm.IsChoiceStep);
        Assert.Single(vm.Choices);
    }

    [Fact]
    public async Task DynamicChoices_ProviderThrows_SetsError()
    {
        var dyn = new ChoiceStep { Id = "c", Title = "C",
            DynamicChoicesProvider = _ => throw new InvalidOperationException("nope") };
        var vm = new WizardDialogViewModel(Runner(Text("a"), dyn));
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal("nope", vm.ValidationError);
    }

    [Fact]
    public void Review_Summaries_TextNumberListEmpty()
    {
        var runner = Runner(
            Text("t"),
            new NumberStep { Id = "n", Title = "N", DefaultValue = 5 },
            new EntityListStep { Id = "e", Title = "E", SubSteps = [new TextStep { Id = "name", Title = "Name" }, new TextStep { Id = "role", Title = "Role" }] },
            Text("empty"));
        // Pre-fill answers via runner so review summarizes them.
        runner.SetAnswer("t", new WizardAnswer { Text = "Hello" });
        runner.SetAnswer("n", new WizardAnswer { Number = 5 });
        runner.SetAnswer("e", new WizardAnswer { List = [new() { ["name"] = new() { Text = "Bob" }, ["role"] = new() { Text = "Hero" } }] });

        var vm = new WizardDialogViewModel(runner);
        // Force review by jumping to last then Next.
        vm.JumpToStepCommand.ExecuteAsync(vm.StepNavItems[3]).GetAwaiter().GetResult();
        vm.NextCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Assert.True(vm.IsReviewMode);
        var summaries = vm.ReviewItems.ToDictionary(r => r.StepId, r => r.AnswerSummary);
        Assert.Equal("Hello", summaries["t"]);
        Assert.Equal("5", summaries["n"]);
        Assert.Contains("Bob", summaries["e"]);
        Assert.Equal("—", summaries["empty"]);
    }

    [Fact]
    public void LoadExistingAnswers_TextChoiceDateNumberEntityList()
    {
        var runner = Runner(new ChoiceStep { Id = "c", Title = "C", Choices = [new() { Value = "x", Label = "X" }] });
        runner.SetAnswer("c", new WizardAnswer { Text = "x" });
        var vm = new WizardDialogViewModel(runner);
        Assert.True(vm.Choices.First(o => o.Value == "x").IsSelected); // pre-selected from existing
    }

    [Fact]
    public void SubViewModels_Basics()
    {
        var opt = new ChoiceOption("v", "l", "desc");
        Assert.Equal("v", opt.Value);
        Assert.Equal("desc", opt.Description);
        var cell = new EntityListCell { Key = "k", Label = "L", Placeholder = "p", Value = "x" };
        Assert.Equal("k", cell.Key);
        var entry = new EntityListEntry();
        entry.Cells.Add(cell);
        Assert.Single(entry.Cells);
        var nav = new StepNavItem { Index = 1, Title = "T", IsCurrent = true, IsAnswered = true };
        Assert.True(nav.IsCurrent);
        var rev = new ReviewItem { StepId = "s", StepTitle = "T", AnswerSummary = "a" };
        Assert.Equal("s", rev.StepId);
    }

    [Fact]
    public void ResultAndDefinition_Exposed()
    {
        var runner = Runner(Text("a"));
        var vm = new WizardDialogViewModel(runner);
        Assert.Same(runner.Result, vm.Result);
        Assert.Same(runner.Definition, vm.Definition);
    }

    [Fact]
    public void ZeroSteps_NullStep_ClearsTitles()
    {
        var vm = new WizardDialogViewModel(Runner()); // no steps
        Assert.Equal(string.Empty, vm.StepTitle);
        Assert.False(vm.IsTextStep);
        Assert.True(vm.CanProceed); // null step -> filled = true
    }

    [Fact]
    public void UnhandledStepType_NotSkippable_CanProceedTrue()
    {
        var vm = new WizardDialogViewModel(Runner(new EntityRefStep { Id = "r", Title = "R", Skippable = false }));
        Assert.False(vm.IsTextStep);
        Assert.True(vm.CanProceed); // CurrentAnswerIsFilled default arm
    }

    [Fact]
    public void DateStep_LoadsExistingAnswer()
    {
        var runner = Runner(new DateStep { Id = "d", Title = "D" });
        runner.SetAnswer("d", new WizardAnswer { Text = "2026-05-01" });
        var vm = new WizardDialogViewModel(runner);
        Assert.True(vm.IsDateStep);
        Assert.Equal(new DateTime(2026, 5, 1), vm.DateAnswer);
    }

    [Fact]
    public void EntityListStep_LoadsExistingEntries()
    {
        var runner = Runner(new EntityListStep { Id = "e", Title = "E",
            SubSteps = [new TextStep { Id = "name", Title = "Name" }, new TextStep { Id = "role", Title = "Role" }] });
        runner.SetAnswer("e", new WizardAnswer { List = [
            new() { ["name"] = new() { Text = "Bob" }, ["role"] = new() { Text = "Hero" } }] });
        var vm = new WizardDialogViewModel(runner);
        Assert.Single(vm.EntityListEntries);
        Assert.Equal("Bob", vm.EntityListEntries[0].Cells[0].Value);
        Assert.Equal("Hero", vm.EntityListEntries[0].Cells[1].Value);
    }

    [Fact]
    public async Task HasFilledAnswer_AllTypes_AllowForwardJump()
    {
        var runner = Runner(
            Text("a"),
            new ChoiceStep { Id = "c", Title = "C", Skippable = false, Choices = [new() { Value = "x", Label = "X" }] },
            new DateStep { Id = "d", Title = "D", Skippable = false },
            new EntityListStep { Id = "e", Title = "E", Skippable = false, SubSteps = [new TextStep { Id = "name", Title = "N" }] },
            Text("tmid", required: true),
            new NumberStep { Id = "num", Title = "Num", Skippable = false },
            Text("z"));
        // Answer all intermediates so the forward-jump gate's HasFilledAnswer passes each type.
        runner.SetAnswer("c", new WizardAnswer { Text = "x" });
        runner.SetAnswer("d", new WizardAnswer { Text = "2026-01-01" });
        runner.SetAnswer("e", new WizardAnswer { List = [new() { ["name"] = new() { Text = "Bob" } }] });
        runner.SetAnswer("tmid", new WizardAnswer { Text = "filled" });
        runner.SetAnswer("num", new WizardAnswer { Number = 7 });
        var vm = new WizardDialogViewModel(runner);
        vm.TextAnswer = "A"; // fill current 'a'

        await vm.JumpToStepCommand.ExecuteAsync(vm.StepNavItems[6]); // jump to z over answered intermediates
        Assert.False(vm.IsFirstStep);
        Assert.True(vm.IsLastStep);
    }

    [Fact]
    public void SummarizeAnswer_MultiValue()
    {
        // 'm' sits in the middle so navigating never commits (and clears) it.
        var runner = Runner(Text("first"), Text("m"), Text("b"));
        runner.SetAnswer("m", new WizardAnswer { Multi = ["x", "y"] });
        var vm = new WizardDialogViewModel(runner);
        vm.JumpToStepCommand.ExecuteAsync(vm.StepNavItems[2]).GetAwaiter().GetResult(); // first -> b (last), m untouched
        vm.NextCommand.ExecuteAsync(null).GetAwaiter().GetResult(); // -> review
        Assert.True(vm.IsReviewMode);
        Assert.Equal("x, y", vm.ReviewItems.First(r => r.StepId == "m").AnswerSummary);
    }

    [Fact]
    public async Task IsAsyncRunning_TracksValidating()
    {
        var tcs = new TaskCompletionSource<string?>();
        var step = Text("a");
        step.Validator = _ => tcs.Task;
        var vm = new WizardDialogViewModel(Runner(step, Text("b")));
        vm.TextAnswer = "v";
        var next = vm.NextCommand.ExecuteAsync(null);
        await Task.Delay(20);
        Assert.True(vm.IsValidating);
        Assert.True(vm.IsAsyncRunning);
        Assert.False(vm.CanProceed); // blocked while validating
        tcs.SetResult(null);
        await next;
        Assert.False(vm.IsValidating);
    }

    // ── Dynamic-choice auto-skip on the LAST step ───────────────────
    [Fact]
    public void DynamicChoices_OnlyStep_Empty_Finishes()
    {
        var dyn = new ChoiceStep
        {
            Id = "c", Title = "C", Skippable = false, AutoSkipIfChoicesEmpty = true,
            DynamicChoicesProvider = _ => Task.FromResult<IReadOnlyList<WizardChoice>>([]),
        };
        // Single dynamic step: ctor populates choices -> empty -> last + only step -> Finish.
        var vm = new WizardDialogViewModel(Runner(dyn));
        Assert.True(vm.IsFinished);
    }

    [Fact]
    public async Task DynamicChoices_LastOfMany_Empty_EntersReview()
    {
        var dyn = new ChoiceStep
        {
            Id = "c", Title = "C", Skippable = false, AutoSkipIfChoicesEmpty = true,
            DynamicChoicesProvider = _ => Task.FromResult<IReadOnlyList<WizardChoice>>([]),
        };
        var vm = new WizardDialogViewModel(Runner(Text("a"), dyn));
        vm.TextAnswer = "x";
        await vm.NextCommand.ExecuteAsync(null); // reach dyn (last) -> empty -> review (>1 visible step)
        Assert.True(vm.IsReviewMode);
    }
}
