using System.Threading.Tasks;
using Avalonia.Controls;
using Novalist.Desktop.ViewModels;

namespace Novalist.Desktop.Dialogs;

public partial class WizardDialog : UserControl
{
    public TaskCompletionSource DialogClosed { get; } = new();

    public WizardDialog()
    {
        InitializeComponent();
    }

    public WizardDialog(WizardDialogViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseRequested += () => DialogClosed.TrySetResult();
    }
}
