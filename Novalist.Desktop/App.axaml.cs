using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;
using Novalist.Desktop.Services;
using Novalist.Desktop.ViewModels;

namespace Novalist.Desktop;

// Application bootstrap: XAML load, splash/main-window + WebKit-gate lifecycle,
// extension startup, and synchronous startup settings IO against the fixed
// %APPDATA% path. No injectable seams — exercised end-to-end by the running app.
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public partial class App : Application
{
    public static IFileService FileService { get; } = new FileService();
    public static ISettingsService SettingsService { get; } = new SettingsService();
    public static IProjectService ProjectService { get; } = new ProjectService(FileService);
    public static IEntityService EntityService { get; } = new EntityService(ProjectService);
    public static IGitService GitService { get; } = new GitService();
    public static ISnapshotService SnapshotService { get; } = new SnapshotService(ProjectService, FileService);
    public static IProjectTemplateService ProjectTemplateService { get; } = new ProjectTemplateService();
    public static IFindReplaceService FindReplaceService { get; } = new FindReplaceService(ProjectService);
    public static ISmartListService SmartListService { get; } = new SmartListService(ProjectService, EntityService);
    public static IPlotlineService PlotlineService { get; } = new PlotlineService(ProjectService);
    public static IResearchService ResearchService { get; } = new ResearchService(ProjectService, FileService);
    public static IMapService MapService { get; } = new MapService(ProjectService, FileService);
    public static IWordHistoryService WordHistoryService { get; } = new WordHistoryService(FileService, ProjectService);
    public static ExtensionManager ExtensionManager { get; private set; } = null!;
    public static ThemeService ThemeService { get; } = new();
    public static IHotkeyService HotkeyService { get; } = new HotkeyService(SettingsService);
    public static HotkeyManager HotkeyManager { get; } = new(HotkeyService);

    private static string GetLocalesDirectory()
    {
        var appBaseDirectory = AppContext.BaseDirectory;
        var defaultDirectory = Path.Combine(appBaseDirectory, "Assets", "Locales");
        if (Directory.Exists(defaultDirectory))
            return defaultDirectory;

        var macBundleResourcesDirectory = Path.GetFullPath(
            Path.Combine(appBaseDirectory, "..", "Resources", "Assets", "Locales"));
        if (Directory.Exists(macBundleResourcesDirectory))
            return macBundleResourcesDirectory;

        return defaultDirectory;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Quick synchronous read of the language setting from the settings file
    /// so the WebView2 environment is created with the correct locale for
    /// spellcheck and context menus.
    /// </summary>
    internal static string ReadLanguageFromSettings()
    {
        try
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Novalist", "settings.json");
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("language", out var prop))
                    return prop.GetString() ?? "en";
            }
        }
        catch { /* fall back to default */ }
        return "en";
    }

    /// <summary>
    /// Quick synchronous read of the diagnostic-logging flag so file logging can
    /// be enabled before the async settings load completes, capturing startup.
    /// </summary>
    internal static bool ReadDiagnosticLoggingFromSettings()
    {
        try
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Novalist", "settings.json");
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("diagnosticLoggingEnabled", out var prop))
                    return prop.ValueKind == JsonValueKind.True;
            }
        }
        catch { /* default off */ }
        return false;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Enable the diagnostic file log as early as possible (synchronous
            // settings read, like the language read below) so startup itself is
            // captured when the user has opted in.
            Utilities.Log.EnableFileLogging(ReadDiagnosticLoggingFromSettings());
            Utilities.Log.Info("App.OnFrameworkInitializationCompleted: startup begin.");

            // Initialize localization with the saved language BEFORE any UI is
            // created so the splash window picks up the correct strings.
            var localesDir = GetLocalesDirectory();
            Loc.Instance.Initialize(localesDir, ReadLanguageFromSettings());

            // Prevent the app from quitting when we swap MainWindow from splash to
            // the real window. On macOS, closing the splash while it is the active
            // MainWindow can race with NSApplication's window-restoration handling
            // (see _reopenWindowsAsNecessary…) and terminate the process.
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

            // Hook the dispatcher's unhandled exception event so any UI-thread
            // exception is logged instead of taking the process down silently.
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                Program.LogCrash("Dispatcher.UnhandledException", e.Exception);
                Utilities.Log.Error("Dispatcher.UnhandledException", e.Exception);
                e.Handled = true;
            };

            desktop.ShutdownRequested += (_, _) =>
            {
                ExtensionManager?.ShutdownAll();
            };

            // On Linux, webkit2gtk-4.1 has to be installed on the host or the
            // first WebView instantiation triggers a fatal GLib abort that we
            // can't catch. Run the install wizard before constructing the
            // main window (which creates WebView-hosting views in its ctor).
            // Set NOVALIST_FORCE_WEBKIT_WIZARD=1 to preview the wizard even
            // when webkit2gtk is already installed.
            var forceWizard = Environment.GetEnvironmentVariable("NOVALIST_FORCE_WEBKIT_WIZARD") == "1";
            if (OperatingSystem.IsLinux()
                && (forceWizard || !LinuxDependencyService.IsWebKitInstalled()))
            {
                _ = RunWebKitGateAsync(desktop, forceWizard);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            StartNormal(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Logs the installed extensions and their package metadata (id, name,
    /// version, author, enabled/loaded state). This is extension developer /
    /// package info, not the user's story content, so it is safe to log in full
    /// — it tells us exactly which third-party code is running when diagnosing a
    /// report. Repository URLs are not in the manifest (they live in the gallery
    /// listing), so they are not available here.
    /// </summary>
    private static void LogActiveExtensions()
    {
        var exts = ExtensionManager.Extensions;
        Utilities.Log.Info($"Extensions: {exts.Count} installed.");
        foreach (var e in exts)
        {
            var m = e.Manifest;
            var author = string.IsNullOrWhiteSpace(m.Author) ? "?" : m.Author;
            var version = string.IsNullOrWhiteSpace(m.Version) ? "?" : m.Version;
            var status = e.IsLoaded ? "loaded" : (e.LoadError != null ? "error" : "not-loaded");
            Utilities.Log.Info(
                $"  ext id={m.Id} name=\"{m.Name}\" v{version} by \"{author}\" enabled={e.IsEnabled} status={status}");
        }
    }

    private static void StartNormal(IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Show splash screen immediately while we initialize
        var splash = new SplashWindow();
        desktop.MainWindow = splash;
        splash.Show();

        var mainVm = new MainWindowViewModel(ProjectService, SettingsService, EntityService, GitService);
        var mainWindow = new MainWindow
        {
            DataContext = mainVm,
            IsVisible = false
        };

        // Run async startup; surface any failure instead of letting it terminate the process.
        _ = RunStartupAsync(desktop, splash, mainWindow, mainVm);
    }

    private static async Task RunWebKitGateAsync(IClassicDesktopStyleApplicationLifetime desktop, bool forced = false)
    {
        var info = LinuxDependencyService.Detect();
        if (forced)
        {
            // Override the detected state so the wizard's "missing" UI shows
            // even though the package is actually installed.
            info = info with { WebKitInstalled = false };
        }
        var window = new WebKitInstallWindow(info);
        desktop.MainWindow = window;
        window.Show();

        var outcome = await window.Outcome;

        if (outcome == WebKitInstallOutcome.Installed)
        {
            // The user installed and clicked Restart. Re-exec via the AppImage
            // (or process path) so the newly-installed libs are picked up.
            SplashWindow.RestartApp();
            return;
        }

        // User chose Quit — exit cleanly.
        desktop.Shutdown();
    }

    private static async Task RunStartupAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        SplashWindow splash,
        MainWindow mainWindow,
        MainWindowViewModel mainVm)
    {
        try
        {
            await mainVm.InitializeAsync();

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                splash.SetStatus(Loc.T("splash.applyingSettings"));
                var lang = SettingsService.Settings.Language;
                if (!string.IsNullOrWhiteSpace(lang)
                    && !string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase))
                    Loc.Instance.CurrentLanguage = lang;

                // Initialize extension system
                splash.SetStatus(Loc.T("splash.loadingExtensions"));
                var hostServices = new HostServices(FileService, ProjectService, EntityService, SettingsService);
                ExtensionManager = new ExtensionManager(SettingsService, hostServices);
                hostServices.ExtensionManager = ExtensionManager;
                hostServices.WizardLauncher = (def, seed) => mainWindow.RunWizardForExtensionAsync(def, seed);
                mainWindow.WireExtensionBusyProgress(hostServices);
                await ExtensionManager.LoadAllAsync();
                LogActiveExtensions();

                // Initialize gallery service
                var galleryService = new Novalist.Core.Services.ExtensionGalleryService();
                if (!string.IsNullOrWhiteSpace(SettingsService.Settings.GitHubToken))
                    galleryService.GitHubToken = SettingsService.Settings.GitHubToken;

                mainVm.OnExtensionsLoaded(ExtensionManager, galleryService);

                // Forward host language changes to extensions
                Loc.Instance.LanguageChanged += () =>
                    hostServices.RaiseLanguageChanged(Loc.Instance.CurrentLanguage);

                // Register built-in and extension themes, then apply saved theme
                splash.SetStatus(Loc.T("splash.applyingTheme"));
                ThemeService.RegisterBuiltInTheme("Discord",
                    "avares://Novalist.Desktop/Assets/Themes/DiscordTheme.axaml",
                    "#5865F2");
                ThemeService.RegisterBuiltInTheme("Catppuccin Mocha",
                    "avares://Novalist.Desktop/Assets/Themes/CatppuccinTheme.axaml",
                    "#89B4FA");
                ThemeService.RegisterFolderThemes(Path.Combine(AppContext.BaseDirectory, "Assets", "Themes"));
                ThemeService.RegisterExtensionThemes(ExtensionManager.ThemeOverrides, ExtensionManager);
                var savedTheme = SettingsService.Settings.Theme;
                if (!string.IsNullOrEmpty(savedTheme) && savedTheme != "system")
                {
                    try
                    {
                        ThemeService.ApplyTheme(savedTheme);
                    }
                    catch (Exception ex)
                    {
                        Program.LogCrash($"ApplyTheme('{savedTheme}') failed; reverting to default.", ex);
                        SettingsService.Settings.Theme = "system";
                        _ = SettingsService.SaveAsync();
                    }
                }

                // Apply saved accent color override (if any)
                var savedAccent = SettingsService.Settings.AccentColor;
                if (!string.IsNullOrEmpty(savedAccent))
                {
                    try
                    {
                        ThemeService.ApplyAccentColor(savedAccent);
                    }
                    catch (Exception ex)
                    {
                        Program.LogCrash($"ApplyAccentColor('{savedAccent}') failed; reverting.", ex);
                        SettingsService.Settings.AccentColor = string.Empty;
                        _ = SettingsService.SaveAsync();
                    }
                }

                // Check for app updates in splash screen
                if (SettingsService.Settings.CheckForUpdates)
                {
                    var userChoseUpdate = await splash.CheckForAppUpdateAsync();
                    if (userChoseUpdate)
                    {
                        // User chose to update — app will shut down after installer launches
                        desktop.Shutdown();
                        return;
                    }
                }

                // Check for extension updates and auto-update them
                if (SettingsService.Settings.CheckForExtensionUpdates)
                {
                    var extensionsUpdated = await splash.CheckAndAutoUpdateExtensionsAsync(
                        galleryService, ExtensionManager);
                    if (extensionsUpdated)
                    {
                        splash.SetStatus("Extensions updated. Restarting...");
                        await Task.Delay(1000);
                        SplashWindow.RestartApp();
                        return;
                    }
                }

                // Everything is ready — swap to the main window before closing the splash.
                desktop.MainWindow = mainWindow;
                mainWindow.Show();
                mainWindow.ShowWelcomeIfNeeded();
                splash.Close();

                // Now it is safe to restore normal shutdown semantics.
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            });
        }
        catch (Exception ex)
        {
            Program.LogCrash("RunStartupAsync", ex);

            // Try to leave the user with *something* rather than dying silently.
            try
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    desktop.MainWindow = mainWindow;
                    mainWindow.Show();
                    splash.Close();
                    desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
                });
            }
            catch (Exception inner)
            {
                Program.LogCrash("RunStartupAsync.Recovery", inner);
            }
        }
    }
}