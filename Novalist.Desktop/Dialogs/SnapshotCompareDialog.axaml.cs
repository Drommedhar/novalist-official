using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;

namespace Novalist.Desktop.Dialogs;

public partial class SnapshotCompareDialog : UserControl
{
    public TaskCompletionSource DialogClosed { get; } = new();

    private readonly SceneSnapshot _snapshot;
    private readonly string _currentHtml;
    private readonly IProjectService _projectService;
    private readonly ChapterData _chapter;
    private readonly SceneData _scene;
    private readonly Action? _onPartialApply;

    private readonly List<CompareRow> _rows = new();

    private static readonly IBrush AddedBg = new SolidColorBrush(Color.Parse("#352EA046"));
    private static readonly IBrush RemovedBg = new SolidColorBrush(Color.Parse("#35E5484D"));
    private static readonly IBrush AddedWordBg = new SolidColorBrush(Color.Parse("#662EA046"));
    private static readonly IBrush RemovedWordBg = new SolidColorBrush(Color.Parse("#66E5484D"));

    public SnapshotCompareDialog()
    {
        InitializeComponent();
    }

    public SnapshotCompareDialog(
        SceneSnapshot snapshot,
        string currentHtml,
        IProjectService projectService,
        ChapterData chapter,
        SceneData scene,
        Action? onPartialApply) : this()
    {
        _snapshot = snapshot;
        _currentHtml = currentHtml;
        _projectService = projectService;
        _chapter = chapter;
        _scene = scene;
        _onPartialApply = onPartialApply;

        HeaderText.Text = $"{snapshot.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}  →  {Localization.Loc.T("snapshots.compareCurrent")}";
        BuildRows();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SizeToTopLevel();
        if (TopLevel.GetTopLevel(this) is { } top)
        {
            top.PropertyChanged += (_, args) =>
            {
                if (args.Property == TopLevel.ClientSizeProperty)
                    SizeToTopLevel();
            };
        }
    }

