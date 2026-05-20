using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Novalist.Desktop.Services;
using Novalist.Sdk.Models.Wizards;
using Xunit;

namespace Novalist.Desktop.Tests.Services;

public class WizardRunnerTests
{
    private static WizardDefinition Def(params WizardStep[] steps)
        => new() { Id = "wiz", Steps = steps.ToList() };

    private static TextStep Step(string id, WizardCondition? visibleWhen = null)
        => new() { Id = id, Title = id, VisibleWhen = visibleWhen };

    private static (WizardRunner Sut, TempDir Dir) Build(out FileService fs)
    {
        var dir = new TempDir();
        fs = new FileService();
        var capturedFs = fs;
        return (new WizardRunner(capturedFs, () => dir.Path), dir);
    }

    [Fact]
    public async Task Start_InitializesStepsAndIndex()
    {
        var (sut, dir) = Build(out _);
        using var _d = dir;
        await sut.StartAsync(Def(Step("a"), Step("b")));
        Assert.Equal(2, sut.VisibleSteps.Count);
        Assert.Equal("a", sut.CurrentStep!.Id);
        Assert.True(sut.IsFirstStep);
        Assert.False(sut.IsLastStep);
        Assert.Equal("1 / 2", sut.StepCounterDisplay);
    }

    [Fact]
    public async Task Start_ResumeFrom_RestoresIndex()
    {
        var (sut, dir) = Build(out _);
        using var _d = dir;
        var resume = new WizardResult { CurrentStepIndex = 1 };
        await sut.StartAsync(Def(Step("a"), Step("b")), resume);
        Assert.Equal(1, sut.CurrentIndex);
        Assert.Equal("b", sut.CurrentStep!.Id);
    }

    [Fact]
    public async Task Navigation_NextBackJump()
    {
        var (sut, dir) = Build(out _);
        using var _d = dir;
        await sut.StartAsync(Def(Step("a"), Step("b"), Step("c")));

        await sut.NextAsync();
        Assert.Equal(1, sut.CurrentIndex);
        await sut.BackAsync();
        Assert.Equal(0, sut.CurrentIndex);
        await sut.BackAsync(); // at 0 -> no-op
        Assert.Equal(0, sut.CurrentIndex);

        await sut.JumpToAsync(2);
        Assert.Equal(2, sut.CurrentIndex);
        Assert.True(sut.IsLastStep);
        await sut.JumpToAsync(99); // out of range -> no-op
        Assert.Equal(2, sut.CurrentIndex);

        await sut.JumpToStepAsync("a");
        Assert.Equal(0, sut.CurrentIndex);
        await sut.JumpToStepAsync("missing"); // no-op
        Assert.Equal(0, sut.CurrentIndex);
    }

    [Fact]
    public async Task Next_OnLastStep_Finishes()
    {
        var (sut, dir) = Build(out _);
        using var _d = dir;
        var completed = false;
        sut.Completed += () => completed = true;
        await sut.StartAsync(Def(Step("a")));
        await sut.NextAsync(); // last step -> finish
        Assert.True(completed);
        Assert.True(sut.Result.Completed);
    }

    [Fact]
    public async Task SetAnswer_StoresAndRemoves()
    {
        var (sut, dir) = Build(out _);
        using var _d = dir;
        await sut.StartAsync(Def(Step("a")));
        sut.SetAnswer("a", new WizardAnswer { Text = "hi" });
        Assert.True(sut.Result.Answers.ContainsKey("a"));
        sut.SetAnswer("a", new WizardAnswer()); // empty -> removed
        Assert.False(sut.Result.Answers.ContainsKey("a"));
    }

    [Fact]
    public async Task Skip_RemovesAnswerAndAdvances()
    {
        var (sut, dir) = Build(out _);
        using var _d = dir;
        await sut.StartAsync(Def(Step("a"), Step("b")));
        sut.SetAnswer("a", new WizardAnswer { Text = "x" });
        await sut.SkipAsync();
        Assert.False(sut.Result.Answers.ContainsKey("a"));
        Assert.Equal(1, sut.CurrentIndex);
    }

