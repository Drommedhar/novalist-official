using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Novalist.Desktop.Localization;

namespace Novalist.Desktop.Dialogs;

public partial class EntityCreationDialog : UserControl
{
    public string? ResultName { get; private set; }
    public string? ResultTemplateId { get; private set; }
    public TaskCompletionSource DialogClosed { get; } = new();

    public EntityCreationDialog()
    {
        InitializeComponent();
    }

    public EntityCreationDialog(string title, string prompt, IReadOnlyList<TemplateOption> templates) : this()
    {
        PromptText.Text = prompt;

        if (templates.Count > 0)
        {
            TemplatePanel.IsVisible = true;
            TemplateLabel.Text = Loc.T("entityPanel.templateLabel");
            var items = new List<TemplateOption> { new(string.Empty, Loc.T("entityPanel.noTemplate")) };
            items.AddRange(templates);
            TemplateComboBox.ItemsSource = items;
            TemplateComboBox.SelectedIndex = 0;
        }
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
            ResultName = null;
            DialogClosed.TrySetResult();
        }
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var text = InputBox.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            ResultName = text;
            ResultTemplateId = (TemplateComboBox.SelectedItem as TemplateOption)?.Id;
            if (string.IsNullOrEmpty(ResultTemplateId))
                ResultTemplateId = null;
            DialogClosed.TrySetResult();
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        ResultName = null;
        DialogClosed.TrySetResult();
    }
}

public sealed class TemplateOption(string id, string name)
{
    public string Id { get; } = id;
    public string Name { get; } = name;

    public override string ToString() => Name;
}
