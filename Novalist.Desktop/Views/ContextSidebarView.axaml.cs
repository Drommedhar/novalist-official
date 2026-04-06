using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Novalist.Desktop.ViewModels;

namespace Novalist.Desktop.Views;

public partial class ContextSidebarView : UserControl
{
    public ContextSidebarView()
    {
        InitializeComponent();
    }

    private static ContextSidebarSceneAnalysisViewModel? GetSceneAnalysis(object? sender)
        => sender is Control { DataContext: ContextSidebarSceneAnalysisViewModel analysis }
            ? analysis
            : null;

    private void OnSceneAnalysisPovGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (sender is TextBox textBox && GetSceneAnalysis(sender) is { } analysis)
            analysis.UpdatePovSuggestions(textBox.Text);
    }

    private void OnSceneAnalysisPovTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox && GetSceneAnalysis(sender) is { } analysis)
            analysis.UpdatePovSuggestions(textBox.Text);
    }

    private async void OnSceneAnalysisPovLostFocus(object? sender, RoutedEventArgs e)
    {
        if (GetSceneAnalysis(sender) is not { } analysis || !analysis.IsEditingPov)
            return;

        if (analysis.SuppressPovLostFocusCommit)
        {
            analysis.SuppressPovLostFocusCommit = false;
            return;
        }

        analysis.HidePovSuggestions();
        await analysis.CommitPovAsync();
    }

    private async void OnSceneAnalysisPovKeyUp(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox || GetSceneAnalysis(sender) is not { } analysis)
            return;

        if (e.Key == Key.Enter)
        {
            analysis.HidePovSuggestions();
            await analysis.CommitPovAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            analysis.CancelEditing();
            e.Handled = true;
            return;
        }

        analysis.UpdatePovSuggestions(textBox.Text);
    }

    private void OnSceneAnalysisPovSuggestionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (GetSceneAnalysis(sender) is { } analysis)
            analysis.SuppressPovLostFocusCommit = true;
    }

    private async void OnSceneAnalysisPovSuggestionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: string selectedItem } listBox || GetSceneAnalysis(sender) is not { } analysis)
            return;

        analysis.SuppressPovLostFocusCommit = false;
        listBox.SelectedItem = null;
        await analysis.SelectPovSuggestionAsync(selectedItem);
    }

    private async void OnSceneAnalysisEmotionOptionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: string selectedEmotion } listBox || GetSceneAnalysis(sender) is not { } analysis || !analysis.IsEditingEmotion)
            return;

        analysis.SelectedEmotion = selectedEmotion;
        listBox.SelectedItem = null;
        await analysis.CommitEmotionAsync();
    }

    private async void OnSceneAnalysisIntensityLostFocus(object? sender, RoutedEventArgs e)
    {
        if (GetSceneAnalysis(sender) is { IsEditingIntensity: true } analysis)
            await analysis.CommitIntensityAsync();
    }

    private async void OnSceneAnalysisIntensityKeyUp(object? sender, KeyEventArgs e)
    {
        if (GetSceneAnalysis(sender) is not { } analysis)
            return;

        if (e.Key == Key.Enter)
        {
            await analysis.CommitIntensityAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            analysis.CancelEditing();
            e.Handled = true;
        }
    }

    private async void OnSceneAnalysisConflictLostFocus(object? sender, RoutedEventArgs e)
    {
        if (GetSceneAnalysis(sender) is { IsEditingConflict: true } analysis)
            await analysis.CommitConflictAsync();
    }

    private async void OnSceneAnalysisConflictKeyUp(object? sender, KeyEventArgs e)
    {
        if (GetSceneAnalysis(sender) is not { } analysis)
            return;

        if (e.Key == Key.Enter)
        {
            await analysis.CommitConflictAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            analysis.CancelEditing();
            e.Handled = true;
        }
    }

    private async void OnSceneAnalysisTagsLostFocus(object? sender, RoutedEventArgs e)
    {
        if (GetSceneAnalysis(sender) is { IsEditingTags: true } analysis)
            await analysis.CommitTagsAsync();
    }

    private async void OnSceneAnalysisTagsKeyUp(object? sender, KeyEventArgs e)
    {
        if (GetSceneAnalysis(sender) is not { } analysis)
            return;

        if (e.Key == Key.Enter)
        {
            await analysis.CommitTagsAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            analysis.CancelEditing();
            e.Handled = true;
        }
    }
}