using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Novalist.Desktop.Localization;

namespace Novalist.Desktop.Dialogs;

public partial class InverseRelationshipDialog : UserControl
{
    private readonly bool _allowEmpty;
    private readonly List<string> _allSuggestions = [];
    private readonly ObservableCollection<string> _visibleSuggestions = [];

    public string? Result { get; private set; }
    public TaskCompletionSource DialogClosed { get; } = new();

    public InverseRelationshipDialog()
    {
        InitializeComponent();
        SuggestionsList.ItemsSource = _visibleSuggestions;
    }

    public InverseRelationshipDialog(
        string relationshipRole,
        string sourceName,
        string targetName,
        IReadOnlyList<string> suggestions,
        string defaultValue = "",
        bool allowEmpty = false) : this()
    {
        _allowEmpty = allowEmpty;
        _allSuggestions = suggestions
            .Where(suggestion => !string.IsNullOrWhiteSpace(suggestion))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(suggestion => suggestion, StringComparer.OrdinalIgnoreCase)
            .ToList();

        DescriptionText.Text = Loc.T("dialog.inverseRelDescription", targetName, relationshipRole);
        QuestionText.Text = Loc.T("dialog.inverseRelQuestion", targetName, sourceName);
        InputBox.Text = defaultValue;

        InputBox.TextChanged += OnInputChanged;
        RenderSuggestions(InputBox.Text);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Input);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Result = null;
            DialogClosed.TrySetResult();
        }
    }

    private void OnInputChanged(object? sender, TextChangedEventArgs e)
    {
        RenderSuggestions(InputBox.Text);
    }

    private void RenderSuggestions(string? query)
    {
        _visibleSuggestions.Clear();
        var normalizedQuery = query?.Trim() ?? string.Empty;

        foreach (var suggestion in _allSuggestions)
        {
            if (normalizedQuery.Length > 0 && !suggestion.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                continue;

            _visibleSuggestions.Add(suggestion);
            if (_visibleSuggestions.Count >= 8)
                break;
        }

        SuggestionsList.IsVisible = _visibleSuggestions.Count > 0;
    }

    private void OnSuggestionClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string suggestion })
        {
            InputBox.Text = suggestion;
            InputBox.CaretIndex = suggestion.Length;
            Submit();
        }
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        Submit();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        DialogClosed.TrySetResult();
    }

    private void Submit()
    {
        var value = InputBox.Text?.Trim();
        if (_allowEmpty || !string.IsNullOrEmpty(value))
        {
            Result = value ?? string.Empty;
            DialogClosed.TrySetResult();
        }
    }
}