    private void SizeToTopLevel()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var bounds = top.ClientSize;
        Width = Math.Max(720, bounds.Width - 80);
        Height = Math.Max(480, bounds.Height - 80);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
            DialogClosed.TrySetResult();
    }

    private void BuildRows()
    {
        var leftPlain = TextDiff.StripHtml(_snapshot.Content);
        var rightPlain = TextDiff.StripHtml(_currentHtml);
        var paired = TextDiff.ComputePaired(leftPlain, rightPlain);

        foreach (var p in paired)
        {
            var row = BuildRow(p);
            _rows.Add(row);
            RowsHost.Children.Add(row.Container);
        }
    }

    private CompareRow BuildRow(PairedDiffRow p)
    {
        var row = new CompareRow { Source = p };

        IBrush leftBg = Brushes.Transparent;
        IBrush rightBg = Brushes.Transparent;
        bool showCheckbox = false;

        if (p.IsEqual)
        {
            // Plain context lines.
        }
        else if (p.IsLeftOnly)
        {
            leftBg = RemovedBg;
            showCheckbox = true; // Checked = re-insert this snapshot line into current.
        }
        else if (p.IsRightOnly)
        {
            rightBg = AddedBg;
            showCheckbox = true; // Checked = drop this current line, use snapshot (which is empty here).
        }
        else if (p.IsChanged)
        {
            leftBg = RemovedBg;
            rightBg = AddedBg;
            showCheckbox = true;
        }

        var leftBlock = BuildLineTextBlock(p.LeftText ?? string.Empty,
            wordSpans: p.IsChanged ? TextDiff.WordDiff(p.LeftText ?? string.Empty, p.RightText ?? string.Empty) : null,
            isLeftSide: true);
        var rightBlock = BuildLineTextBlock(p.RightText ?? string.Empty,
            wordSpans: p.IsChanged ? TextDiff.WordDiff(p.LeftText ?? string.Empty, p.RightText ?? string.Empty) : null,
            isLeftSide: false);

        var leftMarker = p.LeftIndex.HasValue ? (p.LeftIndex.Value + 1).ToString() : string.Empty;
        var rightMarker = p.RightIndex.HasValue ? (p.RightIndex.Value + 1).ToString() : string.Empty;

        var leftPanel = WrapPanel(leftMarker, leftBlock, leftBg, borderRight: false);
        var rightPanel = WrapPanel(rightMarker, rightBlock, rightBg, borderRight: true);

        var checkbox = new CheckBox
        {
            IsVisible = showCheckbox,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };
        ToolTip.SetTip(checkbox, Localization.Loc.T("snapshots.compareApplyHunkTooltip"));
        row.Checkbox = checkbox;

        var grid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("40,*,*"),
            Margin = new Thickness(0, 0, 0, 1)
        };
        Grid.SetColumn(checkbox, 0);
        Grid.SetColumn(leftPanel, 1);
        Grid.SetColumn(rightPanel, 2);
        grid.Children.Add(checkbox);
        grid.Children.Add(leftPanel);
        grid.Children.Add(rightPanel);
        row.Container = grid;
        return row;
    }

    private static Border WrapPanel(string marker, TextBlock content, IBrush background, bool borderRight)
    {
        var grid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*")
        };
        var markerBlock = new TextBlock
        {
            Text = marker,
            FontFamily = new FontFamily("Consolas, Menlo, monospace"),
            FontSize = 11,
            MinWidth = 28,
            Foreground = (IBrush)Application.Current!.FindResource("SubtleText")!
        };
        Grid.SetColumn(markerBlock, 0);
        Grid.SetColumn(content, 1);
        grid.Children.Add(markerBlock);
        grid.Children.Add(content);
        return new Border
        {
            Background = background,
            BorderBrush = (IBrush)Application.Current!.FindResource("ExplorerBorder")!,
            BorderThickness = borderRight ? new Thickness(1, 0, 1, 0) : new Thickness(1, 0, 0, 0),
            Padding = new Thickness(6, 2, 6, 2),
            Child = grid
        };
    }

    private static TextBlock BuildLineTextBlock(string text, List<WordDiffSpan>? wordSpans, bool isLeftSide)
    {
        var tb = new TextBlock
        {
            FontFamily = new FontFamily("Consolas, Menlo, monospace"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (IBrush)Application.Current!.FindResource("NormalText")!
        };

        if (wordSpans == null)
        {
            tb.Text = text;
            return tb;
        }

        // Build inlines: each span is Equal, Removed (only on left), or Added (only on right).
        foreach (var span in wordSpans)
        {
            switch (span.Op)
            {
                case WordDiffOp.Equal:
                    tb.Inlines!.Add(new Run(span.Text));
                    break;
                case WordDiffOp.Removed:
                    if (isLeftSide)
                    {
                        var r = new Run(span.Text) { Background = RemovedWordBg, FontWeight = FontWeight.SemiBold };
                        tb.Inlines!.Add(r);
                    }
                    break;
                case WordDiffOp.Added:
                    if (!isLeftSide)
                    {
                        var r = new Run(span.Text) { Background = AddedWordBg, FontWeight = FontWeight.SemiBold };
                        tb.Inlines!.Add(r);
                    }
                    break;
            }
        }

        return tb;
    }

    private void OnSelectAll(object? sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
            if (row.Checkbox?.IsVisible == true)
                row.Checkbox.IsChecked = true;
    }

    private void OnSelectNone(object? sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
            if (row.Checkbox != null)
                row.Checkbox.IsChecked = false;
    }

    private async void OnApplySelected(object? sender, RoutedEventArgs e)
    {
        try
        {
            await ApplySelectedInnerAsync();
        }
        catch (Exception ex)
        {
            Utilities.Log.Error("OnApplySelected failed", ex);
        }
    }

    private async Task ApplySelectedInnerAsync()
    {
        // Per-row decision: each row independently picks left (snapshot) or
        // right (current). Equal rows always emit their text. Left-only checked
        // = re-insert snapshot line. Right-only checked = drop the current
        // line. Changed checked = use snapshot line; unchecked = keep current.
        var resultLines = new List<string>();

        foreach (var row in _rows)
        {
            var p = row.Source;
            var checkedBox = row.Checkbox?.IsChecked == true;

            if (p.IsEqual)
            {
                resultLines.Add(p.LeftText ?? string.Empty);
            }
            else if (p.IsLeftOnly)
            {
                if (checkedBox && p.LeftText != null)
                    resultLines.Add(p.LeftText);
            }
            else if (p.IsRightOnly)
            {
                if (!checkedBox && p.RightText != null)
                    resultLines.Add(p.RightText);
            }
            else if (p.IsChanged)
            {
                if (checkedBox && p.LeftText != null)
                    resultLines.Add(p.LeftText);
                else if (!checkedBox && p.RightText != null)
                    resultLines.Add(p.RightText);
            }
        }

        // Auto-snapshot before applying.
        await App.SnapshotService.TakeAsync(_chapter, _scene, "Auto-snapshot before partial apply");

        var html = string.Join("\n", resultLines.Select(line => $"<p>{System.Net.WebUtility.HtmlEncode(line)}</p>"));
        await _projectService.WriteSceneContentAsync(_chapter, _scene, html);

        _scene.WordCount = TextStatistics.Calculate(string.Join(" ", resultLines), "en").WordCount;
        await _projectService.SaveScenesAsync();

        _onPartialApply?.Invoke();
        DialogClosed.TrySetResult();
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        DialogClosed.TrySetResult();
    }

    private sealed class CompareRow
    {
        public PairedDiffRow Source { get; init; } = null!;
        public Grid Container { get; set; } = null!;
        public CheckBox? Checkbox { get; set; }
    }
}
