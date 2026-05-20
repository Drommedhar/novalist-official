using Novalist.Desktop.Localization;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

[Collection("Avalonia")]
public class ContextSidebarSceneAnalysisViewModelTests
{
    static ContextSidebarSceneAnalysisViewModelTests()
    {
        var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Locales");
        Loc.Instance.Initialize(dir, "en");
    }

    private sealed class Captures
    {
        public string? Pov;
        public string? Emotion;
        public int? Intensity;
        public string? Conflict;
        public IReadOnlyList<string>? Tags;
        public bool ResetPov, ResetEmotion, ResetIntensity, ResetConflict, ResetTags;
    }

    private static (ContextSidebarSceneAnalysisViewModel Vm, Captures C) Build(
        string pov = "Alice",
        string emotion = "tense",
        int intensity = 3,
        string conflict = "they argued",
        IReadOnlyList<string>? tags = null,
        IReadOnlyList<string>? povOptions = null)
    {
        var c = new Captures();
        var vm = new ContextSidebarSceneAnalysisViewModel(
            pov,
            emotion,
            intensity,
            conflict,
            tags ?? ["dialogue-heavy"],
            wordCount: 350,
            dialogueRatio: 0.4,
            averageSentenceLength: 12.5,
            sparkline: new ContextSidebarSparklineViewModel(180, 44, "M0,0", "M0,0 L1,1",
            [
                new ContextSidebarSparkPointViewModel(1, 1, 10, true),
                new ContextSidebarSparkPointViewModel(2, 2, 6, false),
            ]),
            hasPovOverride: false,
            hasEmotionOverride: false,
            hasIntensityOverride: false,
            hasConflictOverride: false,
            hasTagsOverride: false,
            povOptions: povOptions ?? ["Alice", "Bob", "Alicia"],
            emotionOptions: ["neutral", "tense", "joyful"],
            savePovAsync: v => { c.Pov = v; return Task.CompletedTask; },
            saveEmotionAsync: v => { c.Emotion = v; return Task.CompletedTask; },
            saveIntensityAsync: v => { c.Intensity = v; return Task.CompletedTask; },
            saveConflictAsync: v => { c.Conflict = v; return Task.CompletedTask; },
            saveTagsAsync: v => { c.Tags = v; return Task.CompletedTask; },
            resetPovAsync: () => { c.ResetPov = true; return Task.CompletedTask; },
            resetEmotionAsync: () => { c.ResetEmotion = true; return Task.CompletedTask; },
            resetIntensityAsync: () => { c.ResetIntensity = true; return Task.CompletedTask; },
            resetConflictAsync: () => { c.ResetConflict = true; return Task.CompletedTask; },
            resetTagsAsync: () => { c.ResetTags = true; return Task.CompletedTask; });
        return (vm, c);
    }

    [AvaloniaFact]
    public void Constructor_ComputesDisplayProps()
    {
        var (vm, _) = Build(intensity: 3);
        Assert.Equal("Alice", vm.PovDisplay);
        Assert.Equal("+3", vm.IntensityDisplay);
        Assert.True(vm.IsPositiveIntensity);
        Assert.False(vm.IsNegativeIntensity);
        Assert.True(vm.HasConflict);
        Assert.True(vm.HasTags);
        Assert.False(string.IsNullOrEmpty(vm.WordCountDisplay));
        Assert.False(string.IsNullOrEmpty(vm.DialogueDisplay));
        Assert.True(vm.IntensityBarWidth > 0);
        Assert.False(string.IsNullOrEmpty(vm.EmotionDisplay));
        Assert.False(string.IsNullOrEmpty(vm.ConflictDisplay));
    }

    [AvaloniaFact]
    public void Defaults_EmptyPovAndConflict()
    {
        var (vm, _) = Build(pov: "", conflict: "", intensity: -4, emotion: "");
        Assert.Equal(Loc.T("pov.unknown"), vm.PovDisplay);
        Assert.Equal("neutral", vm.Emotion); // blank -> neutral
        Assert.Equal("-4", vm.IntensityDisplay);
        Assert.True(vm.IsNegativeIntensity);
        Assert.False(vm.HasConflict);
        Assert.Equal(Loc.T("context.none"), vm.ConflictDisplay);
        Assert.True(vm.IntensityBarLeft < 42); // negative bar shifts left
    }

    [AvaloniaFact]
    public async Task Pov_BeginSaveChanged()
    {
        var (vm, c) = Build();
        vm.BeginEditPovCommand.Execute(null);
        Assert.True(vm.IsEditingPov);
        vm.PovInput = "Bob";
        await vm.SavePovCommand.ExecuteAsync(null);
        Assert.Equal("Bob", c.Pov);
    }

    [AvaloniaFact]
    public async Task Pov_SaveUnchanged_CancelsNoSave()
    {
        var (vm, c) = Build(pov: "Alice");
        vm.BeginEditPovCommand.Execute(null);
        vm.PovInput = "Alice"; // unchanged
        await vm.SavePovCommand.ExecuteAsync(null);
        Assert.Null(c.Pov);
        Assert.False(vm.IsEditingPov);
    }

