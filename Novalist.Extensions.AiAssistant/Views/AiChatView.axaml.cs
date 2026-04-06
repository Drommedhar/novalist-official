using Avalonia.Controls;
using Avalonia.Input;
using Novalist.Extensions.AiAssistant.ViewModels;

namespace Novalist.Extensions.AiAssistant.Views;

public partial class AiChatView : UserControl
{
    public AiChatView()
    {
        InitializeComponent();
    }

    private void OnChatInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is AiChatViewModel vm && vm.SendCommand.CanExecute(null))
        {
            vm.SendCommand.Execute(null);
            e.Handled = true;
        }
    }
}
