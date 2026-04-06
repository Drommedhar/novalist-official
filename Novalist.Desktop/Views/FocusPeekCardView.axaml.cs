using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Novalist.Desktop.ViewModels;
using System.Linq;

namespace Novalist.Desktop.Views;

public partial class FocusPeekCardView : UserControl
{
    public FocusPeekCardView()
    {
        InitializeComponent();
        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is FocusPeekViewModel vm)
            vm.HasOpenPopup = IsAnyComboBoxDropDownOpen;
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (DataContext is FocusPeekViewModel vm)
            vm.SetPointerOverCard(true);
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is FocusPeekViewModel vm)
        {
            if (IsAnyComboBoxDropDownOpen())
                return;

            vm.SetPointerOverCard(false);
            vm.PointerExitedRequested?.Invoke();
        }
    }

    private bool IsAnyComboBoxDropDownOpen()
    {
        return this.GetVisualDescendants()
            .OfType<ComboBox>()
            .Any(combo => combo.IsDropDownOpen);
    }
}