    [AvaloniaFact]
    public async Task Pov_ResetAndCommitAndSelectSuggestion()
    {
        var (vm, c) = Build();
        await vm.ResetPovCommand.ExecuteAsync(null);
        Assert.True(c.ResetPov);

        vm.PovInput = "Bob";
        await vm.CommitPovAsync();
        Assert.Equal("Bob", c.Pov);

        await vm.SelectPovSuggestionAsync("Alicia");
        Assert.Equal("Alicia", c.Pov);
    }

    [AvaloniaFact]
    public void PovSuggestions_FilterShowHide()
    {
        var (vm, _) = Build(povOptions: ["Alice", "Alicia", "Bob"]);
        vm.BeginEditPovCommand.Execute(null);
        vm.UpdatePovSuggestions("Ali");
        Assert.True(vm.HasPovSuggestions);
        Assert.True(vm.IsPovSuggestionOpen);
        Assert.True(vm.PovSuggestionsVisible);
        Assert.Equal(2, vm.PovSuggestions.Count);

        vm.UpdatePovSuggestions("   "); // blank -> hide + clear
        Assert.False(vm.IsPovSuggestionOpen);
        Assert.Empty(vm.PovSuggestions);

        vm.UpdatePovSuggestions("zzz"); // no match -> none open
        Assert.False(vm.IsPovSuggestionOpen);

        vm.HidePovSuggestions();
        Assert.False(vm.PovSuggestionsVisible);
    }

    [AvaloniaFact]
    public async Task Emotion_BeginSaveChangedUnchangedReset()
    {
        var (vm, c) = Build(emotion: "tense");
        vm.BeginEditEmotionCommand.Execute(null);
        Assert.True(vm.IsEditingEmotion);
        vm.SelectedEmotion = Loc.T("emotion.joyful");
        await vm.SaveEmotionCommand.ExecuteAsync(null);
        Assert.Equal("joyful", c.Emotion);

        // unchanged path
        var (vm2, c2) = Build(emotion: "tense");
        vm2.SelectedEmotion = Loc.T("emotion.tense");
        await vm2.CommitEmotionAsync();
        Assert.Null(c2.Emotion);

        await vm.ResetEmotionCommand.ExecuteAsync(null);
        Assert.True(c.ResetEmotion);
    }

    [AvaloniaFact]
    public async Task Intensity_BeginSaveParseClampUnchangedReset()
    {
        var (vm, c) = Build(intensity: 3);
        vm.BeginEditIntensityCommand.Execute(null);
        vm.IntensityInput = "99"; // clamp to 10
        await vm.SaveIntensityCommand.ExecuteAsync(null);
        Assert.Equal(10, c.Intensity);

        // unparseable -> falls back to current (3) -> unchanged -> cancel
        var (vm2, c2) = Build(intensity: 3);
        vm2.IntensityInput = "abc";
        await vm2.CommitIntensityAsync();
        Assert.Null(c2.Intensity);

        await vm.ResetIntensityCommand.ExecuteAsync(null);
        Assert.True(c.ResetIntensity);
    }

    [AvaloniaFact]
    public async Task Conflict_BeginSaveUnchangedReset()
    {
        var (vm, c) = Build(conflict: "fight");
        vm.BeginEditConflictCommand.Execute(null);
        vm.ConflictInput = "new conflict";
        await vm.SaveConflictCommand.ExecuteAsync(null);
        Assert.Equal("new conflict", c.Conflict);

        var (vm2, c2) = Build(conflict: "fight");
        vm2.ConflictInput = "fight";
        await vm2.CommitConflictAsync();
        Assert.Null(c2.Conflict);

        await vm.ResetConflictCommand.ExecuteAsync(null);
        Assert.True(c.ResetConflict);
    }

    [AvaloniaFact]
    public async Task Tags_BeginSaveUnchangedReset()
    {
        var (vm, c) = Build(tags: ["a", "b"]);
        vm.BeginEditTagsCommand.Execute(null);
        vm.TagsInput = "x, y, x"; // dedup
        await vm.SaveTagsCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "x", "y" }, c.Tags);

        var (vm2, c2) = Build(tags: ["a", "b"]);
        vm2.TagsInput = "a, b"; // unchanged
        await vm2.CommitTagsAsync();
        Assert.Null(c2.Tags);

        await vm.ResetTagsCommand.ExecuteAsync(null);
        Assert.True(c.ResetTags);
    }

    [AvaloniaFact]
    public void CancelEditing_ResetsAllFlags()
    {
        var (vm, _) = Build();
        vm.BeginEditPovCommand.Execute(null);
        vm.SuppressPovLostFocusCommit = true;
        vm.CancelEditing();
        Assert.False(vm.IsEditingPov);
        Assert.False(vm.SuppressPovLostFocusCommit);
    }

    [AvaloniaFact]
    public void Sparkline_Props()
    {
        var (vm, _) = Build();
        Assert.True(vm.Sparkline.HasLine);
        Assert.True(vm.Sparkline.HasSparkPoints);
        Assert.True(vm.Sparkline.SparkPoints[0].IsCurrent);
        Assert.Equal(180, vm.Sparkline.Width);
    }
}
