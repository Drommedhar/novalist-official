using System;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AvaloniaWebView;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;
using Novalist.Desktop.Services;
using Novalist.Desktop.ViewModels;

namespace Novalist.Desktop;

public partial class App : Application
{
    public static IFileService FileService { get; } = new FileService();
    public static ISettingsService SettingsService { get; } = new SettingsService();
    public static IProjectService ProjectService { get; } = new ProjectService(FileService);
    public static IEntityService EntityService { get; } = new EntityService(ProjectService);
    public static IGitService GitService { get; } = new GitService();
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
        var lang = ReadLanguageFromSettings();
        AvaloniaWebViewBuilder.Initialize(config =>
        {
            config.UserDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Novalist", "WebView2", lang);
            config.AreDefaultContextMenusEnabled = true;
            config.Language = lang;
        });
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

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Initialize localization with default language before any UI is created
            var localesDir = GetLocalesDirectory();
            Loc.Instance.Initialize(localesDir, "en");

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

            // Load settings asynchronously, then apply the user's language choice
            _ = mainVm.InitializeAsync().ContinueWith(_ =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    splash.SetStatus("Applying settings...");
                    var lang = SettingsService.Settings.Language;
                    if (!string.Equals(lang, "en", StringComparison.Ordinal))
                        Loc.Instance.CurrentLanguage = lang;

                    // Initialize extension system
                    splash.SetStatus("Loading extensions...");
                    var hostServices = new HostServices(FileService, ProjectService, EntityService, SettingsService);
                    ExtensionManager = new ExtensionManager(SettingsService, hostServices);
                    hostServices.ExtensionManager = ExtensionManager;
                    await ExtensionManager.LoadAllAsync();
                    mainVm.OnExtensionsLoaded(ExtensionManager);

                    // Forward host language changes to extensions
                    Loc.Instance.LanguageChanged += () =>
                        hostServices.RaiseLanguageChanged(Loc.Instance.CurrentLanguage);

                    // Register extension themes and apply saved theme
                    splash.SetStatus("Applying theme...");
                    ThemeService.RegisterExtensionThemes(ExtensionManager.ThemeOverrides, ExtensionManager);
                    var savedTheme = SettingsService.Settings.Theme;
                    if (!string.IsNullOrEmpty(savedTheme) && savedTheme != "system")
                        ThemeService.ApplyTheme(savedTheme);

                    // Everything is ready — swap to the main window
                    desktop.MainWindow = mainWindow;
                    mainWindow.Show();
                    mainWindow.ShowWelcomeIfNeeded();
                    splash.Close();

                    // Check for updates in background (non-blocking)
                    if (SettingsService.Settings.CheckForUpdates)
                        _ = mainWindow.CheckForUpdateAsync();
                });
            });

            desktop.ShutdownRequested += (_, _) =>
            {
                ExtensionManager?.ShutdownAll();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}