using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Novalist.Desktop.ViewModels;

public partial class EditorTabDescriptor : ObservableObject
{
    public string Id { get; }
    public string ActivationKey { get; }
    public string? Badge { get; }
    public double MinWidth { get; }
    public Action OnClose { get; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private string? _tooltip;

    public EditorTabDescriptor(
        string id,
        string activationKey,
        string title,
        Action onClose,
        string? badge = null,
        double minWidth = 120,
        string? tooltip = null)
    {
        Id = id;
        ActivationKey = activationKey;
        _title = title;
        Badge = badge;
        MinWidth = minWidth;
        _tooltip = tooltip;
        OnClose = onClose;
    }
}
