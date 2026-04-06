using Avalonia.Controls;
using Avalonia.Input;
using Novalist.Desktop.ViewModels;

namespace Novalist.Desktop.Views;

public partial class HotkeySettingsView : UserControl
{
    public HotkeySettingsView()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is HotkeySettingsViewModel vm && vm.IsRecording)
        {
            if (vm.HandleRecordingKeyDown(e))
            {
                e.Handled = true;
                return;
            }
        }

        base.OnKeyDown(e);
    }
}
