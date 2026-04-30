using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Novalist.Core;
using Novalist.Core.Services;

namespace Novalist.Desktop;

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
        SetStatus("Checking for updates...");

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
                versionText.Text = $"A new version is available: {update.Version} (current: {VersionInfo.Version})";

                var progressBar = this.FindControl<ProgressBar>("ProgressBar")!;
                progressBar.IsVisible = false;

                var updatePanel = this.FindControl<Border>("UpdatePanel")!;
                updatePanel.IsVisible = true;

                SetStatus("Update available");

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

        SetStatus("Downloading update...");

        var progress = new Progress<double>(p =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                progressBar.Value = p * 100;
                SetStatus($"Downloading update... {(int)(p * 100)}%");
            });
        });

        try
        {
            var installerPath = await _updateService.DownloadUpdateAsync(_pendingUpdate, progress);
            SetStatus("Launching installer...");
            _updateService.LaunchInstaller(installerPath);
            _updateDecisionTcs?.TrySetResult(true);
        }
        catch (Exception ex)
        {
            SetStatus($"Update failed: {ex.Message}");
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
        SetStatus("Checking for extension updates...");

        try
        {
            var updates = await galleryService.CheckForUpdatesAsync();
            if (updates.Count == 0)
                return false;

            SetStatus($"Updating {updates.Count} extension(s)...");

            foreach (var update in updates)
            {
                SetStatus($"Updating {update.ExtensionId}...");
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
        var mainModule = Environment.ProcessPath;
        var exeAssembly = System.Reflection.Assembly.GetEntryAssembly()?.Location;

        ProcessStartInfo psi;

        // If the process is the native host .exe, just relaunch it.
        // If we're running via `dotnet <dll>`, relaunch through dotnet.
        if (!string.IsNullOrEmpty(mainModule)
            && mainModule.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && !mainModule.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            psi = new ProcessStartInfo
            {
                FileName = mainModule,
                UseShellExecute = false
            };
        }
        else if (!string.IsNullOrEmpty(exeAssembly))
        {
            // Running through dotnet — relaunch as `dotnet <dll>`
            psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{exeAssembly}\"",
                UseShellExecute = false
            };
        }
        else
        {
            return;
        }

        psi.WorkingDirectory = AppContext.BaseDirectory;
        Process.Start(psi);

        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
