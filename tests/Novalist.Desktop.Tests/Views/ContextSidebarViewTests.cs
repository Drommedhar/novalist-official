using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Novalist.Desktop.Localization;
using Novalist.Desktop.Tests.TestHelpers;
using Novalist.Desktop.ViewModels;
using Novalist.Desktop.Views;
using Xunit;

namespace Novalist.Desktop.Tests.Views;

[Collection("Avalonia")]
public class ContextSidebarViewTests
{
    static ContextSidebarViewTests()
        => Loc.Instance.Initialize(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Locales"), "en");

    private static ContextSidebarSceneAnalysisViewModel Analysis()
        => new(
            "Alice", "tense", 3, "they argued", new[] { "tag" },
            wordCount: 350, dialogueRatio: 0.4, averageSentenceLength: 12.5,
            sparkline: new ContextSidebarSparklineViewModel(180, 44, "M0,0", "M0,0 L1,1",
                new List<ContextSidebarSparkPointViewModel>
                {
                    new(1, 1, 10, true),
                    new(2, 2, 6, false),
                }),
            hasPovOverride: false, hasEmotionOverride: false, hasIntensityOverride: false,
            hasConflictOverride: false, hasTagsOverride: false,
            povOptions: new[] { "Alice", "Bob" }, emotionOptions: new[] { "neutral", "tense" },
            savePovAsync: _ => Task.CompletedTask, saveEmotionAsync: _ => Task.CompletedTask,
            saveIntensityAsync: _ => Task.CompletedTask, saveConflictAsync: _ => Task.CompletedTask,
            saveTagsAsync: _ => Task.CompletedTask,
            resetPovAsync: () => Task.CompletedTask, resetEmotionAsync: () => Task.CompletedTask,
            resetIntensityAsync: () => Task.CompletedTask, resetConflictAsync: () => Task.CompletedTask,
            resetTagsAsync: () => Task.CompletedTask);

    private static KeyEventArgs Key2(Key k)
        => new() { Key = k, RoutedEvent = InputElement.KeyUpEvent };

    [AvaloniaFact]
    public void Ctor_Lambdas_FireOnDataContextAndAttach()
    {
        var view = new ContextSidebarView();
        view.DataContext = "x"; // DataContextChanged lambda (cast fails -> VM null branch)
        DialogHost.Show(view);   // AttachedToVisualTree lambda
    }

    [AvaloniaFact]
    public void Pov_GotFocus_TextChanged_KeyUp_SuggestionPressed_LostFocus()
    {
        var a = Analysis();
        var view = new ContextSidebarView();
        var tb = new TextBox { DataContext = a, Text = "Al" };

        DialogHost.Invoke(view, "OnSceneAnalysisPovGotFocus", tb, (FocusChangedEventArgs?)null);
        DialogHost.Invoke(view, "OnSceneAnalysisPovTextChanged", tb, (TextChangedEventArgs?)null);

        a.BeginEditPovCommand.Execute(null);
        DialogHost.Invoke(view, "OnSceneAnalysisPovKeyUp", tb, Key2(Key.A));        // update branch
        DialogHost.Invoke(view, "OnSceneAnalysisPovKeyUp", tb, Key2(Key.Enter));    // commit branch
        a.BeginEditPovCommand.Execute(null);
        DialogHost.Invoke(view, "OnSceneAnalysisPovKeyUp", tb, Key2(Key.Escape));   // cancel branch
        DialogHost.Invoke(view, "OnSceneAnalysisPovKeyUp", new Button(), Key2(Key.Enter)); // not textbox -> return

        var sugg = new Border { DataContext = a };
        DialogHost.Invoke(view, "OnSceneAnalysisPovSuggestionPointerPressed", sugg, DialogHost.UninitializedArgs<PointerPressedEventArgs>());
        Assert.True(a.SuppressPovLostFocusCommit);

        // LostFocus: suppress -> reset+return
        a.BeginEditPovCommand.Execute(null);
        a.SuppressPovLostFocusCommit = true;
        DialogHost.Invoke(view, "OnSceneAnalysisPovLostFocus", tb, new RoutedEventArgs());
        Assert.False(a.SuppressPovLostFocusCommit);

        // LostFocus: editing -> hide+commit
        a.BeginEditPovCommand.Execute(null);
        DialogHost.Invoke(view, "OnSceneAnalysisPovLostFocus", tb, new RoutedEventArgs());

        // LostFocus: not editing -> return
        DialogHost.Invoke(view, "OnSceneAnalysisPovLostFocus", tb, new RoutedEventArgs());
        // LostFocus: no analysis -> return
        DialogHost.Invoke(view, "OnSceneAnalysisPovLostFocus", new Button(), new RoutedEventArgs());
    }

    [AvaloniaFact]
    public void Pov_Emotion_SuggestionSelected()
    {
        var a = Analysis();
        var view = new ContextSidebarView();

        var povList = new ListBox { DataContext = a, ItemsSource = new[] { "Alice", "Bob" }, SelectedItem = "Bob" };
        DialogHost.Invoke(view, "OnSceneAnalysisPovSuggestionSelected", povList, (SelectionChangedEventArgs?)null);
        DialogHost.Invoke(view, "OnSceneAnalysisPovSuggestionSelected", new Button(), (SelectionChangedEventArgs?)null); // early return

        a.BeginEditEmotionCommand.Execute(null);
        var emoList = new ListBox { DataContext = a, ItemsSource = new[] { "tense" }, SelectedItem = "tense" };
        DialogHost.Invoke(view, "OnSceneAnalysisEmotionOptionSelected", emoList, (SelectionChangedEventArgs?)null);
        DialogHost.Invoke(view, "OnSceneAnalysisEmotionOptionSelected", new Button(), (SelectionChangedEventArgs?)null); // early return
    }

    [AvaloniaFact]
    public void Intensity_Conflict_Tags_LostFocus_KeyUp()
    {
        var a = Analysis();
        var view = new ContextSidebarView();
        var tb = new TextBox { DataContext = a };

        foreach (var (begin, lostFocus, keyUp) in new[]
        {
            ("BeginEditIntensityCommand", "OnSceneAnalysisIntensityLostFocus", "OnSceneAnalysisIntensityKeyUp"),
            ("BeginEditConflictCommand", "OnSceneAnalysisConflictLostFocus", "OnSceneAnalysisConflictKeyUp"),
            ("BeginEditTagsCommand", "OnSceneAnalysisTagsLostFocus", "OnSceneAnalysisTagsKeyUp"),
        })
        {
            var cmd = (CommunityToolkit.Mvvm.Input.IRelayCommand)typeof(ContextSidebarSceneAnalysisViewModel)
                .GetProperty(begin)!.GetValue(a)!;

            cmd.Execute(null);
            DialogHost.Invoke(view, lostFocus, tb, new RoutedEventArgs());     // editing -> commit
            DialogHost.Invoke(view, lostFocus, tb, new RoutedEventArgs());     // not editing -> no-op

            cmd.Execute(null);
            DialogHost.Invoke(view, keyUp, tb, Key2(Key.Enter));               // commit + handled
            cmd.Execute(null);
            DialogHost.Invoke(view, keyUp, tb, Key2(Key.Escape));              // cancel + handled
            DialogHost.Invoke(view, keyUp, tb, Key2(Key.A));                   // other -> nothing
            DialogHost.Invoke(view, keyUp, new Button(), Key2(Key.Enter));     // no analysis -> return
        }
    }
}
