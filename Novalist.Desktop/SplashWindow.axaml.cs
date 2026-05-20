using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Novalist.Core;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;

namespace Novalist.Desktop;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] // startup splash: app/extension update checks (network), installer download/launch + process restart/shutdown — process & network interop, not unit-testable
public partial class SplashWindow : Window
{
    private UpdateInfo? _pendingUpdate;
    private IUpdateService? _updateService;
    private TaskCompletionSource<bool>? _updateDecisionTcs;

    public SplashWindow()
    {
        InitializeComponent();
    }

    public void SetStatus(string text)
    {
        if (this.FindControl<TextBlock>("StatusText") is { } tb)
            tb.Text = text;
    }

    /// <summary>
    /// Checks for an app update. Returns true if the user chose to update now (app will shut down).
    /// </summary>
    public async Task<bool> CheckForAppUpdateAsync()
    {
        SetStatus(Loc.T("splash.checkingForUpdates"));

        try
        {
            _updateService = new UpdateService();
            var update = await _updateService.CheckForUpdateAsync();
            if (update is null)
                return false;

            _pendingUpdate = update;

            // Show update UI
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var versionText = this.FindControl<TextBlock>("UpdateVersionText")!;
                versionText.Text = Loc.T("splash.versionInfo", update.Version, VersionInfo.Version);

                var progressBar = this.FindControl<ProgressBar>("ProgressBar")!;
                progressBar.IsVisible = false;

                var updatePanel = this.FindControl<Border>("UpdatePanel")!;
                updatePanel.IsVisible = true;

                SetStatus(Loc.T("splash.updateAvailable"));

                // Grow window to make room for the update buttons
                Height = 480;
            });

            // Wait for user decision
            _updateDecisionTcs = new TaskCompletionSource<bool>();
            return await _updateDecisionTcs.Task;
        }
        catch
        {
            // Silently ignore update check failures
            return false;
        }
    }

    private async void OnUpdateNow(object? sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is null || _updateService is null)
            return;

        var updateNowBtn = this.FindControl<Button>("UpdateNowButton")!;
        var updateLaterBtn = this.FindControl<Button>("UpdateLaterButton")!;
        var progressBar = this.FindControl<ProgressBar>("ProgressBar")!;

        updateNowBtn.IsEnabled = false;
        updateLaterBtn.IsEnabled = false;
        progressBar.IsIndeterminate = false;
        progressBar.IsVisible = true;
        progressBar.Minimum = 0;
        progressBar.Maximum = 100;

        SetStatus(Loc.T("splash.downloadingUpdate"));

        var progress = new Progress<double>(p =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                progressBar.Value = p * 100;
                SetStatus(Loc.T("splash.downloadingUpdateProgress", (int)(p * 100)));
            });
        });

        try
        {
            var installerPath = await _updateService.DownloadUpdateAsync(_pendingUpdate, progress);
            SetStatus(Loc.T("splash.launchingInstaller"));
            _updateService.LaunchInstaller(installerPath);
            _updateDecisionTcs?.TrySetResult(true);
        }
        catch (Exception ex)
        {
            SetStatus(Loc.T("splash.updateFailed", ex.Message));
            updateNowBtn.IsEnabled = true;
            updateLaterBtn.IsEnabled = true;
        }
    }

    private void OnUpdateLater(object? sender, RoutedEventArgs e)
    {
        // Hide update panel, resume startup
        var updatePanel = this.FindControl<Border>("UpdatePanel")!;
        updatePanel.IsVisible = false;

        var progressBar = this.FindControl<ProgressBar>("ProgressBar")!;
        progressBar.IsIndeterminate = true;
        progressBar.IsVisible = true;

        _updateDecisionTcs?.TrySetResult(false);
    }

    /// <summary>
    /// Checks installed extensions for updates and auto-updates them.
    /// Returns true if any extensions were updated (requires restart).
    /// </summary>
    public async Task<bool> CheckAndAutoUpdateExtensionsAsync(
        IExtensionGalleryService galleryService,
        Services.ExtensionManager extensionManager)
    {
        SetStatus(Loc.T("splash.checkingExtensionUpdates"));

        try
        {
            var updates = await galleryService.CheckForUpdatesAsync();
            if (updates.Count == 0)
                return false;

            SetStatus(Loc.T("splash.updatingExtensions", updates.Count));

            foreach (var update in updates)
            {
                SetStatus(Loc.T("splash.updatingExtension", update.ExtensionId));
                await extensionManager.DisableExtensionAsync(update.ExtensionId);
                var zipPath = await galleryService.DownloadExtensionZipAsync(update.Release);
                await galleryService.InstallExtensionAsync(zipPath, update.Entry, update.Release);
                await extensionManager.EnableExtensionAsync(update.ExtensionId);
            }

            return true;
        }
        catch
        {
            // Don't block startup if extension update check fails
            return false;
        }
    }

    /// <summary>
    /// Restarts the application.
    /// </summary>
    public static void RestartApp()
    {
        // Running inside an AppImage: Environment.ProcessPath points into the
        // squashfs mount which is torn down on exit, so a direct relaunch
        // would race with the unmount. Spawn a detached script that waits
        // for us to exit, then execs the AppImage file (the runtime mounts
        // a fresh copy).
        var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
        if (OperatingSystem.IsLinux()
            && !string.IsNullOrEmpty(appImage)
            && File.Exists(appImage))
        {
            RestartViaAppImage(appImage);
            return;
        }

        var mainModule = Environment.ProcessPath;
        if (string.IsNullOrEmpty(mainModule))
            return;

        ProcessStartInfo psi;
        var fileName = Path.GetFileNameWithoutExtension(mainModule);

        if (string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            // Dev path: app launched via `dotnet xyz.dll`. Re-spawn the same.
            // Assembly.Location is valid here because this branch only runs
            // when we are NOT a single-file publish (where ProcessPath points
            // at our own native host, not at dotnet).
#pragma warning disable IL3000
            var exeAssembly = System.Reflection.Assembly.GetEntryAssembly()?.Location;
#pragma warning restore IL3000
            if (string.IsNullOrEmpty(exeAssembly))
                return;
            psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{exeAssembly}\"",
                UseShellExecute = false
            };
        }
        else
        {
            // Native host: single-file publish on every platform. Just relaunch
            // ourselves directly.
            psi = new ProcessStartInfo
            {
                FileName = mainModule,
                UseShellExecute = false
            };
        }

        psi.WorkingDirectory = AppContext.BaseDirectory;
        Process.Start(psi);

        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static void RestartViaAppImage(string appImagePath)
    {
        var pid = Environment.ProcessId;
        var scriptPath = Path.Combine(Path.GetTempPath(),
            $"novalist-restart-{Guid.NewGuid():N}.sh");
        var quoted = "'" + appImagePath.Replace("'", "'\\''") + "'";

        var script =
            "#!/bin/bash\n" +
            "set -u\n" +
            $"while kill -0 {pid} 2>/dev/null; do sleep 0.2; done\n" +
            "sleep 0.5\n" +
            $"setsid nohup {quoted} </dev/null >/dev/null 2>&1 &\n" +
            "rm -f \"$0\"\n";

        File.WriteAllText(scriptPath, script);
        try
        {
            File.SetUnixFileMode(scriptPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch { /* best effort */ }

        Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"\"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
