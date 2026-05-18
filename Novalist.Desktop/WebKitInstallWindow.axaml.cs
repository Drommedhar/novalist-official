using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Novalist.Core.Services;

namespace Novalist.Desktop;

public enum WebKitInstallOutcome
{
    Quit,
    Installed,
}

public partial class WebKitInstallWindow : Window
{
    private readonly LinuxDependencyInfo _info;
    private readonly TaskCompletionSource<WebKitInstallOutcome> _done = new();
    private CancellationTokenSource? _installCts;

    public Task<WebKitInstallOutcome> Outcome => _done.Task;

    public WebKitInstallWindow() : this(LinuxDependencyService.Detect()) { }

    public WebKitInstallWindow(LinuxDependencyInfo info)
    {
        _info = info;
        InitializeComponent();

        DistroLabel.Text  = $"Detected: {info.DistroName}";
        PackageLabel.Text = $"Package:  {info.PackageName}";
        CommandText.Text  = string.IsNullOrEmpty(info.InstallCommand)
            ? "(no automatic command for this distribution — install the package shown above using your package manager)"
            : $"pkexec sh -c '{info.InstallCommand}'";

        if (string.IsNullOrEmpty(info.InstallCommand) || !LinuxDependencyService.IsPkexecAvailable())
        {
            InstallButton.IsEnabled = false;
            NotSupportedHint.IsVisible = true;
            if (!LinuxDependencyService.IsPkexecAvailable())
                NotSupportedHint.Text = "pkexec is not available on this system. Copy the command and run it in a terminal as root, then relaunch Novalist.";
        }
    }

    private async void OnInstall(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_info.InstallCommand)) return;

        IntroView.IsVisible = false;
        ProgressView.IsVisible = true;
        InstallButton.IsVisible = false;
        CopyButton.IsVisible = false;
        QuitButton.IsEnabled = false;

        _installCts = new CancellationTokenSource();
        var progress = new Progress<string>(line =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                LogText.Text += line + "\n";
                LogScroll.ScrollToEnd();
            });
        });

        InstallResult result;
        try
        {
            result = await LinuxDependencyService.InstallAsync(
                _info.InstallCommand, progress, _installCts.Token);
        }
        catch (Exception ex)
        {
            result = new InstallResult(false, ex.Message);
        }

        ProgressBar.IsIndeterminate = false;
        QuitButton.IsEnabled = true;

        if (result.Success && LinuxDependencyService.IsWebKitInstalled())
        {
            ProgressStatus.Text = "Installed successfully. Restart Novalist to start using it.";
            ProgressBar.Value = 100;
            RestartButton.IsVisible = true;
        }
        else if (result.Success)
        {
            // pkexec returned 0 but the SONAME still isn't found — usually
            // means the package manager succeeded but installed a 4.0 fallback
            // or the cache hasn't refreshed yet.
            ProgressStatus.Text = "Install reported success but libwebkit2gtk-4.1.so.0 is still missing. Try restarting and re-checking.";
            RestartButton.IsVisible = true;
        }
        else
        {
            ProgressStatus.Text = $"Install failed: {result.Message}";
            CopyButton.IsVisible = true;
            InstallButton.IsVisible = true;
            InstallButton.Content = "Try again";
        }
    }

    private async void OnCopyCommand(object? sender, RoutedEventArgs e)
    {
        var text = string.IsNullOrEmpty(_info.InstallCommand)
            ? _info.PackageName
            : $"sudo sh -c '{_info.InstallCommand}'";

        var clipboard = GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(text);
    }

    private void OnQuit(object? sender, RoutedEventArgs e)
    {
        _installCts?.Cancel();
        _done.TrySetResult(WebKitInstallOutcome.Quit);
        Close();
    }

    private void OnRestart(object? sender, RoutedEventArgs e)
    {
        _done.TrySetResult(WebKitInstallOutcome.Installed);
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // If the user X's the window (no decorations, but still possible via
        // window manager shortcuts), treat it as Quit.
        _done.TrySetResult(WebKitInstallOutcome.Quit);
        base.OnClosing(e);
    }
}
