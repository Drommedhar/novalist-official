using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Novalist.Core;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;

namespace Novalist.Desktop.Dialogs;

public partial class UpdateDialog : UserControl
{
    private readonly UpdateInfo _update;
    private readonly IUpdateService _updateService;
    private CancellationTokenSource? _cts;
    private bool _downloading;

    public TaskCompletionSource DialogClosed { get; } = new();

    public UpdateDialog()
    {
        InitializeComponent();
        _update = new UpdateInfo();
        _updateService = new UpdateService();
    }

    public UpdateDialog(UpdateInfo update, IUpdateService updateService) : this()
    {
        _update = update;
        _updateService = updateService;

        TitleText.Text = Loc.T("update.available");
        VersionText.Text = Loc.T("update.versionInfo", VersionInfo.Version, update.Version);

        if (!string.IsNullOrWhiteSpace(update.Body))
        {
            ReleaseNotesText.Text = update.Body;
            ReleaseNotesScroll.IsVisible = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && !_downloading)
            DialogClosed.TrySetResult();
    }

    private async void OnDownload(object? sender, RoutedEventArgs e)
    {
        if (_downloading)
            return;

        _downloading = true;
        DownloadButton.IsEnabled = false;
        SkipButton.IsEnabled = false;
        ProgressPanel.IsVisible = true;
        ErrorText.IsVisible = false;

        _cts = new CancellationTokenSource();
        var progress = new Progress<double>(p =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                DownloadProgress.Value = p * 100;
                ProgressText.Text = Loc.T("update.downloading", (int)(p * 100));
            });
        });

        try
        {
            var installerPath = await _updateService.DownloadUpdateAsync(_update, progress, _cts.Token);
            ProgressText.Text = Loc.T("update.launching");

            _updateService.LaunchInstaller(installerPath);

            // Give the installer a moment to start, then signal the app to close
            DialogClosed.TrySetResult();

            // Request application shutdown so the installer can replace files
            if (Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
        catch (OperationCanceledException)
        {
            // User cancelled
            ProgressPanel.IsVisible = false;
            _downloading = false;
            DownloadButton.IsEnabled = true;
            SkipButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = Loc.T("update.error", ex.Message);
            ErrorText.IsVisible = true;
            ProgressPanel.IsVisible = false;
            _downloading = false;
            DownloadButton.IsEnabled = true;
            SkipButton.IsEnabled = true;
        }
    }

    private void OnSkip(object? sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        DialogClosed.TrySetResult();
    }
}
