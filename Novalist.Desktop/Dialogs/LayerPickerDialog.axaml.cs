using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Novalist.Desktop.Dialogs;

public partial class LayerPickerDialog : UserControl
{
    public TaskCompletionSource DialogClosed { get; } = new();
    public string? ResultLayerId { get; private set; }

    private readonly List<LayerOption> _options;

    public LayerPickerDialog(IEnumerable<(string Id, string Name)> options, string currentLayerId)
    {
        InitializeComponent();
        _options = options.Select(o => new LayerOption(o.Id, o.Name)).ToList();
        LayerList.ItemsSource = _options;
        var current = _options.FirstOrDefault(o => o.Id == currentLayerId);
        if (current != null) LayerList.SelectedItem = current;
        LayerList.DoubleTapped += (_, _) => Confirm();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) Cancel();
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Confirm();
    private void OnCancel(object? sender, RoutedEventArgs e) => Cancel();

    private void Confirm()
    {
        if (LayerList.SelectedItem is LayerOption opt) ResultLayerId = opt.Id;
        DialogClosed.TrySetResult();
    }

    private void Cancel()
    {
        ResultLayerId = null;
        DialogClosed.TrySetResult();
    }

    public sealed class LayerOption
    {
        public string Id { get; }
        public string Name { get; }
        public LayerOption(string id, string name) { Id = id; Name = name; }
        public override string ToString() => Name;
    }
}
