using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Novalist.Desktop.Services;
using Novalist.Sdk.Models;

namespace Novalist.Desktop.Dialogs;

public partial class CommandPaletteDialog : UserControl
{
    public TaskCompletionSource DialogClosed { get; } = new();

    private readonly List<CommandPaletteItem> _all = new();

    public CommandPaletteDialog()
    {
        InitializeComponent();
    }

    public CommandPaletteDialog(IHotkeyService hotkeys) : this()
    {
        foreach (var d in hotkeys.GetAllDescriptors())
        {
            // Skip actions that aren't currently runnable.
            if (d.CanExecute != null && !d.CanExecute()) continue;
            var gesture = hotkeys.GetGesture(d.ActionId);
            _all.Add(new CommandPaletteItem(d)
            {
                Gesture = HumanizeGesture(gesture),
                HasGesture = !string.IsNullOrWhiteSpace(gesture)
            });
        }
        // Sort by category then name for stable initial order.
        _all = _all
            .OrderBy(i => i.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Refilter(string.Empty);

        QueryBox.AttachedToVisualTree += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                QueryBox.Focus();
                QueryBox.SelectAll();
            }, DispatcherPriority.Background);
        };
        QueryBox.TextChanged += (_, _) => Refilter(QueryBox.Text ?? string.Empty);
    }

    private void Refilter(string query)
    {
        IEnumerable<CommandPaletteItem> result;
        if (string.IsNullOrWhiteSpace(query))
        {
            result = _all;
        }
        else
        {
            var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            result = _all.Where(i =>
            {
                var hay = $"{i.DisplayName} {i.Category}";
                foreach (var t in tokens)
                {
                    if (hay.IndexOf(t, StringComparison.CurrentCultureIgnoreCase) < 0)
                        return false;
                }
                return true;
            });
        }
        var list = result.ToList();
        ResultsList.ItemsSource = list;
        if (list.Count > 0) ResultsList.SelectedIndex = 0;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
            DialogClosed.TrySetResult();
    }

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            if (ResultsList.ItemCount > 0)
            {
                ResultsList.SelectedIndex = Math.Min(ResultsList.SelectedIndex + 1, ResultsList.ItemCount - 1);
                ResultsList.ScrollIntoView(ResultsList.SelectedIndex);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (ResultsList.ItemCount > 0)
            {
                ResultsList.SelectedIndex = Math.Max(ResultsList.SelectedIndex - 1, 0);
                ResultsList.ScrollIntoView(ResultsList.SelectedIndex);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ExecuteSelected();
            e.Handled = true;
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteSelected();
            e.Handled = true;
        }
    }

    private void OnDoubleTap(object? sender, TappedEventArgs e)
    {
        ExecuteSelected();
    }

    private void ExecuteSelected()
    {
        if (ResultsList.SelectedItem is not CommandPaletteItem item) return;
        DialogClosed.TrySetResult();
        // Defer execution so dialog overlay closes first; otherwise commands
        // that open another overlay may compete with us.
        Dispatcher.UIThread.Post(() =>
        {
            try { item.Descriptor.OnExecute?.Invoke(); }
            catch { /* swallow — caller responsible for surfacing errors */ }
        }, DispatcherPriority.Background);
    }

    private static string HumanizeGesture(string gesture)
    {
        if (string.IsNullOrEmpty(gesture)) return string.Empty;
        // Shorten Avalonia/.NET key names for readability.
        return gesture
            .Replace("Control", "Ctrl", StringComparison.OrdinalIgnoreCase)
            .Replace("OemPlus", "+")
            .Replace("OemMinus", "-")
            .Replace("OemComma", ",")
            .Replace("OemPeriod", ".")
            .Replace("OemQuestion", "/")
            .Replace("OemSemicolon", ";")
            .Replace("OemQuotes", "'")
            .Replace("OemOpenBrackets", "[")
            .Replace("OemCloseBrackets", "]")
            .Replace("OemTilde", "`")
            .Replace("OemBackslash", "\\")
            .Replace("D0", "0").Replace("D1", "1").Replace("D2", "2").Replace("D3", "3")
            .Replace("D4", "4").Replace("D5", "5").Replace("D6", "6").Replace("D7", "7")
            .Replace("D8", "8").Replace("D9", "9");
    }
}

public sealed class CommandPaletteItem
{
    public HotkeyDescriptor Descriptor { get; }
    // Snapshot localised strings at construction so the filter sees the
    // current-language form even if the descriptor was registered earlier.
    public string DisplayName { get; }
    public string Category { get; }
    public string Gesture { get; init; } = string.Empty;
    public bool HasGesture { get; init; }

    public CommandPaletteItem(HotkeyDescriptor d)
    {
        Descriptor = d;
        DisplayName = d.EffectiveDisplayName;
        Category = d.EffectiveCategory;
    }
}