    [Theory]
    [InlineData("equals", "yes", "yes", true)]
    [InlineData("equals", "yes", "no", false)]
    [InlineData("notEquals", "yes", "no", true)]
    [InlineData("contains", "hello world", "world", true)]
    [InlineData("present", "anything", "ignored", true)]
    [InlineData("unknownOp", "x", "y", true)]
    public async Task VisibleWhen_ConditionalSteps(string op, string answer, string condValue, bool childVisible)
    {
        var (sut, dir) = Build(out _);
        using var _d = dir;
        var cond = new WizardCondition { StepId = "a", Operator = op, Value = condValue };
        await sut.StartAsync(Def(Step("a"), Step("b", cond)));
        sut.SetAnswer("a", new WizardAnswer { Text = answer });
        Assert.Equal(childVisible ? 2 : 1, sut.VisibleSteps.Count);
    }

    [Fact]
    public async Task VisibleWhen_GateParentHidden_HidesChild()
    {
        var (sut, dir) = Build(out _);
        using var _d = dir;
        var bCond = new WizardCondition { StepId = "a", Operator = "equals", Value = "show" };
        var cCond = new WizardCondition { StepId = "b", Operator = "present" };
        await sut.StartAsync(Def(Step("a"), Step("b", bCond), Step("c", cCond)));
        // a != "show" -> b hidden -> c (gated on b) also hidden
        Assert.Single(sut.VisibleSteps);
    }

    [Fact]
    public async Task Cancel_RaisesEvent()
    {
        var (sut, dir) = Build(out _);
        using var _d = dir;
        var cancelled = false;
        sut.Cancelled += () => cancelled = true;
        await sut.StartAsync(Def(Step("a")));
        await sut.CancelAsync();
        Assert.True(cancelled);
    }

    [Fact]
    public async Task Persist_And_TryLoadState_RoundTrips()
    {
        var dir = new TempDir();
        using var _d = dir;
        var fs = new FileService();
        var sut = new WizardRunner(fs, () => dir.Path);
        await sut.StartAsync(Def(Step("a"), Step("b")));
        sut.SetAnswer("a", new WizardAnswer { Text = "saved" });
        await sut.NextAsync(); // persists state

        var loaded = await WizardRunner.TryLoadStateAsync(fs, dir.Path, "wiz");
        Assert.NotNull(loaded);
        Assert.Equal("saved", loaded!.Answers["a"].Text);
    }

    [Fact]
    public async Task Finish_DeletesPersistedState()
    {
        var dir = new TempDir();
        using var _d = dir;
        var fs = new FileService();
        var sut = new WizardRunner(fs, () => dir.Path);
        await sut.StartAsync(Def(Step("a")));
        sut.SetAnswer("a", new WizardAnswer { Text = "x" });
        await sut.FinishAsync();
        Assert.Null(await WizardRunner.TryLoadStateAsync(fs, dir.Path, "wiz"));
    }

    [Fact]
    public async Task NoStateDir_DoesNotPersist()
    {
        var fs = new FileService();
        var sut = new WizardRunner(fs, () => null); // provider returns null -> no persistence
        await sut.StartAsync(Def(Step("a")));
        sut.SetAnswer("a", new WizardAnswer { Text = "x" });
        await sut.NextAsync(); // no throw, nothing written
    }

    [Fact]
    public async Task Persist_InvalidStateDir_SwallowsError()
    {
        var fs = new FileService();
        var sut = new WizardRunner(fs, () => "bad\0dir"); // CreateDirectory throws -> catch swallows
        await sut.StartAsync(Def(Step("a")));
        sut.SetAnswer("a", new WizardAnswer { Text = "x" }); // persist fails silently
        await sut.NextAsync(); // no throw
    }

    [Fact]
    public async Task TryLoadState_MissingAndCorrupt_ReturnNull()
    {
        var dir = new TempDir();
        using var _d = dir;
        var fs = new FileService();
        Assert.Null(await WizardRunner.TryLoadStateAsync(fs, dir.Path, "nope")); // missing
        await File.WriteAllTextAsync(Path.Combine(dir.Path, "wizard-state-bad.json"), "{ corrupt");
        Assert.Null(await WizardRunner.TryLoadStateAsync(fs, dir.Path, "bad"));  // corrupt
    }

    [Fact]
    public async Task EmptyDefinition_CurrentStepNull()
    {
        var (sut, dir) = Build(out _);
        using var _d = dir;
        await sut.StartAsync(Def());
        Assert.Null(sut.CurrentStep);
        Assert.Equal(string.Empty, sut.StepCounterDisplay);
    }
}
