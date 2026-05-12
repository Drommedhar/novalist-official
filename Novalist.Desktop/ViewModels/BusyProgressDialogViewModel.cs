using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Novalist.Desktop.ViewModels;

public partial class BusyProgressDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isIndeterminate = true;

    [ObservableProperty]
    private bool _showProgressBar = true;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _allowCancel;

    [ObservableProperty]
    private string _cancelLabel = "Cancel";

    public ObservableCollection<string> Details { get; } = [];

    [ObservableProperty]
    private bool _hasDetails;

    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);

    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(HasStatus));
}
