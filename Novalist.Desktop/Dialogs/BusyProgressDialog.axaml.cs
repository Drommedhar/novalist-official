using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Novalist.Desktop.Localization;
using Novalist.Desktop.ViewModels;
using Novalist.Sdk.Services;

namespace Novalist.Desktop.Dialogs;

public partial class BusyProgressDialog : UserControl, IBusyProgress
{
    private readonly BusyProgressDialogViewModel _vm;
    private readonly CancellationTokenSource _cts = new();
    private readonly BusyProgressOptions _options;

    public TaskCompletionSource DialogClosed { get; } = new();
    public bool IsModal => _options.IsModal;

    public BusyProgressDialog() : this(new BusyProgressOptions())
    {
    }

    public BusyProgressDialog(BusyProgressOptions options)
    {
        _options = options;
        _vm = new BusyProgressDialogViewModel
        {
            Title = options.Title,
            Status = options.InitialStatus,
            IsIndeterminate = options.IsIndeterminate,
            ShowProgressBar = options.ShowProgressBar,
            AllowCancel = options.AllowCancel,
            CancelLabel = !string.IsNullOrWhiteSpace(options.CancelLabel)
                ? options.CancelLabel!
                : Loc.T("dialog.cancel"),
        };
        InitializeComponent();
        DataContext = _vm;
    }

    public CancellationToken CancellationToken => _cts.Token;
    public bool IsClosed { get; private set; }

    public event Action? Cancelled;

    public void SetStatus(string status)
        => Dispatcher.UIThread.Post(() => _vm.Status = status ?? string.Empty);

    public void SetProgress(double value)
    {
        var clamped = Math.Clamp(value, 0d, 1d);
        Dispatcher.UIThread.Post(() => _vm.Progress = clamped);
    }

    public void SetTitle(string title)
        => Dispatcher.UIThread.Post(() => _vm.Title = title ?? string.Empty);

    public void SetIndeterminate(bool isIndeterminate)
        => Dispatcher.UIThread.Post(() => _vm.IsIndeterminate = isIndeterminate);

    public void Dispose()
    {
        if (IsClosed) return;
        IsClosed = true;
        if (Dispatcher.UIThread.CheckAccess())
            DialogClosed.TrySetResult();
        else
            Dispatcher.UIThread.Post(() => DialogClosed.TrySetResult());
        _cts.Dispose();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && _vm.AllowCancel)
            TriggerCancel();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        TriggerCancel();
    }

    private void TriggerCancel()
    {
        if (!_cts.IsCancellationRequested)
        {
            _cts.Cancel();
            Cancelled?.Invoke();
        }
    }
}
