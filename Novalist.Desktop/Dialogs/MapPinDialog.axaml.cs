using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Novalist.Desktop.Dialogs;

public partial class MapPinDialog : UserControl
{
    public TaskCompletionSource DialogClosed { get; } = new();
    public string? ResultLabel { get; private set; }
    public string? ResultEntityId { get; private set; }
    public string? ResultEntityType { get; private set; }
    public string? ResultColor { get; private set; }
    public bool ResultDelete { get; private set; }

    private readonly List<EntityOption> _options;
    private const string DefaultColor = "#F9C46A";

    public MapPinDialog(IEnumerable<EntityOption> entityOptions, string? initialLabel = null,
        string? initialEntityId = null, string? initialColor = null, bool allowDelete = false)
    {
        InitializeComponent();
        _options = entityOptions.ToList();
        EntityInput.ItemsSource = _options;
        DeleteButton.IsVisible = allowDelete;

        if (!string.IsNullOrEmpty(initialLabel)) LabelInput.Text = initialLabel;
        if (!string.IsNullOrEmpty(initialEntityId))
        {
            var match = _options.FirstOrDefault(o => o.Id == initialEntityId);
            if (match != null) EntityInput.SelectedItem = match;
        }
        var colorText = string.IsNullOrEmpty(initialColor) ? DefaultColor : initialColor;
        if (Avalonia.Media.Color.TryParse(colorText, out var color))
            ColorInput.Color = color;
    }

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        ResultDelete = true;
        ResultLabel = string.Empty;
        DialogClosed.TrySetResult();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => LabelInput.Focus(),
            Avalonia.Threading.DispatcherPriority.Input);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { Cancel(); }
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        ResultLabel = LabelInput.Text?.Trim() ?? string.Empty;
        if (EntityInput.SelectedItem is EntityOption opt)
        {
            ResultEntityId = opt.Id;
            ResultEntityType = opt.Type;
        }
        else if (!string.IsNullOrWhiteSpace(EntityInput.Text))
        {
            var match = _options.FirstOrDefault(o =>
                string.Equals(o.Name, EntityInput.Text!.Trim(), System.StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                ResultEntityId = match.Id;
                ResultEntityType = match.Type;
            }
        }
        var c = ColorInput.Color;
        ResultColor = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        DialogClosed.TrySetResult();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Cancel();

    private void Cancel()
    {
        ResultLabel = null;
        ResultEntityId = null;
        ResultEntityType = null;
        DialogClosed.TrySetResult();
    }
}

public sealed class EntityOption
{
    public string Id { get; }
    public string Name { get; }
    public string Type { get; } // "character" | "location" | "item" | "lore" | "custom"

    public EntityOption(string id, string name, string type)
    {
        Id = id;
        Name = name;
        Type = type;
    }

    public override string ToString() => $"{Type}: {Name}";
}
